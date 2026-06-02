using Optimizer.Server.Data.Entities;

namespace Optimizer.Server.Models;

public record PluginListingDto(
    string PluginId,
    string Name,
    string AuthorDisplayName,
    string Description,
    string Category,
    int Downloads,
    double AverageRating,
    int RatingCount,
    bool Verified);

public record PluginBrowseResponse(
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<PluginListingDto> Listings);

public record PluginDetailDto(
    string PluginId,
    string Name,
    string AuthorDisplayName,
    string Description,
    string Category,
    string ManifestYaml,
    string? Signature,
    bool Verified,
    int Downloads,
    double AverageRating,
    int RatingCount);

public record SubmitPluginRequest(string ManifestYaml);
public record SubmitPluginResponse(Guid Id, ListingStatusDto Status);

public record PublicKeyResponse(string PublicKey, bool IsProductionKey);
