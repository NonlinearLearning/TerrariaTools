using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Passes;

/// <summary>
/// 为图中出现过的类型全名补齐 `TYPE` 节点。
///
/// 这个 pass 对齐 Joern `TypeNodePass` 的最小职责：
/// - 从图中所有节点的 `TypeFullName` 收集类型；
/// - 从 `TYPE_DECL.FullName` 与 `InheritsFromTypeFullNames` 收集类型；
/// - 为缺失的 `TYPE` 节点补壳。
/// </summary>
public sealed class BuildTypeNodePass : CpgPass
{

    protected override void Execute(CpgGraphBuilder builder)
    {
        HashSet<string> discoveredTypes = new(StringComparer.Ordinal);

        foreach (CpgNode node in builder.Graph.Nodes)
        {
            if (node.TryGetProperty<string>("TypeFullName", out string? typeFullName) &&
                !string.IsNullOrWhiteSpace(typeFullName) &&
                !string.Equals(typeFullName, "<unknown>", StringComparison.Ordinal))
            {
                discoveredTypes.Add(typeFullName);
            }
        }

        foreach (CpgNode typeDeclNode in builder.Graph.GetNodes(CpgNodeKind.TypeDecl))
        {
            if (typeDeclNode.TryGetProperty<string>("FullName", out string? typeDeclFullName) &&
                !string.IsNullOrWhiteSpace(typeDeclFullName))
            {
                discoveredTypes.Add(typeDeclFullName);
            }

            if (typeDeclNode.TryGetProperty<IReadOnlyCollection<string>>("InheritsFromTypeFullNames", out IReadOnlyCollection<string>? baseTypes))
            {
                foreach (string baseType in baseTypes.Where(baseType => !string.IsNullOrWhiteSpace(baseType)))
                {
                    discoveredTypes.Add(baseType);
                }
            }
        }

        IReadOnlySet<string> existingTypeFullNames = builder.Graph
            .GetNodes(CpgNodeKind.Type)
            .Where(node => node.TryGetProperty<string>("FullName", out string? fullName) && !string.IsNullOrWhiteSpace(fullName))
            .Select(node => node.TryGetProperty<string>("FullName", out string? fullName) ? fullName! : string.Empty)
            .ToHashSet(StringComparer.Ordinal);

        foreach (string typeFullName in discoveredTypes.Where(typeFullName => !existingTypeFullNames.Contains(typeFullName)).OrderBy(name => name, StringComparer.Ordinal))
        {
            CpgNode typeNode = builder.CreateNode(CpgNodeKind.Type);
            typeNode.SetProperty("Name", GetShortName(typeFullName));
            typeNode.SetProperty("FullName", typeFullName);
            typeNode.SetProperty("TypeDeclFullName", typeFullName);
        }
    }

    private static string GetShortName(string typeFullName)
    {
        if (string.IsNullOrWhiteSpace(typeFullName))
        {
            return string.Empty;
        }

        int genericIndex = typeFullName.IndexOf('<');
        string typePart = genericIndex >= 0 ? typeFullName[..genericIndex] : typeFullName;
        int lastDot = typePart.LastIndexOf('.');
        return lastDot >= 0 ? typePart[(lastDot + 1)..] : typePart;
    }
}
