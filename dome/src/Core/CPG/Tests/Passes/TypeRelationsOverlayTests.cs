namespace TerrariaTools.Dome.Core.Cpg.Tests.Passes;

public sealed class TypeRelationsOverlayTests
{
    [Fact]
    public void TypeRelationsOverlay_ShouldCreateInheritsFromEdges()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class B { } class C : B { }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);
        DefaultOverlays.ApplyTypeRelations(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge.Label == "INHERITS_FROM");
    }

    [Fact]
    public void TypeRelationsOverlay_ShouldLinkDerivedTypeToItsDeclaredBaseType()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class B { } class C : B { }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);
        DefaultOverlays.ApplyTypeRelations(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "INHERITS_FROM", SourceId: "type:C", TargetId: "type:B" });
    }

    [Fact]
    public void TypeRelationsOverlay_ShouldLinkDerivedTypeToNamespacedBaseDeclaration()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("using N; class C : Base { } namespace N { public class Base { } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);
        DefaultOverlays.ApplyTypeRelations(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "INHERITS_FROM", SourceId: "type:C", TargetId: "type:N.Base" });
    }

    [Fact]
    public void TypeRelationsOverlay_ShouldCreateRefEdgesFromMembersToDeclaredTypeNodes()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { private int _value; }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);
        DefaultOverlays.ApplyTypeRelations(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "REF", SourceId: "member:C:_value", TargetId: "type-node:int" });
    }

    [Fact]
    public void TypeRelationsOverlay_ShouldCreateRefEdgesFromParametersAndLocalsToDeclaredTypeNodes()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void M(int input) { int count = 1; } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);
        DefaultOverlays.ApplyTypeRelations(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "REF", SourceId: "param-in:C.M:input:1", TargetId: "type-node:int" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "REF", SourceId: "local:C.M:count:0", TargetId: "type-node:int" });
    }

    [Fact]
    public void TypeRelationsOverlay_ShouldCreateRefEdgesFromParameterOutAndMethodReturnToDeclaredTypeNodes()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("using N; class C { Output M(Input input) { return null; } } namespace N { public class Input { } public class Output { } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);
        DefaultOverlays.ApplyTypeRelations(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "REF", SourceId: "param-out:C.M:input:1", TargetId: "type-node:N.Input" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "REF", SourceId: "method-return:C.M", TargetId: "type-node:N.Output" });
    }

    [Fact]
    public void TypeRelationsOverlay_ShouldCreateAliasOfEdgesForAliasedTypeReferences()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(
            new RoslynFrontendConfig(
                "using BaseAlias = N.Base; using OutputAlias = N.Output; class C : BaseAlias { OutputAlias M(OutputAlias input) { OutputAlias local = input; return local; } } namespace N { public class Base { } public class Output { } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);
        DefaultOverlays.ApplyTypeRelations(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "ALIAS_OF", SourceId: "type-ref:type:C:base:N.Base", TargetId: "type-node:N.Base" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "ALIAS_OF", SourceId: "method-return:C.M", TargetId: "type-node:N.Output" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "ALIAS_OF", SourceId: "param-in:C.M:input:1", TargetId: "type-node:N.Output" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "ALIAS_OF", SourceId: "param-out:C.M:input:1", TargetId: "type-node:N.Output" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "ALIAS_OF", SourceId: "local:C.M:local:0", TargetId: "type-node:N.Output" });
    }

    [Fact]
    public void TypeRelationsOverlay_ShouldCreateAliasOfEdgesUsingFullyQualifiedDeclarationIds()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(
            new RoslynFrontendConfig(
                "using OutputAlias1 = N1.Output; using OutputAlias2 = N2.Output; " +
                "namespace N1 { public class Output { } } namespace N2 { public class Output { } } " +
                "namespace A { class C { OutputAlias1 M(OutputAlias1 input) { OutputAlias1 local = input; return local; } } } " +
                "namespace B { class C { OutputAlias2 M(OutputAlias2 input) { OutputAlias2 local = input; return local; } } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);
        DefaultOverlays.ApplyTypeRelations(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "ALIAS_OF", SourceId: "param-in:A.C.M:input:1", TargetId: "type-node:N1.Output" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "ALIAS_OF", SourceId: "param-out:A.C.M:input:1", TargetId: "type-node:N1.Output" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "ALIAS_OF", SourceId: "local:A.C.M:local:0", TargetId: "type-node:N1.Output" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "ALIAS_OF", SourceId: "method-return:A.C.M", TargetId: "type-node:N1.Output" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "ALIAS_OF", SourceId: "param-in:B.C.M:input:1", TargetId: "type-node:N2.Output" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "ALIAS_OF", SourceId: "param-out:B.C.M:input:1", TargetId: "type-node:N2.Output" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "ALIAS_OF", SourceId: "local:B.C.M:local:0", TargetId: "type-node:N2.Output" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "ALIAS_OF", SourceId: "method-return:B.C.M", TargetId: "type-node:N2.Output" });
    }

    [Fact]
    public void TypeRelationsOverlay_ShouldCreateExplicitFieldAccessEdges()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { private int _value; void Use(int value) { } void M() { Use(this._value); this._value.ToString(); } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);
        DefaultOverlays.ApplyTypeRelations(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "FIELD_ACCESS", SourceId: "field-identifier:C.M:Use:0:0", TargetId: "member:C:_value" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "FIELD_ACCESS", SourceId: "field-identifier-receiver:C.M:ToString:1", TargetId: "member:C:_value" });
    }

    [Fact]
    public void TypeRelationsOverlay_ShouldCreateFieldAccessEdgesUsingFullyQualifiedMemberIds()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(
            new RoslynFrontendConfig(
                "namespace N1 { class C { private int _value; void Use(int value) { } void M() { Use(this._value); this._value.ToString(); } } } " +
                "namespace N2 { class C { private int _value; void Use(int value) { } void M() { Use(this._value); this._value.ToString(); } } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);
        DefaultOverlays.ApplyControlFlow(cpg, context);
        DefaultOverlays.ApplyTypeRelations(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "FIELD_ACCESS", SourceId: "field-identifier:N1.C.M:Use:0:0", TargetId: "member:N1.C:_value" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "FIELD_ACCESS", SourceId: "field-identifier-receiver:N1.C.M:ToString:1", TargetId: "member:N1.C:_value" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "FIELD_ACCESS", SourceId: "field-identifier:N2.C.M:Use:0:0", TargetId: "member:N2.C:_value" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "FIELD_ACCESS", SourceId: "field-identifier-receiver:N2.C.M:ToString:1", TargetId: "member:N2.C:_value" });
    }

    [Fact]
    public void FieldAccessLinkerPass_ShouldCreateFieldAccessFromExistingRefEdges()
    {
        DomeCpg cpg = new();
        DiffGraph diff = new();
        FieldIdentifierNode fieldIdentifierNode = new("field-identifier:test", "_value", "int");
        MemberNode memberNode = new("member:C:_value", "_value", "int", "C");

        diff.AddNode(new MetaDataNode("meta-data", "CSHARP", "input.cs", "0.1"));
        diff.AddNode(fieldIdentifierNode);
        diff.AddNode(memberNode);
        diff.AddEdge(new CpgEdge(EdgeKinds.Ref, fieldIdentifierNode.Id, memberNode.Id));
        DiffGraphApplier.Apply(cpg, diff);

        CpgContext context = new(cpg, BuiltinSchema.Create());

        new FieldAccessLinkerPass(context).CreateAndApply();

        Assert.Contains(
            cpg.Edges,
            edge => edge is { Label: EdgeKinds.FieldAccess, SourceId: "field-identifier:test", TargetId: "member:C:_value" });
    }
}
