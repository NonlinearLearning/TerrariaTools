using System.Diagnostics;
using NLISSN.Performance;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class ExternalDiagnosticIntegrationTests : IDisposable
{
    private readonly string _artifactRoot = Path.Combine(
      Path.GetTempPath(),
      $"nlissn-external-diagnostic-tests-{Guid.NewGuid():N}");

    public ExternalDiagnosticIntegrationTests()
    {
        Directory.CreateDirectory(_artifactRoot);
    }

    [Fact]
    public void AttachmentManifest_RoundTripsRunAndStageAssociation()
    {
        var runId = "run-representative-001";
        var stageId = "CPG.Syntax";
        var manifest = new PerformanceDiagnosticAttachment(
          runId,
          stageId,
          "profile",
          new[]
          {
              new ExternalDiagnosticAttachment(
                "dotnet-trace",
                "dotnet-trace 10.0.0",
                "dotnet-trace collect --process-id 42",
                DateTimeOffset.Parse("2026-09-11T01:02:03Z"),
                DateTimeOffset.Parse("2026-09-11T01:02:04Z"),
                0,
                "attachments/trace.nettrace",
                ExternalDiagnosticAttachmentState.Available),
              new ExternalDiagnosticAttachment(
                "dotnet-counters",
                "dotnet-counters 10.0.0",
                "dotnet-counters collect --process-id 42",
                DateTimeOffset.Parse("2026-09-11T01:02:03Z"),
                DateTimeOffset.Parse("2026-09-11T01:02:04Z"),
                0,
                "attachments/counters.csv",
                ExternalDiagnosticAttachmentState.Available),
              new ExternalDiagnosticAttachment(
                "dotnet-gcdump",
                null,
                null,
                null,
                null,
                null,
                null,
                ExternalDiagnosticAttachmentState.Unavailable)
          });
        var manifestPath = Path.Combine(_artifactRoot, "attachments.json");

        PerformanceDiagnosticAttachment.Write(manifestPath, manifest);

        var loaded = PerformanceDiagnosticAttachment.Read(manifestPath);

        Assert.Equal(runId, loaded.RunId);
        Assert.Equal(stageId, loaded.StageId);
        Assert.Equal("profile", loaded.Mode);
        Assert.Equal(
          new[] { "dotnet-counters", "dotnet-gcdump", "dotnet-trace" },
          loaded.Attachments.Select(attachment => attachment.Tool).ToArray());
        Assert.All(loaded.Attachments, attachment =>
        {
            Assert.Equal(runId, loaded.RunId);
            Assert.Equal(stageId, loaded.StageId);
            Assert.True(attachment.RelativeArtifactPath is null ||
              !Path.IsPathRooted(attachment.RelativeArtifactPath));
        });
    }

    [ExternalDiagnosticFact]
    public void RepresentativeProfileTools_WriteAssociationsForOneFixture()
    {
        var runId = $"run-{Guid.NewGuid():N}";
        var stageId = "Rule.Propagate";
        var outputDirectory = Path.Combine(_artifactRoot, "attachments");
        Directory.CreateDirectory(outputDirectory);
        var processId = Environment.ProcessId;
        var policies = ExternalDiagnosticToolPolicy.CreateDefaultProfilePolicies(
          processId,
          outputDirectory);
        var attachments = new List<ExternalDiagnosticAttachment>();

        foreach (var policy in policies)
        {
            var workload = Task.Run(RunRepresentativeFixture);
            try
            {
                attachments.Add(RunTool(policy));
            }
            finally
            {
                workload.GetAwaiter().GetResult();
            }
        }

        var manifest = new PerformanceDiagnosticAttachment(
          runId,
          stageId,
          "profile",
          attachments);
        var manifestPath = Path.Combine(_artifactRoot, "profile-manifest.json");
        PerformanceDiagnosticAttachment.Write(manifestPath, manifest);

        var loaded = PerformanceDiagnosticAttachment.Read(manifestPath);

        Assert.Equal(runId, loaded.RunId);
        Assert.Equal(stageId, loaded.StageId);
        Assert.All(loaded.Attachments, attachment =>
        {
            Assert.Equal(ExternalDiagnosticAttachmentState.Available, attachment.State);
            Assert.False(string.IsNullOrWhiteSpace(attachment.Version));
            Assert.NotNull(attachment.RelativeArtifactPath);
            Assert.False(Path.IsPathRooted(attachment.RelativeArtifactPath));
            Assert.True(File.Exists(Path.Combine(_artifactRoot, attachment.RelativeArtifactPath)));
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_artifactRoot))
        {
            Directory.Delete(_artifactRoot, recursive: true);
        }
    }

    private static ExternalDiagnosticAttachment RunTool(
      ExternalDiagnosticToolPolicy policy)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = policy.Executable,
            Arguments = policy.Arguments,
            WorkingDirectory = policy.OutputDirectory,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException($"Could not start {policy.Tool}.");
        using (process)
        {
            if (!process.WaitForExit((int)policy.Timeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                throw new TimeoutException($"{policy.Tool} exceeded {policy.Timeout}.");
            }

            var stderr = process.StandardError.ReadToEnd();
            _ = process.StandardOutput.ReadToEnd();
            Assert.True(process.ExitCode == 0, $"{policy.Tool} failed: {stderr}");
        }

        return new ExternalDiagnosticAttachment(
          policy.Tool,
          policy.Version,
          policy.DisplayCommand,
          startedAt,
          DateTimeOffset.UtcNow,
          0,
          Path.GetRelativePath(policy.ArtifactRoot, policy.ArtifactPath),
          ExternalDiagnosticAttachmentState.Available);
    }

    private static void RunRepresentativeFixture()
    {
        const string source = """
          public sealed class ProfileFixture
          {
              public int Compute(int input)
              {
                  var value = input * 17;
                  return value > 100 ? value - 3 : value + 3;
              }
          }
          """;
        var tree = CSharpSyntaxTree.ParseText(source, path: "ProfileFixture.cs");
        var checksum = 0;
        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 3;
        while (Stopwatch.GetTimestamp() < deadline)
        {
            checksum = unchecked(checksum + tree.GetRoot().DescendantNodes().Count());
            _ = new byte[1024];
        }

        GC.KeepAlive(checksum);
    }
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class ExternalDiagnosticFactAttribute : FactAttribute
{
    public ExternalDiagnosticFactAttribute()
    {
        if (!string.Equals(
              Environment.GetEnvironmentVariable("NLISSN_RUN_EXTERNAL_DIAGNOSTIC_TESTS"),
              "1",
              StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Requires NLISSN_RUN_EXTERNAL_DIAGNOSTIC_TESTS=1.";
            return;
        }

        var missingTools = ExternalDiagnosticToolPolicy.RequiredTools
          .Where(tool => !ExternalDiagnosticToolPolicy.IsAvailable(tool))
          .ToArray();
        if (missingTools.Length > 0)
        {
            Skip = $"Required external diagnostic tools are unavailable: {string.Join(", ", missingTools)}.";
        }
    }
}
