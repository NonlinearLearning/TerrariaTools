using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Rules;

namespace NLISSN.Core.Propagation;

public abstract class SObjectPropagationRuleBase : RuleDefinitionPropagate
{
    private const string DeleteSObjectGroupKey = "DEL-SOBJ";

    protected static readonly IReadOnlyList<SyntaxKind> SharedAllowedPropagateNodeKinds =
      new[]
      {
        SyntaxKind.IdentifierName,
        SyntaxKind.ThisExpression,
        SyntaxKind.BaseExpression,
        SyntaxKind.VariableDeclarator,
        SyntaxKind.SimpleMemberAccessExpression,
        SyntaxKind.MemberBindingExpression,
        SyntaxKind.InvocationExpression,
        SyntaxKind.ElementAccessExpression,
        SyntaxKind.ConditionalAccessExpression,
        SyntaxKind.ObjectCreationExpression,
        SyntaxKind.ImplicitObjectCreationExpression,
        SyntaxKind.LogicalNotExpression,
        SyntaxKind.SimpleAssignmentExpression,
        SyntaxKind.AddAssignmentExpression,
        SyntaxKind.SubtractAssignmentExpression,
        SyntaxKind.MultiplyAssignmentExpression,
        SyntaxKind.DivideAssignmentExpression,
        SyntaxKind.LogicalAndExpression,
        SyntaxKind.LogicalOrExpression,
        SyntaxKind.TupleExpression,
        SyntaxKind.Block,
        SyntaxKind.LocalDeclarationStatement,
        SyntaxKind.ExpressionStatement,
        SyntaxKind.ElseClause,
        SyntaxKind.IfStatement,
        SyntaxKind.SwitchStatement,
        SyntaxKind.SwitchSection,
        SyntaxKind.ReturnStatement
      };

    public override string GroupKey { get; } = DeleteSObjectGroupKey;

    public override IReadOnlyList<RuleDependency> Dependencies { get; } =
      new[]
      {
        "DEL-SOBJ-MARK-ID-001", "DEL-SOBJ-MARK-THIS-001", "DEL-SOBJ-MARK-BASE-001",
        "DEL-SOBJ-MARK-DECL-001", "DEL-SOBJ-MARK-LIT-NUM-001", "DEL-SOBJ-MARK-LIT-STR-001",
        "DEL-SOBJ-MARK-LIT-TRUE-001", "DEL-SOBJ-MARK-LIT-FALSE-001", "DEL-SOBJ-MARK-LIT-NULL-001",
        "DEL-SOBJ-MARK-MEMBER-001", "DEL-SOBJ-MARK-BINDING-001", "DEL-SOBJ-MARK-INVOKE-001",
        "DEL-SOBJ-MARK-NEW-001", "DEL-SOBJ-MARK-IMPLICIT-NEW-001", "DEL-SOBJ-MARK-ELEMENT-001",
        "DEL-SOBJ-MARK-CONDITIONAL-001"
      }
      .Select(ruleId => new RuleDependency(RuleNodeId.For(RuleKind.Mark, ruleId), RuleOutputKind.SeedMark))
      .ToList();

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds =>
      SharedAllowedPropagateNodeKinds;

    protected static (int Start, int Length, int RawKind) BuildNodeKey(SyntaxNode syntaxNode)
    {
        return (syntaxNode.SpanStart, syntaxNode.Span.Length, syntaxNode.RawKind);
    }
}
