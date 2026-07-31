using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// Propagates an atomic logical operand to its containing logical expression chain.
public sealed class LogicalExpressionPropagationRule : ExpressionFlowPropagationRuleBase
{
    private static readonly IReadOnlyList<SyntaxKind> LogicalExpressionNodeKinds =
      new[]
      {
        SyntaxKind.LogicalAndExpression,
        SyntaxKind.LogicalOrExpression
      };

    private static readonly RuleProducesContract LogicalExpressionProduces = new(
      new[]
      {
        new RuleProducedSyntax(LogicalExpressionNodeKinds, RuleFactPorts.FlowLogicalExpression)
      });

    public override string CapabilityId { get; } = "propagate.target.logical-expression";

    public override string RuleId { get; } = "DEL-SOBJ-PROP-LOGICAL-OPERAND-001";

    public override RuleProducesContract Produces => LogicalExpressionProduces;

    public override string Name { get; } = "Propagate atomic logical operands to logical expression hosts";

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds => LogicalExpressionNodeKinds;

    public override IEnumerable<PropagatedMarkRecord> Propagate(
      IPropagationRuleContext context,
      IReadOnlyList<MarkRecord> seedMarks)
    {
        _ = context;
        var emittedHosts = new HashSet<(int Start, int Length, int RawKind)>();
        foreach (var seedMark in seedMarks)
        {
            if (seedMark.SyntaxNode is not ExpressionSyntax expression)
            {
                continue;
            }

            var logicalHost = FindLogicalHost(expression);
            if (logicalHost is null || !emittedHosts.Add(BuildNodeKey(logicalHost)))
            {
                continue;
            }

            yield return new PropagatedMarkRecord(
              RuleId,
              MarkRecordFactory.Create(
                RuleId,
                logicalHost,
                "Atomic logical operand is marked; propagate mark to logical expression host.",
                semanticTag: RuleFactPorts.FlowLogicalExpression),
              seedMark,
              1);
        }
    }

    private static BinaryExpressionSyntax? FindLogicalHost(ExpressionSyntax expression)
    {
        var operand = UnwrapLogicalOperand(expression);
        if (operand.Parent is not BinaryExpressionSyntax immediateHost ||
            !IsLogicalExpression(immediateHost) ||
            !IsOperandOf(immediateHost, operand))
        {
            return null;
        }

        var host = immediateHost;
        while (host.Parent is BinaryExpressionSyntax parent &&
               parent.IsKind(host.Kind()) &&
               IsOperandOf(parent, host))
        {
            host = parent;
        }

        return host;
    }

    private static ExpressionSyntax UnwrapLogicalOperand(ExpressionSyntax expression)
    {
        var operand = expression;
        while ((operand.Parent is ParenthesizedExpressionSyntax parenthesized &&
                ReferenceEquals(parenthesized.Expression, operand)) ||
               (operand.Parent is PrefixUnaryExpressionSyntax logicalNot &&
                logicalNot.IsKind(SyntaxKind.LogicalNotExpression) &&
                ReferenceEquals(logicalNot.Operand, operand)))
        {
            operand = (ExpressionSyntax)operand.Parent;
        }

        return operand;
    }

    private static bool IsLogicalExpression(BinaryExpressionSyntax expression)
    {
        return expression.IsKind(SyntaxKind.LogicalAndExpression) ||
          expression.IsKind(SyntaxKind.LogicalOrExpression);
    }

    private static bool IsOperandOf(BinaryExpressionSyntax host, ExpressionSyntax operand)
    {
        return ReferenceEquals(host.Left, operand) || ReferenceEquals(host.Right, operand);
    }
}
