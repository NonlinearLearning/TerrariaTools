using NLISSN.Core.Propagation;
using NLISSN.Rules;

namespace NLISSN.Rules;

/// 从声明宿主传播事实中筛出唯一宿主，避免同一语法节点产生重复决策。
public static class DeclarationHostProposalHelpers
{
    // 按宿主种类提取传播 payload，并用宿主节点键去重，避免重复生成同类决策。
    public static IEnumerable<DeclarationHostPayload> EnumeratePayloads(IReadOnlyList<PropagatedMarkRecord> propagatedMarks, DeclarationHostKind kind)
    {
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var propagatedMark in propagatedMarks)
        {
            if (propagatedMark.Payload is not DeclarationHostPayload payload ||
                payload.Kind != kind)
            {
                continue;
            }

            var key = DecisionCpgFactory.BuildNodeKey(payload.HostDeclaration);
            if (!seenKeys.Add(key))
            {
                continue;
            }

            yield return payload;
        }
    }
}
