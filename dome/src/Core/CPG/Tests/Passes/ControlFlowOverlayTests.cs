namespace TerrariaTools.Dome.Core.Cpg.Tests.Passes;

public sealed class ControlFlowOverlayTests
{
    [Fact]
    public void ControlFlowOverlay_ShouldRequireBaseAndCreateCfgEdges()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void M() { A(); B(); } void A() { } void B() { } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);

        Assert.Contains("controlflow", cpg.MetaData.Overlays);
        Assert.Contains(cpg.Edges, edge => edge.Label == "CFG");
    }

    [Fact]
    public void ControlFlowOverlay_ShouldCreateDominatorsForSequentialCalls()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void M() { A(); B(); } void A() { } void B() { } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "DOMINATE", SourceId: "method:C.M", TargetId: "call:C.M:A:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "DOMINATE", SourceId: "call:C.M:A:0", TargetId: "call:C.M:B:1" });
    }

    [Fact]
    public void ControlFlowOverlay_ShouldCreatePostDominatorsForSequentialCalls()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void M() { A(); B(); } void A() { } void B() { } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "POST_DOMINATE", SourceId: "call:C.M:B:1", TargetId: "call:C.M:A:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "POST_DOMINATE", SourceId: "call:C.M:B:1", TargetId: "method:C.M" });
    }

    [Fact]
    public void ControlFlowOverlay_ShouldCreateControlDependenceEdgesFromMethodEntry()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void M() { A(); B(); } void A() { } void B() { } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "CDG", SourceId: "method:C.M", TargetId: "call:C.M:A:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CDG", SourceId: "method:C.M", TargetId: "call:C.M:B:1" });
    }

    [Fact]
    public void ControlFlowOverlay_ShouldCreateControlDependenceEdgesFromIfStatements()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void A() { } void B() { } void M(bool flag) { if (flag) { A(); } B(); } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "CDG", SourceId: "control-structure:C.M:0", TargetId: "call:C.M:A:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CDG", SourceId: "method:C.M", TargetId: "call:C.M:B:1" });
    }

    [Fact]
    public void ControlFlowOverlay_ShouldCreateCfgEdgesToReturnNodes()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void A() { } void M() { A(); return; } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "CFG", SourceId: "call:C.M:A:0", TargetId: "return:C.M:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "DOMINATE", SourceId: "call:C.M:A:0", TargetId: "return:C.M:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "POST_DOMINATE", SourceId: "return:C.M:0", TargetId: "call:C.M:A:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CDG", SourceId: "method:C.M", TargetId: "return:C.M:0" });
    }

    [Fact]
    public void ControlFlowOverlay_ShouldCreateBranchAndMergeCfgForConditionalCalls()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void A() { } void B() { } void M(bool flag) { if (flag) { A(); } B(); } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "CFG", SourceId: "method:C.M", TargetId: "control-structure:C.M:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CFG", SourceId: "control-structure:C.M:0", TargetId: "call:C.M:A:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CFG", SourceId: "control-structure:C.M:0", TargetId: "call:C.M:B:1" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CFG", SourceId: "call:C.M:A:0", TargetId: "call:C.M:B:1" });
        Assert.DoesNotContain(cpg.Edges, edge => edge is { Label: "DOMINATE", SourceId: "call:C.M:A:0", TargetId: "call:C.M:B:1" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "POST_DOMINATE", SourceId: "call:C.M:B:1", TargetId: "control-structure:C.M:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CDG", SourceId: "control-structure:C.M:0", TargetId: "call:C.M:A:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CDG", SourceId: "method:C.M", TargetId: "call:C.M:B:1" });
    }

    [Fact]
    public void ControlFlowOverlay_ShouldCreateConditionalReturnBranchesWithoutLinearizingCalls()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void A() { } void M(bool flag) { if (flag) { return; } A(); return; } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "CFG", SourceId: "method:C.M", TargetId: "control-structure:C.M:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CFG", SourceId: "control-structure:C.M:0", TargetId: "return:C.M:1" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CFG", SourceId: "control-structure:C.M:0", TargetId: "call:C.M:A:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CFG", SourceId: "call:C.M:A:0", TargetId: "return:C.M:2" });
        Assert.DoesNotContain(cpg.Edges, edge => edge is { Label: "CFG", SourceId: "return:C.M:1", TargetId: "call:C.M:A:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CDG", SourceId: "control-structure:C.M:0", TargetId: "return:C.M:1" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CDG", SourceId: "control-structure:C.M:0", TargetId: "call:C.M:A:0" });
    }

    [Fact]
    public void ControlFlowOverlay_ShouldConnectCallInsideReturnStatementToReturnNode()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { int A() { return 1; } int M() { return A(); } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "CFG", SourceId: "method:C.M", TargetId: "call:C.M:A:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CFG", SourceId: "call:C.M:A:0", TargetId: "return:C.M:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "DOMINATE", SourceId: "call:C.M:A:0", TargetId: "return:C.M:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "POST_DOMINATE", SourceId: "return:C.M:0", TargetId: "call:C.M:A:0" });
    }

    [Fact]
    public void ControlFlowOverlay_ShouldCreateNestedConditionalCfgAndCdgEdges()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void A() { } void B() { } void Cc() { } void M(bool a, bool b) { if (a) { if (b) { A(); } B(); } Cc(); } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "CFG", SourceId: "method:C.M", TargetId: "control-structure:C.M:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CFG", SourceId: "control-structure:C.M:0", TargetId: "control-structure:C.M:1" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CFG", SourceId: "control-structure:C.M:0", TargetId: "call:C.M:Cc:2" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CFG", SourceId: "control-structure:C.M:1", TargetId: "call:C.M:A:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CFG", SourceId: "control-structure:C.M:1", TargetId: "call:C.M:B:1" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CFG", SourceId: "call:C.M:A:0", TargetId: "call:C.M:B:1" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CFG", SourceId: "call:C.M:B:1", TargetId: "call:C.M:Cc:2" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CDG", SourceId: "control-structure:C.M:0", TargetId: "control-structure:C.M:1" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CDG", SourceId: "control-structure:C.M:1", TargetId: "call:C.M:A:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CDG", SourceId: "control-structure:C.M:0", TargetId: "call:C.M:B:1" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CDG", SourceId: "method:C.M", TargetId: "call:C.M:Cc:2" });
    }

    [Fact]
    public void ControlFlowOverlay_ShouldCreateCfgAndCdgEdgesForExplicitIfElseBranches()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void A() { } void B() { } void M(bool flag) { if (flag) { A(); } else { B(); } } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "CFG", SourceId: "method:C.M", TargetId: "control-structure:C.M:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CFG", SourceId: "control-structure:C.M:0", TargetId: "call:C.M:A:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CFG", SourceId: "control-structure:C.M:0", TargetId: "call:C.M:B:1" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CDG", SourceId: "control-structure:C.M:0", TargetId: "call:C.M:A:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CDG", SourceId: "control-structure:C.M:0", TargetId: "call:C.M:B:1" });
        Assert.DoesNotContain(cpg.Edges, edge => edge is { Label: "CDG", SourceId: "method:C.M", TargetId: "call:C.M:A:0" });
        Assert.DoesNotContain(cpg.Edges, edge => edge is { Label: "CDG", SourceId: "method:C.M", TargetId: "call:C.M:B:1" });
    }

    [Fact]
    public void ControlFlowOverlay_ShouldCreateBranchSpecificCfgForElseIfChains()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void A() { } void B() { } void Cc() { } void M(bool a, bool b) { if (a) { A(); } else if (b) { B(); } else { Cc(); } } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "CFG", SourceId: "method:C.M", TargetId: "control-structure:C.M:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CFG", SourceId: "control-structure:C.M:0", TargetId: "call:C.M:A:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CFG", SourceId: "control-structure:C.M:0", TargetId: "control-structure:C.M:1" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CFG", SourceId: "control-structure:C.M:1", TargetId: "call:C.M:B:1" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CFG", SourceId: "control-structure:C.M:1", TargetId: "call:C.M:Cc:2" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CDG", SourceId: "control-structure:C.M:0", TargetId: "call:C.M:A:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CDG", SourceId: "control-structure:C.M:0", TargetId: "control-structure:C.M:1" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CDG", SourceId: "control-structure:C.M:1", TargetId: "call:C.M:B:1" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CDG", SourceId: "control-structure:C.M:1", TargetId: "call:C.M:Cc:2" });
    }
}
