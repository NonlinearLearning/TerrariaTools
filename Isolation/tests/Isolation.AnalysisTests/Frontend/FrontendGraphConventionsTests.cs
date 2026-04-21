using Xunit;

namespace Isolation.AnalysisTests.Frontend;

public sealed class FrontendGraphConventionsTests
{
    [Fact]
    public void BuildMemberAndAccessorNames_useStableRules()
    {
        string memberFullName = FrontendGraphConventions.BuildMemberFullName("global::Demo.Widget", "Count");
        string getterName = FrontendGraphConventions.BuildPropertyAccessorName("get", "Total");
        string setterName = FrontendGraphConventions.BuildPropertyAccessorName("set", "Total");

        Assert.Equal("Demo.Widget.Count", memberFullName);
        Assert.Equal("get_Total", getterName);
        Assert.Equal("set_Total", setterName);
    }

    [Fact]
    public void DispatchAndFallbackRules_useSharedConventions()
    {
        string staticDispatch = FrontendGraphConventions.GetDispatchType(true);
        string reducedDispatch = FrontendGraphConventions.GetDispatchType(false, true);
        string fieldFullName = FrontendGraphConventions.BuildFallbackFieldFullName("receiver.Child", "Value");

        Assert.Equal(FrontendGraphConventions.StaticDispatch, staticDispatch);
        Assert.Equal(FrontendGraphConventions.StaticDispatch, reducedDispatch);
        Assert.Equal("receiver.Child.Value", fieldFullName);
    }

    [Fact]
    public void TryGetCompoundAssignmentOperator_mapsKnownKinds()
    {
        Assert.Equal("+", FrontendGraphConventions.TryGetCompoundAssignmentOperator("AddAssignmentExpression"));
        Assert.Equal(">>", FrontendGraphConventions.TryGetCompoundAssignmentOperator("RightShiftAssignmentExpression"));
        Assert.Null(FrontendGraphConventions.TryGetCompoundAssignmentOperator("SimpleAssignmentExpression"));
    }

    [Fact]
    public void ImportLambdaAndOperationRules_useStableFallbacks()
    {
        string alias = FrontendGraphConventions.BuildImportAlias(null, "Demo.Tools.Widget");
        string lambdaName = FrontendGraphConventions.BuildLambdaName(3);
        string lambdaFullName = FrontendGraphConventions.BuildLambdaFallbackFullName("demo.cs", lambdaName);
        string lambdaSymbolId = FrontendGraphConventions.BuildLambdaFallbackSymbolId("demo.cs:10:123");
        string operationId = FrontendGraphConventions.BuildOperationId(null, 10, 123);

        Assert.Equal("Widget", alias);
        Assert.Equal("<lambda>3", lambdaName);
        Assert.Equal("demo.cs::<lambda>3", lambdaFullName);
        Assert.Equal("lambda:demo.cs:10:123", lambdaSymbolId);
        Assert.Equal("<memory>:10:123", operationId);
    }

    [Fact]
    public void ReceiverDisposeAndOperatorRules_shareLogicConstants()
    {
        InvocationReceiverInfo? receiverInfo =
            FrontendGraphConventions.TryParseReceiverFromInvocationText("service.client.Run");

        Assert.NotNull(receiverInfo);
        Assert.Equal("client", receiverInfo!.Name);
        Assert.Equal("service.client", receiverInfo.Code);
        Assert.Equal("resource.Dispose()", FrontendGraphConventions.BuildDisposeCallCode("resource"));
        Assert.Equal("Dispose()", FrontendGraphConventions.BuildDisposeCallCode(null));
        Assert.Equal("formatString", FrontendGraphConventions.FormatStringOperator);
        Assert.Equal("string", FrontendGraphConventions.StringTypeFullName);
    }
}
