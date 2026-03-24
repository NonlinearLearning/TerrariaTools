namespace TerrariaTools.Dome.Core.Cpg.Tests;

public sealed class AssemblyMarkerTests
{
    [Fact]
    public void AssemblyMarkerValue_ShouldBeDomeCoreCpg()
    {
        Assert.Equal("Dome.Core.Cpg", AssemblyMarker.Value);
    }
}
