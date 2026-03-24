namespace TerrariaTools.Dome.Core.Cpg.Tests.EndToEnd;

public sealed class DefaultOverlayPipelineTests
{
    [Fact]
    public void DefaultOverlayPipeline_ShouldApplyOverlaysInJoernOrder()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class B { } class C : B { void M() { M(); } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.Apply(cpg, context);

        Assert.Equal(["base", "controlflow", "typerel", "callgraph"], cpg.MetaData.Overlays);
    }

    [Fact]
    public void DefaultOverlayPipeline_ShouldSkipAlreadyAppliedOverlayWithoutMutatingGraph()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void A() { } void M() { A(); } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);

        int nodeCount = cpg.Nodes.Count;
        int edgeCount = cpg.Edges.Count;
        string[] overlays = cpg.MetaData.Overlays.ToArray();

        DefaultOverlays.ApplyBase(cpg, context);

        Assert.Equal(nodeCount, cpg.Nodes.Count);
        Assert.Equal(edgeCount, cpg.Edges.Count);
        Assert.Equal(overlays, cpg.MetaData.Overlays);
    }

    [Fact]
    public void DefaultOverlayPipeline_ShouldSkipEntirePipelineWhenAllOverlaysAlreadyApplied()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class B { } class C : B { void M() { M(); } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.Apply(cpg, context);

        int nodeCount = cpg.Nodes.Count;
        int edgeCount = cpg.Edges.Count;
        string[] overlays = cpg.MetaData.Overlays.ToArray();

        DefaultOverlays.Apply(cpg, context);

        Assert.Equal(nodeCount, cpg.Nodes.Count);
        Assert.Equal(edgeCount, cpg.Edges.Count);
        Assert.Equal(overlays, cpg.MetaData.Overlays);
    }

    [Fact]
    public void DefaultOverlayPipeline_ShouldSkipDeterministicallyWhenDependenciesAreMissing()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void M() { } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        int nodeCount = cpg.Nodes.Count;
        int edgeCount = cpg.Edges.Count;

        DefaultOverlays.ApplyControlFlow(cpg, context);

        Assert.Equal(nodeCount, cpg.Nodes.Count);
        Assert.Equal(edgeCount, cpg.Edges.Count);
        Assert.Empty(cpg.MetaData.Overlays);
    }
}
