using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Pipeline;

namespace NLISSN.Core.Propagation;

public abstract class ExpressionFlowPropagationRuleBase : RuleDefinitionPropagate
{
    private static readonly RuleSemanticTag TargetExpressionSemanticTag = RuleFactPorts.TargetExpression;

    public static readonly IReadOnlyList<SyntaxKind> TargetExpressionInputNodeKinds =
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
        SyntaxKind.ConditionalAccessExpression,
        SyntaxKind.ParenthesizedExpression,
        SyntaxKind.LogicalAndExpression,
        SyntaxKind.LogicalNotExpression,
        SyntaxKind.BitwiseAndExpression,
        SyntaxKind.BitwiseOrExpression,
        SyntaxKind.ExclusiveOrExpression,
        SyntaxKind.BitwiseNotExpression,
        SyntaxKind.UnaryPlusExpression,
        SyntaxKind.UnaryMinusExpression,
        SyntaxKind.AddExpression,
        SyntaxKind.SubtractExpression,
        SyntaxKind.MultiplyExpression,
        SyntaxKind.DivideExpression,
        SyntaxKind.ModuloExpression,
        SyntaxKind.LeftShiftExpression,
        SyntaxKind.RightShiftExpression,
        SyntaxKind.UnsignedRightShiftExpression,
        SyntaxKind.EqualsExpression,
        SyntaxKind.NotEqualsExpression,
        SyntaxKind.GreaterThanExpression,
        SyntaxKind.GreaterThanOrEqualExpression,
        SyntaxKind.LessThanExpression,
        SyntaxKind.LessThanOrEqualExpression,
        SyntaxKind.IsPatternExpression,
        SyntaxKind.AsExpression,
        SyntaxKind.CoalesceExpression,
        SyntaxKind.CastExpression,
        SyntaxKind.CheckedExpression,
        SyntaxKind.UncheckedExpression,
        SyntaxKind.AwaitExpression,
        SyntaxKind.SuppressNullableWarningExpression,
        SyntaxKind.SwitchExpression
      };

    public static readonly IReadOnlyList<SyntaxKind> AssignmentTargetNodeKinds =
      new[]
      {
        SyntaxKind.IdentifierName,
        SyntaxKind.ThisExpression,
        SyntaxKind.BaseExpression,
        SyntaxKind.SimpleMemberAccessExpression,
        SyntaxKind.MemberBindingExpression,
        SyntaxKind.ElementAccessExpression,
        SyntaxKind.TupleExpression,
        SyntaxKind.DeclarationExpression
      };

    public static readonly IReadOnlyList<SyntaxKind> TargetExpressionNodeKinds =
      TargetExpressionInputNodeKinds
        .Concat(AssignmentTargetNodeKinds)
        .Distinct()
        .ToArray();

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
        SyntaxKind.DeclarationExpression,
        SyntaxKind.Block,
        SyntaxKind.LocalDeclarationStatement,
        SyntaxKind.ExpressionStatement,
        SyntaxKind.ElseClause,
        SyntaxKind.IfStatement,
        SyntaxKind.SwitchStatement,
        SyntaxKind.SwitchSection,
        SyntaxKind.ReturnStatement
      };

    private static readonly RuleConsumesContract TargetExpressionConsumes = new(
      new[]
      {
        new RuleConsumedSyntax(TargetExpressionNodeKinds, TargetExpressionSemanticTag)
      });

    private static readonly RuleProducesContract PropagatedTargetProduces = new(
      new[]
      {
        new RuleProducedSyntax(SharedAllowedPropagateNodeKinds, TargetExpressionSemanticTag)
      });


    public override RuleConsumesContract Consumes => TargetExpressionConsumes;

    public override RuleProducesContract Produces => PropagatedTargetProduces;

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds =>
      SharedAllowedPropagateNodeKinds;

    protected static (int Start, int Length, int RawKind) BuildNodeKey(SyntaxNode syntaxNode)
    {
        return (syntaxNode.SpanStart, syntaxNode.Span.Length, syntaxNode.RawKind);
    }
}
