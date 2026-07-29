using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using NLCPG.Model;

namespace NLISSN.Core.Analysis;

public interface IRuleGraphBinding
{
  bool TryResolvePrimaryGraphNode(SyntaxNode syntaxNode, out NLCPGNode? graphNode);

  bool ContainsPrimaryGraphNodeInRegion(SyntaxNode syntaxNode, TextSpan regionSpan);
}
