namespace TerrariaTools.Dome.Core.Cpg.Tests.Frontend;

public sealed class RoslynFrontendTests
{
    [Fact]
    public void Frontend_ShouldCreateMetaDataNamespaceBlockAndMethodNodes()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void M() { } }"));

        Assert.Contains(cpg.Nodes, node => node is MetaDataNode);
        Assert.Contains(cpg.Nodes, node => node is NamespaceBlockNode);
        Assert.Contains(cpg.Nodes, node => node is TypeDeclNode);
        Assert.Contains(cpg.Nodes, node => node is MethodNode);
    }

    [Fact]
    public void Frontend_ShouldPopulateMethodAndTypeFullNamesAndSignatures()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("namespace N; class C { int M(string value) { return 1; } }"));

        Assert.Contains(cpg.Nodes, node => node is TypeDeclNode { Name: "C", FullName: "N.C" });
        Assert.Contains(cpg.Nodes, node => node is MethodNode { Name: "M", FullName: "N.C.M", Signature: "int(string)" });
    }

    [Fact]
    public void Frontend_ShouldUseFullyQualifiedIdsForNamespacedTypeAndMethodDeclarations()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("namespace N1 { class C { void M() { } } } namespace N2 { class C { void M() { } } }"));

        Assert.Contains(cpg.Nodes, node => node is TypeDeclNode { Id: "type:N1.C", FullName: "N1.C" });
        Assert.Contains(cpg.Nodes, node => node is TypeDeclNode { Id: "type:N2.C", FullName: "N2.C" });
        Assert.Contains(cpg.Nodes, node => node is MethodNode { Id: "method:N1.C.M", FullName: "N1.C.M" });
        Assert.Contains(cpg.Nodes, node => node is MethodNode { Id: "method:N2.C.M", FullName: "N2.C.M" });
        Assert.DoesNotContain(cpg.Nodes, node => node is TypeDeclNode { Id: "type:C" });
        Assert.DoesNotContain(cpg.Nodes, node => node is MethodNode { Id: "method:C.M" });
    }

    [Fact]
    public void Frontend_ShouldUseFullyQualifiedIdsForBlockAndCallNodes()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("namespace N; class C { void A() { } void M() { A(); } }"));

        Assert.Contains(cpg.Nodes, node => node is BlockNode { Id: "block:N.C.M", MethodName: "M" });
        Assert.Contains(cpg.Nodes, node => node is CallNode { Id: "call:N.C.M:A:0", TargetMethodName: "A" });
        Assert.DoesNotContain(cpg.Nodes, node => node is BlockNode { Id: "block:M" });
        Assert.DoesNotContain(cpg.Nodes, node => node is CallNode { Id: "call:M:A:0" });
    }

    [Fact]
    public void Frontend_ShouldCreateTypeNodesForDeclaredAndReferencedTypes()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class B { } class C : B { int M() { return 1; } }"));

        Assert.Contains(cpg.Nodes, node => node is TypeNode { FullName: "B" });
        Assert.Contains(cpg.Nodes, node => node is TypeNode { FullName: "C" });
        Assert.Contains(cpg.Nodes, node => node is TypeNode { FullName: "int" });
    }

    [Fact]
    public void Frontend_ShouldPreferFullyQualifiedTypeNodesForNamespacedDeclarations()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("namespace N; class C { }"));

        Assert.Contains(cpg.Nodes, node => node is TypeNode { FullName: "N.C" });
        Assert.DoesNotContain(cpg.Nodes, node => node is TypeNode { FullName: "C" });
    }

    [Fact]
    public void Frontend_ShouldCreateMethodParameterNodes()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void M(int x, string y) { } }"));

        Assert.Contains(cpg.Nodes, node => node is MethodParameterInNode { MethodName: "M", Name: "x", Order: 1, TypeFullName: "int" });
        Assert.Contains(cpg.Nodes, node => node is MethodParameterInNode { MethodName: "M", Name: "y", Order: 2, TypeFullName: "string" });
    }

    [Fact]
    public void Frontend_ShouldPopulateFullyQualifiedTypeNamesForParametersAndReturns()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(
            new RoslynFrontendConfig(
                "using N; class C { Output M(Input value) { return null; } } namespace N { public class Input { } public class Output { } }"));

        Assert.Contains(cpg.Nodes, node => node is MethodNode { Name: "M", ReturnTypeName: "N.Output" });
        Assert.Contains(cpg.Nodes, node => node is MethodParameterInNode { MethodName: "M", Name: "value", Order: 1, TypeFullName: "N.Input" });
    }

    [Fact]
    public void Frontend_ShouldUseFullyQualifiedIdsForMethodParametersAndLocals()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(
            new RoslynFrontendConfig(
                "namespace N1 { class C { void M(int value) { int count = 0; } } } " +
                "namespace N2 { class C { void M(int value) { int count = 0; } } }"));

        Assert.Contains(cpg.Nodes, node => node is MethodParameterInNode { Id: "param-in:N1.C.M:value:1" });
        Assert.Contains(cpg.Nodes, node => node is MethodParameterInNode { Id: "param-in:N2.C.M:value:1" });
        Assert.Contains(cpg.Nodes, node => node is LocalNode { Id: "local:N1.C.M:count:0" });
        Assert.Contains(cpg.Nodes, node => node is LocalNode { Id: "local:N2.C.M:count:0" });
        Assert.DoesNotContain(cpg.Nodes, node => node is MethodParameterInNode { Id: "param-in:M:value:1" });
        Assert.DoesNotContain(cpg.Nodes, node => node is LocalNode { Id: "local:M:count:0" });
    }

    [Fact]
    public void Frontend_ShouldCreateMemberNodesForFieldDeclarations()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { private string _name; }"));

        Assert.Contains(cpg.Nodes, node => node is MemberNode { ContainingTypeName: "C", Name: "_name", TypeFullName: "string" });
    }

    [Fact]
    public void Frontend_ShouldPopulateFullyQualifiedTypeNamesForMembersLocalsAndExpressions()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(
            new RoslynFrontendConfig(
                "using N; class C { private Item _field; Output A(Input value) { return null; } void Use(Output output, Item item) { } void M(Item item) { Output result = A(item); Use(result, this._field); } } namespace N { public class Input { } public class Output { } public class Item { } }"));

        Assert.Contains(cpg.Nodes, node => node is MemberNode { ContainingTypeName: "C", Name: "_field", TypeFullName: "N.Item" });
        Assert.Contains(cpg.Nodes, node => node is LocalNode { MethodName: "M", Name: "result", TypeFullName: "N.Output" });
        Assert.Contains(cpg.Nodes, node => node is CallNode { TargetMethodName: "A", TypeFullName: "N.Output" });
        Assert.Contains(cpg.Nodes, node => node is IdentifierNode { Name: "item", TypeFullName: "N.Item" });
        Assert.Contains(cpg.Nodes, node => node is FieldIdentifierNode { Name: "_field", TypeFullName: "N.Item" });
        Assert.Contains(cpg.Nodes, node => node is TypeNode { FullName: "N.Output" });
        Assert.Contains(cpg.Nodes, node => node is TypeNode { FullName: "N.Item" });
        Assert.DoesNotContain(cpg.Nodes, node => node is TypeNode { FullName: "Output" });
        Assert.DoesNotContain(cpg.Nodes, node => node is TypeNode { FullName: "Item" });
    }

    [Fact]
    public void Frontend_ShouldCreateBlockReturnAndControlStructureNodes()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void A() { } void M(bool flag) { if (flag) { A(); } return; } }"));

        Assert.Contains(cpg.Nodes, node => node is BlockNode { MethodName: "M" });
        Assert.Contains(cpg.Nodes, node => node is ControlStructureNode { MethodName: "M", ControlStructureType: "IF" });
        Assert.Contains(cpg.Nodes, node => node is ReturnNode { MethodName: "M", Order: 1 });
    }

    [Fact]
    public void Frontend_ShouldCreateNestedControlStructureAndReturnNodes()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void A() { } void M(bool a, bool b) { if (a) { if (b) { A(); return; } } return; } }"));

        Assert.Contains(cpg.Nodes, node => node is ControlStructureNode { Id: "control-structure:C.M:0", MethodName: "M", ControlStructureType: "IF" });
        Assert.Contains(cpg.Nodes, node => node is ControlStructureNode { Id: "control-structure:C.M:1", MethodName: "M", ControlStructureType: "IF" });
        Assert.Contains(cpg.Nodes, node => node is ReturnNode { Id: "return:C.M:2", MethodName: "M", Order: 2 });
        Assert.Contains(cpg.Nodes, node => node is ReturnNode { Id: "return:C.M:3", MethodName: "M", Order: 3 });
    }

    [Fact]
    public void Frontend_ShouldUseFullyQualifiedIdsForReturnAndControlStructureNodes()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(
            new RoslynFrontendConfig(
                "namespace N1 { class C { void M(bool flag) { if (flag) { return; } } } } " +
                "namespace N2 { class C { void M(bool flag) { if (flag) { return; } } } }"));

        Assert.Contains(cpg.Nodes, node => node is ControlStructureNode { Id: "control-structure:N1.C.M:0" });
        Assert.Contains(cpg.Nodes, node => node is ControlStructureNode { Id: "control-structure:N2.C.M:0" });
        Assert.Contains(cpg.Nodes, node => node is ReturnNode { Id: "return:N1.C.M:1" });
        Assert.Contains(cpg.Nodes, node => node is ReturnNode { Id: "return:N2.C.M:1" });
        Assert.DoesNotContain(cpg.Nodes, node => node is ControlStructureNode { Id: "control-structure:M:0" });
        Assert.DoesNotContain(cpg.Nodes, node => node is ReturnNode { Id: "return:M:1" });
    }

    [Fact]
    public void Frontend_ShouldCreateLocalNodesForLocalDeclarations()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void M() { int count = 1; } }"));

        Assert.Contains(cpg.Nodes, node => node is LocalNode { MethodName: "M", Name: "count", TypeFullName: "int" });
    }

    [Fact]
    public void Frontend_ShouldCreateArgumentExpressionNodesAndArgumentEdges()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void A(int value, int count) { } void M(int x) { A(x, 1); } }"));

        Assert.Contains(cpg.Nodes, node => node is IdentifierNode { Name: "x" });
        Assert.Contains(cpg.Nodes, node => node is LiteralNode { Code: "1" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "ARGUMENT", SourceId: "call:C.M:A:0", TargetId: "identifier:M:A:0:0" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "ARGUMENT", SourceId: "call:C.M:A:0", TargetId: "literal:M:A:0:1" });
    }

    [Fact]
    public void Frontend_ShouldCreateFieldIdentifierNodesForFieldAccessArguments()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { private int _value; void A(int value) { } void M() { A(this._value); } }"));

        Assert.Contains(cpg.Nodes, node => node is FieldIdentifierNode { Name: "_value", TypeFullName: "int" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "ARGUMENT", SourceId: "call:C.M:A:0", TargetId: "field-identifier:C.M:A:0:0" });
    }

    [Fact]
    public void Frontend_ShouldCreateMethodRefNodesForMethodGroupArguments()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void A() { } void Use(System.Action action) { } void M() { Use(A); } }"));

        Assert.Contains(cpg.Nodes, node => node is MethodRefNode { MethodName: "A" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "ARGUMENT", SourceId: "call:C.M:Use:0", TargetId: "method-ref:M:Use:0:0" });
    }

    [Fact]
    public void Frontend_ShouldCreateReceiverEdgesForInstanceCalls()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void A() { } void M() { C other = new C(); other.A(); } }"));

        Assert.Contains(cpg.Nodes, node => node is IdentifierNode { Name: "other" });
        Assert.Contains(cpg.Edges, edge => edge is { Label: "RECEIVER", SourceId: "call:C.M:A:0", TargetId: "identifier-receiver:M:A:0" });
    }

    [Fact]
    public void Frontend_ShouldCaptureResolvedCallTargetMethodId()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void A() { } void M(D other) { other.A(); } } class D { public void A() { } }"));

        Assert.Contains(cpg.Nodes, node => node is CallNode { OwnerMethodName: "M", TargetMethodName: "A", ResolvedTargetMethodId: "method:D.A" });
    }

    [Fact]
    public void Frontend_ShouldPopulateCallMethodFullName()
    {
        RoslynCSharpFrontend frontend = new();
        DomeCpg cpg = frontend.CreateCpg(new RoslynFrontendConfig("class C { void A() { } void M(D other) { other.A(); } } class D { public void A() { } }"));

        Assert.Contains(cpg.Nodes, node => node is CallNode { TargetMethodName: "A", MethodFullName: "D.A" });
    }
}
