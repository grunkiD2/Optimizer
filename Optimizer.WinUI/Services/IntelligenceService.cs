using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms.TimeSeries;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services.Events;

namespace Optimizer.WinUI.Services;

public class IntelligenceService : IIntelligenceService
{
    private readonly MLContext _ml = new(seed: 42);
    private readonly IRecommendationsService _recommendations;
    private readonly IHistoryService _history;
    private readonly IEventBus _eventBus;
    private ITransformer? _acceptanceModel;
    private DataViewSchema? _modelSchema;

    private readonly string _modelPath = AppPaths.GetDataFile("ml-acceptance-model.zip");

    private const int ModelVersion = 2;  // bump when upgrading ML.NET
    private string _versionFile => Path.Combine(Path.GetDirectoryName(_modelPath)!, "ml-model.version");

    public bool IsTrained => _acceptanceModel != null;
    public DateTime? LastTrainedAt { get; private set; }

    public IntelligenceService(IRecommendationsService recommendations, IHistoryService history, IEventBus eventBus)
    {
        _recommendations = recommendations;
        _history = history;
        _eventBus = eventBus;
        TryLoadModel();
    }

    private void TryLoadModel()
    {
        try
        {
            if (!File.Exists(_modelPath)) return;

            // Check version compatibility
            if (File.Exists(_versionFile) && int.TryParse(File.ReadAllText(_versionFile), out var v) && v == ModelVersion)
            {
                _acceptanceModel = _ml.Model.Load(_modelPath, out _modelSchema);
                LastTrainedAt = File.GetLastWriteTime(_modelPath);
            }
            else
            {
                // Stale model — delete and retrain on schedule
                EngineLog.Write("ML model from previous version detected, deleting for retrain");
                try { File.Delete(_modelPath); } catch { }
                try { if (File.Exists(_versionFile)) File.Delete(_versionFile); } catch { }
            }
        }
        catch (Exception ex)
        {
            EngineLog.Error("Failed to load ML model — will retrain", ex);
            try { File.Delete(_modelPath); } catch { }
        }
    }

    public async Task TrainAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                var features = CollectTrainingData();
                if (features.Count < 5)
                {
                    EngineLog.Write($"Not enough data to train ML model ({features.Count} samples; need 5+)");
                    return;
                }

                var data = _ml.Data.LoadFromEnumerable(features);

                var pipeline = _ml.Transforms.Categorical.OneHotEncoding("CategoryEncoded", nameof(RecommendationFeature.Category))
                    .Append(_ml.Transforms.Categorical.OneHotEncoding("SeverityEncoded", nameof(RecommendationFeature.Severity)))
                    .Append(_ml.Transforms.Concatenate("Features",
                        "CategoryEncoded", "SeverityEncoded",
                        nameof(RecommendationFeature.DayOfWeek),
                        nameof(RecommendationFeature.HourOfDay)))
                    .Append(_ml.BinaryClassification.Trainers.SdcaLogisticRegression(
                        labelColumnName: nameof(RecommendationFeature.Accepted)));

                _acceptanceModel = pipeline.Fit(data);
                _modelSchema = data.Schema;

                Directory.CreateDirectory(Path.GetDirectoryName(_modelPath)!);
                _ml.Model.Save(_acceptanceModel, _modelSchema, _modelPath);
                File.WriteAllText(_versionFile, ModelVersion.ToString());
                LastTrainedAt = DateTime.Now;

                EngineLog.Write($"ML model trained on {features.Count} samples");
            }
            catch (Exception ex)
            {
                EngineLog.Error("ML training failed", ex);
            }
        });
    }

    public Task<float?> PredictAcceptanceAsync(string category, string severity)
    {
        if (_acceptanceModel == null || _modelSchema == null)
            return Task.FromResult<float?>(null);

        try
        {
            var engine = _ml.Model.CreatePredictionEngine<RecommendationFeature, RecommendationPrediction>(
                _acceptanceModel, _modelSchema);
            var prediction = engine.Predict(new RecommendationFeature
            {
                Category = category,
                Severity = severity,
                DayOfWeek = (float)DateTime.Now.DayOfWeek,
                HourOfDay = DateTime.Now.Hour
            });
            return Task.FromResult<float?>(prediction.Probability);
        }
        catch (Exception ex)
        {
            EngineLog.Error("ML prediction failed", ex);
            return Task.FromResult<float?>(null);
        }
    }

    public Task<IReadOnlyList<AnomalyAlert>> DetectAnomaliesAsync(IReadOnlyList<double> recentValues, string metricName)
    {
        var alerts = new List<AnomalyAlert>();
        if (recentValues.Count < 12) return Task.FromResult<IReadOnlyList<AnomalyAlert>>(alerts);

        try
        {
            // Simple statistical anomaly: 3-sigma rule
            var avg = recentValues.Average();
            var stdDev = Math.Sqrt(recentValues.Sum(v => Math.Pow(v - avg, 2)) / recentValues.Count);
            var threshold = 3 * stdDev;

            var latest = recentValues[^1];
            if (Math.Abs(latest - avg) > threshold && stdDev > 1)
            {
                var alert = new AnomalyAlert
                {
                    MetricName = metricName,
                    Value = latest,
                    ExpectedValue = avg,
                    Severity = Math.Min(1.0, Math.Abs(latest - avg) / (5 * stdDev)),
                    Description = $"{metricName} is unusually {(latest > avg ? "high" : "low")}: " +
                                  $"{latest:F1} vs expected ~{avg:F1} ±{threshold:F1}"
                };
                alerts.Add(alert);

                _eventBus.Publish(OptimizerEvent.Create(
                    OptimizerEventType.AnomalyDetected,
                    $"Anomaly detected: {metricName}",
                    alert.Description,
                    new Dictionary<string, string>
                    {
                        ["metric"]   = metricName,
                        ["value"]    = latest.ToString("F1"),
                        ["expected"] = avg.ToString("F1"),
                        ["severity"] = alert.Severity.ToString("F2")
                    }));
            }
        }
        catch (Exception ex)
        {
            EngineLog.Error($"Anomaly detection failed for {metricName}", ex);
        }

        return Task.FromResult<IReadOnlyList<AnomalyAlert>>(alerts);
    }

    // ── SSA anomaly analysis ──────────────────────────────────────────────────

    /// <summary>
    /// Analyzes a time series with ML.NET SSA (Singular Spectrum Analysis).
    /// Falls back to the 3-sigma heuristic when there are fewer than 24 points
    /// or when SSA throws. Never throws to the caller.
    /// </summary>
    public Task<AnomalyAnalysis> AnalyzeSeriesAsync(IReadOnlyList<double> values, string metricName)
    {
        if (values == null || values.Count == 0)
            return Task.FromResult(new AnomalyAnalysis(false, AnomalyClass.None, 0, 0, 0, ""));

        // Sanitise: replace NaN/Infinity so SSA doesn't blow up
        var clean = values.Select(v => double.IsFinite(v) ? v : 0.0).ToArray();

        var latest     = clean[^1];
        var mean       = clean.Average();

        const int SsaMinPoints = 24;

        if (clean.Length >= SsaMinPoints)
        {
            try
            {
                return Task.FromResult(RunSsaAnalysis(clean, metricName, latest, mean));
            }
            catch (Exception ex)
            {
                EngineLog.Error($"SSA analysis failed for {metricName}, falling back to 3-sigma", ex);
            }
        }

        // Fallback: 3-sigma
        return Task.FromResult(ThreeSigmaAnalysis(clean, metricName, latest, mean));
    }

    private AnomalyAnalysis RunSsaAnalysis(double[] values, string metricName, double latest, double mean)
    {
        int n         = values.Length;
        // Seasonality and training window heuristics
        int trainSize = Math.Min(n, 100);
        int window    = Math.Max(4, trainSize / 4);
        int season    = Math.Max(2, window / 2);

        // ── Spike detection ───────────────────────────────────────────────────
        bool spikeAlert     = false;
        double spikeScore   = 0;
        bool dipAlert       = false;

        try
        {
            var mlSpike = new MLContext(seed: 1);
            var inputData = mlSpike.Data.LoadFromEnumerable(
                values.Select(v => new SsaInput { Value = (float)v }));

            var spikePipeline = mlSpike.Transforms.DetectSpikeBySsa(
                outputColumnName: "Alert",
                inputColumnName: nameof(SsaInput.Value),
                confidence: 95.0,
                pvalueHistoryLength: Math.Max(2, n / 4),
                trainingWindowSize: trainSize,
                seasonalityWindowSize: season);

            var spikeModel    = spikePipeline.Fit(inputData);
            var spikeOutput   = spikeModel.Transform(inputData);
            var spikeCol      = spikeOutput.GetColumn<VBuffer<double>>("Alert").ToArray();

            if (spikeCol.Length > 0)
            {
                var lastRow = spikeCol[^1].GetValues().ToArray();
                // lastRow[0] = alert flag (0/1), lastRow[1] = raw score, lastRow[2] = p-value
                if (lastRow.Length >= 1 && lastRow[0] > 0)
                {
                    spikeAlert  = true;
                    spikeScore  = lastRow.Length >= 3 ? Math.Min(1.0, 1.0 - lastRow[2]) : 0.8;
                    dipAlert    = latest < mean;
                }
            }
        }
        catch (Exception ex)
        {
            EngineLog.Error($"SSA spike detection failed for {metricName}", ex);
        }

        // ── Change-point detection (sustained trend) ──────────────────────────
        bool changeAlert      = false;
        double changeScore    = 0;
        bool changeTrendUp    = false;

        try
        {
            var mlChange = new MLContext(seed: 2);
            var inputData = mlChange.Data.LoadFromEnumerable(
                values.Select(v => new SsaInput { Value = (float)v }));

            var changePipeline = mlChange.Transforms.DetectChangePointBySsa(
                outputColumnName: "Alert",
                inputColumnName: nameof(SsaInput.Value),
                confidence: 90.0,
                changeHistoryLength: Math.Max(2, n / 4),
                trainingWindowSize: trainSize,
                seasonalityWindowSize: season);

            var changeModel   = changePipeline.Fit(inputData);
            var changeOutput  = changeModel.Transform(inputData);
            var changeCol     = changeOutput.GetColumn<VBuffer<double>>("Alert").ToArray();

            if (changeCol.Length > 0)
            {
                // Find the most-recent change point
                for (int i = changeCol.Length - 1; i >= 0; i--)
                {
                    var row = changeCol[i].GetValues().ToArray();
                    if (row.Length >= 1 && row[0] > 0)
                    {
                        changeAlert  = true;
                        changeScore  = row.Length >= 3 ? Math.Min(1.0, 1.0 - row[2]) : 0.7;
                        // Check whether the trailing window (after change point) trends up or down
                        var tail = values.Skip(i).ToArray();
                        changeTrendUp = tail.Average() > mean;
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            EngineLog.Error($"SSA change-point detection failed for {metricName}", ex);
        }

        // ── Classify ──────────────────────────────────────────────────────────
        // A spike alert that coincides with a change point and a rising tail =>
        // treat as UpwardTrend (more useful diagnostic).
        // A standalone spike without a persistent change => Spike.
        if (changeAlert && !spikeAlert)
        {
            var cls     = changeTrendUp ? AnomalyClass.UpwardTrend : AnomalyClass.DownwardTrend;
            var desc    = BuildDescription(metricName, cls, latest, mean);
            return new AnomalyAnalysis(true, cls, Math.Round(changeScore, 2), latest, mean, desc);
        }

        if (changeAlert && spikeAlert)
        {
            // Both: favour trend classification (more actionable)
            var cls     = changeTrendUp ? AnomalyClass.UpwardTrend : AnomalyClass.DownwardTrend;
            var score   = Math.Max(spikeScore, changeScore);
            var desc    = BuildDescription(metricName, cls, latest, mean);
            return new AnomalyAnalysis(true, cls, Math.Round(score, 2), latest, mean, desc);
        }

        if (spikeAlert)
        {
            var cls     = dipAlert ? AnomalyClass.Dip : AnomalyClass.Spike;
            var desc    = BuildDescription(metricName, cls, latest, mean);
            return new AnomalyAnalysis(true, cls, Math.Round(spikeScore, 2), latest, mean, desc);
        }

        return new AnomalyAnalysis(false, AnomalyClass.None, 0, latest, mean, "");
    }

    private static string BuildDescription(string metricName, AnomalyClass cls, double latest, double mean) =>
        cls switch
        {
            AnomalyClass.Spike        => $"{metricName} spike detected: {latest:F1} vs typical {mean:F1}.",
            AnomalyClass.Dip          => $"{metricName} sudden drop detected: {latest:F1} vs typical {mean:F1}.",
            AnomalyClass.UpwardTrend  => $"{metricName} shows a sustained upward trend (possible leak): now {latest:F1}% vs baseline {mean:F1}%.",
            AnomalyClass.DownwardTrend=> $"{metricName} shows a sustained downward trend: now {latest:F1} vs baseline {mean:F1}.",
            _                         => ""
        };

    private static AnomalyAnalysis ThreeSigmaAnalysis(double[] values, string metricName, double latest, double mean)
    {
        if (values.Length < 2)
            return new AnomalyAnalysis(false, AnomalyClass.None, 0, latest, mean, "");

        var stdDev    = Math.Sqrt(values.Sum(v => Math.Pow(v - mean, 2)) / values.Length);
        var threshold = 3 * stdDev;

        if (Math.Abs(latest - mean) > threshold && stdDev > 1)
        {
            var isHigh  = latest > mean;
            var cls     = isHigh ? AnomalyClass.Spike : AnomalyClass.Dip;
            var score   = Math.Min(1.0, Math.Abs(latest - mean) / (5 * stdDev));
            var desc    = $"{metricName} is unusually {(isHigh ? "high" : "low")}: {latest:F1} vs expected ~{mean:F1} ±{threshold:F1}";
            return new AnomalyAnalysis(true, cls, Math.Round(score, 2), latest, mean, desc);
        }

        return new AnomalyAnalysis(false, AnomalyClass.None, 0, latest, mean, "");
    }

    // ML.NET input schema for SSA transforms
    private sealed class SsaInput
    {
        public float Value { get; set; }
    }

    private List<RecommendationFeature> CollectTrainingData()
    {
        var features = new List<RecommendationFeature>();
        var prefs = _recommendations.GetPreferences();

        // Synthesize features from accept/dismiss counts
        foreach (var (id, pref) in prefs)
        {
            var (cat, sev) = ParseIdHeuristic(id);

            for (int i = 0; i < Math.Min(pref.AcceptCount, 10); i++)
            {
                features.Add(new RecommendationFeature
                {
                    Category = cat,
                    Severity = sev,
                    DayOfWeek = (float)pref.LastShownUtc.DayOfWeek,
                    HourOfDay = pref.LastShownUtc.Hour,
                    Accepted = true
                });
            }
            for (int i = 0; i < Math.Min(pref.DismissCount, 10); i++)
            {
                features.Add(new RecommendationFeature
                {
                    Category = cat,
                    Severity = sev,
                    DayOfWeek = (float)pref.LastShownUtc.DayOfWeek,
                    HourOfDay = pref.LastShownUtc.Hour,
                    Accepted = false
                });
            }
        }

        return features;
    }

    private static (string Category, string Severity) ParseIdHeuristic(string id)
    {
        if (id.Contains("disk", StringComparison.OrdinalIgnoreCase)) return ("Storage", "Warning");
        if (id.Contains("cpu", StringComparison.OrdinalIgnoreCase) || id.Contains("perf", StringComparison.OrdinalIgnoreCase)) return ("Performance", "Info");
        if (id.Contains("privacy", StringComparison.OrdinalIgnoreCase) || id.Contains("telem", StringComparison.OrdinalIgnoreCase)) return ("Privacy", "Info");
        if (id.Contains("smart", StringComparison.OrdinalIgnoreCase) || id.Contains("temp", StringComparison.OrdinalIgnoreCase)) return ("Hardware", "Critical");
        if (id.Contains("battery", StringComparison.OrdinalIgnoreCase)) return ("Hardware", "Warning");
        return ("Performance", "Info");
    }
}
