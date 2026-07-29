using RoslynPrototype.Testing.TestCodeSet.Cpg;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

public sealed class GeneratedCSharpFixtureTests
{
  [Fact]
  public void Create_TwoFileReferenceSeed_ProducesPrimaryAndSupportSources()
  {
    var fixture = GeneratedCSharpFixture.Create(8);

    Assert.Equal(["Generated.cs", "Support.cs"], fixture.Files.Keys.OrderBy(key => key, StringComparer.Ordinal));
    Assert.Contains("Support.Add", fixture.PrimarySource, StringComparison.Ordinal);
    Assert.Contains("class Support", fixture.Files["Support.cs"], StringComparison.Ordinal);
  }
}
