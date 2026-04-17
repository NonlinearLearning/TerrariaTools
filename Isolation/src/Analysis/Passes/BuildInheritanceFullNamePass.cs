using Analysis.Core;

namespace Analysis.Passes;

/// <summary>
/// 把类型声明上的短继承名尽量补成稳定全名。
///
/// 这个 pass 对齐 Joern `XInheritanceFullNamePass` 的最小职责：
/// - 如果 `TYPE_DECL.InheritsFromTypeFullNames` 里还是短名；
/// - 尝试用当前图中的 `TYPE_DECL/TYPE` 名字反推唯一全名；
/// - 成功时直接覆写为全名，供后续类型层级 pass 使用。
/// </summary>
public sealed class BuildInheritanceFullNamePass : CpgPass
{
    /// <inheritdoc />
    protected override void Execute(CpgGraphBuilder builder)
    {
        Dictionary<string, List<string>> candidateFullNamesByShortName = new(StringComparer.Ordinal);

        foreach (string fullName in builder.Graph
                     .GetNodes(CpgNodeKind.TypeDecl)
                     .Concat(builder.Graph.GetNodes(CpgNodeKind.Type))
                     .Where(node => node.TryGetProperty<string>("FullName", out string? fullName) && !string.IsNullOrWhiteSpace(fullName))
                     .Select(node => node.TryGetProperty<string>("FullName", out string? fullName) ? fullName! : string.Empty)
                     .Distinct(StringComparer.Ordinal))
        {
            string shortName = GetShortName(fullName);
            if (!candidateFullNamesByShortName.TryGetValue(shortName, out List<string>? fullNames))
            {
                fullNames = new List<string>();
                candidateFullNamesByShortName[shortName] = fullNames;
            }

            if (!fullNames.Contains(fullName, StringComparer.Ordinal))
            {
                fullNames.Add(fullName);
            }
        }

        foreach (CpgNode typeDeclNode in builder.Graph.GetNodes(CpgNodeKind.TypeDecl))
        {
            if (!typeDeclNode.TryGetProperty<IReadOnlyCollection<string>>("InheritsFromTypeFullNames", out IReadOnlyCollection<string>? inheritedTypes) ||
                inheritedTypes.Count == 0)
            {
                continue;
            }

            List<string> resolvedTypes = new();
            bool changed = false;

            foreach (string inheritedType in inheritedTypes)
            {
                if (string.IsNullOrWhiteSpace(inheritedType))
                {
                    continue;
                }

                if (inheritedType.Contains('.', StringComparison.Ordinal))
                {
                    resolvedTypes.Add(inheritedType);
                    continue;
                }

                if (candidateFullNamesByShortName.TryGetValue(inheritedType, out List<string>? candidates) &&
                    candidates.Count == 1)
                {
                    resolvedTypes.Add(candidates[0]);
                    changed = true;
                    continue;
                }

                resolvedTypes.Add(inheritedType);
            }

            if (changed)
            {
                typeDeclNode.SetProperty("InheritsFromTypeFullNames", resolvedTypes.Distinct(StringComparer.Ordinal).ToArray());
            }
        }
    }

    private static string GetShortName(string fullName)
    {
        int lastDot = fullName.LastIndexOf('.');
        return lastDot >= 0 ? fullName[(lastDot + 1)..] : fullName;
    }
}
