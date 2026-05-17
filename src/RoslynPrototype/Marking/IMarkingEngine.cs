using Microsoft.CodeAnalysis;
using Rules;

namespace RoslynPrototype.Marking;

public interface IMarkingEngine
{
  IReadOnlyList<MarkRecord> Run(
    RuleContext context,
    SyntaxNode root,
    IReadOnlyList<IDeletionRule> rules);
}
