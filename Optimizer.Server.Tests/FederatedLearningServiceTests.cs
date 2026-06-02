using Microsoft.EntityFrameworkCore;
using Optimizer.Server.Data;
using Optimizer.Server.Services;
using Xunit;

namespace Optimizer.Server.Tests;

/// <summary>
/// Tests for the federated-averaging scaffold service.
/// Uses an in-memory EF database for isolation.
/// </summary>
public class FederatedLearningServiceTests : IDisposable
{
    private readonly OptimizerDbContext _db;
    private readonly FederatedLearningService _fl;

    private readonly Guid _user1 = Guid.NewGuid();
    private readonly Guid _user2 = Guid.NewGuid();
    private readonly Guid _user3 = Guid.NewGuid();
    private readonly Guid _user4 = Guid.NewGuid();
    private readonly Guid _user5 = Guid.NewGuid();

    public FederatedLearningServiceTests()
    {
        var opts = new DbContextOptionsBuilder<OptimizerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new OptimizerDbContext(opts);
        _fl = new FederatedLearningService(_db);
    }

    public void Dispose() { _db.Dispose(); GC.SuppressFinalize(this); }

    // ── Submit / upsert ──────────────────────────────────────────────────────

    [Fact]
    public async Task Submit_InsertsOneRowPerUserCategory()
    {
        await _fl.SubmitAsync(_user1,
        [
            new CategoryContribution("Performance", 0.8, 10),
            new CategoryContribution("Storage",     0.6,  5)
        ]);

        Assert.Equal(2, _db.FederatedContributions.Count());
    }

    [Fact]
    public async Task Submit_Upserts_ReplacesExistingContribution()
    {
        await _fl.SubmitAsync(_user1, [new CategoryContribution("Performance", 0.7, 10)]);
        await _fl.SubmitAsync(_user1, [new CategoryContribution("Performance", 0.9, 20)]);

        // Only one row should exist for this (user, category)
        var rows = _db.FederatedContributions
            .Where(f => f.UserId == _user1 && f.Category == "Performance")
            .ToList();

        Assert.Single(rows);
        Assert.Equal(0.9, rows[0].AcceptanceRate);
        Assert.Equal(20,  rows[0].SampleWeight);
    }

    // ── GetBaselines / k-anonymity ────────────────────────────────────────────

    [Fact]
    public async Task GetBaselines_ReturnsEmpty_WhenNoContributions()
    {
        var baselines = await _fl.GetBaselinesAsync();
        Assert.Empty(baselines);
    }

    [Fact]
    public async Task GetBaselines_WithholdCategory_BelowMinContributors()
    {
        // Only 4 contributors — below the threshold of 5
        foreach (var uid in new[] { _user1, _user2, _user3, _user4 })
            await _fl.SubmitAsync(uid, [new CategoryContribution("Privacy", 0.5, 10)]);

        var baselines = await _fl.GetBaselinesAsync();

        Assert.DoesNotContain(baselines, b => b.Category == "Privacy");
    }

    [Fact]
    public async Task GetBaselines_ExposesCategory_AtOrAboveMinContributors()
    {
        // Exactly at threshold (5 contributors)
        foreach (var uid in new[] { _user1, _user2, _user3, _user4, _user5 })
            await _fl.SubmitAsync(uid, [new CategoryContribution("Hardware", 0.6, 10)]);

        var baselines = await _fl.GetBaselinesAsync();

        Assert.Contains(baselines, b => b.Category == "Hardware");
    }

    [Fact]
    public async Task GetBaselines_WeightedAverageMath_IsCorrect()
    {
        // 5 users, two weights: 10 and 20
        // user1: rate=0.4, weight=20
        // user2-5: rate=0.8, weight=10 each
        // weighted avg = (0.4*20 + 0.8*10 + 0.8*10 + 0.8*10 + 0.8*10) / (20+10+10+10+10)
        //              = (8 + 8 + 8 + 8 + 8) / 60 = 40/60 ≈ 0.6667
        await _fl.SubmitAsync(_user1, [new CategoryContribution("Performance", 0.4, 20)]);
        await _fl.SubmitAsync(_user2, [new CategoryContribution("Performance", 0.8, 10)]);
        await _fl.SubmitAsync(_user3, [new CategoryContribution("Performance", 0.8, 10)]);
        await _fl.SubmitAsync(_user4, [new CategoryContribution("Performance", 0.8, 10)]);
        await _fl.SubmitAsync(_user5, [new CategoryContribution("Performance", 0.8, 10)]);

        var baselines = await _fl.GetBaselinesAsync();
        var b = Assert.Single(baselines, b => b.Category == "Performance");

        Assert.Equal(40.0 / 60.0, b.CommunityAcceptanceRate, precision: 4);
        Assert.Equal(5, b.ContributorCount);
    }

    [Fact]
    public async Task GetBaselines_ContributorCount_IsDistinctUsers()
    {
        // Each user appears once per category
        foreach (var uid in new[] { _user1, _user2, _user3, _user4, _user5 })
            await _fl.SubmitAsync(uid, [new CategoryContribution("Storage", 0.5, 5)]);

        // Resubmit from user1 (upsert) — should not increase count
        await _fl.SubmitAsync(_user1, [new CategoryContribution("Storage", 0.7, 8)]);

        var baselines = await _fl.GetBaselinesAsync();
        var b = Assert.Single(baselines, b => b.Category == "Storage");

        Assert.Equal(5, b.ContributorCount);
    }

    // ── Validation ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Submit_ThrowsOnInvalidCategory()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _fl.SubmitAsync(_user1, [new CategoryContribution("", 0.5, 10)]));
    }

    [Fact]
    public async Task Submit_ThrowsOnRateOutOfBounds()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _fl.SubmitAsync(_user1, [new CategoryContribution("Performance", 1.5, 10)]));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _fl.SubmitAsync(_user1, [new CategoryContribution("Performance", -0.1, 10)]));
    }
}
