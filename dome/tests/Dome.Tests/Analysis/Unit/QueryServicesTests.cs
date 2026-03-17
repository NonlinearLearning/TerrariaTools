using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;
using Xunit;

namespace TerrariaTools.Dome.Tests.Analysis;

public sealed class QueryServicesTests
{
    [Fact]
    public void InheritanceQueryService_ReturnsTrueOnlyForConfiguredIds()
    {
        var service = new InheritanceQueryService(["Sample.Player.Run()"], ["Sample.Player.Value.get"], ["Sample.Player"]);

        Assert.True(service.IsOverrideMember("Sample.Player.Run()"));
        Assert.False(service.IsOverrideMember("Sample.Player.Other()"));
        Assert.True(service.ImplementsInterfaceMember("Sample.Player.Value.get"));
        Assert.False(service.ImplementsInterfaceMember("Sample.Player.Run()"));
        Assert.True(service.IsInInheritanceChain("Sample.Player"));
        Assert.False(service.IsInInheritanceChain("Sample.Other"));
    }

    [Fact]
    public void ReferenceQueryService_HasReferences_WhenAnyLookupContainsId()
    {
        var service = new ReferenceQueryService(
            new Dictionary<string, HashSet<MemberId>>(StringComparer.Ordinal) { ["member"] = [new MemberId("Sample.A.Run()")] },
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal),
            new Dictionary<string, HashSet<MemberId>>(StringComparer.Ordinal),
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal) { ["type"] = ["Sample.Type"] });

        Assert.True(service.HasReferences("member"));
        Assert.True(service.HasReferences("type"));
        Assert.False(service.HasReferences("missing"));
    }

    [Fact]
    public void ReferenceQueryService_GetReferencingFunctions_ReturnsSortedUnionFromMemberAndTypeLookups()
    {
        var service = new ReferenceQueryService(
            new Dictionary<string, HashSet<MemberId>>(StringComparer.Ordinal)
            {
                ["symbol"] = [new MemberId("B.Run()"), new MemberId("A.Run()")]
            },
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal),
            new Dictionary<string, HashSet<MemberId>>(StringComparer.Ordinal)
            {
                ["symbol"] = [new MemberId("C.Run()"), new MemberId("A.Run()")]
            },
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal));

        var functions = service.GetReferencingFunctions("symbol");

        Assert.Equal(["A.Run()", "B.Run()", "C.Run()"], functions.Select(item => item.Value).ToArray());
    }

    [Fact]
    public void ReferenceQueryService_GetReferencingTypes_ReturnsSortedUnionFromMemberAndTypeLookups()
    {
        var service = new ReferenceQueryService(
            new Dictionary<string, HashSet<MemberId>>(StringComparer.Ordinal),
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
            {
                ["symbol"] = ["B.Type", "A.Type"]
            },
            new Dictionary<string, HashSet<MemberId>>(StringComparer.Ordinal),
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
            {
                ["symbol"] = ["C.Type", "A.Type"]
            });

        var types = service.GetReferencingTypes("symbol");

        Assert.Equal(["A.Type", "B.Type", "C.Type"], types);
    }

    [Fact]
    public void ReferenceQueryService_MissingIds_ReturnEmptyArrays()
    {
        var service = new ReferenceQueryService(
            new Dictionary<string, HashSet<MemberId>>(StringComparer.Ordinal),
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal),
            new Dictionary<string, HashSet<MemberId>>(StringComparer.Ordinal),
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal));

        Assert.Empty(service.GetReferencingFunctions("missing"));
        Assert.Empty(service.GetReferencingTypes("missing"));
    }
}
