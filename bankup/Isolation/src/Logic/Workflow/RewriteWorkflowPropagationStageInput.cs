using Domain.Analysis;
using Domain.Marking;
using Domain.Propagation;
using Domain.Rules;

namespace Logic.Workflow;

/// <summary>
/// 表示工作流传播阶段输入。
/// </summary>
public sealed class RewriteWorkflowPropagationStageInput
{
    public Guid RuleTargetId { get; init; }

    public string RuleCode { get; init; } = string.Empty;

    public string TargetName { get; init; } = string.Empty;

    public CandidateKind CandidateKind { get; init; }

    public CandidateReason PrimaryReason { get; init; }

    public IReadOnlyCollection<CandidateReason> AdditionalReasons { get; init; } = Array.Empty<CandidateReason>();

    public IReadOnlyCollection<ScenarioTag> ScenarioTags { get; init; } = Array.Empty<ScenarioTag>();

    public string BoundaryName { get; init; } = string.Empty;

    public SliceDirection SliceDirection { get; init; }

    public int MaxDepth { get; init; }

    public bool IncludeExternalReferences { get; init; }

    public IReadOnlyCollection<string> PropagationTargets { get; init; } = Array.Empty<string>();

    public ChangeCandidate? Candidate { get; init; }

    public AnalysisCpgSnapshot? Snapshot { get; init; }

    public static RewriteWorkflowPropagationStageInput Create(
        Guid ruleTargetId,
        string ruleCode,
        string targetName,
        CandidateKind candidateKind,
        CandidateReason primaryReason,
        IReadOnlyCollection<CandidateReason> additionalReasons,
        IReadOnlyCollection<ScenarioTag> scenarioTags,
        string boundaryName,
        SliceDirection sliceDirection,
        int maxDepth,
        bool includeExternalReferences,
        IReadOnlyCollection<string> propagationTargets,
        ChangeCandidate? candidate,
        AnalysisCpgSnapshot? snapshot)
    {
        return new()
        {
            RuleTargetId = ruleTargetId,
            RuleCode = ruleCode,
            TargetName = targetName,
            CandidateKind = candidateKind,
            PrimaryReason = primaryReason,
            AdditionalReasons = additionalReasons,
            ScenarioTags = scenarioTags,
            BoundaryName = boundaryName,
            SliceDirection = sliceDirection,
            MaxDepth = maxDepth,
            IncludeExternalReferences = includeExternalReferences,
            PropagationTargets = propagationTargets,
            Candidate = candidate,
            Snapshot = snapshot,
        };
    }
}
