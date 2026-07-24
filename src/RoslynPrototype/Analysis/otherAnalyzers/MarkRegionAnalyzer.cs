using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace RoslynPrototype.Analysis;

/// <summary>
/// 描述标记阶段允许直接检查的局部语法区域。
/// </summary>
public sealed record MarkCodeRegion(
  SyntaxNode AnchorNode,
  SyntaxNode RegionNode,
  TextSpan Span,
  int NodeCount,
  int ExpressionCount,
  int StatementCount);

/// <summary>
/// 为标记分析选择最小的语句或声明级区域。
/// </summary>
public sealed class MarkRegionAnalyzer
{
  /// <summary>
  /// 返回直接种子标记应当落入的有界语法区域。
  /// </summary>
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

  /// <summary>
  /// 优先使用语句边界，否则回退到声明锚点。
  /// </summary>
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
