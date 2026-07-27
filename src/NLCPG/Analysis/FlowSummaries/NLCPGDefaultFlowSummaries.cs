namespace NLCPG.Analysis.FlowSummaries;

/// 保存内置摘要。默认列表为空；未显式登记的外部调用不建立跨过程数据流边。
public static class NLCPGDefaultFlowSummaries
{
    public static IReadOnlyList<NLCPGFlowSummary> All { get; } = Array.Empty<NLCPGFlowSummary>();

    // 按稳定键查找内置跨过程流摘要。
    public static bool TryGet(string stableKey, out NLCPGFlowSummary? summary)
    {
        summary = All.FirstOrDefault(candidate => string.Equals(
          candidate.StableKey,
          stableKey,
          StringComparison.Ordinal));
        return summary is not null;
    }
}
