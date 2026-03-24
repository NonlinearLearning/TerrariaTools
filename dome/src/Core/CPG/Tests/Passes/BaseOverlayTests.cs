namespace TerrariaTools.Dome.Core.Cpg.Tests.Passes;

public sealed class BaseOverlayTests
{
    [Fact]
    public void BaseOverlay_ShouldAppendBaseToMetaDataAndCreateFileNodes()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void M() { } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);

        Assert.Contains("base", cpg.MetaData.Overlays);
        Assert.Contains(cpg.Nodes, node => node is FileNode);
    }

    [Fact]
    public void BaseOverlay_ShouldCreateAstEdgesForTypeMethodAndCallHierarchy()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void M() { A(); } void A() { } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "AST", SourceId: "namespace-block:<global>", TargetId: "type:C" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "AST", SourceId: "type:C", TargetId: "method:C.M" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "AST", SourceId: "method:C.M", TargetId: "call:C.M:A:0" });
    }

    [Fact]
    public void BaseOverlay_ShouldCreateContainsEdgesFromFileToNamespaceAndDeclarations()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void M() { A(); } void A() { } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "CONTAINS", SourceId: "file:input.cs", TargetId: "namespace-block:<global>" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CONTAINS", SourceId: "namespace-block:<global>", TargetId: "type:C" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CONTAINS", SourceId: "type:C", TargetId: "method:C.M" });
    }

    [Fact]
    public void BaseOverlay_ShouldCreateAstAndContainsEdgesForMembers()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { private string _name; }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "AST", SourceId: "type:C", TargetId: "member:C:_name" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CONTAINS", SourceId: "type:C", TargetId: "member:C:_name" });
    }

    [Fact]
    public void BaseOverlay_ShouldCreateAstAndContainsEdgesForMethodBodyNodes()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void A() { } void M(bool flag) { if (flag) { A(); } return; } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "AST", SourceId: "method:C.M", TargetId: "block:C.M" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "AST", SourceId: "block:C.M", TargetId: "control-structure:C.M:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "AST", SourceId: "block:C.M", TargetId: "return:C.M:1" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CONTAINS", SourceId: "method:C.M", TargetId: "block:C.M" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CONTAINS", SourceId: "block:C.M", TargetId: "return:C.M:1" });
    }

    [Fact]
    public void BaseOverlay_ShouldCreateAstContainsAndRefEdgesForLocals()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void A(int value) { } void M(int input) { int count = 1; A(count); A(input); } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);

        Assert.Contains(cpg.Nodes, node => node is LocalNode { MethodName: "M", Name: "count", TypeFullName: "int" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "AST", SourceId: "block:C.M", TargetId: "local:C.M:count:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "CONTAINS", SourceId: "block:C.M", TargetId: "local:C.M:count:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "REF", SourceId: "identifier:M:A:0:0", TargetId: "local:C.M:count:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "REF", SourceId: "identifier:M:A:1:0", TargetId: "param-in:C.M:input:1" });
    }

    [Fact]
    public void BaseOverlay_ShouldCreateRefEdgesFromFieldIdentifiersToMembers()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { private int _value; void A(int value) { } void M() { A(this._value); } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "REF", SourceId: "field-identifier:C.M:A:0:0", TargetId: "member:C:_value" });
    }

    [Fact]
    public void BaseOverlay_ShouldCreateRefEdgesFromMethodRefsToMethods()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void A() { } void Use(System.Action action) { } void M() { Use(A); } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "REF", SourceId: "method-ref:M:Use:0:0", TargetId: "method:C.A" });
    }

    [Fact]
    public void BaseOverlay_ShouldCreateRefEdgesFromReceiverIdentifiersToLocals()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void A() { } void M() { C other = new C(); other.A(); } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "REF", SourceId: "identifier-receiver:M:A:0", TargetId: "local:C.M:other:0" });
    }

    [Fact]
    public void BaseOverlay_ShouldCreateNamespaceNodesAndMethodReturnNodes()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("namespace N; class C { int M() { return 1; } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);

        Assert.Contains(cpg.Nodes, node => node is NamespaceNode { Name: "N" });
        Assert.Contains(cpg.Nodes, node => node is MethodReturnNode { MethodName: "M", TypeFullName: "int" });
    }

    [Fact]
    public void BaseOverlay_ShouldCreateMethodParameterOutNodes()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void M(int input, string name) { } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);

        Assert.Contains(cpg.Nodes, node => node is MethodParameterOutNode { MethodName: "M", Name: "input", Order: 1, TypeFullName: "int", ContainingTypeName: "C" });
        Assert.Contains(cpg.Nodes, node => node is MethodParameterOutNode { MethodName: "M", Name: "name", Order: 2, TypeFullName: "string", ContainingTypeName: "C" });
    }

    [Fact]
    public void BaseOverlay_ShouldUseFullyQualifiedIdsForMethodParameterOutNodes()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(
            new RoslynFrontendConfig(
                "namespace N1 { class C { void M(int value) { } } } " +
                "namespace N2 { class C { void M(int value) { } } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);

        Assert.Contains(cpg.Nodes, node => node is MethodParameterOutNode { Id: "param-out:N1.C.M:value:1" });
        Assert.Contains(cpg.Nodes, node => node is MethodParameterOutNode { Id: "param-out:N2.C.M:value:1" });
        Assert.DoesNotContain(cpg.Nodes, node => node is MethodParameterOutNode { Id: "param-out:M:value:1" });
    }

    [Fact]
    public void BaseOverlay_ShouldCreateEvalTypeEdgesForCalls()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { int A() { return 1; } void M() { A(); } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "EVAL_TYPE", SourceId: "call:C.M:A:0", TargetId: "type-node:int" });
    }

    [Fact]
    public void BaseOverlay_ShouldCreateTypeDeclStubForMissingBaseType()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C : ExternalBase { }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);

        Assert.Contains(cpg.Nodes, node => node is TypeDeclNode { Name: "ExternalBase" });
    }

    [Fact]
    public void BaseOverlay_ShouldNotCreateDuplicateTypeDeclStubForNamespacedBaseDeclaration()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("using N; class C : Base { } namespace N { public class Base { } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);

        Assert.Single(cpg.Nodes.OfType<TypeDeclNode>().Where(node => string.Equals(node.FullName, "N.Base", StringComparison.Ordinal)));
    }

    [Fact]
    public void BaseOverlay_ShouldCreateMethodStubForUnresolvedCallTarget()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void M() { ExternalCall(); } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);

        Assert.Contains(cpg.Nodes, node => node is MethodNode { Name: "ExternalCall" });
    }

    [Fact]
    public void BaseOverlay_ShouldCreateSemanticMethodStubForResolvedExternalCallTarget()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("using System; class C { void M() { Console.WriteLine(1); } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);

        Assert.Contains(cpg.Nodes, node => node is MethodNode { Id: "method:System.Console.WriteLine", Name: "WriteLine", ContainingTypeName: "System.Console", FullName: "System.Console.WriteLine" });
    }

    [Fact]
    public void BaseOverlay_ShouldCreateTypeRefNodesForMethodReturnAndBaseType()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class B { } class C : B { int M() { return 1; } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);

        Assert.Contains(cpg.Nodes, node => node is TypeRefNode { TypeFullName: "B" });
        Assert.Contains(cpg.Nodes, node => node is TypeRefNode { TypeFullName: "int" });
    }

    [Fact]
    public void BaseOverlay_ShouldPreserveFullyQualifiedTypeNamesForMethodOutputsAndTypeRefs()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(
            new RoslynFrontendConfig(
                "using N; class C : Base { Output M(Input value) { return null; } } namespace N { public class Base { } public class Input { } public class Output { } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);

        Assert.Contains(cpg.Nodes, node => node is MethodReturnNode { MethodName: "M", TypeFullName: "N.Output", ContainingTypeName: "C" });
        Assert.Contains(cpg.Nodes, node => node is MethodParameterOutNode { MethodName: "M", Name: "value", Order: 1, TypeFullName: "N.Input", ContainingTypeName: "C" });
        Assert.Contains(cpg.Nodes, node => node is TypeRefNode { TypeFullName: "N.Base" });
        Assert.Contains(cpg.Nodes, node => node is TypeRefNode { TypeFullName: "N.Output" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "REF", SourceId: "type-ref:type:C:base:N.Base", TargetId: "type-node:N.Base" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "REF", SourceId: "type-ref:method:C.M:return:N.Output", TargetId: "type-node:N.Output" });
    }

    [Fact]
    public void BaseOverlay_ShouldUseFullyQualifiedIdsForNamespacedMethodReturnAndTypeRefNodes()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("namespace N; class C { int M() { return 1; } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);

        Assert.Contains(cpg.Nodes, node => node is MethodNode { Id: "method:N.C.M", ContainingTypeName: "N.C", FullName: "N.C.M" });
        Assert.Contains(cpg.Nodes, node => node is MethodReturnNode { Id: "method-return:N.C.M", ContainingTypeName: "N.C", TypeFullName: "int" });
        Assert.Contains(cpg.Nodes, node => node is TypeRefNode { Id: "type-ref:method:N.C.M:return:int", TypeFullName: "int" });
    }

    [Fact]
    public void BaseOverlay_ShouldCreateRefEdgesFromTypeRefsToTypeNodes()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class B { } class C : B { int M() { return 1; } }"));
        CpgContext context = new(cpg, BuiltinSchema.Create());

        DefaultOverlays.ApplyBase(cpg, context);

        Assert.Contains(cpg.Edges, edge => edge is { Label: "REF", SourceId: "type-ref:type:C:base:B", TargetId: "type-node:B" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "REF", SourceId: "type-ref:method:C.M:return:int", TargetId: "type-node:int" });
    }
}
