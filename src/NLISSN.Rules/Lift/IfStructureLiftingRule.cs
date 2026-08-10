using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Core.Decision;

namespace NLISSN.Rules;

/// 将 if 结构的完成态传播事实转换为结构化 Lift 记录。
public sealed class IfStructureLiftingRule : RuleDefinitionLift
{
    private static readonly RuleSemanticTag IfStructureSemanticTag = RuleFactPorts.LiftIfStructure;
    private static readonly RuleSemanticTag ExpressionHostSemanticTag = RuleFactPorts.LiftExpressionHost;

    private static readonly RuleConsumesContract AtomicFactsConsumes = new(new[]
    {
        new RuleConsumedSyntax(ExpressionFlowPropagationRuleBase.TargetExpressionInputNodeKinds, RuleFactPorts.TargetExpression),
        new RuleConsumedSyntax(ExpressionFlowPropagationRuleBase.AssignmentTargetNodeKinds, RuleFactPorts.FlowAssignmentTarget),
        new RuleConsumedSyntax(new[] { SyntaxKind.VariableDeclarator }, RuleFactPorts.FlowLocalDefinition),
        new RuleConsumedSyntax(new[] { SyntaxKind.IdentifierName }, RuleFactPorts.FlowSymbolReference),
        new RuleConsumedSyntax(LiftingCommon.AllowedLiftNodeKinds, ExpressionHostSemanticTag),
    });

    private static readonly RuleProducesContract IfStructureProduces = new(
      new[]
      {
        new RuleProducedSyntax(
          new[] { SyntaxKind.IfStatement, SyntaxKind.ElseClause },
          IfStructureSemanticTag)
      });

    public override string CapabilityId { get; } = "lift.target.if-structure";

    public override string RuleId { get; } = "DEL-SOBJ-LIFT-IF-001";

    public override RuleConsumesContract Consumes => AtomicFactsConsumes;

    public override RuleProducesContract Produces => IfStructureProduces;


    public override string Name { get; } = "Lift s-object marks into if/elseif/else structure tails";

    public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds =>
      LiftingCommon.AllowedLiftNodeKinds;

    // 在 if / else if / else 已具备完整删除条件时，产出结构级 lifted mark。
    public override IEnumerable<LiftedMarkRecord> Lift(ILiftRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        var hostMarks = ExpressionHostLiftingHelpers.BuildHostLiftedMarks(
            context,
            RuleId,
            seedMarks,
            propagatedMarks)
          .Select(mark => mark.Mark with
          {
            OutputKind = RuleOutputKind.ExpressionHost,
            SemanticTag = ExpressionHostSemanticTag
          })
          .ToList();
        return IfStructureLiftingHelpers.BuildIfStructureLiftedMarks(
          context,
          RuleId,
          seedMarks.Concat(hostMarks).ToList(),
          propagatedMarks)
          .Select(mark =>
          {
            var payload = IfStructureLiftingHelpers.TryBuildPayload(
              context,
              mark.Mark.SyntaxNode,
              seedMarks.Concat(hostMarks).Concat(propagatedMarks.Select(item => item.Mark)).ToList());
            return mark with
            {
              Mark = mark.Mark with
              {
                OutputKind = RuleOutputKind.IfStructure,
                SemanticTag = IsIfStructureMember(mark.Mark.SyntaxNode) ? IfStructureSemanticTag : null
              },
              StructureKind = payload is null ? null : StructuralKind.If,
              Payload = payload
            };
          });
    }

    public override IEnumerable<LiftedMarkRecord> Lift(
      ILiftRuleContext context,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
      IReadOnlyList<LiftedMarkRecord> existingLiftedMarks)
    {
        var hostMarks = existingLiftedMarks
          .Where(mark => mark.Mark.SemanticTag == ExpressionHostSemanticTag &&
            mark.Mark.SyntaxNode.RawKind is not (int)SyntaxKind.LogicalAndExpression and not (int)SyntaxKind.LogicalOrExpression)
          .Select(mark => mark.Mark)
          .ToList();
        return Lift(context, seedMarks.Concat(hostMarks).ToList(), propagatedMarks);
    }

    private static bool IsIfStructureMember(Microsoft.CodeAnalysis.SyntaxNode syntaxNode)
    {
        return syntaxNode.RawKind is (int)SyntaxKind.IfStatement or (int)SyntaxKind.ElseClause;
    }
}
