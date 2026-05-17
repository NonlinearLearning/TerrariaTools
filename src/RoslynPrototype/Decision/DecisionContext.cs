using Microsoft.CodeAnalysis;

namespace RoslynPrototype.Decision;

public sealed record DecisionContext(IReadOnlySet<SyntaxNode> ReducibleLogicalHosts);
