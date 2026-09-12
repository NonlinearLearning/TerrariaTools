using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Core.Lifting;

public interface ILiftPayload
{
}

public enum MethodDeletionKind
{
  Unreachable,
  Unreferenced
}

/// <summary>Proof that a method declaration crossed the deletion lift boundary.</summary>
public sealed record MethodDeletionLiftPayload(
  MethodDeletionKind Kind,
  string MethodName,
  string OriginalReason) : ILiftPayload;

/// <summary>Payload for a non-structural logical expression reduction produced by Lift.</summary>
public sealed record LogicalExpressionReductionPayload(
  BinaryExpressionSyntax Host,
  IReadOnlyList<ExpressionSyntax> RemovableOperands,
  IReadOnlyList<ExpressionSyntax> SurvivorOperands,
  CoverageProof? Proof = null) : ILiftPayload
{
    public bool DominatesChildren => false;
}

public enum IfStructureLiftKind
{
  DeleteWholeIf,
  DeleteOwningElseClause,
  ReplaceIfWithElseIfTail,
  ReplaceIfWithElseTail,
  ReplaceOwningElseWithElseTail
}

/// <summary>Decision data for an if structure whose condition was proven covered by Lift.</summary>
public sealed record IfStructureLiftPayload(
  IfStatementSyntax AnchorIf,
  ElseClauseSyntax? ParentElseClause,
  SyntaxNode? TailNode,
  IfStructureLiftKind Kind,
  CoverageProof? Proof = null) : ILiftPayload;

/// <summary>Lift-only proof payload for loop and return structure candidates.</summary>
public sealed record ControlStructureLiftPayload(
  SyntaxNode Anchor,
  CoverageProof Proof) : ILiftPayload;

/// <summary>Lift-only proof payload for switch section and switch candidates.</summary>
public sealed record SwitchStructureLiftPayload(
  SyntaxNode Anchor,
  CoverageProof Proof) : ILiftPayload;
