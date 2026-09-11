using NLISSN.Core.Marking;
using NLISSN.Core.Lifting;
using NLISSN.Core.Pipeline;

namespace NLISSN.Core.Propagation;

/// 表示一次传播产生的标记，以及它来自哪个种子标记。
public sealed record PropagatedMarkRecord(
  /// 产生这条传播标记的规则标识。
    string RuleId,
  /// 传播后实际落到的新标记。
    MarkRecord Mark,
  /// 触发本次传播的源种子标记。
    MarkRecord SourceMark,
  /// 从源种子标记传播到当前标记的层级深度。
    int Depth,
    /// 传播阶段额外收集的非结构关系事实；结构结论和结构决策 payload 只能由 Lift 产生。
    object? Payload = null,
    /// 传播路径的结构化来源链；旧调用点可省略，由证据适配器补齐。
    FactProvenance? Provenance = null)
{
  public RuleEvidenceOrigin Origins => Mark.Origins | SourceMark.Origins;

  public FactCapability Capability => FactCapabilityRules.For(Mark);

  public FactCertainty Certainty => Mark.Certainty;
}
