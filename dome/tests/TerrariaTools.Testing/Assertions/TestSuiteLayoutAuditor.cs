using System.Runtime.CompilerServices;
using Xunit;

namespace TerrariaTools.Testing.Assertions;

public static class TestSuiteLayoutAuditor
{
    private static readonly string[] StructuredDomains =
    [
        "Analysis",
        "Application",
        "Cli",
        "Plan",
        "Reporting",
        "Rewrite",
        "Rules"
    ];

    public static void AssertDomeTestsStructure([CallerFilePath] string sourceFilePath = "")
    {
        var domeTestsRoot = ResolveAncestorDirectory(sourceFilePath, "Dome.Tests");
        var testsRoot = Directory.GetParent(domeTestsRoot)?.FullName
            ?? throw new InvalidOperationException("Unable to resolve tests root.");
        var sharedTestingRoot = Path.Combine(testsRoot, "TerrariaTools.Testing");

        AssertNoRootTestFiles(domeTestsRoot);
        AssertDomeTestingContainsOnlySupportFolders(domeTestsRoot);
        AssertUnitTestsAvoidDirectEnvironmentIo(domeTestsRoot);
        AssertDomeTestingAvoidsGenericHelpers(domeTestsRoot);
        AssertSharedTestingBoundary(sharedTestingRoot);
        AssertApplicationIntegrationStaysWithinApprovedSurface(domeTestsRoot);
        AssertSnapshotsStayOutOfSharedTesting(sharedTestingRoot);
        AssertSnapshotsRemainColocated(domeTestsRoot);
    }

    private static void AssertNoRootTestFiles(string domeTestsRoot)
    {
        var offenders = StructuredDomains
            .SelectMany(domain => Directory.GetFiles(Path.Combine(domeTestsRoot, domain), "*.cs", SearchOption.TopDirectoryOnly))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Root-level test files must be classified into Unit/Slice/Integration/Contracts/Golden. Offenders:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    private static void AssertDomeTestingContainsOnlySupportFolders(string domeTestsRoot)
    {
        var domeTestingRoot = Path.Combine(domeTestsRoot, "DomeTesting");
        if (!Directory.Exists(domeTestingRoot))
        {
            return;
        }

        var children = Directory.GetDirectories(domeTestingRoot)
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["TestBuilders", "TestDoubles"], children);

        var unexpectedFiles = Directory.GetFiles(domeTestingRoot, "*", SearchOption.TopDirectoryOnly);
        Assert.True(
            unexpectedFiles.Length == 0,
            $"DomeTesting should only contain support subdirectories. Unexpected files:{Environment.NewLine}{string.Join(Environment.NewLine, unexpectedFiles)}");
    }

    private static void AssertSnapshotsStayOutOfSharedTesting(string sharedTestingRoot)
    {
        if (!Directory.Exists(sharedTestingRoot))
        {
            return;
        }

        var offenders = Directory.GetFiles(sharedTestingRoot, "*.verified.*", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(sharedTestingRoot, "*.received.*", SearchOption.AllDirectories))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Snapshot baselines must stay next to their tests, not under TerrariaTools.Testing. Offenders:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    private static void AssertSnapshotsRemainColocated(string domeTestsRoot)
    {
        var baselines = Directory.GetFiles(domeTestsRoot, "*.verified.*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var missingTests = baselines
            .Where(path => !HasMatchingTestFile(path))
            .ToArray();

        Assert.True(
            missingTests.Length == 0,
            $"Each verified snapshot must live beside its owning test file. Missing test files:{Environment.NewLine}{string.Join(Environment.NewLine, missingTests)}");
    }

    private static void AssertUnitTestsAvoidDirectEnvironmentIo(string domeTestsRoot)
    {
        var unitRoots = new[]
        {
            Path.Combine(domeTestsRoot, "Application", "Unit"),
            Path.Combine(domeTestsRoot, "Cli", "Unit")
        };
        var bannedPatterns = new[]
        {
            "Path.GetTempPath(",
            "Directory.CreateDirectory(",
            "File.WriteAllText(",
            "File.WriteAllTextAsync(",
            "File.ReadAllText(",
            "File.ReadAllTextAsync("
        };

        var offenders = unitRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => bannedPatterns.Any(pattern => File.ReadAllText(path).Contains(pattern, StringComparison.Ordinal)))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Application/Unit and Cli/Unit tests must not directly use temp-dir or file I/O helpers. Offenders:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    private static void AssertDomeTestingAvoidsGenericHelpers(string domeTestsRoot)
    {
        var domeTestingRoot = Path.Combine(domeTestsRoot, "DomeTesting");
        if (!Directory.Exists(domeTestingRoot))
        {
            return;
        }

        var offenders = Directory.GetFiles(domeTestingRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var content = File.ReadAllText(path);
                return content.Contains("class RecordingProcessCompatibilityRunner", StringComparison.Ordinal) ||
                       content.Contains("class FakeRewriteOutputStore", StringComparison.Ordinal) ||
                       content.Contains("class FakeArtifactEmissionService", StringComparison.Ordinal);
            })
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Generic recording/store helpers must live in TerrariaTools.Testing, not DomeTesting. Offenders:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    private static void AssertSharedTestingBoundary(string sharedTestingRoot)
    {
        if (!Directory.Exists(sharedTestingRoot))
        {
            return;
        }

        var allowedRuntimeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TestSuiteLayoutAuditor.cs",
            "RecordingProcessRunner.cs",
            "RuntimeLayoutCompatibilityBuilder.cs"
        };

        var offenders = Directory.GetFiles(sharedTestingRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var fileName = Path.GetFileName(path);
                if (allowedRuntimeFiles.Contains(fileName))
                {
                    return false;
                }

                var content = File.ReadAllText(path);
                return content.Contains("ITerrariaRuntime", StringComparison.Ordinal) ||
                       content.Contains("TerrariaRuntimeShadow", StringComparison.Ordinal) ||
                       content.Contains("ShadowClosurePlan", StringComparison.Ordinal) ||
                       content.Contains("IShadow", StringComparison.Ordinal);
            })
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"TerrariaTools.Testing should not accumulate runtime/shadow-specific helpers beyond the approved seam files. Offenders:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    private static void AssertApplicationIntegrationStaysWithinApprovedSurface(string domeTestsRoot)
    {
        var applicationIntegrationRoot = Path.Combine(domeTestsRoot, "Application", "Integration");
        if (!Directory.Exists(applicationIntegrationRoot))
        {
            return;
        }

        var allowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DomeApplicationTests.cs",
            "TerrariaRuntimeApplicationTests.cs",
            "TerrariaRuntimeEnvironmentBuilderTests.cs",
            "TerrariaRuntimeShadowExtractionApplicationTests.cs"
        };

        var offenders = Directory.GetFiles(applicationIntegrationRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !allowedFiles.Contains(Path.GetFileName(path)))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Application/Integration should stay a small approved end-to-end surface. Offenders:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    private static bool HasMatchingTestFile(string baselinePath)
    {
        var directory = Path.GetDirectoryName(baselinePath)
            ?? throw new InvalidOperationException("Baseline path must have a directory.");
        var fileName = Path.GetFileName(baselinePath);
        var marker = ".verified.";
        var markerIndex = fileName.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex <= 0)
        {
            return false;
        }

        var expectedTestPrefix = fileName[..markerIndex];
        return Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Any(testFileName =>
                string.Equals(testFileName, expectedTestPrefix, StringComparison.Ordinal) ||
                expectedTestPrefix.StartsWith($"{testFileName}.", StringComparison.Ordinal));
    }

    private static string ResolveAncestorDirectory(string sourceFilePath, string expectedName)
    {
        var directory = Path.GetDirectoryName(sourceFilePath);
        while (directory is not null && !string.Equals(Path.GetFileName(directory), expectedName, StringComparison.Ordinal))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return directory ?? throw new InvalidOperationException($"Unable to locate '{expectedName}' from '{sourceFilePath}'.");
    }
}

