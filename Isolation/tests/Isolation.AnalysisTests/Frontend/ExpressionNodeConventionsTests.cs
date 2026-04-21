using Logic.Analysis.Engine.Frontend;
using Xunit;

namespace Isolation.AnalysisTests.Frontend;

public sealed class ExpressionNodeConventionsTests
{
    [Fact]
    public void BuildLambdaIdentity_usesFallbackValuesWhenResolvedValuesMissing()
    {
        LambdaIdentity identity = ExpressionNodeConventions.BuildLambdaIdentity(
            ordinal: 3,
            fileName: "demo.cs",
            resolvedFullName: null,
            resolvedSignature: null,
            resolvedSymbolId: null,
            operationId: "demo.cs:10:123");

        Assert.Equal("<lambda>3", identity.Name);
        Assert.Equal("demo.cs::<lambda>3", identity.FullName);
        Assert.Equal(FrontendGraphConventions.Unknown, identity.Signature);
        Assert.Equal("lambda:demo.cs:10:123", identity.DeclaredSymbolId);
    }

    [Fact]
    public void BuildLambdaFallbackSignature_normalizesUnknownValues()
    {
        string signature = ExpressionNodeConventions.BuildLambdaFallbackSignature(
            null,
            new string?[] { "int", null, " string " });

        Assert.Equal("<unknown> (int, <unknown>, string)", signature);
    }

    [Fact]
    public void BuildFallbackCallMetadata_returnsDynamicUnknownDefaults()
    {
        FallbackCallMetadata metadata = ExpressionNodeConventions.BuildFallbackCallMetadata();

        Assert.Equal(FrontendGraphConventions.Unknown, metadata.MethodFullName);
        Assert.Equal(FrontendGraphConventions.Unknown, metadata.Signature);
        Assert.Equal(FrontendGraphConventions.Unknown, metadata.TypeFullName);
        Assert.Equal(FrontendGraphConventions.DynamicDispatch, metadata.DispatchType);
    }
}
