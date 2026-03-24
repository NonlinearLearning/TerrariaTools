namespace TerrariaTools.Dome.Core.Cpg;

public sealed class MetaDataNode(
    string id,
    string? language = null,
    string? root = null,
    string? version = null) : StoredNode(id)
{
    private readonly List<string> overlays = new();

    public IReadOnlyList<string> Overlays => overlays;

    public string? Language { get; } = language;

    public string? Root { get; } = root;

    public string? Version { get; } = version;

    public void AppendOverlay(string overlayName)
    {
        if (!overlays.Contains(overlayName, StringComparer.Ordinal))
        {
            overlays.Add(overlayName);
        }
    }
}

public sealed class NamespaceNode(string id, string? name = null, string? fullName = null) : StoredNode(id)
{
    public string? Name { get; } = name;

    public string? FullName { get; } = fullName ?? name;
}

public sealed class NamespaceBlockNode(string id, string? name = null, string? fullName = null) : AstNode(id)
{
    public string? Name { get; } = name;

    public string? FullName { get; } = fullName ?? name;
}

public sealed class FileNode(string id, string? path = null) : StoredNode(id)
{
    public string? Path { get; } = path;

    public string? Name { get; } = path;
}

public sealed class MethodNode(
    string id,
    string? name = null,
    string? containingTypeName = null,
    string? returnTypeName = null,
    string? fullName = null,
    string? signature = null,
    string? typeFullName = null) : AstNode(id), ICfgNode, IDeclarationNode
{
    public string? Name { get; } = name;

    public string? ContainingTypeName { get; } = containingTypeName;

    public string? ReturnTypeName { get; } = returnTypeName;

    public string? FullName { get; } = fullName;

    public string? Signature { get; } = signature;

    public string? TypeFullName { get; } = typeFullName ?? returnTypeName;
}

public sealed class MemberNode(
    string id,
    string? name = null,
    string? typeFullName = null,
    string? containingTypeName = null) : AstNode(id), IDeclarationNode
{
    public string? Name { get; } = name;

    public string? TypeFullName { get; } = typeFullName;

    public string? ContainingTypeName { get; } = containingTypeName;
}

public sealed class MethodParameterInNode(
    string id,
    string? methodName = null,
    string? name = null,
    int order = 0,
    string? typeFullName = null,
    string? containingTypeName = null) : AstNode(id), ICfgNode, IDeclarationNode
{
    public string? MethodName { get; } = methodName;

    public string? Name { get; } = name;

    public int Order { get; } = order;

    public string? TypeFullName { get; } = typeFullName;

    public string? ContainingTypeName { get; } = containingTypeName;
}

public sealed class MethodParameterOutNode(
    string id,
    string? methodName = null,
    string? name = null,
    int order = 0,
    string? typeFullName = null,
    string? containingTypeName = null) : AstNode(id), ICfgNode, IDeclarationNode
{
    public string? MethodName { get; } = methodName;

    public string? Name { get; } = name;

    public int Order { get; } = order;

    public string? TypeFullName { get; } = typeFullName;

    public string? ContainingTypeName { get; } = containingTypeName;
}

public sealed class MethodReturnNode(
    string id,
    string? methodName = null,
    string? typeFullName = null,
    string? containingTypeName = null) : AstNode(id), ICfgNode
{
    public string? MethodName { get; } = methodName;

    public string? TypeFullName { get; } = typeFullName;

    public string? ContainingTypeName { get; } = containingTypeName;
}

public sealed class LocalNode(
    string id,
    string? methodName = null,
    string? name = null,
    int order = 0,
    string? typeFullName = null,
    string? containingTypeName = null) : AstNode(id), ICfgNode, IDeclarationNode
{
    public string? MethodName { get; } = methodName;

    public string? Name { get; } = name;

    public int Order { get; } = order;

    public string? TypeFullName { get; } = typeFullName;

    public string? ContainingTypeName { get; } = containingTypeName;
}

public sealed class TypeNode(string id, string? fullName = null) : StoredNode(id)
{
    public string? FullName { get; } = fullName;
}

public sealed class TypeRefNode(string id, string? typeFullName = null) : AstNode(id)
{
    public string? TypeFullName { get; } = typeFullName;
}

public sealed class TypeDeclNode(string id, string? name = null, string? baseTypeName = null, string? fullName = null) : AstNode(id)
{
    public string? Name { get; } = name;

    public string? BaseTypeName { get; } = baseTypeName;

    public string? FullName { get; } = fullName;
}

public sealed class BlockNode(
    string id,
    string? methodName = null,
    string? containingTypeName = null) : AstNode(id)
{
    public string? MethodName { get; } = methodName;

    public string? ContainingTypeName { get; } = containingTypeName;
}

public sealed class CallNode(
    string id,
    string? ownerMethodName = null,
    string? targetMethodName = null,
    int? order = null,
    string? containingTypeName = null,
    string? typeFullName = null,
    string? resolvedTargetMethodId = null,
    string? methodFullName = null) : ExpressionNode(id)
{
    public string? OwnerMethodName { get; } = ownerMethodName;

    public string? TargetMethodName { get; } = targetMethodName;

    public int? Order { get; } = order;

    public string? ContainingTypeName { get; } = containingTypeName;

    public string? TypeFullName { get; } = typeFullName;

    public string? ResolvedTargetMethodId { get; } = resolvedTargetMethodId;

    public string? MethodFullName { get; } = methodFullName;
}

public sealed class ControlStructureNode(
    string id,
    string? methodName = null,
    string? controlStructureType = null,
    int order = 0,
    string? containingTypeName = null) : AstNode(id), ICfgNode
{
    public string? MethodName { get; } = methodName;

    public string? ControlStructureType { get; } = controlStructureType;

    public int Order { get; } = order;

    public string? ContainingTypeName { get; } = containingTypeName;
}

public sealed class ReturnNode(
    string id,
    string? methodName = null,
    int order = 0,
    string? containingTypeName = null) : AstNode(id), ICfgNode
{
    public string? MethodName { get; } = methodName;

    public int Order { get; } = order;

    public string? ContainingTypeName { get; } = containingTypeName;
}

public sealed class FieldIdentifierNode(
    string id,
    string? name = null,
    string? typeFullName = null) : ExpressionNode(id)
{
    public string? Name { get; } = name;

    public string? TypeFullName { get; } = typeFullName;
}

public sealed class MethodRefNode(
    string id,
    string? methodName = null,
    string? typeFullName = null) : ExpressionNode(id)
{
    public string? MethodName { get; } = methodName;

    public string? TypeFullName { get; } = typeFullName;
}

public sealed class IdentifierNode(
    string id,
    string? name = null,
    int order = 0,
    string? typeFullName = null) : ExpressionNode(id)
{
    public string? Name { get; } = name;

    public int Order { get; } = order;

    public string? TypeFullName { get; } = typeFullName;
}

public sealed class LiteralNode(
    string id,
    string? code = null,
    int order = 0,
    string? typeFullName = null) : ExpressionNode(id)
{
    public string? Code { get; } = code;

    public int Order { get; } = order;

    public string? TypeFullName { get; } = typeFullName;
}
