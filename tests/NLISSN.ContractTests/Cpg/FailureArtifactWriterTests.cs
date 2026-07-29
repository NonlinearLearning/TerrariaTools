using RoslynPrototype.Testing.TestCodeSet.Cpg;
using RoslynPrototype.Testing.TestInfrastructure;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

public sealed class FailureArtifactWriterTests
{
  [Fact]
  public void Write_GeneratedFixture_WritesReplayableInputsAndSnapshot()
  {
    var root = Path.Combine(Path.GetTempPath(), "failure-artifact", Guid.NewGuid().ToString("N"));
    try
    {
      var fixture = GeneratedCSharpFixture.Create(7);
      var artifact = FailureArtifactWriter.Write(
        root,
        fixture,
        new Dictionary<string, string> { ["dop"] = "8" },
        new CpgExecutionSnapshot("v1", ["1:Node"], [], [], [], [], [], "", ""));

      Assert.True(File.Exists(Path.Combine(artifact, "inputs", fixture.PrimaryFileName)));
      Assert.True(File.Exists(Path.Combine(artifact, "options.json")));
      Assert.True(File.Exists(Path.Combine(artifact, "snapshot.json")));
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public void Write_GeneratedFixture_WritesNamedExecutionSnapshotsAndReplayMetadata()
  {
    var root = Path.Combine(Path.GetTempPath(), "failure-artifact", Guid.NewGuid().ToString("N"));
    try
    {
      var fixture = GeneratedCSharpFixture.Create(7);
      var snapshots = new Dictionary<string, CpgExecutionSnapshot>(StringComparer.Ordinal)
      {
        ["serial"] = new("v1", ["serial-node"], [], [], [], [], [], "", ""),
        ["parallel"] = new("v1", ["parallel-node"], [], [], [], [], [], "", ""),
        ["persisted"] = new("v1", ["persisted-node"], [], [], [], [], [], "", ""),
      };

      var artifact = FailureArtifactWriter.Write(
        root,
        fixture,
        new Dictionary<string, string>
        {
          ["dop"] = "8",
          ["replay"] = "12345,67891",
        },
        snapshots);

      Assert.True(File.Exists(Path.Combine(artifact, "replay.json")));
      Assert.True(File.Exists(Path.Combine(artifact, "snapshots", "serial.json")));
      Assert.True(File.Exists(Path.Combine(artifact, "snapshots", "parallel.json")));
      Assert.True(File.Exists(Path.Combine(artifact, "snapshots", "persisted.json")));
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }
}
