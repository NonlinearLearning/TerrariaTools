using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Passes;

/// <summary>
/// 解析 `Import` 节点引用到的目标，并写回最小解析结果。
///
/// 这个 pass 对应 Joern `XImportResolverPass.scala` 的职责范围，
/// 当前只做最小静态解析：
/// - 精确命中已有 `TYPE/TYPE_DECL` 视为类型导入；
/// - 命中已有类型前缀视为命名空间导入；
/// - 其余保留为未解析导入。
/// </summary>
public sealed class BuildImportResolverPass : CpgPass
{

    protected override void Execute(CpgGraphBuilder builder)
    {
        HashSet<string> knownTypeFullNames = builder.Graph
            .GetNodes(CpgNodeKind.Type)
            .Concat(builder.Graph.GetNodes(CpgNodeKind.TypeDecl))
            .Select(node => node.TryGetProperty<string>("FullName", out string? fullName) ? fullName : null)
            .Where(static fullName => !string.IsNullOrWhiteSpace(fullName))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        foreach (CpgNode importNode in builder.Graph.GetNodes(CpgNodeKind.Import).ToArray())
        {
            if (!importNode.TryGetProperty<string>("ImportedEntity", out string? importedEntity) ||
                string.IsNullOrWhiteSpace(importedEntity))
            {
                continue;
            }

            string resolution = ResolveImport(importedEntity, knownTypeFullNames);
            importNode.SetProperty("ResolvedImportKind", resolution);
            AddResolutionTag(builder, importNode, resolution, importedEntity);
        }
    }

    private static string ResolveImport(string importedEntity, IReadOnlySet<string> knownTypeFullNames)
    {
        if (knownTypeFullNames.Contains(importedEntity))
        {
            return "TYPE";
        }

        return knownTypeFullNames.Any(typeFullName =>
            typeFullName.StartsWith(importedEntity + ".", StringComparison.Ordinal))
            ? "NAMESPACE"
            : "UNKNOWN";
    }

    private static void AddResolutionTag(
        CpgGraphBuilder builder,
        CpgNode importNode,
        string resolution,
        string importedEntity)
    {
        bool exists = builder.Graph
            .GetOutgoingEdges(importNode.Id, CpgEdgeKind.TaggedBy)
            .Select(edge => builder.Graph.GetNode(edge.TargetId))
            .Any(node =>
                node.TryGetProperty<string>("Name", out string? name) &&
                string.Equals(name, "ResolvedImport", StringComparison.Ordinal) &&
                node.TryGetProperty<string>("Value", out string? value) &&
                string.Equals(value, $"{resolution}:{importedEntity}", StringComparison.Ordinal));
        if (exists)
        {
            return;
        }

        CpgNode tagNode = builder.CreateNode(CpgNodeKind.Tag);
        tagNode.SetProperty("Name", "ResolvedImport");
        tagNode.SetProperty("Value", $"{resolution}:{importedEntity}");
        tagNode.SetProperty("AstParentId", importNode.Id);
        builder.AddEdge(importNode.Id, tagNode.Id, CpgEdgeKind.TaggedBy);
    }
}
