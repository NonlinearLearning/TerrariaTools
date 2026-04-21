using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Passes;

/// <summary>
/// 为节点补齐“求值后类型”的关系。
///
/// 这个 pass 对应 Joern 的 `TypeEvalPass` 思路：
/// - 先找到图中所有 `TYPE` 节点；
/// - 再把拥有 `TypeFullName` 的表达式或声明节点连到对应 `TYPE` 节点。
///
/// 当前实现先服务阶段二的最小目标：
/// - 节点只要带有 `TypeFullName` 就参与；
/// - 关系统一写成 `EvalType`；
/// - 不做复杂推断，只做已知结果的归档。
/// </summary>
public sealed class EvaluateNodeTypesPass : CpgPass
{

    protected override void Execute(CpgGraphBuilder builder)
    {
        IReadOnlyDictionary<string, CpgNode> types = builder.Graph
            .GetNodes(CpgNodeKind.Type)
            .Where(node => node.TryGetProperty<string>("FullName", out _))
            .ToDictionary(
                node => node.TryGetProperty<string>("FullName", out string? name) ? name! : string.Empty,
                node => node,
                StringComparer.Ordinal);

        foreach (CpgNode source in builder.Graph.Nodes)
        {
            if (!source.TryGetProperty<string>("TypeFullName", out string? typeFullName))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(typeFullName))
            {
                continue;
            }

            if (types.TryGetValue(typeFullName, out CpgNode? target))
            {
                bool relationExists = builder.Graph
                    .GetOutgoingEdges(source.Id, CpgEdgeKind.EvalType)
                    .Any(edge => edge.TargetId == target.Id);

                if (!relationExists)
                {
                    builder.AddEdge(source.Id, target.Id, CpgEdgeKind.EvalType);
                }
            }
        }
    }
}
