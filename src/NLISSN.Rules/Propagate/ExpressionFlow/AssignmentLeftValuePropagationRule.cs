using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Analysis.ExpressionPropagation;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 把赋值右侧的原子命中迁移到左值，供后续符号引用和声明宿主规则继续沿“被写入的位置”扩散。
public sealed class AssignmentLeftValuePropagationRule : ExpressionFlowPropagationRuleBase
{
    private static readonly RuleProducesContract AssignmentTargetProduces = new(new[]
    {
        new RuleProducedSyntax(ExpressionFlowPropagationRuleBase.AssignmentTargetNodeKinds, RuleFactKind.FlowAssignmentTarget)
    });

public override string RuleId { get; } = "propagate.target.assignment-left-value";
    public override RuleProducesContract Produces => AssignmentTargetProduces;
    public override string Name { get; } = "Propagate s-object marks from assignment right values to left values";

    public override IEnumerable<PropagatedMarkRecord> Propagate(IPropagationRuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
        foreach (var seedMark in seedMarks)
        {
            if (seedMark.SyntaxNode is not ExpressionSyntax expression)
            {
                continue;
            }

            foreach (var step in context.ResolveExpressionTopology(expression).Steps)
            {
                if (step.Host is AssignmentExpressionSyntax assignmentExpression &&
                    ReferenceEquals(step.Input, assignmentExpression.Right))
                {
                    yield return new PropagatedMarkRecord(
                      RuleId,
                      MarkRecordFactory.Create(
                        RuleId,
                        assignmentExpression.Left,
                        "Assignment right value is marked; propagate mark to assignment left value."),
                      seedMark,
                      1);
                    break;
                }
            }
        }
    }
}
