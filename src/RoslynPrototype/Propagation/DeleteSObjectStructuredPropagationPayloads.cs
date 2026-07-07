using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynPrototype.Propagation;

public sealed record LogicalHostPayload(
  BinaryExpressionSyntax Host,
  IReadOnlyList<ExpressionSyntax> RemovableOperands,
  IReadOnlyList<ExpressionSyntax> SurvivorOperands);

public enum IfStructureCompletionKind
{
    DeleteWholeIf,
    DeleteOwningElseClause,
    ReplaceIfWithElseIfTail,
    ReplaceIfWithElseTail,
    ReplaceOwningElseWithElseTail
}

public sealed record IfStructureCompletionPayload(
  IfStatementSyntax AnchorIf,
  ElseClauseSyntax? ParentElseClause,
  SyntaxNode? TailNode,
  IfStructureCompletionKind Kind);
