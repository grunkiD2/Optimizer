using System;
using System.Collections.Generic;
using Optimizer.WinUI.Models.Plugins;
using Optimizer.WinUI.Services.Plugins;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Unit tests for ManifestParser (B1): YAML + JSON parsing, underscore mapping,
/// and all validation rules.
/// </summary>
public class ManifestParserTests
{
    private readonly ManifestParser _parser = new();

    // ── Helper YAML / JSON builders ────────────────────────────────────────────

    private static string MinimalValidYaml(
        string id = "community-disable-cortana",
        string name = "Disable Cortana",
        string category = "Privacy",
        int manifestVersion = 1,
        string extraChanges = "") =>
        $"""
        manifest_version: {manifestVersion}
        id: {id}
        name: {name}
        description: Disables Cortana.
        author: Tester
        category: {category}
        requires_admin: true
        requires_restart: false
        reversible: true
        changes:
          - type: registry
            path: HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search
            value: AllowCortana
            value_type: dword
            apply: "0"
            revert: "1"
        {extraChanges}
        """;

    private static string MinimalValidJson(string id = "community-disable-cortana") =>
        $$"""
        {
          "manifest_version": 1,
          "id": "{{id}}",
          "name": "Disable Cortana",
          "description": "Disables Cortana.",
          "author": "Tester",
          "category": "Privacy",
          "requires_admin": true,
          "reversible": true,
          "changes": [
            {
              "type": "registry",
              "path": "HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\Windows Search",
              "value": "AllowCortana",
              "value_type": "dword",
              "apply": "0",
              "revert": "1"
            }
          ]
        }
        """;

    // ── Test 1: Valid YAML → success, correct fields ───────────────────────────

    [Fact]
    public void ParseYaml_ValidManifest_ReturnsSuccessWithCorrectFields()
    {
        var result = _parser.ParseYaml(MinimalValidYaml());

        Assert.True(result.Success);
        Assert.NotNull(result.Manifest);
        Assert.Empty(result.Errors);
        Assert.Equal("community-disable-cortana", result.Manifest!.Id);
        Assert.Equal("Disable Cortana", result.Manifest.Name);
        Assert.Equal("Privacy", result.Manifest.Category);
        Assert.Equal(1, result.Manifest.ManifestVersion);
        Assert.Single(result.Manifest.Changes);
    }

    // ── Test 2: Valid JSON → success ──────────────────────────────────────────

    [Fact]
    public void ParseJson_ValidManifest_ReturnsSuccess()
    {
        var result = _parser.ParseJson(MinimalValidJson());

        Assert.True(result.Success);
        Assert.NotNull(result.Manifest);
        Assert.Equal("community-disable-cortana", result.Manifest!.Id);
        Assert.Single(result.Manifest.Changes);
    }

    // ── Test 3: Underscore YAML keys map to PascalCase properties ─────────────

    [Fact]
    public void ParseYaml_SnakeCaseKeys_MapToPascalCaseProperties()
    {
        var yaml = MinimalValidYaml();
        // The YAML has requires_admin: true and requires_restart: false
        var result = _parser.ParseYaml(yaml);

        Assert.True(result.Success);
        Assert.True(result.Manifest!.RequiresAdmin);
        Assert.False(result.Manifest.RequiresRestart);
        Assert.True(result.Manifest.Reversible);
    }

    // ── Test 4: Invalid YAML syntax → failure, no throw ──────────────────────

    [Fact]
    public void ParseYaml_InvalidSyntax_ReturnsFail_NoThrow()
    {
        var result = _parser.ParseYaml(": bad: yaml: [[[");

        Assert.False(result.Success);
        Assert.Null(result.Manifest);
        Assert.NotEmpty(result.Errors);
    }

    // ── Test 5: Missing Id → validation error ─────────────────────────────────

    [Fact]
    public void Validate_MissingId_ReturnsError()
    {
        var manifest = new OptimizationManifest
        {
            ManifestVersion = 1,
            Id = "",
            Name = "Test",
            Category = "System",
            Changes = { new ManifestChange { Type = "registry", Path = @"HKCU\Software\Test", Value = "Foo", Apply = "1" } }
        };

        var errors = _parser.Validate(manifest);
        Assert.Contains(errors, e => e.Contains("'id' is required", StringComparison.Ordinal));
    }

    // ── Test 6: Bad Id format (uppercase/spaces) → validation error ──────────

    [Fact]
    public void Validate_BadIdFormat_ReturnsError()
    {
        var manifest = new OptimizationManifest
        {
            ManifestVersion = 1,
            Id = "BadId With Spaces",
            Name = "Test",
            Category = "System",
            Changes = { new ManifestChange { Type = "registry", Path = @"HKCU\Software\Test", Value = "Foo", Apply = "1" } }
        };

        var errors = _parser.Validate(manifest);
        Assert.Contains(errors, e => e.Contains("lowercase slug", StringComparison.Ordinal));
    }

    // ── Test 7: Name too long → validation error ──────────────────────────────

    [Fact]
    public void Validate_NameTooLong_ReturnsError()
    {
        var manifest = new OptimizationManifest
        {
            ManifestVersion = 1,
            Id = "test-id-valid",
            Name = new string('X', 81),
            Category = "System",
            Changes = { new ManifestChange { Type = "registry", Path = @"HKCU\Software\Test", Value = "Foo", Apply = "1" } }
        };

        var errors = _parser.Validate(manifest);
        Assert.Contains(errors, e => e.Contains("'name' must be 80 characters", StringComparison.Ordinal));
    }

    // ── Test 8: Unknown category → validation error ───────────────────────────

    [Fact]
    public void Validate_UnknownCategory_ReturnsError()
    {
        var manifest = new OptimizationManifest
        {
            ManifestVersion = 1,
            Id = "test-id-valid",
            Name = "Test",
            Category = "Miscellaneous",
            Changes = { new ManifestChange { Type = "registry", Path = @"HKCU\Software\Test", Value = "Foo", Apply = "1" } }
        };

        var errors = _parser.Validate(manifest);
        Assert.Contains(errors, e => e.Contains("'category' must be one of", StringComparison.Ordinal));
    }

    // ── Test 9: Zero changes → validation error ───────────────────────────────

    [Fact]
    public void Validate_ZeroChanges_ReturnsError()
    {
        var manifest = new OptimizationManifest
        {
            ManifestVersion = 1,
            Id = "test-id-valid",
            Name = "Test",
            Category = "System",
            Changes = []
        };

        var errors = _parser.Validate(manifest);
        Assert.Contains(errors, e => e.Contains("At least one change", StringComparison.Ordinal));
    }

    // ── Test 10: Registry change missing required field → validation error ─────

    [Fact]
    public void Validate_RegistryChangeMissingPath_ReturnsError()
    {
        var manifest = new OptimizationManifest
        {
            ManifestVersion = 1,
            Id = "test-id-valid",
            Name = "Test",
            Category = "System",
            Changes =
            {
                new ManifestChange
                {
                    Type = "registry",
                    Path = null,  // missing
                    Value = "Foo",
                    Apply = "1"
                }
            }
        };

        var errors = _parser.Validate(manifest);
        Assert.Contains(errors, e => e.Contains("'path' is required", StringComparison.Ordinal));
    }

    // ── Test 11: Unknown change type → validation error ───────────────────────

    [Fact]
    public void Validate_UnknownChangeType_ReturnsError()
    {
        var manifest = new OptimizationManifest
        {
            ManifestVersion = 1,
            Id = "test-id-valid",
            Name = "Test",
            Category = "System",
            Changes = { new ManifestChange { Type = "firewall-rule" } }
        };

        var errors = _parser.Validate(manifest);
        Assert.Contains(errors, e => e.Contains("unknown change type", StringComparison.Ordinal));
    }

    // ── Test 12: ManifestVersion = 2 → rejected ───────────────────────────────

    [Fact]
    public void ParseYaml_ManifestVersion2_ReturnsFail()
    {
        var yaml = MinimalValidYaml(manifestVersion: 2);
        var result = _parser.ParseYaml(yaml);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("Unsupported manifest_version", StringComparison.Ordinal));
    }
}
