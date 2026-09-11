using Microsoft.CodeAnalysis.Text;
using NLISSN.Core.Decision;

namespace NLISSN.Core.Rewrite;

/// <summary>
/// Describes the intentionally permitted difference introduced by one rule.
/// </summary>
public sealed record RuleTransformationContract(
    string ContractId,
    IReadOnlySet<DecisionActionKind> AllowedActions,
    IReadOnlySet<RewriteControlFlowEffect> ControlFlowEffects,
    bool RequiresNoUnexpectedDiagnostics,
    bool RequiresPreservedUnmarkedSiblings)
{
    /// <summary>
    /// Validates that a resolved decision witness is compatible with this contract.
    /// </summary>
    public void ValidateWitness(RewriteDecisionWitness witness)
    {
        ArgumentNullException.ThrowIfNull(witness);

        if (!string.Equals(ContractId, witness.ContractId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Witness contract '{witness.ContractId}' does not match '{ContractId}'.");
        }

        if (!AllowedActions.Contains(witness.Action))
        {
            throw new InvalidOperationException(
                $"Contract '{ContractId}' does not allow action '{witness.Action}'.");
        }

        if (RequiresPreservedUnmarkedSiblings &&
            !witness.Effects.Any(effect => effect != RewriteControlFlowEffect.None))
        {
            throw new InvalidOperationException(
                $"Contract '{ContractId}' requires a declared structural effect.");
        }

        if (witness.Effects.Any(effect => !ControlFlowEffects.Contains(effect)))
        {
            throw new InvalidOperationException(
                $"Witness declares an effect that contract '{ContractId}' does not allow.");
        }
    }

    /// <summary>
    /// Validates that every planned edit is covered by the final decision authorization.
    /// </summary>
    public void ValidatePlanOperations(
        RewriteDecisionWitness witness,
        IReadOnlyList<RewritePlanEdit> operations)
    {
        ValidateWitness(witness);
        ArgumentNullException.ThrowIfNull(operations);

        foreach (var operation in operations)
        {
            var operationSpan = TextSpan.FromBounds(operation.Start, operation.Start + operation.Length);
            if (!witness.AuthorizedSpans.Any(span => span.Contains(operationSpan)))
            {
                throw new InvalidOperationException(
                    $"Rewrite operation at '{operation.Start}' is not covered by contract '{ContractId}'.");
            }
        }
    }
}

/// <summary>
/// Declares the control-flow fact a structural transformation is permitted to change.
/// </summary>
public enum RewriteControlFlowEffect
{
    None,
    SimplifyLogicalCondition,
    RemoveThenBranch,
    PromoteElseBranch,
    RemoveLoop,
    RemoveSwitchSection,
    PreserveFinally,
    ReplaceReturnValue,
}
