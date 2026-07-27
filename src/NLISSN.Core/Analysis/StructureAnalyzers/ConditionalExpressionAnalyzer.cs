using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Core.Analysis;

/// 三元条件表达式结构分析结果。
public sealed record ConditionalExpressionAnalysis(IReadOnlyList<SyntaxNode> AffectedSyntaxTree);

/// 分析 <c>condition ? whenTrue : whenFalse</c> 结构。
public sealed class ConditionalExpressionAnalyzer
{
    private sealed record ConditionalExpressionStructure(
        ConditionalExpressionSyntax Root,
        SyntaxNode Condition,
        SyntaxNode WhenTrue,
        SyntaxNode WhenFalse);

    // 提取三元条件表达式的条件与两个分支节点。
    public ConditionalExpressionAnalysis Analyze(ConditionalExpressionSyntax root, CpgAnalysisContext context)
    {
        _ = context;

        var structure = new ConditionalExpressionStructure(
            root,
            root.Condition,
            root.WhenTrue,
            root.WhenFalse);
        var affectedNodes = new SyntaxNode[]
        {
            structure.Root,
            structure.Condition,
            structure.WhenTrue,
            structure.WhenFalse
        };

        return new ConditionalExpressionAnalysis(
            AnalysisSyntaxNodeCollector.BuildAffectedSyntaxTree(structure.Root, affectedNodes));
    }
}
