using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MinimalRoslynCpg.Analysis;

/// <summary>
/// 二元表达式结构分析结果。
/// </summary>
public sealed record BinaryExpressionAnalysis(IReadOnlyList<SyntaxNode> AffectedSyntaxTree);

/// <summary>
/// 分析同类二元表达式链，例如连续的 <c>&amp;&amp;</c>、<c>||</c> 或加法表达式。
/// </summary>
public sealed class BinaryExpressionAnalyzer
{
    /// <summary>
    /// 返回同类二元表达式层级和最终操作数组成的受影响语法树。
    /// </summary>
    public BinaryExpressionAnalysis Analyze(BinaryExpressionSyntax root, ExpressionSyntax operand, CpgAnalysisContext Context)
    {
        if (!root.Span.Contains(operand.Span))
        {
            throw new ArgumentException("Operand must be inside the binary expression root.", nameof(operand));
        }

        var (binaryExpressions, operands) = BuildLevels(root, root.Kind());
        var affectedSyntaxTree = BuildAffectedSyntaxTree(root, binaryExpressions, operands);
        return new BinaryExpressionAnalysis(affectedSyntaxTree);
    }

    /// <summary>
    /// 将同类二元表达式节点和叶子操作数合并为源码顺序。
    /// </summary>
    private static IReadOnlyList<SyntaxNode> BuildAffectedSyntaxTree(
      BinaryExpressionSyntax root,
      IReadOnlyList<BinaryExpressionSyntax> binaryExpressions,
      IReadOnlyList<ExpressionSyntax> operands)
    {
        var affectedNodes = new List<SyntaxNode>();

        affectedNodes.AddRange(operands);
        affectedNodes.AddRange(binaryExpressions);

        return affectedNodes
          .Where(node => root.Span.Contains(node.Span))
          .Distinct()
          .OrderBy(node => node.SpanStart)
          .ThenBy(node => node.Span.Length)
          .ToList();
    }

    /// <summary>
    /// 按层展开同一种二元表达式，直到遇到不同类型表达式作为操作数。
    /// </summary>
    private static (IReadOnlyList<BinaryExpressionSyntax> BinaryExpressions, IReadOnlyList<ExpressionSyntax> Operands) BuildLevels(BinaryExpressionSyntax root, SyntaxKind binaryKind)
    {
        var allBinaryExpressions = new List<BinaryExpressionSyntax>();
        var allOperands = new List<ExpressionSyntax>();
        var currentLevel = new List<ExpressionSyntax> { root };

        while (currentLevel.Count > 0)
        {
            var binaryExpressions = currentLevel
              .OfType<BinaryExpressionSyntax>()
              .Where(node => node.IsKind(binaryKind))
              .ToList();
            var operands = currentLevel
              .Where(node => node is not BinaryExpressionSyntax binaryExpression ||
                !binaryExpression.IsKind(binaryKind))
              .ToList();
            var nextLevel = new List<ExpressionSyntax>();

            foreach (var binaryExpression in binaryExpressions)
            {
                nextLevel.Add(binaryExpression.Left);
                nextLevel.Add(binaryExpression.Right);
            }

            allBinaryExpressions.AddRange(binaryExpressions);
            allOperands.AddRange(operands);
            currentLevel = nextLevel;
        }

        return (allBinaryExpressions, allOperands);
    }
}
