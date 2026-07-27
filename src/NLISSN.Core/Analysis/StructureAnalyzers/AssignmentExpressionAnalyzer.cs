using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Core.Analysis;

/// 赋值表达式结构分析结果。
public sealed record AssignmentExpressionAnalysis(IReadOnlyList<SyntaxNode> AffectedSyntaxTree);

/// 分析普通赋值和复合赋值表达式，例如 <c>a = b</c>、<c>a += b</c>。
public sealed class AssignmentExpressionAnalyzer
{
    private sealed record AssignmentStructure(
        AssignmentExpressionSyntax Root,
        SyntaxNode Left,
        SyntaxNode Right);

    // 提取赋值表达式左右两侧及根节点的受影响语法树。
    public AssignmentExpressionAnalysis Analyze(AssignmentExpressionSyntax root, CpgAnalysisContext context)
    {
        _ = context;

        var structure = new AssignmentStructure(root, root.Left, root.Right);
        var affectedNodes = new SyntaxNode[]
        {
            structure.Root,
            structure.Left,
            structure.Right
        };

        return new AssignmentExpressionAnalysis(
            AnalysisSyntaxNodeCollector.BuildAffectedSyntaxTree(structure.Root, affectedNodes));
    }
}
