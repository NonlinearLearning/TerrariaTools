using Microsoft.CodeAnalysis;

namespace NLISSN.Core.Analysis.ExpressionPropagation;

/// <summary>Preserves the topology step that authorized a propagated expression fact.</summary>
public sealed record ExpressionTopologyPayload(ExpressionTopologyStep Step, int StepIndex)
{
    public bool CanContinueOutward => Step.CanContinueOutward;
}
