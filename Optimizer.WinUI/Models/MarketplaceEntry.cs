using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Optimizer.WinUI.Models;

public partial class MarketplaceEntry : ObservableObject
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public int Downloads { get; set; }
    public double AverageRating { get; set; }
    public int RatingCount { get; set; }
    public bool Verified { get; set; }
    public List<string> Optimizations { get; set; } = [];

    [ObservableProperty] private int userRating;   // 0-5, set if user rated locally
    [ObservableProperty] private bool isInstalled;

    public string DownloadsText => Downloads switch
    {
        >= 1_000_000 => string.Create(CultureInfo.InvariantCulture, $"{Downloads / 1_000_000.0:F1}M"),
        >= 1_000     => string.Create(CultureInfo.InvariantCulture, $"{Downloads / 1_000.0:F1}K"),
        _            => Downloads.ToString(CultureInfo.InvariantCulture)
    };

    public string RatingText => string.Create(CultureInfo.InvariantCulture, $"{AverageRating:F1} ({RatingCount})");
    public string TagsText   => string.Join(" • ", Tags);
}
