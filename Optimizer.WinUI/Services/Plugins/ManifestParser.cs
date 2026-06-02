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

    // Scheduled-task name: word chars (no whitespace except plain space ' '), backslash (path
    // separator), forward-slash, dot, hyphen — anything else (especially '"' or newline/control
    // chars) is rejected to prevent schtasks injection.
    // Example valid: "Microsoft\Windows\Defrag\ScheduledDefrag"
    private static readonly Regex TaskNamePattern = new(@"^[\w \.\-\\/]+$", RegexOptions.Compiled);

    // Shell metacharacters that must never appear in powercfg arguments.
    // A manifest could otherwise inject: /h off & calc.exe, /h off; malware, etc.
    private static readonly char[] PowerCfgForbiddenChars = ['&', '|', ';', '>', '<', '`', '\n', '\r'];

    /// <summary>
    /// Allowed powercfg sub-command first tokens (the part after "powercfg").
    /// Only well-known safe sub-commands are permitted.
    ///
    /// Allowlist:
    ///   /h [on|off]                    — hibernate
    ///   /hibernate [on|off]            — hibernate (long form)
    ///   /setactive {GUID}              — set active power scheme
    ///   /change sleep-timeout-ac N    — change power setting
    ///   /change sleep-timeout-dc N
    ///   /change monitor-timeout-ac N
    ///   /change monitor-timeout-dc N
    ///   /change disk-timeout-ac N
    ///   /change disk-timeout-dc N
    ///   /change hibernate-timeout-ac N
    ///   /change hibernate-timeout-dc N
    ///   /change standby-timeout-ac N
    ///   /change standby-timeout-dc N
    /// </summary>
    private static readonly HashSet<string> AllowedPowerCfgFirstTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "/h", "/hibernate", "/setactive", "/change"
    };

    private static readonly Regex GuidPattern =
        new(@"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$",
            RegexOptions.Compiled);

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
                        {
                            errors.Add($"{prefix} (powercfg): 'power_cfg_args' is required.");
                        }
                        else
                        {
                            errors.AddRange(ValidatePowerCfgArgs(prefix, c.PowerCfgArgs));
                        }
                        break;

                    case "scheduled-task":
                        if (string.IsNullOrWhiteSpace(c.TaskName))
                            errors.Add($"{prefix} (scheduled-task): 'task_name' is required.");
                        else if (!TaskNamePattern.IsMatch(c.TaskName))
                            errors.Add($"{prefix} (scheduled-task): 'task_name' contains disallowed characters. " +
                                       @"Only word characters, spaces, \, /, ., - are permitted.");
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

    /// <summary>
    /// Validates <paramref name="args"/> as a powercfg argument string.
    ///
    /// Rules:
    ///   1. No shell metacharacters (&amp; | ; &gt; &lt; ` newline).
    ///   2. First token must start with '/' (i.e. be a sub-command flag).
    ///   3. First token must be one of the allowed sub-commands:
    ///        /h, /hibernate, /setactive, /change
    ///   4. For /setactive, the second token must be a valid GUID in braces.
    ///   5. For /h and /hibernate, the second token must be "on" or "off".
    ///   6. For /change, the second token must be a known setting name.
    ///
    /// Returns an enumerable of error strings (empty if valid).
    /// </summary>
    private static IEnumerable<string> ValidatePowerCfgArgs(string prefix, string args)
    {
        var errors = new List<string>();

        // Rule 1: no shell metacharacters
        if (args.IndexOfAny(PowerCfgForbiddenChars) >= 0)
        {
            errors.Add($"{prefix} (powercfg): 'power_cfg_args' contains forbidden characters " +
                       $"(&, |, ;, >, <, `, newline). Value: '{args}'");
            return errors;  // stop early — further checks may be misleading
        }

        var tokens = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            errors.Add($"{prefix} (powercfg): 'power_cfg_args' is blank after trimming.");
            return errors;
        }

        // Rule 2: first token must start with '/'
        if (!tokens[0].StartsWith('/'))
        {
            errors.Add($"{prefix} (powercfg): 'power_cfg_args' must start with a sub-command flag " +
                       $"beginning with '/' (got '{tokens[0]}').");
            return errors;
        }

        // Rule 3: first token must be in the allowlist
        if (!AllowedPowerCfgFirstTokens.Contains(tokens[0]))
        {
            errors.Add($"{prefix} (powercfg): sub-command '{tokens[0]}' is not permitted. " +
                       $"Allowed: {string.Join(", ", AllowedPowerCfgFirstTokens)}.");
            return errors;
        }

        switch (tokens[0].ToLowerInvariant())
        {
            case "/setactive":
                // Rule 4: second token must be a GUID in braces
                if (tokens.Length < 2 || !GuidPattern.IsMatch(tokens[1]))
                    errors.Add($"{prefix} (powercfg): /setactive requires a GUID in braces " +
                               "e.g. {{8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c}}.");
                break;

            case "/h":
            case "/hibernate":
                // Rule 5: second token must be "on" or "off"
                if (tokens.Length < 2 ||
                    (!tokens[1].Equals("on", StringComparison.OrdinalIgnoreCase) &&
                     !tokens[1].Equals("off", StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add($"{prefix} (powercfg): {tokens[0]} requires 'on' or 'off'.");
                }
                break;

            case "/change":
                // Rule 6: second token must be a known setting name
                var allowedSettings = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "sleep-timeout-ac", "sleep-timeout-dc",
                    "monitor-timeout-ac", "monitor-timeout-dc",
                    "disk-timeout-ac", "disk-timeout-dc",
                    "hibernate-timeout-ac", "hibernate-timeout-dc",
                    "standby-timeout-ac", "standby-timeout-dc",
                };
                if (tokens.Length < 2 || !allowedSettings.Contains(tokens[1]))
                {
                    errors.Add($"{prefix} (powercfg): /change requires a known setting name " +
                               $"(e.g. sleep-timeout-ac). Got: '{(tokens.Length >= 2 ? tokens[1] : "<none>")}'.");
                }
                break;
        }

        return errors;
    }
}
