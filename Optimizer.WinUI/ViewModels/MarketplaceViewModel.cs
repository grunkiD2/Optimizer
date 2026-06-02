using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class MarketplaceViewModel : ObservableObject
{
    private readonly IMarketplaceService _marketplace;
    private List<MarketplaceEntry> _allEntries = [];

    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string selectedCategory = "All";
    [ObservableProperty] private string sortBy = "Most Downloaded";

    public ObservableCollection<MarketplaceEntry> Entries { get; } = [];

    public List<string> CategoryOptions { get; } =
        ["All", "Gaming", "Productivity", "Content Creation", "Laptop", "Privacy", "Maintenance"];

    public List<string> SortOptions { get; } =
        ["Most Downloaded", "Highest Rated", "Newest", "A-Z"];

    public string CategoryName => "Marketplace";
    public string CategoryIcon => "🛒";

    public MarketplaceViewModel(IMarketplaceService marketplace)
    {
        _marketplace = marketplace;
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            _allEntries = (await _marketplace.LoadCatalogAsync()).ToList();
            ApplyFilters();
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchTextChanged(string value)         => ApplyFilters();
    partial void OnSelectedCategoryChanged(string value)   => ApplyFilters();
    partial void OnSortByChanged(string value)             => ApplyFilters();

    private void ApplyFilters()
    {
        IEnumerable<MarketplaceEntry> filtered = _allEntries;

        if (!string.IsNullOrWhiteSpace(SearchText))
            filtered = filtered.Where(e =>
                e.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                e.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                e.Tags.Any(t => t.Contains(SearchText, StringComparison.OrdinalIgnoreCase)));

        if (SelectedCategory != "All")
            filtered = filtered.Where(e => e.Category == SelectedCategory);

        filtered = SortBy switch
        {
            "Highest Rated"  => filtered.OrderByDescending(e => e.AverageRating),
            "Newest"         => filtered.Reverse(),   // catalog ordered oldest-to-newest
            "A-Z"            => filtered.OrderBy(e => e.Name),
            _                => filtered.OrderByDescending(e => e.Downloads)
        };

        Entries.Clear();
        foreach (var e in filtered) Entries.Add(e);
    }

    [RelayCommand]
    public async Task InstallAsync(MarketplaceEntry entry)
    {
        if (entry is null) return;
        IsLoading = true;
        try { await _marketplace.InstallAsync(entry); }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task RateAsync(object? parameter)
    {
        // parameter is a Tuple<MarketplaceEntry, int> passed from code-behind
        if (parameter is not (MarketplaceEntry entry, int rating)) return;
        await _marketplace.RateAsync(entry.Id, rating);
        entry.UserRating = rating;
    }

    [RelayCommand]
    public async Task SubmitAsync(MarketplaceEntry entry)
    {
        if (entry is null) return;
        await _marketplace.GenerateSubmissionAsync(entry);
    }
}
