namespace TerrariaTools.Dome.Core.Cpg;

public static class SchemaIndex
{
    public static readonly IReadOnlyDictionary<string, string> NodeClrNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [NodeKinds.MetaData] = nameof(MetaDataNode),
            [NodeKinds.Namespace] = nameof(NamespaceNode),
            [NodeKinds.NamespaceBlock] = nameof(NamespaceBlockNode),
            [NodeKinds.File] = nameof(FileNode),
            [NodeKinds.Method] = nameof(MethodNode),
            [NodeKinds.Member] = nameof(MemberNode),
            [NodeKinds.MethodParameterIn] = nameof(MethodParameterInNode),
            [NodeKinds.MethodParameterOut] = nameof(MethodParameterOutNode),
            [NodeKinds.MethodReturn] = nameof(MethodReturnNode),
            [NodeKinds.Local] = nameof(LocalNode),
            [NodeKinds.Type] = nameof(TypeNode),
            [NodeKinds.TypeRef] = nameof(TypeRefNode),
            [NodeKinds.TypeDecl] = nameof(TypeDeclNode),
            [NodeKinds.Block] = nameof(BlockNode),
            [NodeKinds.Call] = nameof(CallNode),
            [NodeKinds.ControlStructure] = nameof(ControlStructureNode),
            [NodeKinds.Return] = nameof(ReturnNode),
            [NodeKinds.FieldIdentifier] = nameof(FieldIdentifierNode),
            [NodeKinds.MethodRef] = nameof(MethodRefNode),
            [NodeKinds.Identifier] = nameof(IdentifierNode),
            [NodeKinds.Literal] = nameof(LiteralNode),
        };

    public static readonly IReadOnlyDictionary<string, string> NodeLayers =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [NodeKinds.MetaData] = "base",
            [NodeKinds.Namespace] = "base",
            [NodeKinds.NamespaceBlock] = "base",
            [NodeKinds.File] = "base",
            [NodeKinds.Method] = "base",
            [NodeKinds.Member] = "base",
            [NodeKinds.MethodParameterIn] = "base",
            [NodeKinds.MethodParameterOut] = "base",
            [NodeKinds.MethodReturn] = "base",
            [NodeKinds.Local] = "base",
            [NodeKinds.Type] = "base",
            [NodeKinds.TypeRef] = "base",
            [NodeKinds.TypeDecl] = "base",
            [NodeKinds.Block] = "base",
            [NodeKinds.Call] = "base",
            [NodeKinds.ControlStructure] = "base",
            [NodeKinds.Return] = "base",
            [NodeKinds.FieldIdentifier] = "base",
            [NodeKinds.MethodRef] = "base",
            [NodeKinds.Identifier] = "base",
            [NodeKinds.Literal] = "base",
        };

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NodeProperties =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [NodeKinds.MetaData] = [PropertyKinds.Language, PropertyKinds.Overlays, PropertyKinds.Root, PropertyKinds.Version],
            [NodeKinds.Namespace] = [PropertyKinds.Name, PropertyKinds.FullName],
            [NodeKinds.NamespaceBlock] = [PropertyKinds.Name, PropertyKinds.FullName],
            [NodeKinds.File] = [PropertyKinds.Path, PropertyKinds.Name],
            [NodeKinds.Method] = [PropertyKinds.Name, PropertyKinds.ContainingTypeName, PropertyKinds.ReturnTypeName, PropertyKinds.FullName, PropertyKinds.Signature, PropertyKinds.TypeFullName],
            [NodeKinds.Member] = [PropertyKinds.Name, PropertyKinds.TypeFullName, PropertyKinds.ContainingTypeName],
            [NodeKinds.MethodParameterIn] = [PropertyKinds.MethodName, PropertyKinds.Name, PropertyKinds.Order, PropertyKinds.TypeFullName, PropertyKinds.ContainingTypeName],
            [NodeKinds.MethodParameterOut] = [PropertyKinds.MethodName, PropertyKinds.Name, PropertyKinds.Order, PropertyKinds.TypeFullName, PropertyKinds.ContainingTypeName],
            [NodeKinds.MethodReturn] = [PropertyKinds.MethodName, PropertyKinds.TypeFullName, PropertyKinds.ContainingTypeName],
            [NodeKinds.Local] = [PropertyKinds.MethodName, PropertyKinds.Name, PropertyKinds.Order, PropertyKinds.TypeFullName, PropertyKinds.ContainingTypeName],
            [NodeKinds.Type] = [PropertyKinds.FullName],
            [NodeKinds.TypeRef] = [PropertyKinds.TypeFullName],
            [NodeKinds.TypeDecl] = [PropertyKinds.Name, PropertyKinds.BaseTypeName, PropertyKinds.FullName],
            [NodeKinds.Block] = [PropertyKinds.MethodName, PropertyKinds.ContainingTypeName],
            [NodeKinds.Call] = [PropertyKinds.OwnerMethodName, PropertyKinds.TargetMethodName, PropertyKinds.Order, PropertyKinds.ContainingTypeName, PropertyKinds.TypeFullName, PropertyKinds.ResolvedTargetMethodId, PropertyKinds.MethodFullName],
            [NodeKinds.ControlStructure] = [PropertyKinds.MethodName, PropertyKinds.ControlStructureType, PropertyKinds.Order, PropertyKinds.ContainingTypeName],
            [NodeKinds.Return] = [PropertyKinds.MethodName, PropertyKinds.Order, PropertyKinds.ContainingTypeName],
            [NodeKinds.FieldIdentifier] = [PropertyKinds.Name, PropertyKinds.TypeFullName],
            [NodeKinds.MethodRef] = [PropertyKinds.MethodName, PropertyKinds.TypeFullName],
            [NodeKinds.Identifier] = [PropertyKinds.Name, PropertyKinds.Order, PropertyKinds.TypeFullName],
            [NodeKinds.Literal] = [PropertyKinds.Code, PropertyKinds.Order, PropertyKinds.TypeFullName],
        };

    public static readonly IReadOnlyDictionary<string, string> EdgeClrNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [EdgeKinds.Ast] = "AstEdge",
            [EdgeKinds.Argument] = "ArgumentEdge",
            [EdgeKinds.Receiver] = "ReceiverEdge",
            [EdgeKinds.Ref] = "RefEdge",
            [EdgeKinds.SourceFile] = "SourceFileEdge",
            [EdgeKinds.Contains] = "ContainsEdge",
            [EdgeKinds.EvalType] = "EvalTypeEdge",
            [EdgeKinds.Cfg] = "CfgEdge",
            [EdgeKinds.Dominate] = "DominateEdge",
            [EdgeKinds.PostDominate] = "PostDominateEdge",
            [EdgeKinds.Cdg] = "CdgEdge",
            [EdgeKinds.InheritsFrom] = "InheritsFromEdge",
            [EdgeKinds.AliasOf] = "AliasOfEdge",
            [EdgeKinds.FieldAccess] = "FieldAccessEdge",
            [EdgeKinds.Call] = "CallEdge",
        };

    public static readonly IReadOnlyDictionary<string, string> EdgeLayers =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [EdgeKinds.Ast] = "base",
            [EdgeKinds.Argument] = "base",
            [EdgeKinds.Receiver] = "base",
            [EdgeKinds.Ref] = "base",
            [EdgeKinds.SourceFile] = "base",
            [EdgeKinds.Contains] = "base",
            [EdgeKinds.EvalType] = "base",
            [EdgeKinds.Cfg] = "controlflow",
            [EdgeKinds.Dominate] = "controlflow",
            [EdgeKinds.PostDominate] = "controlflow",
            [EdgeKinds.Cdg] = "controlflow",
            [EdgeKinds.InheritsFrom] = "typerel",
            [EdgeKinds.AliasOf] = "typerel",
            [EdgeKinds.FieldAccess] = "typerel",
            [EdgeKinds.Call] = "callgraph",
        };

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EdgeSourceKinds =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [EdgeKinds.Ast] = [NodeKinds.NamespaceBlock, NodeKinds.TypeDecl, NodeKinds.Method, NodeKinds.Block, NodeKinds.Call, NodeKinds.ControlStructure, NodeKinds.Return],
            [EdgeKinds.Argument] = [NodeKinds.Call],
            [EdgeKinds.Receiver] = [NodeKinds.Call],
            [EdgeKinds.Ref] = [NodeKinds.Identifier, NodeKinds.FieldIdentifier, NodeKinds.MethodRef, NodeKinds.TypeRef, NodeKinds.Member, NodeKinds.MethodParameterIn, NodeKinds.MethodParameterOut, NodeKinds.MethodReturn, NodeKinds.Local],
            [EdgeKinds.SourceFile] = [NodeKinds.NamespaceBlock, NodeKinds.TypeDecl, NodeKinds.Method, NodeKinds.Member],
            [EdgeKinds.Contains] = [NodeKinds.File, NodeKinds.Namespace, NodeKinds.TypeDecl, NodeKinds.Method, NodeKinds.Block],
            [EdgeKinds.EvalType] = [NodeKinds.Call, NodeKinds.Identifier, NodeKinds.Literal, NodeKinds.FieldIdentifier, NodeKinds.MethodRef],
            [EdgeKinds.Cfg] = [NodeKinds.Method, NodeKinds.MethodParameterIn, NodeKinds.MethodParameterOut, NodeKinds.MethodReturn, NodeKinds.Local, NodeKinds.Call, NodeKinds.ControlStructure, NodeKinds.Return],
            [EdgeKinds.Dominate] = [NodeKinds.Method, NodeKinds.MethodParameterIn, NodeKinds.MethodParameterOut, NodeKinds.MethodReturn, NodeKinds.Local, NodeKinds.Call, NodeKinds.ControlStructure, NodeKinds.Return],
            [EdgeKinds.PostDominate] = [NodeKinds.Method, NodeKinds.MethodParameterIn, NodeKinds.MethodParameterOut, NodeKinds.MethodReturn, NodeKinds.Local, NodeKinds.Call, NodeKinds.ControlStructure, NodeKinds.Return],
            [EdgeKinds.Cdg] = [NodeKinds.Method, NodeKinds.ControlStructure],
            [EdgeKinds.InheritsFrom] = [NodeKinds.TypeDecl],
            [EdgeKinds.AliasOf] = [NodeKinds.Member, NodeKinds.MethodParameterIn, NodeKinds.MethodParameterOut, NodeKinds.MethodReturn, NodeKinds.Local, NodeKinds.TypeRef],
            [EdgeKinds.FieldAccess] = [NodeKinds.FieldIdentifier],
            [EdgeKinds.Call] = [NodeKinds.Method],
        };

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EdgeTargetKinds =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [EdgeKinds.Ast] = [NodeKinds.TypeDecl, NodeKinds.Member, NodeKinds.Method, NodeKinds.MethodParameterIn, NodeKinds.MethodReturn, NodeKinds.Local, NodeKinds.Block, NodeKinds.Call, NodeKinds.ControlStructure, NodeKinds.Return, NodeKinds.Identifier, NodeKinds.Literal, NodeKinds.FieldIdentifier, NodeKinds.MethodRef, NodeKinds.TypeRef],
            [EdgeKinds.Argument] = [NodeKinds.Identifier, NodeKinds.Literal, NodeKinds.Call, NodeKinds.MethodRef, NodeKinds.FieldIdentifier],
            [EdgeKinds.Receiver] = [NodeKinds.Identifier, NodeKinds.FieldIdentifier],
            [EdgeKinds.Ref] = [NodeKinds.Method, NodeKinds.Member, NodeKinds.MethodParameterIn, NodeKinds.MethodParameterOut, NodeKinds.Local, NodeKinds.Type],
            [EdgeKinds.SourceFile] = [NodeKinds.File],
            [EdgeKinds.Contains] = [NodeKinds.NamespaceBlock, NodeKinds.TypeDecl, NodeKinds.Member, NodeKinds.Method, NodeKinds.MethodParameterIn, NodeKinds.MethodParameterOut, NodeKinds.MethodReturn, NodeKinds.Local, NodeKinds.Block, NodeKinds.Call, NodeKinds.ControlStructure, NodeKinds.Return, NodeKinds.TypeRef, NodeKinds.Identifier, NodeKinds.Literal, NodeKinds.FieldIdentifier, NodeKinds.MethodRef],
            [EdgeKinds.EvalType] = [NodeKinds.Type],
            [EdgeKinds.Cfg] = [NodeKinds.Method, NodeKinds.MethodParameterIn, NodeKinds.MethodParameterOut, NodeKinds.MethodReturn, NodeKinds.Local, NodeKinds.Call, NodeKinds.ControlStructure, NodeKinds.Return],
            [EdgeKinds.Dominate] = [NodeKinds.Method, NodeKinds.MethodParameterIn, NodeKinds.MethodParameterOut, NodeKinds.MethodReturn, NodeKinds.Local, NodeKinds.Call, NodeKinds.ControlStructure, NodeKinds.Return],
            [EdgeKinds.PostDominate] = [NodeKinds.Method, NodeKinds.MethodParameterIn, NodeKinds.MethodParameterOut, NodeKinds.MethodReturn, NodeKinds.Local, NodeKinds.Call, NodeKinds.ControlStructure, NodeKinds.Return],
            [EdgeKinds.Cdg] = [NodeKinds.Call, NodeKinds.ControlStructure, NodeKinds.Return],
            [EdgeKinds.InheritsFrom] = [NodeKinds.TypeDecl, NodeKinds.Type],
            [EdgeKinds.AliasOf] = [NodeKinds.Type],
            [EdgeKinds.FieldAccess] = [NodeKinds.Member],
            [EdgeKinds.Call] = [NodeKinds.Method],
        };

    public static readonly IReadOnlyDictionary<string, string> PropertyValueKinds =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PropertyKinds.Language] = "string",
            [PropertyKinds.Overlays] = "string[]",
            [PropertyKinds.Root] = "string",
            [PropertyKinds.Version] = "string",
            [PropertyKinds.Path] = "string",
            [PropertyKinds.Name] = "string",
            [PropertyKinds.FullName] = "string",
            [PropertyKinds.Signature] = "string",
            [PropertyKinds.MethodFullName] = "string",
            [PropertyKinds.TypeFullName] = "string",
            [PropertyKinds.MethodName] = "string",
            [PropertyKinds.ContainingTypeName] = "string",
            [PropertyKinds.ReturnTypeName] = "string",
            [PropertyKinds.BaseTypeName] = "string",
            [PropertyKinds.OwnerMethodName] = "string",
            [PropertyKinds.TargetMethodName] = "string",
            [PropertyKinds.ResolvedTargetMethodId] = "string",
            [PropertyKinds.Order] = "int",
            [PropertyKinds.Code] = "string",
            [PropertyKinds.ControlStructureType] = "string",
        };

    public static readonly IReadOnlyDictionary<string, bool> PropertyIsRequired =
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [PropertyKinds.Language] = false,
            [PropertyKinds.Overlays] = false,
            [PropertyKinds.Root] = false,
            [PropertyKinds.Version] = false,
            [PropertyKinds.Path] = false,
            [PropertyKinds.Name] = false,
            [PropertyKinds.FullName] = false,
            [PropertyKinds.Signature] = false,
            [PropertyKinds.MethodFullName] = false,
            [PropertyKinds.TypeFullName] = false,
            [PropertyKinds.MethodName] = false,
            [PropertyKinds.ContainingTypeName] = false,
            [PropertyKinds.ReturnTypeName] = false,
            [PropertyKinds.BaseTypeName] = false,
            [PropertyKinds.OwnerMethodName] = false,
            [PropertyKinds.TargetMethodName] = false,
            [PropertyKinds.ResolvedTargetMethodId] = false,
            [PropertyKinds.Order] = false,
            [PropertyKinds.Code] = false,
            [PropertyKinds.ControlStructureType] = false,
        };
}
