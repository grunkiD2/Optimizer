using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.Services.Cloud;

/// <summary>
/// Lightweight federated-averaging scaffold client.
///
/// Privacy guarantees enforced here:
///   - FederatedLearningEnabled defaults to FALSE. Nothing is uploaded unless the user
///     explicitly opts in via Settings.
///   - Only ComputePrivatizedSummary() output (Laplace DP-noised) is ever uploaded.
///     ComputeLocalSummary() stays on-device.
///   - No raw preferences, user behavior, system info, or identifiable data leaves the device.
///   - The user can opt out at any time; on next app start no upload will occur.
/// </summary>
public class FederatedClient : IFederatedClient
{
    private readonly ISettingsService _settings;
    private readonly IOptimizerCloudClient _cloud;
    private readonly IIntelligenceService _intelligence;

    // Default epsilon for DP noise. Lower = more privacy.
    // 1.0 is a commonly used value that provides meaningful privacy while preserving utility.
    private const double DefaultEpsilon = 1.0;

    private List<FederatedCommunityBaseline> _baselines = [];

    public IReadOnlyList<FederatedCommunityBaseline> CommunityBaselines => _baselines;

    public FederatedClient(
        ISettingsService settings,
        IOptimizerCloudClient cloud,
        IIntelligenceService intelligence)
    {
        _settings      = settings;
        _cloud         = cloud;
        _intelligence  = intelligence;
    }

    public async Task SyncAsync()
    {
        // Hard gate: if the user hasn't opted in, do nothing at all.
        if (!_settings.Settings.FederatedLearningEnabled)
        {
            EngineLog.Write("Federated learning is disabled — skipping sync.");
            return;
        }

        // Also requires cloud authentication.
        if (!_cloud.IsAuthenticated)
        {
            EngineLog.Write("Federated sync skipped: not authenticated.");
            return;
        }

        try
        {
            // Step 1: compute DP-noised summary — raw data NEVER leaves the device.
            var privatizedSummary = _intelligence.ComputePrivatizedSummary(DefaultEpsilon);

            // Step 2: map to upload format.
            var contributions = privatizedSummary.CategoryAcceptanceRates
                .Select(kvp => new FederatedCategoryContribution(
                    Category       : kvp.Key,
                    AcceptanceRate : kvp.Value,
                    SampleWeight   : privatizedSummary.TotalSamples))
                .ToList();

            if (contributions.Count == 0)
            {
                EngineLog.Write("Federated sync: no local data to contribute yet.");
            }
            else
            {
                // Step 3: upload (only DP-noised aggregates).
                var ok = await _cloud.ContributeFederatedAsync(contributions);
                EngineLog.Write(ok
                    ? $"Federated: contributed {contributions.Count} DP-noised category rate(s)."
                    : "Federated: contribution upload failed (will retry next sync).");
            }

            // Step 4: fetch community baselines (regardless of upload success).
            var baselines = await _cloud.GetCommunityBaselinesAsync();
            if (baselines != null)
            {
                _baselines = [.. baselines];
                EngineLog.Write($"Federated: fetched {_baselines.Count} community baseline(s).");
            }
        }
        catch (Exception ex)
        {
            EngineLog.Error("Federated sync failed", ex);
        }
    }
}
