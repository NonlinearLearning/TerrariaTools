using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 将 if 结构的完成态传播事实转换为结构化 Lift 记录。
public sealed class SObjectIfStructureLiftingRule : RuleDefinitionLift
{
    private static readonly RuleSemanticTag IfStructureSemanticTag = new("SObject.IfStructure");
    private static readonly RuleSemanticTag AtomicTargetSemanticTag = new("Target.Atomic");
    private static readonly RuleSemanticTag PropagatedTargetSemanticTag = new("Target.Propagated");
    private static readonly RuleSemanticTag LocalDefinitionSemanticTag = new("SObject.LocalDefinitionFromInitializer");
    private static readonly RuleSemanticTag LogicalHostSemanticTag = new("SObject.LogicalHost");
    private static readonly RuleSemanticTag IfCompletionSemanticTag = new("SObject.IfCompletion");

    private static readonly RuleConsumesContract SObjectFactsConsumes = new(new[]
    {
        new RuleConsumedSyntax(SObjectPropagationRuleBase.AtomicTargetNodeKinds, AtomicTargetSemanticTag),
        new RuleConsumedSyntax(DeleteSObjectLiftingCommon.AllowedLiftNodeKinds, PropagatedTargetSemanticTag),
        new RuleConsumedSyntax(new[] { SyntaxKind.VariableDeclarator }, LocalDefinitionSemanticTag),
        new RuleConsumedSyntax(new[] { SyntaxKind.LogicalAndExpression, SyntaxKind.LogicalOrExpression }, LogicalHostSemanticTag),
        new RuleConsumedSyntax(new[] { SyntaxKind.IfStatement, SyntaxKind.ElseClause }, IfCompletionSemanticTag)
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

    public override RuleConsumesContract Consumes => SObjectFactsConsumes;

    public override RuleProducesContract Produces => IfStructureProduces;


    public override string Name { get; } = "Lift s-object marks into if/elseif/else structure tails";

    public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds =>
      DeleteSObjectLiftingCommon.AllowedLiftNodeKinds;

    // 在 if / else if / else 已具备完整删除条件时，产出结构级 lifted mark。
    public override IEnumerable<LiftedMarkRecord> Lift(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        return DeleteSObjectIfStructureLiftingHelpers.BuildIfStructureLiftedMarks(
          context,
          RuleId,
          seedMarks,
          propagatedMarks)
          .Select(mark => mark with
          {
            Mark = mark.Mark with
            {
              OutputKind = RuleOutputKind.IfStructure,
              SemanticTag = IsIfStructureMember(mark.Mark.SyntaxNode)
                ? IfStructureSemanticTag
                : null
            }
          });
    }

    private static bool IsIfStructureMember(Microsoft.CodeAnalysis.SyntaxNode syntaxNode)
    {
        return syntaxNode.RawKind is (int)SyntaxKind.IfStatement or (int)SyntaxKind.ElseClause;
    }
}
