using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynPrototype.Marking;

namespace Rules;

public interface IDeletionRule : IRuleMarker, IRulePropagator
{
  RuleMetadata Metadata { get; }

  IReadOnlyList<SyntaxKind> AllowedNodeKinds { get; }
}
