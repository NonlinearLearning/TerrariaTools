using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Passes;

/// <summary>
/// 把字段访问 `CALL` 连接到被访问的 `MEMBER`。
///
/// 这个 pass 对齐 Joern `FieldAccessLinkerPass` 的最小职责：
/// - 当前前端把字段访问建模为名字为 `.` 的 `CALL`；
/// - `CALL.FieldFullName` 保存目标成员全名；
/// - pass 负责补一条 `REF` 到目标 `MEMBER`。
/// </summary>
public sealed class BuildFieldAccessRelationPass : CpgPass
{

    protected override void Execute(CpgGraphBuilder builder)
    {
        IReadOnlyDictionary<string, CpgNode> membersByFullName = builder.Graph
            .GetNodes(CpgNodeKind.Member)
            .Where(node => node.TryGetProperty<string>("FullName", out string? fullName) &&
                           !string.IsNullOrWhiteSpace(fullName))
            .GroupBy(node => node.TryGetProperty<string>("FullName", out string? fullName) ? fullName! : string.Empty)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (CpgNode callNode in builder.Graph.GetNodes(CpgNodeKind.Call))
        {
            if (!callNode.TryGetProperty<string>("Name", out string? name) ||
                !string.Equals(name, ".", StringComparison.Ordinal) ||
                !callNode.TryGetProperty<string>("FieldFullName", out string? fieldFullName) ||
                string.IsNullOrWhiteSpace(fieldFullName) ||
                !membersByFullName.TryGetValue(fieldFullName, out CpgNode? memberNode))
            {
                continue;
            }

            bool relationExists = builder.Graph
                .GetOutgoingEdges(callNode.Id, CpgEdgeKind.Ref)
                .Any(edge => edge.TargetId == memberNode.Id);

            if (!relationExists)
            {
                builder.AddEdge(callNode.Id, memberNode.Id, CpgEdgeKind.Ref);
            }
        }
    }
}
