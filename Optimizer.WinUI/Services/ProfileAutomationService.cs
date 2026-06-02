using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class ProfileAutomationService : IProfileAutomationService
{
    private readonly IWindowsOptimizerService _optimizer;
    private readonly List<ProfileRule> _rules = [];
    private DispatcherTimer? _timer;
    private string? _lastAppliedProfile;

    private static readonly string RulesFile = AppPaths.GetDataFile("profile-rules.json");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public IReadOnlyList<ProfileRule> Rules => _rules;

    public ProfileAutomationService(IWindowsOptimizerService optimizer)
    {
        _optimizer = optimizer;
        Load();
    }

    public Task AddRuleAsync(ProfileRule rule)
    {
        _rules.Add(rule);
        Save();
        return Task.CompletedTask;
    }

    public Task UpdateRuleAsync(ProfileRule rule)
    {
        Save();
        return Task.CompletedTask;
    }

    public Task DeleteRuleAsync(string ruleId)
    {
        _rules.RemoveAll(r => r.Id == ruleId);
        Save();
        return Task.CompletedTask;
    }

    public void Start()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _timer.Tick += async (_, _) => await EvaluateRulesAsync();
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    private async Task EvaluateRulesAsync()
    {
        var now = DateTime.Now.TimeOfDay;
        HashSet<string>? runningProcesses = null;

        foreach (var rule in _rules.Where(r => r.IsEnabled))
        {
            bool matches;

            if (rule.Trigger == RuleTrigger.ProcessRunning)
            {
                // Lazy-load process list once per evaluation cycle
                runningProcesses ??= Process.GetProcesses()
                    .Select(p => { try { return p.ProcessName; } catch { return ""; } })
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                matches = !string.IsNullOrEmpty(rule.ProcessName)
                    && runningProcesses.Contains(rule.ProcessName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                matches = InTimeRange(now, rule.StartTime, rule.EndTime);
            }

            if (matches && _lastAppliedProfile != rule.ProfileId)
            {
                try
                {
                    await _optimizer.ApplyProfileAsync(rule.ProfileId);
                    _lastAppliedProfile = rule.ProfileId;
                    EngineLog.Write($"Smart profile switch: applied '{rule.ProfileName}' (rule: {rule.Name})");
                    break; // apply first matching rule only
                }
                catch (Exception ex)
                {
                    EngineLog.Error("Smart profile apply failed", ex);
                }
            }
        }
    }

    private static bool InTimeRange(TimeSpan now, TimeSpan start, TimeSpan end)
    {
        // Handle range crossing midnight (e.g. 22:00 – 06:00)
        if (start <= end) return now >= start && now < end;
        return now >= start || now < end;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(RulesFile)) return;
            var json = File.ReadAllText(RulesFile);
            var rules = JsonSerializer.Deserialize<List<ProfileRule>>(json, SerializerOptions);
            if (rules != null) _rules.AddRange(rules);
        }
        catch { }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RulesFile)!);
            File.WriteAllText(RulesFile, JsonSerializer.Serialize(_rules, SerializerOptions));
        }
        catch { }
    }
}
