using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Architecture invariants enforced by source walk. These catch *pattern drift* —
/// the moment a new caller bypasses an established convention — at test time, before
/// it surfaces as user-visible weirdness (Bug C: AI navigated to standalone pages).
///
/// These tests are intentionally NOT runtime/behavioral. They grep source files. The
/// trade-off: brittle to source moves, but cheap to add and impossible to ignore once
/// they fail.
/// </summary>
public class NavigationInvariantTests
{
    /// <summary>
    /// Per CLAUDE.md: code-behinds and ViewModels must use
    /// <c>App.GetService&lt;IPageNavigator&gt;().NavigateTo(tag)</c>. Calling
    /// <c>NavigationService.NavigateTo(typeof(X))</c> directly bypasses the hub router
    /// and dumps the user on a standalone page outside its hub Segmented sub-nav.
    /// The only legitimate caller is the shell (<c>MainWindow.xaml.cs</c>).
    /// </summary>
    [Fact]
    public void Only_MainWindow_Calls_NavigationService_NavigateTo_Directly()
    {
        var sourceRoot = FindSourceRoot("Optimizer.WinUI");
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // The shell is allowed — it's the lowest-level navigation primitive.
            "MainWindow.xaml.cs",
        };

        // Match: NavigationService.NavigateTo(typeof(<anything>))
        // Whitespace-tolerant so reformatting doesn't break the assertion.
        var forbidden = new Regex(@"NavigationService\.NavigateTo\s*\(\s*typeof\s*\(", RegexOptions.Compiled);

        var violations = EnumerateCsFiles(sourceRoot)
            .Where(f => !allowed.Contains(Path.GetFileName(f)))
            .Where(f => forbidden.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(sourceRoot, f))
            .OrderBy(p => p)
            .ToList();

        Assert.True(violations.Count == 0,
            "Files calling NavigationService.NavigateTo(typeof(...)) outside the allowlist:\n" +
            string.Join("\n", violations.Select(v => $"  - {v}")) + "\n" +
            "Route through `App.GetService<IPageNavigator>().NavigateTo(tag)` instead — " +
            "this keeps the slim rail in sync and lands inside the hub's Segmented sub-nav.");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static IEnumerable<string> EnumerateCsFiles(string root)
    {
        // Skip build outputs and generated XAML codebehinds (*.g.cs).
        var skip = new[] { Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar,
                           Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar };
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !skip.Any(s => p.Contains(s, StringComparison.OrdinalIgnoreCase)))
            .Where(p => !p.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Walk up from the test assembly to find the source root of the named project.</summary>
    private static string FindSourceRoot(string projectName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, projectName);
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, $"{projectName}.csproj")))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            $"Could not locate '{projectName}/' source root walking up from {AppContext.BaseDirectory}. " +
            "This invariant test runs against the source tree, not the assembly.");
    }
}
