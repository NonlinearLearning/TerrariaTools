using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Analysis.ExpressionPropagation;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// Propagates an atomic logical operand to its containing logical expression chain.
[NLISSN.Core.Pipeline.RuleRegistration(NLISSN.Core.Pipeline.RuleFeature.Core)]
public sealed class LogicalExpressionPropagationRule : ExpressionFlowPropagationRuleBase
{
    private static readonly IReadOnlyList<SyntaxKind> LogicalExpressionNodeKinds =
      new[]
      {
        SyntaxKind.LogicalAndExpression,
        SyntaxKind.LogicalOrExpression
      };

    private static readonly IReadOnlyList<SyntaxKind> UnaryExpressionNodeKinds =
      new[]
      {
        SyntaxKind.LogicalNotExpression,
        SyntaxKind.UnaryPlusExpression,
        SyntaxKind.UnaryMinusExpression,
        SyntaxKind.BitwiseNotExpression,
        SyntaxKind.PreIncrementExpression,
        SyntaxKind.PreDecrementExpression,
        SyntaxKind.PostIncrementExpression,
        SyntaxKind.PostDecrementExpression,
        SyntaxKind.AddressOfExpression,
        SyntaxKind.AwaitExpression,
        SyntaxKind.SuppressNullableWarningExpression
      };

    private static readonly RuleProducesContract LogicalExpressionProduces = new(
      new[]
      {
        new RuleProducedSyntax(LogicalExpressionNodeKinds, RuleFactKind.FlowLogicalExpression),
        new RuleProducedSyntax(UnaryExpressionNodeKinds, RuleFactKind.FlowUnaryExpression),
        new RuleProducedSyntax(
          new[] { SyntaxKind.ConditionalExpression },
          RuleFactKind.FlowConditionalExpression),
        new RuleProducedSyntax(
          ExpressionFlowPropagationRuleBase.TargetExpressionInputNodeKinds,
          RuleFactKind.TargetExpression)
      });

    private static readonly RuleConsumesContract LogicalExpressionConsumes = new(
      new[]
      {
        new RuleConsumedSyntax(
          ExpressionFlowPropagationRuleBase.TargetExpressionNodeKinds,
          RuleFactKind.TargetExpression),
        new RuleConsumedSyntax(
          LogicalExpressionNodeKinds,
          RuleFactKind.FlowLogicalExpression),
        new RuleConsumedSyntax(
          new[] { SyntaxKind.IdentifierName },
          RuleFactKind.FlowSymbolReference)
      });


    public override string RuleId { get; } = "propagate.target.logical-expression";

    public override RuleConsumesContract Consumes => LogicalExpressionConsumes;

    public override RuleProducesContract Produces => LogicalExpressionProduces;

    public override string Name { get; } = "Propagate atomic logical operands to logical expression hosts";

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds
      => LogicalExpressionNodeKinds.Concat(
          UnaryExpressionNodeKinds)
        .Append(SyntaxKind.ConditionalExpression)
        .Concat(
          ExpressionFlowPropagationRuleBase.TargetExpressionInputNodeKinds)
        .Distinct()
        .ToArray();

    public override IEnumerable<PropagatedMarkRecord> Propagate(
      IPropagationRuleContext context,
      IReadOnlyList<MarkRecord> seedMarks)
    {
        var emittedHosts = new HashSet<(int Start, int Length, int RawKind)>();
        foreach (var seedMark in seedMarks)
        {
            if (seedMark.SyntaxNode is not ExpressionSyntax expression)
            {
                continue;
            }

            var path = context.ResolveExpressionTopology(expression);
            if (path.Steps.Count == 0)
            {
                continue;
            }

            var step = path.Steps[0];
            if (step.Kind == ExpressionTopologyKind.Unary)
            {
                yield return CreateOperatorHostMark(
                  step.Host,
                  RuleFactKind.FlowUnaryExpression,
                  "Atomic operand is marked; propagate to its unary expression.",
                  seedMark,
                  new ExpressionTopologyPayload(step, 0));
                continue;
            }

            if (step.Kind == ExpressionTopologyKind.Conditional)
            {
                yield return CreateOperatorHostMark(
                  step.Host,
                  RuleFactKind.FlowConditionalExpression,
                  "Atomic operand is marked; retain the conditional expression as one propagation boundary.",
                  seedMark,
                  new ExpressionTopologyPayload(step, 0));
                continue;
            }

            if (step.Kind is ExpressionTopologyKind.Grouping or ExpressionTopologyKind.Wrapper)
            {
                yield return CreateOperatorHostMark(
                  step.Host,
                  RuleFactKind.TargetExpression,
                  "Expression is preserved through an explicit topology wrapper.",
                  seedMark,
                  new ExpressionTopologyPayload(step, 0));
                continue;
            }

            if (step.Host is not BinaryExpressionSyntax binaryExpression)
            {
                continue;
            }

            if (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression) ||
                binaryExpression.IsKind(SyntaxKind.LogicalOrExpression))
            {
                if (emittedHosts.Add(BuildNodeKey(binaryExpression)))
                {
                    yield return CreateOperatorHostMark(
                      binaryExpression,
                      RuleFactKind.FlowLogicalExpression,
                      "Expression topology identifies the direct logical host.",
                      seedMark,
                      new ExpressionTopologyPayload(step, 0));
                }

                if (step.Mode != ExpressionPropagationMode.SiblingAndContinue ||
                    !CanUseDefaultLogicalReduction(step))
                {
                    continue;
                }

                foreach (var sibling in step.DirectOperands.Where(operand => !ReferenceEquals(operand, step.Input)))
                {
                    if (ExpressionFlowPropagationRuleBase.TargetExpressionInputNodeKinds.Contains(sibling.Kind()))
                    {
                        yield return CreateOperatorHostMark(
                          sibling,
                          RuleFactKind.TargetExpression,
                          "Logical-and topology permits its direct sibling operand.",
                          seedMark,
                          new ExpressionTopologyPayload(step, 0));
                    }
                }

                continue;
            }

            if (step.Mode == ExpressionPropagationMode.AggregateOnly &&
                CanUseDefaultLogicalReduction(step) &&
                ExpressionFlowPropagationRuleBase.TargetExpressionInputNodeKinds.Contains(binaryExpression.Kind()))
            {
                yield return CreateOperatorHostMark(
                  binaryExpression,
                  RuleFactKind.TargetExpression,
                  "Binary operand is marked; propagate to its complete binary expression.",
                  seedMark,
                  new ExpressionTopologyPayload(step, 0));
            }
        }
    }

    private PropagatedMarkRecord CreateOperatorHostMark(
      ExpressionSyntax syntaxNode,
      RuleFactKind semanticTag,
      string reason,
      MarkRecord seedMark,
      ExpressionTopologyPayload payload)
    {
        return new PropagatedMarkRecord(
          RuleId,
          MarkRecordFactory.Create(
            RuleId,
            syntaxNode,
            reason,
            factKind: semanticTag),
          seedMark,
          1,
          payload);
    }

    private static bool CanUseDefaultLogicalReduction(ExpressionTopologyStep step) =>
      !step.OperatorFacts.HasOperatorMethod && !step.OperatorFacts.IsChecked && !step.OperatorFacts.IsLifted;

}
