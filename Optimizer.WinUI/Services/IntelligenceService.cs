using Microsoft.ML;
using Microsoft.ML.Data;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class IntelligenceService : IIntelligenceService
{
    private readonly MLContext _ml = new(seed: 42);
    private readonly IRecommendationsService _recommendations;
    private readonly HistoryService _history;
    private ITransformer? _acceptanceModel;
    private DataViewSchema? _modelSchema;

    private readonly string _modelPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Optimizer", "ml-acceptance-model.zip");

    public bool IsTrained => _acceptanceModel != null;
    public DateTime? LastTrainedAt { get; private set; }

    public IntelligenceService(IRecommendationsService recommendations, HistoryService history)
    {
        _recommendations = recommendations;
        _history = history;
        TryLoadModel();
    }

    private void TryLoadModel()
    {
        try
        {
            if (File.Exists(_modelPath))
            {
                _acceptanceModel = _ml.Model.Load(_modelPath, out _modelSchema);
                LastTrainedAt = File.GetLastWriteTime(_modelPath);
            }
        }
        catch (Exception ex)
        {
            EngineLog.Error("Failed to load ML model", ex);
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
                alerts.Add(new AnomalyAlert
                {
                    MetricName = metricName,
                    Value = latest,
                    ExpectedValue = avg,
                    Severity = Math.Min(1.0, Math.Abs(latest - avg) / (5 * stdDev)),
                    Description = $"{metricName} is unusually {(latest > avg ? "high" : "low")}: " +
                                  $"{latest:F1} vs expected ~{avg:F1} ±{threshold:F1}"
                });
            }
        }
        catch (Exception ex)
        {
            EngineLog.Error($"Anomaly detection failed for {metricName}", ex);
        }

        return Task.FromResult<IReadOnlyList<AnomalyAlert>>(alerts);
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
        if (id.Contains("disk")) return ("Storage", "Warning");
        if (id.Contains("cpu") || id.Contains("perf")) return ("Performance", "Info");
        if (id.Contains("privacy") || id.Contains("telem")) return ("Privacy", "Info");
        if (id.Contains("smart") || id.Contains("temp")) return ("Hardware", "Critical");
        if (id.Contains("battery")) return ("Hardware", "Warning");
        return ("Performance", "Info");
    }
}
