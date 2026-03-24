namespace TerrariaTools.Dome.Core.Cpg.Tests.Passes;

public sealed class CallGraphOverlayTests
{
    [Fact]
    public void CallGraphOverlay_ShouldCreateCallEdges()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void M() { M(); } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);
        DefaultOverlays.ApplyTypeRelations(cpg, context);
        DefaultOverlays.ApplyCallGraph(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge.Label == "CALL");
    }

    [Fact]
    public void CallGraphOverlay_ShouldLinkCallerMethodToResolvedTargetMethod()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void M() { A(); } void A() { } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);
        DefaultOverlays.ApplyTypeRelations(cpg, context);
        DefaultOverlays.ApplyCallGraph(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "CALL", SourceId: "method:C.M", TargetId: "method:C.A" });
    }

    [Fact]
    public void CallGraphOverlay_ShouldLinkMethodReferencesThroughDedicatedMethodRefPath()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void A() { } void Use(System.Action action) { } void M() { Use(A); } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);
        DefaultOverlays.ApplyTypeRelations(cpg, context);
        DefaultOverlays.ApplyCallGraph(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "CALL", SourceId: "method:C.M", TargetId: "method:C.Use" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CALL", SourceId: "method:C.M", TargetId: "method:C.A" });
    }

    [Fact]
    public void CallGraphOverlay_ShouldUseSemanticTargetInsteadOfMatchingByNameOnly()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void A() { } void M(D other) { other.A(); } } class D { public void A() { } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);
        DefaultOverlays.ApplyTypeRelations(cpg, context);
        DefaultOverlays.ApplyCallGraph(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "CALL", SourceId: "method:C.M", TargetId: "method:D.A" });
        Assert.DoesNotContain(cpg.Edges, edge => edge is { Label: "CALL", SourceId: "method:C.M", TargetId: "method:C.A" });
    }

    [Fact]
    public void CallGraphOverlay_ShouldUseFallbackPathForUnresolvedTargetsOnly()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void M() { ExternalCall(); } void ExternalCall(int value) { } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);
        DefaultOverlays.ApplyTypeRelations(cpg, context);
        DefaultOverlays.ApplyCallGraph(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "CALL", SourceId: "method:C.M", TargetId: "method:ExternalCall" });
        Assert.DoesNotContain(cpg.Edges, edge => edge is { Label: "CALL", SourceId: "method:C.M", TargetId: "method:C.ExternalCall" });
    }

    [Fact]
    public void CallGraphOverlay_ShouldLinkCallsToCreatedMethodStubs()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void M() { ExternalCall(); } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);
        DefaultOverlays.ApplyTypeRelations(cpg, context);
        DefaultOverlays.ApplyCallGraph(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "CALL", SourceId: "method:C.M", TargetId: "method:ExternalCall" });
    }

    [Fact]
    public void CallGraphOverlay_ShouldLinkCallsToSemanticExternalMethodStubs()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("using System; class C { void M() { Console.WriteLine(1); } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);
        DefaultOverlays.ApplyTypeRelations(cpg, context);
        DefaultOverlays.ApplyCallGraph(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "CALL", SourceId: "method:C.M", TargetId: "method:System.Console.WriteLine" });
    }

    [Fact]
    public void CallGraphOverlay_ShouldNotFallbackToGlobalShortNameWhenReceiverTypeDistinguishesTarget()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(
            new RoslynFrontendConfig(
                "namespace N1 { public class D { public void A() { } } } " +
                "namespace N2 { public class D { public void A() { } } } " +
                "class C { void M(N1.D left, N2.D right) { left.A(); right.A(); } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);
        DefaultOverlays.ApplyTypeRelations(cpg, context);
        DefaultOverlays.ApplyCallGraph(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "CALL", SourceId: "method:C.M", TargetId: "method:N1.D.A" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CALL", SourceId: "method:C.M", TargetId: "method:N2.D.A" });
        Assert.DoesNotContain(cpg.Edges, edge => edge is { Label: "CALL", TargetId: "method:A" });
    }

    [Fact]
    public void CallGraphOverlay_ShouldLinkBaseTypedReceiverToHierarchyCandidates()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(
            new RoslynFrontendConfig(
                "class Base { public virtual void A() { } } " +
                "class Derived : Base { public override void A() { } } " +
                "class C { void M(Base value) { value.A(); } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);
        DefaultOverlays.ApplyTypeRelations(cpg, context);
        DefaultOverlays.ApplyCallGraph(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "CALL", SourceId: "method:C.M", TargetId: "method:Base.A" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CALL", SourceId: "method:C.M", TargetId: "method:Derived.A" });
        Assert.DoesNotContain(cpg.Edges, edge => edge is { Label: "CALL", TargetId: "method:A" });
    }
}
