using RoslynPrototype.Testing.TestInfrastructure;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

public sealed class CpgSnapshotNormalizerTests
{
  [Fact]
  public void Normalize_UnorderedVolatileValues_ReturnsStableRepresentation()
  {
    var normalized = CpgSnapshotNormalizer.Normalize(new[]
    {
      "elapsedMs=14 node=B path=C:\\temp\\run-1",
      "node=A ts=2026-07-23T03:00:00Z",
    });

    Assert.Equal<string>(new[] { "node=A", "node=B" }, normalized);
  }
}
