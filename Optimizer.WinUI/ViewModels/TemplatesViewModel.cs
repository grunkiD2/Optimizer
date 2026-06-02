using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class TemplatesViewModel : ObservableObject
{
    private readonly ITemplatesService _templates;

    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string statusMessage = "";

    // Create-template dialog fields
    [ObservableProperty] private string newTemplateName        = "";
    [ObservableProperty] private string newTemplateDescription = "";

    public ObservableCollection<ConfigTemplate> Templates { get; } = [];

    public string CategoryName => "Templates";
    public string CategoryIcon => "📋";

    public TemplatesViewModel(ITemplatesService templates)
    {
        _templates = templates;
    }

    // ── Load ──────────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var list = await _templates.GetTemplatesAsync();
            Templates.Clear();
            foreach (var t in list) Templates.Add(t);
            StatusMessage = list.Count == 0
                ? "No templates yet. Create one from the current system state."
                : $"{list.Count} template(s) saved.";
        }
        finally { IsLoading = false; }
    }

    // ── Create from current state ─────────────────────────────────────────────

    [RelayCommand]
    public async Task CreateTemplateAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTemplateName)) return;
        IsLoading = true;
        try
        {
            await _templates.CreateFromCurrentStateAsync(NewTemplateName, NewTemplateDescription);
            NewTemplateName = NewTemplateDescription = "";
            await LoadAsync();
            StatusMessage = "Template created successfully.";
        }
        finally { IsLoading = false; }
    }

    // ── Export DSC ────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task ExportDscAsync(ConfigTemplate template)
    {
        IsLoading = true;
        try
        {
            var path = await _templates.ExportToDscAsync(template);
            StatusMessage = $"DSC exported: {path}";
        }
        finally { IsLoading = false; }
    }

    // ── Export Intune ─────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task ExportIntuneAsync(ConfigTemplate template)
    {
        IsLoading = true;
        try
        {
            var path = await _templates.ExportToIntuneAsync(template);
            StatusMessage = $"Intune JSON exported: {path}";
        }
        finally { IsLoading = false; }
    }

    // ── Export WinGet ─────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task ExportWingetAsync(ConfigTemplate template)
    {
        IsLoading = true;
        try
        {
            var path = await _templates.ExportToWingetAsync(template);
            StatusMessage = $"WinGet YAML exported: {path}";
        }
        finally { IsLoading = false; }
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task DeleteTemplateAsync(string id)
    {
        await _templates.DeleteAsync(id);
        await LoadAsync();
    }
}
