using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Marking;

namespace NLISSN.Core.Lifting;

/// 汇集 Lift 阶段的通用筛选与去重逻辑，防止同一宿主在多个提升入口重复出现。
public static class DeleteSObjectLiftingCommon
{
    public static readonly IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds =
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

    // 识别来自符号引用传播的 mark，避免它们继续被宿主提升误扩散。
    public static bool IsSymbolReferencePropagation(MarkRecord mark)
    {
        return mark.Reason.StartsWith(
          "Symbol reference ",
          StringComparison.Ordinal);
    }

    // 用 span 与 raw kind 生成稳定去重键，保证不同阶段对同一语法节点共用一个身份。
    public static (int Start, int Length, int RawKind) BuildNodeKey(SyntaxNode syntaxNode)
    {
        return (syntaxNode.SpanStart, syntaxNode.Span.Length, syntaxNode.RawKind);
    }
}
