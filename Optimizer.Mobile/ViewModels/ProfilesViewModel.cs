using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.Mobile.Services;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace Optimizer.Mobile.ViewModels;

public partial class ProfileModel : ObservableObject
{
    public string Id          { get; set; } = "";
    public string Name        { get; set; } = "";
    public string Description { get; set; } = "";
}

public partial class ProfilesViewModel : ObservableObject
{
    private readonly ApiClient _api;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _hasError;

    public ObservableCollection<ProfileModel> Profiles { get; } = new();

    public ProfilesViewModel(ApiClient api)
    {
        _api = api;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        HasError = false;
        try
        {
            var data = await _api.GetJsonAsync("/api/profiles");
            Profiles.Clear();
            if (data.HasValue && data.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in data.Value.EnumerateArray())
                {
                    Profiles.Add(new ProfileModel
                    {
                        Id          = p.TryGetProperty("id",          out var id)  ? id.GetString()  ?? "" : "",
                        Name        = p.TryGetProperty("name",        out var n)   ? n.GetString()   ?? "" : "",
                        Description = p.TryGetProperty("description", out var d)   ? d.GetString()   ?? "" : ""
                    });
                }
            }
            if (Profiles.Count == 0) StatusMessage = "No profiles found.";
        }
        catch
        {
            HasError = true;
            StatusMessage = "Could not load profiles. Check connection.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task ApplyAsync(string profileId)
    {
        var result = await _api.PostJsonAsync($"/api/apply/{Uri.EscapeDataString(profileId)}");
        bool success = result.HasValue &&
            result.Value.TryGetProperty("success", out var s) && s.GetBoolean();
        await Shell.Current.DisplayAlertAsync(
            success ? "Applied" : "Failed",
            success ? "Profile applied successfully." : "Could not apply profile.",
            "OK");
    }
}
