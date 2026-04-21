namespace Domain.Rules;

/// <summary>
/// 表示候选原因。
/// </summary>
public enum CandidateReason
{
    /// <summary>
    /// 未知原因。
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 数据流可达。
    /// </summary>
    DataFlowReachable = 1,

    /// <summary>
    /// 调用链命中。
    /// </summary>
    CallChainMatched = 2,

    /// <summary>
    /// 组成层冲突。
    /// </summary>
    CompositeLayerConflict = 3,

    /// <summary>
    /// 需要人工复核。
    /// </summary>
    ManualReviewRequired = 4,
}
