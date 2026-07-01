using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MinimalRoslynCpg.Analysis;

/// <summary>
/// 三元条件表达式结构分析结果。
/// </summary>
public sealed record ConditionalExpressionAnalysis(IReadOnlyList<SyntaxNode> AffectedSyntaxTree);

/// <summary>
/// 分析 <c>condition ? whenTrue : whenFalse</c> 结构。
/// </summary>
public sealed class ConditionalExpressionAnalyzer
{
    /// <summary>
    /// 返回条件、真分支和假分支表达式。
    /// </summary>
    public ConditionalExpressionAnalysis Analyze(ConditionalExpressionSyntax root, CpgAnalysisContext context)
    {
        var affectedNodes = new SyntaxNode[]
        {
            root,
            root.Condition,
            root.WhenTrue,
            root.WhenFalse
        };

        return new ConditionalExpressionAnalysis(
            AnalysisSyntaxNodeCollector.BuildAffectedSyntaxTree(root, affectedNodes));
    }
}
