using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynPrototype.Marking;

namespace Rules;

internal static class DeleteSObjectLiftingCommon
{
    internal static readonly IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds =
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
        SyntaxKind.ObjectInitializerExpression,
        SyntaxKind.CollectionInitializerExpression,
        SyntaxKind.ComplexElementInitializerExpression,
        SyntaxKind.ConditionalExpression,
        SyntaxKind.ParenthesizedExpression,
        SyntaxKind.PreIncrementExpression,
        SyntaxKind.PreDecrementExpression,
        SyntaxKind.UnaryMinusExpression,
        SyntaxKind.UnaryPlusExpression,
        SyntaxKind.LogicalNotExpression,
        SyntaxKind.PostIncrementExpression,
        SyntaxKind.PostDecrementExpression,
        SyntaxKind.CastExpression,
        SyntaxKind.AwaitExpression,
        SyntaxKind.CheckedExpression,
        SyntaxKind.UncheckedExpression,
        SyntaxKind.RefExpression,
        SyntaxKind.AddressOfExpression,
        SyntaxKind.Argument,
        SyntaxKind.ArgumentList,
        SyntaxKind.BracketedArgumentList,
        SyntaxKind.Interpolation,
        SyntaxKind.InterpolatedStringExpression,
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
        SyntaxKind.LocalFunctionStatement,
        SyntaxKind.ExpressionStatement,
        SyntaxKind.ElseClause,
        SyntaxKind.IfStatement,
        SyntaxKind.ForStatement,
        SyntaxKind.WhileStatement,
        SyntaxKind.DoStatement,
        SyntaxKind.SwitchStatement,
        SyntaxKind.SwitchSection,
        SyntaxKind.SwitchExpression,
        SyntaxKind.SwitchExpressionArm,
        SyntaxKind.ReturnStatement,
        SyntaxKind.YieldReturnStatement,
        SyntaxKind.ThrowStatement,
        SyntaxKind.ArrowExpressionClause,
        SyntaxKind.LockStatement,
        SyntaxKind.UsingStatement,
        SyntaxKind.FixedStatement,
        SyntaxKind.ForEachStatement
      };

    internal static bool IsSymbolReferencePropagation(MarkRecord mark)
    {
        return mark.Reason.StartsWith(
          "Symbol reference ",
          StringComparison.Ordinal);
    }

    internal static (int Start, int Length, int RawKind) BuildNodeKey(SyntaxNode syntaxNode)
    {
        return (syntaxNode.SpanStart, syntaxNode.Span.Length, syntaxNode.RawKind);
    }
}
