using Optimizer.WinUI.Models.Plugins;

namespace Optimizer.WinUI.Services.Plugins;

/// <summary>Result of a manifest parse + validation attempt.</summary>
public record ManifestParseResult(bool Success, OptimizationManifest? Manifest, IReadOnlyList<string> Errors);

/// <summary>
/// Parses and validates optimization manifests from YAML or JSON sources.
/// </summary>
public interface IManifestParser
{
    /// <summary>Deserialises YAML text and validates the manifest.</summary>
    ManifestParseResult ParseYaml(string yaml);

    /// <summary>Deserialises JSON text and validates the manifest.</summary>
    ManifestParseResult ParseJson(string json);

    /// <summary>
    /// Reads <paramref name="path"/> from disk and delegates to ParseYaml or ParseJson
    /// based on file extension (.yaml/.yml → YAML, .json → JSON).
    /// </summary>
    ManifestParseResult ParseFile(string path);

    /// <summary>
    /// Validates a pre-parsed manifest and returns a list of human-readable errors.
    /// An empty list means the manifest is valid.
    /// </summary>
    IReadOnlyList<string> Validate(OptimizationManifest manifest);
}
