using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace NLISSN.Core.Analysis;

/// 描述标记阶段允许直接检查的局部语法区域。
public sealed record MarkCodeRegion(
  SyntaxNode AnchorNode,
  SyntaxNode RegionNode,
  TextSpan Span,
  int NodeCount,
  int ExpressionCount,
  int StatementCount);

/// 为标记分析选择最小的语句或声明级区域。
public sealed class MarkRegionAnalyzer
{
    // 为一个锚点挑选 mark 阶段允许直接查看的最小局部区域。
    public MarkCodeRegion Analyze(SyntaxNode anchorNode, CpgAnalysisContext context)
    {
        var regionNode = ResolveRegionNode(anchorNode);
        var nodes = regionNode.DescendantNodesAndSelf().ToList();
        return new MarkCodeRegion(
          anchorNode,
          regionNode,
          regionNode.Span,
          nodes.Count,
          nodes.OfType<ExpressionSyntax>().Count(),
          nodes.OfType<StatementSyntax>().Count());
    }

    internal static SyntaxNode ResolveRegionNode(SyntaxNode anchorNode)
    {
        var statement = anchorNode.FirstAncestorOrSelf<StatementSyntax>();
        if (statement is not null)
        {
            return statement;
        }

        return anchorNode.FirstAncestorOrSelf<VariableDeclaratorSyntax>()
          ?? anchorNode.FirstAncestorOrSelf<ParameterSyntax>()
          ?? anchorNode;
    }
}
