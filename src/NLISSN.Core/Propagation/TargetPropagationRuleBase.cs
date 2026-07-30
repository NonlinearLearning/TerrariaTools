using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Pipeline;

namespace NLISSN.Core.Propagation;

public abstract class SObjectPropagationRuleBase : RuleDefinitionPropagate
{
    private const string DeleteSObjectGroupKey = "DEL-SOBJ";
    private static readonly RuleSemanticTag AtomicTargetSemanticTag = new("Target.Atomic");
    private static readonly RuleSemanticTag PropagatedTargetSemanticTag = new("Target.Propagated");
    public static readonly IReadOnlyList<SyntaxKind> AtomicTargetNodeKinds =
      new[]
      {
        SyntaxKind.IdentifierName,
        SyntaxKind.ThisExpression,
        SyntaxKind.BaseExpression,
        SyntaxKind.VariableDeclarator,
        SyntaxKind.NumericLiteralExpression,
        SyntaxKind.StringLiteralExpression,
        SyntaxKind.TrueLiteralExpression,
        SyntaxKind.FalseLiteralExpression,
        SyntaxKind.NullLiteralExpression,
        SyntaxKind.SimpleMemberAccessExpression,
        SyntaxKind.MemberBindingExpression,
        SyntaxKind.InvocationExpression,
        SyntaxKind.ObjectCreationExpression,
        SyntaxKind.ImplicitObjectCreationExpression,
        SyntaxKind.ElementAccessExpression,
        SyntaxKind.ConditionalAccessExpression
      };


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

    private static readonly RuleConsumesContract AtomicTargetConsumes = new(
      new[]
      {
        new RuleConsumedSyntax(AtomicTargetNodeKinds, AtomicTargetSemanticTag)
      });

    private static readonly RuleProducesContract PropagatedTargetProduces = new(
      new[]
      {
        new RuleProducedSyntax(SharedAllowedPropagateNodeKinds, PropagatedTargetSemanticTag)
      });


    public override RuleConsumesContract Consumes => AtomicTargetConsumes;

    public override RuleProducesContract Produces => PropagatedTargetProduces;

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds =>
      SharedAllowedPropagateNodeKinds;

    protected static (int Start, int Length, int RawKind) BuildNodeKey(SyntaxNode syntaxNode)
    {
        return (syntaxNode.SpanStart, syntaxNode.Span.Length, syntaxNode.RawKind);
    }
}
