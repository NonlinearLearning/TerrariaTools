using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

[NLISSN.Core.Pipeline.RuleRegistration(NLISSN.Core.Pipeline.RuleFeature.Core)]
public sealed class NamedArgumentLocalFunctionParameterShrinkProposalRule : ParameterUsageProposalRuleBase
{

    public override string RuleId { get; } = "propose.type.named-argument-local-function-parameter-shrink";


    public override string Name { get; } = "Shrink local function parameters whose type references the delete-class target when callsites use named arguments";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.LocalFunctionStatement,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 针对命名参数局部函数调用，同步收缩声明与命名实参。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in LocalFunctionUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     LocalFunctionParameterUsageMode.NamedArgument))
        {
            if (!LocalFunctionUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementLocalFunction))
            {
                continue;
            }

            yield return ReplaceDecisionFactory.CreateLocalFunctionReplaceDecision(
              RuleId,
              payload.LocalFunction,
              replacementLocalFunction,
              "Local function parameter type references the delete-class target; shrink the signature for named-argument callsites.");

            foreach (var decision in LocalFunctionUsageProposalHelpers.CreateInvocationReplaceDecisions(
                         RuleId,
                         context.SemanticModel.Compilation,
                         payload,
                         "Local function invocation passes the deleted class type value by name; remove the matching named argument."))
            {
                yield return decision;
            }
        }
    }
}

