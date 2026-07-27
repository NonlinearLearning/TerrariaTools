using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace NLISSN.Core.Analysis;

/// 描述传播阶段允许遍历的局部语法区域。
public sealed record PropagationCodeRegion(
  SyntaxNode AnchorNode,
  SyntaxNode RegionNode,
  TextSpan Span,
  int NodeCount,
  int ExpressionCount,
  int StatementCount);

/// 为传播阶段选择受限的语法区域。
public sealed class PropagationRegionAnalyzer
{
    // 为一个锚点挑选 propagation 阶段允许扩展的受限局部区域。
    public PropagationCodeRegion Analyze(SyntaxNode anchorNode, CpgAnalysisContext context)
    {
        var regionNode = ResolveRegionNode(anchorNode);
        var nodes = regionNode.DescendantNodesAndSelf().ToList();
        return new PropagationCodeRegion(
          anchorNode,
          regionNode,
          regionNode.Span,
          nodes.Count,
          nodes.OfType<ExpressionSyntax>().Count(),
          nodes.OfType<StatementSyntax>().Count());
    }

    private static SyntaxNode ResolveRegionNode(SyntaxNode anchorNode)
    {
        if (TryResolveElseIfRegion(anchorNode, out var elseIfRegion))
        {
            return elseIfRegion!;
        }

        var statement = anchorNode.FirstAncestorOrSelf<StatementSyntax>();
        if (statement is not null)
        {
            return statement;
        }

        return anchorNode.FirstAncestorOrSelf<VariableDeclaratorSyntax>()
          ?? anchorNode.FirstAncestorOrSelf<ParameterSyntax>()
          ?? anchorNode;
    }

    private static bool TryResolveElseIfRegion(SyntaxNode anchorNode, out ElseClauseSyntax? elseClause)
    {
        var nestedIf = anchorNode.FirstAncestorOrSelf<IfStatementSyntax>();
        if (nestedIf?.Parent is ElseClauseSyntax parentElseClause &&
            (ReferenceEquals(anchorNode, nestedIf) ||
             nestedIf.Condition.Span.Contains(anchorNode.Span)))
        {
            elseClause = parentElseClause;
            return true;
        }

        elseClause = null;
        return false;
    }
}
