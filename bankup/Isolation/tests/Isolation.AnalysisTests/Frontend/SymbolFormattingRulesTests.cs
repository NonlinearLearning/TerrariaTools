using Domain.Analysis.Engine.Model;
using Xunit;

namespace Isolation.AnalysisTests.Frontend;

public sealed class SymbolFormattingRulesTests
{
    [Fact]
    public void BuildMethodMembers_normalizesTypeNames()
    {
        string fullName = SymbolFormattingRules.BuildMethodFullName(
            "global::Demo.Widget",
            "Run",
            new[] { "global::System.String", "System.Int32" });
        string signature = SymbolFormattingRules.BuildMethodSignature(
            "global::System.Boolean",
            new[] { "global::System.String", "System.Int32" });

        Assert.Equal("Demo.Widget.Run(System.String, System.Int32)", fullName);
        Assert.Equal("System.Boolean (System.String, System.Int32)", signature);
    }

    [Fact]
    public void BuildSymbolId_usesStableRulePatterns()
    {
        SymbolId symbolId = SymbolFormattingRules.BuildSymbolId(new SymbolIdentityDescriptor(
            SymbolIdentityKind.Field,
            "Count",
            "global::Demo.Widget",
            Array.Empty<string>(),
            "42"));

        Assert.Equal("field:Demo.Widget.Count", symbolId.Value);
    }
}
