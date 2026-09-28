// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.IO;
using Xunit;

namespace GoGoGadgetHardwareMonitor.Tests;

/// <summary>
/// A minimal regression guard for one of this fork's stated obligations
/// (see CHANGES.md / CONTRIBUTING.md "License"): every .cs file in the app
/// project must keep its MPL-2.0 header. This is deliberately simple and
/// dependency-free - it's the first thing in this project, meant as a
/// scaffold for real gadget-logic tests (settings, threshold resolution,
/// etc.) to join as those features land in later phases.
/// </summary>
public class LicenseHeaderTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LibreHardwareMonitor.sln")))
            dir = dir.Parent;

        return dir?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repo root (LibreHardwareMonitor.sln) from the test output directory.");
    }

    public static IEnumerable<object[]> SourceFiles()
    {
        string projectDir = Path.Combine(RepoRoot, "LibreHardwareMonitor.Windows.Forms");

        foreach (string file in Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            yield return new object[] { file };
        }
    }

    [Theory]
    [MemberData(nameof(SourceFiles))]
    public void SourceFile_KeepsMplLicenseHeader(string path)
    {
        string text = File.ReadAllText(path);
        Assert.Contains("Mozilla Public License", text);
    }
}
