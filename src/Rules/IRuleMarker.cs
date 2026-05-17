using Microsoft.CodeAnalysis;
using RoslynPrototype.Marking;

namespace Rules;

public interface IRuleMarker
{
  IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root);
}
