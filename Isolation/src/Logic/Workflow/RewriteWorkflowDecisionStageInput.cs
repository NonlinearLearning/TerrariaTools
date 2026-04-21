using Domain.Decision;
using Logic.Propagation;

namespace Logic.Workflow;

/// <summary>
/// 表示工作流决策阶段输入。
/// </summary>
public sealed class RewriteWorkflowDecisionStageInput
{
    public PropagationResolution Propagation { get; init; } = null!;

    public bool IncludeExternalReferences { get; init; }

    public bool SimulateFailure { get; init; }

    public IReadOnlyCollection<string> ProtectionRules { get; init; } = Array.Empty<string>();

    public IReadOnlyCollection<string> ConflictTargets { get; init; } = Array.Empty<string>();

    public ConfidenceLevel ConfidenceLevel { get; init; } = ConfidenceLevel.Medium;

    public bool ForceReject { get; init; }

    public static RewriteWorkflowDecisionStageInput Create(
        PropagationResolution propagation,
        bool includeExternalReferences,
        bool simulateFailure,
        IReadOnlyCollection<string> protectionRules,
        IReadOnlyCollection<string> conflictTargets,
        ConfidenceLevel confidenceLevel,
        bool forceReject)
    {
        return new()
        {
            Propagation = propagation,
            IncludeExternalReferences = includeExternalReferences,
            SimulateFailure = simulateFailure,
            ProtectionRules = protectionRules,
            ConflictTargets = conflictTargets,
            ConfidenceLevel = confidenceLevel,
            ForceReject = forceReject,
        };
    }
}
