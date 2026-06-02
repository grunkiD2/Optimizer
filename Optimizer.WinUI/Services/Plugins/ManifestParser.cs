using System.Text.Json;
using System.Text.RegularExpressions;
using Optimizer.WinUI.Models.Plugins;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Optimizer.WinUI.Services.Plugins;

/// <summary>
/// Parses and validates optimization manifests from YAML (snake_case keys) or JSON.
/// </summary>
public sealed class ManifestParser : IManifestParser
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private static readonly int SupportedManifestVersion = 1;

    private static readonly HashSet<string> AllowedCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Performance", "Network", "Storage", "System", "Privacy"
    };

    private static readonly HashSet<string> AllowedChangeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "registry", "service", "file", "powercfg", "scheduled-task"
    };

    // Lowercase slug: starts with [a-z0-9], then 2–63 more [a-z0-9-] chars → total 3–64
    private static readonly Regex IdPattern = new(@"^[a-z0-9][a-z0-9-]{2,63}$", RegexOptions.Compiled);

    // ── YAML deserialiser (snake_case → PascalCase) ───────────────────────────

    private static readonly IDeserializer YamlDeserialiser = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    // ── JSON options (snake_case → PascalCase) ────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        // SnakeCaseLower maps "manifest_version" → ManifestVersion etc.
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    // ── IManifestParser ───────────────────────────────────────────────────────

    public ManifestParseResult ParseYaml(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            return Fail("YAML content is empty.");

        try
        {
            var manifest = YamlDeserialiser.Deserialize<OptimizationManifest>(yaml);
            if (manifest == null)
                return Fail("YAML deserialised to null.");

            var errors = Validate(manifest);
            return errors.Count == 0
                ? new ManifestParseResult(true, manifest, Array.Empty<string>())
                : new ManifestParseResult(false, null, errors);
        }
        catch (Exception ex)
        {
            return Fail($"YAML parse error: {ex.Message}");
        }
    }

    public ManifestParseResult ParseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Fail("JSON content is empty.");

        try
        {
            var manifest = JsonSerializer.Deserialize<OptimizationManifest>(json, JsonOptions);
            if (manifest == null)
                return Fail("JSON deserialised to null.");

            var errors = Validate(manifest);
            return errors.Count == 0
                ? new ManifestParseResult(true, manifest, Array.Empty<string>())
                : new ManifestParseResult(false, null, errors);
        }
        catch (Exception ex)
        {
            return Fail($"JSON parse error: {ex.Message}");
        }
    }

    public ManifestParseResult ParseFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Fail("File path is empty.");

        try
        {
            if (!File.Exists(path))
                return Fail($"File not found: {path}");

            var ext = Path.GetExtension(path).ToLowerInvariant();
            var text = File.ReadAllText(path);

            return ext switch
            {
                ".yaml" or ".yml" => ParseYaml(text),
                ".json" => ParseJson(text),
                _ => Fail($"Unsupported manifest file extension '{ext}'. Use .yaml, .yml, or .json.")
            };
        }
        catch (Exception ex)
        {
            return Fail($"Failed to read file '{path}': {ex.Message}");
        }
    }

    public IReadOnlyList<string> Validate(OptimizationManifest manifest)
    {
        var errors = new List<string>();

        // ── ManifestVersion ──────────────────────────────────────────────────
        if (manifest.ManifestVersion != SupportedManifestVersion)
            errors.Add($"Unsupported manifest_version '{manifest.ManifestVersion}'. Only version {SupportedManifestVersion} is supported.");

        // ── Id ───────────────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            errors.Add("'id' is required.");
        }
        else if (!IdPattern.IsMatch(manifest.Id))
        {
            errors.Add($"'id' must be a lowercase slug matching ^[a-z0-9][a-z0-9-]{{2,63}}$ (got '{manifest.Id}').");
        }

        // ── Name ─────────────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(manifest.Name))
            errors.Add("'name' is required.");
        else if (manifest.Name.Length > 80)
            errors.Add($"'name' must be 80 characters or fewer (got {manifest.Name.Length}).");

        // ── Description ──────────────────────────────────────────────────────
        if (manifest.Description.Length > 500)
            errors.Add($"'description' must be 500 characters or fewer (got {manifest.Description.Length}).");

        // ── Category ─────────────────────────────────────────────────────────
        if (!AllowedCategories.Contains(manifest.Category))
            errors.Add($"'category' must be one of: {string.Join(", ", AllowedCategories)} (got '{manifest.Category}').");

        // ── Changes ──────────────────────────────────────────────────────────
        if (manifest.Changes == null || manifest.Changes.Count == 0)
        {
            errors.Add("At least one change is required.");
        }
        else
        {
            for (int i = 0; i < manifest.Changes.Count; i++)
            {
                var c = manifest.Changes[i];
                var prefix = $"changes[{i}]";

                if (string.IsNullOrWhiteSpace(c.Type))
                {
                    errors.Add($"{prefix}: 'type' is required.");
                    continue;
                }

                if (!AllowedChangeTypes.Contains(c.Type))
                {
                    errors.Add($"{prefix}: unknown change type '{c.Type}'. Must be one of: {string.Join(", ", AllowedChangeTypes)}.");
                    continue;
                }

                switch (c.Type.ToLowerInvariant())
                {
                    case "registry":
                        if (string.IsNullOrWhiteSpace(c.Path))
                            errors.Add($"{prefix} (registry): 'path' is required.");
                        if (string.IsNullOrWhiteSpace(c.Value))
                            errors.Add($"{prefix} (registry): 'value' is required.");
                        if (string.IsNullOrWhiteSpace(c.Apply))
                            errors.Add($"{prefix} (registry): 'apply' is required.");
                        break;

                    case "service":
                        if (string.IsNullOrWhiteSpace(c.ServiceName))
                            errors.Add($"{prefix} (service): 'service_name' is required.");
                        break;

                    case "file":
                        if (string.IsNullOrWhiteSpace(c.FilePath))
                            errors.Add($"{prefix} (file): 'file_path' is required.");
                        if (string.IsNullOrWhiteSpace(c.FileAction))
                            errors.Add($"{prefix} (file): 'file_action' is required.");
                        break;

                    case "powercfg":
                        if (string.IsNullOrWhiteSpace(c.PowerCfgArgs))
                            errors.Add($"{prefix} (powercfg): 'power_cfg_args' is required.");
                        break;

                    case "scheduled-task":
                        if (string.IsNullOrWhiteSpace(c.TaskName))
                            errors.Add($"{prefix} (scheduled-task): 'task_name' is required.");
                        if (string.IsNullOrWhiteSpace(c.TaskAction))
                            errors.Add($"{prefix} (scheduled-task): 'task_action' is required.");
                        break;
                }
            }
        }

        return errors;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ManifestParseResult Fail(string error)
        => new(false, null, new[] { error });
}
