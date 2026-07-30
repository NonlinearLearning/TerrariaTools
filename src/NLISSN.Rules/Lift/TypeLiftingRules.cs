using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 删除类链路的表达式宿主提升规则。
public sealed class ClassExpressionHostLiftingRule : RuleDefinitionLift
{
    private static readonly RuleSemanticTag ExpressionHostSemanticTag = new("Class.ExpressionHost");

    private static readonly RuleConsumesContract ClassFactsConsumes = ClassLiftContracts.CreateFactsConsumes();

    private static readonly RuleProducesContract ExpressionHostProduces = new(
      new[]
      {
        new RuleProducedSyntax(
          DeleteSObjectLiftingCommon.AllowedLiftNodeKinds,
          ExpressionHostSemanticTag)
      });

    public override string CapabilityId { get; } = "lift.type.expression-host";

    public override string RuleId { get; } = "DEL-CLASS-LIFT-HOST-001";

    public override RuleConsumesContract Consumes => ClassFactsConsumes;

    public override RuleProducesContract Produces => ExpressionHostProduces;


    public override string Name { get; } = "Lift delete-class marks to direct expression and statement hosts";

    public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds =>
      DeleteSObjectLiftingCommon.AllowedLiftNodeKinds;

    // 复用通用宿主提升逻辑，把删除类命中提升到最小可改写的表达式或语句宿主。
    public override IEnumerable<LiftedMarkRecord> Lift(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        return DeleteSObjectHostLiftingHelpers.BuildHostLiftedMarks(
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

/// 删除类链路的 if 结构完成态提升规则。
public sealed class ClassIfStructureLiftingRule : RuleDefinitionLift
{
    private static readonly RuleSemanticTag IfStructureSemanticTag = new("Class.IfStructure");

    private static readonly RuleConsumesContract ClassFactsConsumes = ClassLiftContracts.CreateFactsConsumes();

    private static readonly RuleProducesContract IfStructureProduces = new(
      new[]
      {
        new RuleProducedSyntax(
          new[] { SyntaxKind.IfStatement, SyntaxKind.ElseClause },
          IfStructureSemanticTag)
      });

    public override string CapabilityId { get; } = "lift.type.if-structure";

    public override string RuleId { get; } = "DEL-CLASS-LIFT-IF-001";

    public override RuleConsumesContract Consumes => ClassFactsConsumes;

    public override RuleProducesContract Produces => IfStructureProduces;


    public override string Name { get; } = "Lift delete-class marks into if/elseif/else structure tails";

    public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds =>
      DeleteSObjectLiftingCommon.AllowedLiftNodeKinds;

    // 仅在 if 结构已形成完整删除条件时，补出后续提案需要的 lifted mark。
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

internal static class ClassLiftContracts
{
    public static RuleConsumesContract CreateFactsConsumes()
    {
        return new RuleConsumesContract(new[]
        {
          new RuleConsumedSyntax(new[] { SyntaxKind.ClassDeclaration }, new RuleSemanticTag("Class.DeclarationTarget")),
        new RuleConsumedSyntax(
          new[]
          {
            SyntaxKind.IdentifierName, SyntaxKind.SimpleMemberAccessExpression,
            SyntaxKind.MemberBindingExpression, SyntaxKind.InvocationExpression,
            SyntaxKind.ElementAccessExpression, SyntaxKind.ConditionalAccessExpression,
            SyntaxKind.ObjectCreationExpression, SyntaxKind.ImplicitObjectCreationExpression
          },
          new RuleSemanticTag("Class.ExpressionTarget")),
        new RuleConsumedSyntax(
          new[] { SyntaxKind.IdentifierName, SyntaxKind.QualifiedName, SyntaxKind.AliasQualifiedName, SyntaxKind.GenericName },
          new RuleSemanticTag("Class.TypeSyntaxTarget")),
        new RuleConsumedSyntax(
          new[]
          {
            SyntaxKind.BaseList, SyntaxKind.DelegateDeclaration, SyntaxKind.EventDeclaration,
            SyntaxKind.EventFieldDeclaration, SyntaxKind.FieldDeclaration, SyntaxKind.IndexerDeclaration,
            SyntaxKind.LocalDeclarationStatement, SyntaxKind.MethodDeclaration,
            SyntaxKind.PropertyDeclaration, SyntaxKind.SimpleBaseType
          },
          new RuleSemanticTag("Class.DeclarationHost")),
        new RuleConsumedSyntax(new[] { SyntaxKind.VariableDeclarator }, new RuleSemanticTag("Class.LocalDefinitionFromObjectCreation")),
        new RuleConsumedSyntax(new[] { SyntaxKind.IdentifierName }, new RuleSemanticTag("Class.SymbolReference")),
        new RuleConsumedSyntax(new[] { SyntaxKind.IfStatement, SyntaxKind.ElseClause }, new RuleSemanticTag("Class.IfCompletion")),
        new RuleConsumedSyntax(new[] { SyntaxKind.MethodDeclaration, SyntaxKind.InvocationExpression }, new RuleSemanticTag("Class.MethodParameterUsage")),
        new RuleConsumedSyntax(new[] { SyntaxKind.LocalFunctionStatement, SyntaxKind.InvocationExpression }, new RuleSemanticTag("Class.LocalFunctionParameterUsage")),
        new RuleConsumedSyntax(new[] { SyntaxKind.IndexerDeclaration, SyntaxKind.ElementAccessExpression }, new RuleSemanticTag("Class.IndexerParameterUsage")),
        new RuleConsumedSyntax(new[] { SyntaxKind.MethodDeclaration, SyntaxKind.InvocationExpression }, new RuleSemanticTag("Class.ExtensionMethodParameterUsage")),
        new RuleConsumedSyntax(
          new[]
          {
            SyntaxKind.DelegateDeclaration, SyntaxKind.MethodDeclaration, SyntaxKind.LocalFunctionStatement,
            SyntaxKind.ParenthesizedLambdaExpression, SyntaxKind.SimpleLambdaExpression,
            SyntaxKind.AnonymousMethodExpression, SyntaxKind.InvocationExpression
          },
          new RuleSemanticTag("Class.DelegateUsage"))
        });
    }

    public static RuleConsumesContract CreateProposalFactsConsumes()
    {
        var inputs = CreateFactsConsumes().Inputs.ToList();
        inputs.Add(new RuleConsumedSyntax(
          DeleteSObjectLiftingCommon.AllowedLiftNodeKinds,
          new RuleSemanticTag("Class.ExpressionHost")));
        inputs.Add(new RuleConsumedSyntax(
          new[] { SyntaxKind.IfStatement, SyntaxKind.ElseClause },
          new RuleSemanticTag("Class.IfStructure")));
        return new RuleConsumesContract(inputs);
    }
}

/// 删除类链路的 switch 结构完成态提升规则。
public sealed class ClassSwitchStructureLiftingRule : RuleDefinitionLift
{
    private static readonly RuleSemanticTag IfStructureSemanticTag = new("Class.IfStructure");

    private static readonly RuleSemanticTag ExpressionHostSemanticTag = new("Class.ExpressionHost");

    private static readonly RuleConsumesContract SwitchConsumes = new(
      new[]
      {
        new RuleConsumedSyntax(
          DeleteSObjectLiftingCommon.AllowedLiftNodeKinds,
          ExpressionHostSemanticTag),
        new RuleConsumedSyntax(
          new[] { SyntaxKind.IfStatement, SyntaxKind.ElseClause },
          IfStructureSemanticTag)
      });

    public override string CapabilityId { get; } = "lift.type.switch-structure";

    public override string RuleId { get; } = "DEL-CLASS-LIFT-SWITCH-001";

    public override RuleConsumesContract Consumes => SwitchConsumes;


    public override string Name { get; } = "Lift delete-class marks through switch structures";

    public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds =>
      DeleteSObjectLiftingCommon.AllowedLiftNodeKinds;

    // 先收集宿主与 if 提升结果，再把它们继续折叠成可整体规约的 switch 结构标记。
    public override IEnumerable<LiftedMarkRecord> Lift(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        var hostLiftedMarks = DeleteSObjectHostLiftingHelpers.BuildHostLiftedMarks(
          context,
          RuleId,
          seedMarks,
          propagatedMarks).ToList();
        var ifLiftedMarks = DeleteSObjectIfStructureLiftingHelpers.BuildIfStructureLiftedMarks(
          context,
          RuleId,
          seedMarks,
          propagatedMarks).ToList();

        return DeleteSObjectSwitchLiftingHelpers.BuildSwitchLiftedMarks(
          RuleId,
          seedMarks,
          propagatedMarks,
          hostLiftedMarks.Concat(ifLiftedMarks).ToList());
    }

    public override IEnumerable<LiftedMarkRecord> Lift(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> existingLiftedMarks)
    {
        _ = context;
        return DeleteSObjectSwitchLiftingHelpers.BuildSwitchLiftedMarks(
          RuleId,
          seedMarks,
          propagatedMarks,
          existingLiftedMarks
            .Where(mark => mark.Mark.OutputKind is RuleOutputKind.ExpressionHost or RuleOutputKind.IfStructure)
            .ToList());
    }
}
