using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 把已传播的原子命中提升到可单独改写的表达式宿主，避免 Proposal 重复向上遍历语法树。
public sealed class ExpressionHostLiftingRule : RuleDefinitionLift
{
    private static readonly RuleSemanticTag ExpressionHostSemanticTag = RuleFactPorts.LiftExpressionHost;
    private static readonly RuleSemanticTag TargetSemanticTag = RuleFactPorts.TargetExpression;

    private static readonly RuleConsumesContract TargetFactsConsumes = new(new[]
    {
        new RuleConsumedSyntax(ExpressionFlowPropagationRuleBase.TargetExpressionInputNodeKinds, TargetSemanticTag),
        new RuleConsumedSyntax(ExpressionFlowPropagationRuleBase.AssignmentTargetNodeKinds, RuleFactPorts.FlowAssignmentTarget),
        new RuleConsumedSyntax(new[] { SyntaxKind.VariableDeclarator }, RuleFactPorts.FlowLocalDefinition),
        new RuleConsumedSyntax(new[] { SyntaxKind.IdentifierName }, RuleFactPorts.FlowSymbolReference),
    });

    private static readonly RuleProducesContract ExpressionHostProduces = new(
      new[]
      {
        new RuleProducedSyntax(
          LiftingCommon.AllowedLiftNodeKinds,
          ExpressionHostSemanticTag)
      });

    public override string CapabilityId { get; } = "lift.target.expression-host";

    public override string RuleId { get; } = "DEL-SOBJ-LIFT-HOST-001";

    public override RuleConsumesContract Consumes => TargetFactsConsumes;

    public override RuleProducesContract Produces => ExpressionHostProduces;


    public override string Name { get; } = "Lift s-object marks to direct expression and statement hosts";

    public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds =>
      LiftingCommon.AllowedLiftNodeKinds;

    // 把 s-object 原子命中提升到最小可改写宿主，避免提案阶段直接操作脆弱的子表达式。
    public override IEnumerable<LiftedMarkRecord> Lift(ILiftRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        return ExpressionHostLiftingHelpers.BuildHostLiftedMarks(
          context,
          RuleId,
          seedMarks,
          propagatedMarks)
          .Select(mark => mark with
          {
            Mark = mark.Mark with
            {
              OutputKind = RuleOutputKind.ExpressionHost,
              SemanticTag = ExpressionHostSemanticTag
            }
          });
    }
}
