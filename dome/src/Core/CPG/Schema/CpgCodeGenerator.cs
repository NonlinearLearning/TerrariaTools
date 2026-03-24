using System.Text;

namespace TerrariaTools.Dome.Core.Cpg;

public sealed class CpgCodeGenerator
{
    private const string NamespaceDeclaration = "namespace TerrariaTools.Dome.Core.Cpg;";

    public string BuildNodeKindsSource(CpgSchema schema)
    {
        StringBuilder builder = new();
        builder.AppendLine("public static class NodeKinds");
        builder.AppendLine("{");
        foreach (CpgNodeSchema node in schema.Nodes)
        {
            builder.Append("    public const string ");
            builder.Append(ToConstantName(node.Label));
            builder.Append(" = \"");
            builder.Append(node.Label);
            builder.AppendLine("\";");
        }

        builder.AppendLine("}");
        return WrapInNamespace(builder);
    }

    public string BuildNodeInterfacesSource(CpgSchema schema)
    {
        StringBuilder builder = new();
        IEnumerable<string> interfaces = schema.Nodes
            .SelectMany(node => node.RoleInterfaces)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal);

        foreach (string interfaceName in interfaces)
        {
            builder.Append("public interface ");
            builder.Append(interfaceName);
            builder.AppendLine();
            builder.AppendLine("{");
            builder.AppendLine("}");
            builder.AppendLine();
        }

        return WrapInNamespace(builder);
    }

    public string BuildNodeBaseTypesSource(CpgSchema schema)
    {
        StringBuilder builder = new();
        HashSet<string> baseTypes = schema.Nodes
            .Select(node => node.PrimaryBase)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);

        if (baseTypes.Contains(nameof(StoredNode)))
        {
            builder.AppendLine("public abstract class StoredNode(string id)");
            builder.AppendLine("{");
            builder.AppendLine("    public string Id { get; } = id;");
            builder.AppendLine("}");
            builder.AppendLine();
        }

        if (baseTypes.Contains(nameof(AstNode)))
        {
            builder.AppendLine("public abstract class AstNode(string id) : StoredNode(id)");
            builder.AppendLine("{");
            builder.AppendLine("}");
            builder.AppendLine();
        }

        if (baseTypes.Contains(nameof(ExpressionNode)))
        {
            builder.AppendLine("public abstract class ExpressionNode(string id) : AstNode(id), ICfgNode");
            builder.AppendLine("{");
            builder.AppendLine("}");
            builder.AppendLine();
        }

        return WrapInNamespace(builder);
    }

    public string BuildNodeTypesSource(CpgSchema schema)
    {
        StringBuilder builder = new();

        for (int index = 0; index < schema.Nodes.Count; index++)
        {
            AppendNodeTypeSource(builder, schema.Nodes[index]);
            if (index < schema.Nodes.Count - 1)
            {
                builder.AppendLine();
            }
        }

        return WrapInNamespace(builder);
    }

    public string BuildEdgeKindsSource(CpgSchema schema)
    {
        StringBuilder builder = new();
        builder.AppendLine("public static class EdgeKinds");
        builder.AppendLine("{");
        foreach (CpgEdgeSchema edge in schema.Edges)
        {
            builder.Append("    public const string ");
            builder.Append(ToConstantName(edge.Label));
            builder.Append(" = \"");
            builder.Append(edge.Label);
            builder.AppendLine("\";");
        }

        builder.AppendLine("}");
        return WrapInNamespace(builder);
    }

    public string BuildPropertyKindsSource(CpgSchema schema)
    {
        StringBuilder builder = new();
        builder.AppendLine("public static class PropertyKinds");
        builder.AppendLine("{");
        foreach (CpgPropertySchema property in schema.Properties)
        {
            builder.Append("    public const string ");
            builder.Append(ToConstantName(property.Name));
            builder.Append(" = \"");
            builder.Append(property.Name);
            builder.AppendLine("\";");
        }

        builder.AppendLine("}");
        return WrapInNamespace(builder);
    }

    public string BuildSchemaIndexSource(CpgSchema schema)
    {
        StringBuilder builder = new();
        builder.AppendLine("public static class SchemaIndex");
        builder.AppendLine("{");
        builder.AppendLine("    public static readonly IReadOnlyDictionary<string, string> NodeClrNames =");
        builder.AppendLine("        new Dictionary<string, string>(StringComparer.Ordinal)");
        builder.AppendLine("        {");
        foreach (CpgNodeSchema node in schema.Nodes)
        {
            builder.Append("            [NodeKinds.");
            builder.Append(ToConstantName(node.Label));
            builder.Append("] = nameof(");
            builder.Append(node.ClrName);
            builder.AppendLine("),");
        }

        builder.AppendLine("        };");
        builder.AppendLine();
        builder.AppendLine("    public static readonly IReadOnlyDictionary<string, string> NodeLayers =");
        builder.AppendLine("        new Dictionary<string, string>(StringComparer.Ordinal)");
        builder.AppendLine("        {");
        foreach (CpgNodeSchema node in schema.Nodes)
        {
            builder.Append("            [NodeKinds.");
            builder.Append(ToConstantName(node.Label));
            builder.Append("] = \"");
            builder.Append(node.Layer);
            builder.AppendLine("\",");
        }

        builder.AppendLine("        };");
        builder.AppendLine();
        builder.AppendLine("    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NodeProperties =");
        builder.AppendLine("        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)");
        builder.AppendLine("        {");
        foreach (CpgNodeSchema node in schema.Nodes)
        {
            builder.Append("            [NodeKinds.");
            builder.Append(ToConstantName(node.Label));
            builder.Append("] = ");
            builder.Append(BuildArrayLiteral("PropertyKinds", node.Properties));
            builder.AppendLine(",");
        }

        builder.AppendLine("        };");
        builder.AppendLine();
        builder.AppendLine("    public static readonly IReadOnlyDictionary<string, string> EdgeClrNames =");
        builder.AppendLine("        new Dictionary<string, string>(StringComparer.Ordinal)");
        builder.AppendLine("        {");
        foreach (CpgEdgeSchema edge in schema.Edges)
        {
            builder.Append("            [EdgeKinds.");
            builder.Append(ToConstantName(edge.Label));
            builder.Append("] = \"");
            builder.Append(edge.ClrName);
            builder.AppendLine("\",");
        }

        builder.AppendLine("        };");
        builder.AppendLine();
        builder.AppendLine("    public static readonly IReadOnlyDictionary<string, string> EdgeLayers =");
        builder.AppendLine("        new Dictionary<string, string>(StringComparer.Ordinal)");
        builder.AppendLine("        {");
        foreach (CpgEdgeSchema edge in schema.Edges)
        {
            builder.Append("            [EdgeKinds.");
            builder.Append(ToConstantName(edge.Label));
            builder.Append("] = \"");
            builder.Append(edge.Layer);
            builder.AppendLine("\",");
        }

        builder.AppendLine("        };");
        builder.AppendLine();
        builder.AppendLine("    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EdgeSourceKinds =");
        builder.AppendLine("        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)");
        builder.AppendLine("        {");
        foreach (CpgEdgeSchema edge in schema.Edges)
        {
            builder.Append("            [EdgeKinds.");
            builder.Append(ToConstantName(edge.Label));
            builder.Append("] = ");
            builder.Append(BuildArrayLiteral("NodeKinds", edge.SourceKinds));
            builder.AppendLine(",");
        }

        builder.AppendLine("        };");
        builder.AppendLine();
        builder.AppendLine("    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EdgeTargetKinds =");
        builder.AppendLine("        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)");
        builder.AppendLine("        {");
        foreach (CpgEdgeSchema edge in schema.Edges)
        {
            builder.Append("            [EdgeKinds.");
            builder.Append(ToConstantName(edge.Label));
            builder.Append("] = ");
            builder.Append(BuildArrayLiteral("NodeKinds", edge.TargetKinds));
            builder.AppendLine(",");
        }

        builder.AppendLine("        };");
        builder.AppendLine();
        builder.AppendLine("    public static readonly IReadOnlyDictionary<string, string> PropertyValueKinds =");
        builder.AppendLine("        new Dictionary<string, string>(StringComparer.Ordinal)");
        builder.AppendLine("        {");
        foreach (CpgPropertySchema property in schema.Properties)
        {
            builder.Append("            [PropertyKinds.");
            builder.Append(ToConstantName(property.Name));
            builder.Append("] = \"");
            builder.Append(property.ValueKind);
            builder.AppendLine("\",");
        }

        builder.AppendLine("        };");
        builder.AppendLine();
        builder.AppendLine("    public static readonly IReadOnlyDictionary<string, bool> PropertyIsRequired =");
        builder.AppendLine("        new Dictionary<string, bool>(StringComparer.Ordinal)");
        builder.AppendLine("        {");
        foreach (CpgPropertySchema property in schema.Properties)
        {
            builder.Append("            [PropertyKinds.");
            builder.Append(ToConstantName(property.Name));
            builder.Append("] = ");
            builder.Append(property.IsRequired ? "true" : "false");
            builder.AppendLine(",");
        }

        builder.AppendLine("        };");
        builder.AppendLine("}");
        return WrapInNamespace(builder);
    }

    public static string ToConstantName(string schemaName)
    {
        string[] parts = schemaName.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return schemaName;
        }

        StringBuilder builder = new();
        foreach (string part in parts)
        {
            if (part.Length == 0)
            {
                continue;
            }

            builder.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
            {
                builder.Append(part[1..].ToLowerInvariant());
            }
        }

        return builder.ToString();
    }

    private static string WrapInNamespace(StringBuilder builder)
    {
        return $"{NamespaceDeclaration}{Environment.NewLine}{Environment.NewLine}{builder.ToString().TrimEnd()}";
    }

    private static string BuildArrayLiteral(string typeName, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return "Array.Empty<string>()";
        }

        return $"[{string.Join(", ", values.Select(value => $"{typeName}.{ToConstantName(value)}"))}]";
    }

    private static void AppendNodeTypeSource(StringBuilder builder, CpgNodeSchema node)
    {
        switch (node.ClrName)
        {
            case nameof(MetaDataNode):
                builder.AppendLine("public sealed class MetaDataNode(");
                builder.AppendLine("    string id,");
                builder.AppendLine("    string? language = null,");
                builder.AppendLine("    string? root = null,");
                builder.AppendLine("    string? version = null) : StoredNode(id)");
                builder.AppendLine("{");
                builder.AppendLine("    private readonly List<string> overlays = new();");
                builder.AppendLine();
                builder.AppendLine("    public IReadOnlyList<string> Overlays => overlays;");
                builder.AppendLine();
                builder.AppendLine("    public string? Language { get; } = language;");
                builder.AppendLine();
                builder.AppendLine("    public string? Root { get; } = root;");
                builder.AppendLine();
                builder.AppendLine("    public string? Version { get; } = version;");
                builder.AppendLine();
                builder.AppendLine("    public void AppendOverlay(string overlayName)");
                builder.AppendLine("    {");
                builder.AppendLine("        if (!overlays.Contains(overlayName, StringComparer.Ordinal))");
                builder.AppendLine("        {");
                builder.AppendLine("            overlays.Add(overlayName);");
                builder.AppendLine("        }");
                builder.AppendLine("    }");
                builder.AppendLine("}");
                return;
            case nameof(NamespaceNode):
                builder.AppendLine("public sealed class NamespaceNode(string id, string? name = null, string? fullName = null) : StoredNode(id)");
                builder.AppendLine("{");
                builder.AppendLine("    public string? Name { get; } = name;");
                builder.AppendLine();
                builder.AppendLine("    public string? FullName { get; } = fullName ?? name;");
                builder.AppendLine("}");
                return;
            case nameof(NamespaceBlockNode):
                builder.AppendLine("public sealed class NamespaceBlockNode(string id, string? name = null, string? fullName = null) : AstNode(id)");
                builder.AppendLine("{");
                builder.AppendLine("    public string? Name { get; } = name;");
                builder.AppendLine();
                builder.AppendLine("    public string? FullName { get; } = fullName ?? name;");
                builder.AppendLine("}");
                return;
            case nameof(FileNode):
                builder.AppendLine("public sealed class FileNode(string id, string? path = null) : StoredNode(id)");
                builder.AppendLine("{");
                builder.AppendLine("    public string? Path { get; } = path;");
                builder.AppendLine();
                builder.AppendLine("    public string? Name { get; } = path;");
                builder.AppendLine("}");
                return;
            case nameof(MethodNode):
                builder.AppendLine("public sealed class MethodNode(");
                builder.AppendLine("    string id,");
                builder.AppendLine("    string? name = null,");
                builder.AppendLine("    string? containingTypeName = null,");
                builder.AppendLine("    string? returnTypeName = null,");
                builder.AppendLine("    string? fullName = null,");
                builder.AppendLine("    string? signature = null,");
                builder.AppendLine("    string? typeFullName = null) : AstNode(id), ICfgNode, IDeclarationNode");
                builder.AppendLine("{");
                builder.AppendLine("    public string? Name { get; } = name;");
                builder.AppendLine();
                builder.AppendLine("    public string? ContainingTypeName { get; } = containingTypeName;");
                builder.AppendLine();
                builder.AppendLine("    public string? ReturnTypeName { get; } = returnTypeName;");
                builder.AppendLine();
                builder.AppendLine("    public string? FullName { get; } = fullName;");
                builder.AppendLine();
                builder.AppendLine("    public string? Signature { get; } = signature;");
                builder.AppendLine();
                builder.AppendLine("    public string? TypeFullName { get; } = typeFullName ?? returnTypeName;");
                builder.AppendLine("}");
                return;
            case nameof(MemberNode):
                builder.AppendLine("public sealed class MemberNode(");
                builder.AppendLine("    string id,");
                builder.AppendLine("    string? name = null,");
                builder.AppendLine("    string? typeFullName = null,");
                builder.AppendLine("    string? containingTypeName = null) : AstNode(id), IDeclarationNode");
                builder.AppendLine("{");
                builder.AppendLine("    public string? Name { get; } = name;");
                builder.AppendLine();
                builder.AppendLine("    public string? TypeFullName { get; } = typeFullName;");
                builder.AppendLine();
                builder.AppendLine("    public string? ContainingTypeName { get; } = containingTypeName;");
                builder.AppendLine("}");
                return;
            case nameof(MethodParameterInNode):
                builder.AppendLine("public sealed class MethodParameterInNode(");
                builder.AppendLine("    string id,");
                builder.AppendLine("    string? methodName = null,");
                builder.AppendLine("    string? name = null,");
                builder.AppendLine("    int order = 0,");
                builder.AppendLine("    string? typeFullName = null,");
                builder.AppendLine("    string? containingTypeName = null) : AstNode(id), ICfgNode, IDeclarationNode");
                builder.AppendLine("{");
                builder.AppendLine("    public string? MethodName { get; } = methodName;");
                builder.AppendLine();
                builder.AppendLine("    public string? Name { get; } = name;");
                builder.AppendLine();
                builder.AppendLine("    public int Order { get; } = order;");
                builder.AppendLine();
                builder.AppendLine("    public string? TypeFullName { get; } = typeFullName;");
                builder.AppendLine();
                builder.AppendLine("    public string? ContainingTypeName { get; } = containingTypeName;");
                builder.AppendLine("}");
                return;
            case nameof(MethodParameterOutNode):
                builder.AppendLine("public sealed class MethodParameterOutNode(");
                builder.AppendLine("    string id,");
                builder.AppendLine("    string? methodName = null,");
                builder.AppendLine("    string? name = null,");
                builder.AppendLine("    int order = 0,");
                builder.AppendLine("    string? typeFullName = null,");
                builder.AppendLine("    string? containingTypeName = null) : AstNode(id), ICfgNode, IDeclarationNode");
                builder.AppendLine("{");
                builder.AppendLine("    public string? MethodName { get; } = methodName;");
                builder.AppendLine();
                builder.AppendLine("    public string? Name { get; } = name;");
                builder.AppendLine();
                builder.AppendLine("    public int Order { get; } = order;");
                builder.AppendLine();
                builder.AppendLine("    public string? TypeFullName { get; } = typeFullName;");
                builder.AppendLine();
                builder.AppendLine("    public string? ContainingTypeName { get; } = containingTypeName;");
                builder.AppendLine("}");
                return;
            case nameof(MethodReturnNode):
                builder.AppendLine("public sealed class MethodReturnNode(");
                builder.AppendLine("    string id,");
                builder.AppendLine("    string? methodName = null,");
                builder.AppendLine("    string? typeFullName = null,");
                builder.AppendLine("    string? containingTypeName = null) : AstNode(id), ICfgNode");
                builder.AppendLine("{");
                builder.AppendLine("    public string? MethodName { get; } = methodName;");
                builder.AppendLine();
                builder.AppendLine("    public string? TypeFullName { get; } = typeFullName;");
                builder.AppendLine();
                builder.AppendLine("    public string? ContainingTypeName { get; } = containingTypeName;");
                builder.AppendLine("}");
                return;
            case nameof(LocalNode):
                builder.AppendLine("public sealed class LocalNode(");
                builder.AppendLine("    string id,");
                builder.AppendLine("    string? methodName = null,");
                builder.AppendLine("    string? name = null,");
                builder.AppendLine("    int order = 0,");
                builder.AppendLine("    string? typeFullName = null,");
                builder.AppendLine("    string? containingTypeName = null) : AstNode(id), ICfgNode, IDeclarationNode");
                builder.AppendLine("{");
                builder.AppendLine("    public string? MethodName { get; } = methodName;");
                builder.AppendLine();
                builder.AppendLine("    public string? Name { get; } = name;");
                builder.AppendLine();
                builder.AppendLine("    public int Order { get; } = order;");
                builder.AppendLine();
                builder.AppendLine("    public string? TypeFullName { get; } = typeFullName;");
                builder.AppendLine();
                builder.AppendLine("    public string? ContainingTypeName { get; } = containingTypeName;");
                builder.AppendLine("}");
                return;
            case nameof(TypeNode):
                builder.AppendLine("public sealed class TypeNode(string id, string? fullName = null) : StoredNode(id)");
                builder.AppendLine("{");
                builder.AppendLine("    public string? FullName { get; } = fullName;");
                builder.AppendLine("}");
                return;
            case nameof(TypeRefNode):
                builder.AppendLine("public sealed class TypeRefNode(string id, string? typeFullName = null) : AstNode(id)");
                builder.AppendLine("{");
                builder.AppendLine("    public string? TypeFullName { get; } = typeFullName;");
                builder.AppendLine("}");
                return;
            case nameof(TypeDeclNode):
                builder.AppendLine("public sealed class TypeDeclNode(string id, string? name = null, string? baseTypeName = null, string? fullName = null) : AstNode(id)");
                builder.AppendLine("{");
                builder.AppendLine("    public string? Name { get; } = name;");
                builder.AppendLine();
                builder.AppendLine("    public string? BaseTypeName { get; } = baseTypeName;");
                builder.AppendLine();
                builder.AppendLine("    public string? FullName { get; } = fullName;");
                builder.AppendLine("}");
                return;
            case nameof(BlockNode):
                builder.AppendLine("public sealed class BlockNode(");
                builder.AppendLine("    string id,");
                builder.AppendLine("    string? methodName = null,");
                builder.AppendLine("    string? containingTypeName = null) : AstNode(id)");
                builder.AppendLine("{");
                builder.AppendLine("    public string? MethodName { get; } = methodName;");
                builder.AppendLine();
                builder.AppendLine("    public string? ContainingTypeName { get; } = containingTypeName;");
                builder.AppendLine("}");
                return;
            case nameof(IdentifierNode):
                builder.AppendLine("public sealed class IdentifierNode(");
                builder.AppendLine("    string id,");
                builder.AppendLine("    string? name = null,");
                builder.AppendLine("    int order = 0,");
                builder.AppendLine("    string? typeFullName = null) : ExpressionNode(id)");
                builder.AppendLine("{");
                builder.AppendLine("    public string? Name { get; } = name;");
                builder.AppendLine();
                builder.AppendLine("    public int Order { get; } = order;");
                builder.AppendLine();
                builder.AppendLine("    public string? TypeFullName { get; } = typeFullName;");
                builder.AppendLine("}");
                return;
            case nameof(LiteralNode):
                builder.AppendLine("public sealed class LiteralNode(");
                builder.AppendLine("    string id,");
                builder.AppendLine("    string? code = null,");
                builder.AppendLine("    int order = 0,");
                builder.AppendLine("    string? typeFullName = null) : ExpressionNode(id)");
                builder.AppendLine("{");
                builder.AppendLine("    public string? Code { get; } = code;");
                builder.AppendLine();
                builder.AppendLine("    public int Order { get; } = order;");
                builder.AppendLine();
                builder.AppendLine("    public string? TypeFullName { get; } = typeFullName;");
                builder.AppendLine("}");
                return;
            case nameof(CallNode):
                builder.AppendLine("public sealed class CallNode(");
                builder.AppendLine("    string id,");
                builder.AppendLine("    string? ownerMethodName = null,");
                builder.AppendLine("    string? targetMethodName = null,");
                builder.AppendLine("    int? order = null,");
                builder.AppendLine("    string? containingTypeName = null,");
                builder.AppendLine("    string? typeFullName = null,");
                builder.AppendLine("    string? resolvedTargetMethodId = null,");
                builder.AppendLine("    string? methodFullName = null) : ExpressionNode(id)");
                builder.AppendLine("{");
                builder.AppendLine("    public string? OwnerMethodName { get; } = ownerMethodName;");
                builder.AppendLine();
                builder.AppendLine("    public string? TargetMethodName { get; } = targetMethodName;");
                builder.AppendLine();
                builder.AppendLine("    public int? Order { get; } = order;");
                builder.AppendLine();
                builder.AppendLine("    public string? ContainingTypeName { get; } = containingTypeName;");
                builder.AppendLine();
                builder.AppendLine("    public string? TypeFullName { get; } = typeFullName;");
                builder.AppendLine();
                builder.AppendLine("    public string? ResolvedTargetMethodId { get; } = resolvedTargetMethodId;");
                builder.AppendLine();
                builder.AppendLine("    public string? MethodFullName { get; } = methodFullName;");
                builder.AppendLine("}");
                return;
            case nameof(ControlStructureNode):
                builder.AppendLine("public sealed class ControlStructureNode(");
                builder.AppendLine("    string id,");
                builder.AppendLine("    string? methodName = null,");
                builder.AppendLine("    string? controlStructureType = null,");
                builder.AppendLine("    int order = 0,");
                builder.AppendLine("    string? containingTypeName = null) : AstNode(id), ICfgNode");
                builder.AppendLine("{");
                builder.AppendLine("    public string? MethodName { get; } = methodName;");
                builder.AppendLine();
                builder.AppendLine("    public string? ControlStructureType { get; } = controlStructureType;");
                builder.AppendLine();
                builder.AppendLine("    public int Order { get; } = order;");
                builder.AppendLine();
                builder.AppendLine("    public string? ContainingTypeName { get; } = containingTypeName;");
                builder.AppendLine("}");
                return;
            case nameof(ReturnNode):
                builder.AppendLine("public sealed class ReturnNode(");
                builder.AppendLine("    string id,");
                builder.AppendLine("    string? methodName = null,");
                builder.AppendLine("    int order = 0,");
                builder.AppendLine("    string? containingTypeName = null) : AstNode(id), ICfgNode");
                builder.AppendLine("{");
                builder.AppendLine("    public string? MethodName { get; } = methodName;");
                builder.AppendLine();
                builder.AppendLine("    public int Order { get; } = order;");
                builder.AppendLine();
                builder.AppendLine("    public string? ContainingTypeName { get; } = containingTypeName;");
                builder.AppendLine("}");
                return;
            case nameof(FieldIdentifierNode):
                builder.AppendLine("public sealed class FieldIdentifierNode(");
                builder.AppendLine("    string id,");
                builder.AppendLine("    string? name = null,");
                builder.AppendLine("    string? typeFullName = null) : ExpressionNode(id)");
                builder.AppendLine("{");
                builder.AppendLine("    public string? Name { get; } = name;");
                builder.AppendLine();
                builder.AppendLine("    public string? TypeFullName { get; } = typeFullName;");
                builder.AppendLine("}");
                return;
            case nameof(MethodRefNode):
                builder.AppendLine("public sealed class MethodRefNode(");
                builder.AppendLine("    string id,");
                builder.AppendLine("    string? methodName = null,");
                builder.AppendLine("    string? typeFullName = null) : ExpressionNode(id)");
                builder.AppendLine("{");
                builder.AppendLine("    public string? MethodName { get; } = methodName;");
                builder.AppendLine();
                builder.AppendLine("    public string? TypeFullName { get; } = typeFullName;");
                builder.AppendLine("}");
                return;
            default:
                throw new InvalidOperationException($"No node type template exists for {node.ClrName}.");
        }
    }
}
