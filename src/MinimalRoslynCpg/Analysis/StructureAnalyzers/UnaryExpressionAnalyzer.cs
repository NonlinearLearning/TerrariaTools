using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MinimalRoslynCpg.Analysis;

/// <summary>
/// 一元表达式结构分析结果。
/// </summary>
public sealed record UnaryExpressionAnalysis(IReadOnlyList<SyntaxNode> AffectedSyntaxTree);

/// <summary>
/// 分析前缀、后缀、await 和强制类型转换这类单操作数表达式。
/// </summary>
public sealed class UnaryExpressionAnalyzer
{
    private sealed record UnaryStructure(
        SyntaxNode Root,
        IReadOnlyList<SyntaxNode> Members);

    /// <summary>
    /// 返回一元表达式本身和它的核心操作数。
    /// </summary>
    public UnaryExpressionAnalysis Analyze(ExpressionSyntax root, CpgAnalysisContext context)
    {
        _ = context;

        var structure = root switch
        {
            PrefixUnaryExpressionSyntax prefixUnary => new UnaryStructure(prefixUnary, new SyntaxNode[] { prefixUnary, prefixUnary.Operand }),
            PostfixUnaryExpressionSyntax postfixUnary => new UnaryStructure(postfixUnary, new SyntaxNode[] { postfixUnary, postfixUnary.Operand }),
            AwaitExpressionSyntax awaitExpression => new UnaryStructure(awaitExpression, new SyntaxNode[] { awaitExpression, awaitExpression.Expression }),
            CastExpressionSyntax castExpression => new UnaryStructure(
                castExpression,
                new SyntaxNode[]
                {
                    castExpression,
                    castExpression.Type,
                    castExpression.Expression
                }),
            _ => throw new ArgumentException("Unsupported unary expression syntax node.", nameof(root))
        };

        return new UnaryExpressionAnalysis(
            AnalysisSyntaxNodeCollector.BuildAffectedSyntaxTree(structure.Root, structure.Members));
    }
}
