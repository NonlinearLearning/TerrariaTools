using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

[global::NLISSN.Core.Pipeline.RuleRegistration]
public sealed class LocalFunctionParameterShrinkProposalRule : ParameterUsageProposalRuleBase
{

    public override string RuleId { get; } = "propose.type.local-function-parameter-shrink";


    public override string Name { get; } = "Shrink local function parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.LocalFunctionStatement,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 为普通位置参数局部函数同步收缩声明与调用点。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in LocalFunctionUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     LocalFunctionParameterUsageMode.Positional))
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
              "Local function parameter type references the delete-class target; shrink the signature.");

            foreach (var decision in LocalFunctionUsageProposalHelpers.CreateInvocationReplaceDecisions(
                         RuleId,
                         context.SemanticModel.Compilation,
                         payload,
                         "Local function invocation passes the deleted class type argument; remove the matching positional argument."))
            {
                yield return decision;
            }
        }
    }
}

