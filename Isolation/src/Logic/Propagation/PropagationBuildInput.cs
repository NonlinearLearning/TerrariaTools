using Domain.Propagation;
using Domain.Analysis;
using Domain.Rules;

namespace Logic.Propagation;

/// <summary>
/// 表示传播构造输入。
/// </summary>
public sealed class PropagationBuildInput
{
    /// <summary>
    /// 获取或初始化规则目标标识。
    /// </summary>
    public Guid RuleTargetId { get; init; }

    /// <summary>
    /// 获取或初始化规则编码。
    /// </summary>
    public RuleCode RuleCode { get; init; } = RuleCode.Create("unknown");

    /// <summary>
    /// 获取或初始化目标名称。
    /// </summary>
    public string TargetName { get; init; } = string.Empty;

    /// <summary>
    /// 获取或初始化候选类型。
    /// </summary>
    public CandidateKind CandidateKind { get; init; }

    /// <summary>
    /// 获取或初始化主候选原因。
    /// </summary>
    public CandidateReason PrimaryReason { get; init; }

    /// <summary>
    /// 获取或初始化附加原因集合。
    /// </summary>
    public IReadOnlyCollection<CandidateReason> AdditionalReasons { get; init; } = Array.Empty<CandidateReason>();

    /// <summary>
    /// 获取或初始化场景标签集合。
    /// </summary>
    public IReadOnlyCollection<ScenarioTag> ScenarioTags { get; init; } = Array.Empty<ScenarioTag>();

    /// <summary>
    /// 获取或初始化边界名称。
    /// </summary>
    public string BoundaryName { get; init; } = "DefaultBoundary";

    /// <summary>
    /// 获取或初始化切片方向。
    /// </summary>
    public SliceDirection SliceDirection { get; init; } = SliceDirection.Bidirectional;

    /// <summary>
    /// 获取或初始化最大深度。
    /// </summary>
    public int MaxDepth { get; init; } = 1;

    /// <summary>
    /// 获取或初始化是否包含外部引用。
    /// </summary>
    public bool IncludeExternalReferences { get; init; }

    /// <summary>
    /// 获取或初始化传播目标集合。
    /// </summary>
    public IReadOnlyCollection<string> PropagationTargets { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 获取或初始化已有候选。
    /// </summary>
    public ChangeCandidate? Candidate { get; init; }

    /// <summary>
    /// 获取或初始化分析快照。
    /// </summary>
    public AnalysisCpgSnapshot? Snapshot { get; init; }
}
