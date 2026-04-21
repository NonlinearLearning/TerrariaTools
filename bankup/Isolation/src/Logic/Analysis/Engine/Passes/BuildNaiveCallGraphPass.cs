using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Passes;

/// <summary>
/// 使用最朴素的名字匹配方式补齐调用图。
///
/// 这个 pass 直接参考 Joern 的 `NaiveCallLinker`：
/// - 先把方法节点按名字分组；
/// - 再找所有尚未建立 `Call` 关系的调用节点；
/// - 如果调用名和某组方法名一致，就先连上。
///
/// 这个策略并不精准，但它有现实价值：
/// - 第一批实现成本低；
/// - 可以尽快让图具备“基础可导航性”；
/// - 后面可以再被静态解析版调用图覆盖。
/// </summary>
public sealed class BuildNaiveCallGraphPass : CpgPass
{

    protected override void Execute(CpgGraphBuilder builder)
    {
        IReadOnlyDictionary<string, List<CpgNode>> methodsByName = builder.Graph
            .GetNodes(CpgNodeKind.Method)
            .Where(node => node.TryGetProperty<string>("Name", out string? name) && !string.IsNullOrWhiteSpace(name))
            .GroupBy(node => node.TryGetProperty<string>("Name", out string? name) ? name! : string.Empty, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        foreach (CpgNode callNode in builder.Graph.GetNodes(CpgNodeKind.Call))
        {
            bool hasCallRelation = builder.Graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Call).Any();
            if (hasCallRelation)
            {
                continue;
            }

            if (!callNode.TryGetProperty<string>("Name", out string? callName) || string.IsNullOrWhiteSpace(callName))
            {
                continue;
            }

            if (!methodsByName.TryGetValue(callName, out List<CpgNode>? methods))
            {
                continue;
            }

            foreach (CpgNode methodNode in methods)
            {
                builder.AddEdge(callNode.Id, methodNode.Id, CpgEdgeKind.Call);
            }

            if (methods.Count == 1 && methods[0].TryGetProperty<string>("FullName", out string? methodFullName))
            {
                callNode.SetProperty("ResolvedMethodFullName", methodFullName);
            }
        }
    }
}
