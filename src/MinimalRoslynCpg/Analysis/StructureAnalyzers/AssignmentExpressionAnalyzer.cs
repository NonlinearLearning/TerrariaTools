using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MinimalRoslynCpg.Analysis;

/// <summary>
/// 赋值表达式结构分析结果。
/// </summary>
public sealed record AssignmentExpressionAnalysis(IReadOnlyList<SyntaxNode> AffectedSyntaxTree);

/// <summary>
/// 分析普通赋值和复合赋值表达式，例如 <c>a = b</c>、<c>a += b</c>。
/// </summary>
public sealed class AssignmentExpressionAnalyzer
{
    /// <summary>
    /// 返回赋值表达式本身、左侧目标和右侧值表达式。
    /// </summary>
    public AssignmentExpressionAnalysis Analyze(AssignmentExpressionSyntax root, CpgAnalysisContext context)
    {
        var affectedNodes = new SyntaxNode[]
        {
            root,
            root.Left,
            root.Right
        };

        return new AssignmentExpressionAnalysis(
            AnalysisSyntaxNodeCollector.BuildAffectedSyntaxTree(root, affectedNodes));
    }
}
