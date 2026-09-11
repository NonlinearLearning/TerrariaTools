using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;

namespace NLISSN.Rules;

/// <summary>Proves syntax coverage from marks without promoting an arbitrary descendant.</summary>
public static class MarkCoverage
{
  /// <summary>
  /// Evaluates a fact set against a goal. This is the typed entry point used
  /// by Lift; the legacy boolean helper below is intentionally kept only for
  /// callers that already own a non-structural compatibility decision.
  /// </summary>
  public static CoverageProof Evaluate(
    CoverageGoal goal,
    SyntaxNode target,
    IEnumerable<MarkRecord> marks)
  {
    ArgumentNullException.ThrowIfNull(marks);
    return Evaluate(
      goal,
      target,
      marks.Select(mark => CoverageEvidence.FromMark(mark)));
  }

  public static CoverageProof Evaluate(
    CoverageGoal goal,
    SyntaxNode target,
    IEnumerable<CoverageEvidence> evidence)
  {
    ArgumentNullException.ThrowIfNull(evidence);
    return goal == CoverageGoal.StructureComplete
      ? EvaluateStructureComplete(target, evidence)
      : CoverageProofEvaluator.Evaluate(goal, target, evidence);
  }

  /// <summary>
  /// Builds a structure proof from Lift-owned validation of every required
  /// expression. A topology host is useful evidence, but it is never enough
  /// by itself to complete this proof.
  /// </summary>
  public static CoverageProof EvaluateStructureComplete(
    SyntaxNode target,
    IEnumerable<CoverageEvidence> evidence,
    IReadOnlyList<SyntaxNode>? requiredExpressions = null)
  {
    ArgumentNullException.ThrowIfNull(target);
    ArgumentNullException.ThrowIfNull(evidence);

    var allEvidence = evidence.ToArray();
    var required = requiredExpressions ?? GetDefaultRequiredExpressions(target);
    var accepted = new List<CoverageEvidence>();
    var rejected = new List<CoverageEvidence>();
    var missing = new List<CoverageRequirement>();
    var diagnostics = new List<string>();
    var hasUnknown = false;

    foreach (var expression in required.DistinctBy(FactIdentity.BuildNodeKey))
    {
      var result = EvaluateStructureExpression(expression, allEvidence);
      accepted.AddRange(result.Accepted);
      rejected.AddRange(result.Rejected);
      missing.AddRange(result.Missing);
      if (result.Unknown)
      {
        hasUnknown = true;
      }

      if (!string.IsNullOrWhiteSpace(result.Diagnostic))
      {
        diagnostics.Add(result.Diagnostic!);
      }
    }

    var acceptedEvidence = accepted
      .DistinctBy(item => item.Identity.StableKey)
      .ToArray();
    var rejectedEvidence = rejected
      .Where(item => !acceptedEvidence.Any(acceptedItem =>
        string.Equals(acceptedItem.Identity.StableKey, item.Identity.StableKey, StringComparison.Ordinal)))
      .DistinctBy(item => item.Identity.StableKey)
      .ToArray();
    var consumed = acceptedEvidence
      .Select(item => item.AnchorNodeKey)
      .Append(FactIdentity.BuildNodeKey(target))
      .Concat(required.Select(FactIdentity.BuildNodeKey))
      .ToHashSet(StringComparer.Ordinal);

    var distinctMissing = missing.Distinct().ToArray();
    var status = hasUnknown
      ? CoverageProofStatus.Unknown
      : distinctMissing.Length == 0
        ? CoverageProofStatus.Complete
        : acceptedEvidence.Length > 0
          ? CoverageProofStatus.Partial
          : CoverageProofStatus.Rejected;
    if (status != CoverageProofStatus.Complete)
    {
      distinctMissing = distinctMissing
        .Append(CoverageRequirement.StructureEvidence)
        .Distinct()
        .ToArray();
    }

    return new CoverageProof(
      CoverageGoal.StructureComplete,
      status,
      acceptedEvidence,
      rejectedEvidence,
      distinctMissing,
      consumed,
      preservedObligations: BuildPreservedObligations(target),
      diagnostic: diagnostics.Count == 0 ? null : string.Join(" ", diagnostics));
  }

  public static bool IsCovered(SyntaxNode requiredNode, IEnumerable<MarkRecord> marks)
  {
    var markedKeys = marks
      .Select(mark => LiftingCommon.BuildNodeKey(mark.SyntaxNode))
      .ToHashSet();
    return IsCovered(requiredNode, markedKeys);
  }

  private static bool IsCovered(
    SyntaxNode requiredNode,
    IReadOnlySet<(int Start, int Length, int RawKind)> markedKeys)
  {
    if (markedKeys.Contains(LiftingCommon.BuildNodeKey(requiredNode)))
    {
      return true;
    }

    var children = requiredNode.ChildNodes().ToList();
    return children.Count > 0 && children.All(child => IsCovered(child, markedKeys));
  }

  private static IReadOnlyList<SyntaxNode> GetDefaultRequiredExpressions(SyntaxNode target)
  {
    return target switch
    {
      IfStatementSyntax ifStatement => new[] { ifStatement.Condition },
      WhileStatementSyntax whileStatement => new[] { whileStatement.Condition },
      DoStatementSyntax doStatement => new[] { doStatement.Condition },
      ForEachStatementSyntax forEachStatement => new[] { forEachStatement.Expression },
      ForEachVariableStatementSyntax forEachVariableStatement => new[] { forEachVariableStatement.Expression },
      ForStatementSyntax forStatement => forStatement.Initializers
        .Cast<ExpressionSyntax>()
        .Concat(forStatement.Condition is null ? Array.Empty<ExpressionSyntax>() : new[] { forStatement.Condition })
        .Concat(forStatement.Incrementors)
        .Concat(forStatement.Declaration?.Variables
          .Where(variable => variable.Initializer is not null)
          .Select(variable => variable.Initializer!.Value) ?? Array.Empty<ExpressionSyntax>())
        .ToArray(),
      ReturnStatementSyntax returnStatement when returnStatement.Expression is not null => new[] { returnStatement.Expression },
      _ => new[] { target }
    };
  }

  private static StructureExpressionResult EvaluateStructureExpression(
    SyntaxNode expression,
    IReadOnlyList<CoverageEvidence> evidence)
  {
    if (expression is BinaryExpressionSyntax binary &&
        binary.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression)
    {
      var accepted = new List<CoverageEvidence>();
      var rejected = new List<CoverageEvidence>();
      var missing = new List<CoverageRequirement>();
      var diagnostics = new List<string>();
      var unknown = false;

      var hostEvidence = evidence
        .Where(item => string.Equals(
          item.AnchorNodeKey,
          FactIdentity.BuildNodeKey(binary),
          StringComparison.Ordinal))
        .ToArray();
      var availableHosts = hostEvidence.Where(item => item.IsAvailable &&
        item.Capability == FactCapability.TopologyHost).ToArray();
      if (availableHosts.Length == 0)
      {
        if (hostEvidence.Any(item => !item.IsAvailable))
        {
          unknown = true;
          missing.Add(CoverageRequirement.CompleteQuery);
        }
        else
        {
          missing.Add(CoverageRequirement.TopologyHost);
        }
      }
      else
      {
        accepted.AddRange(availableHosts);
      }

      foreach (var operand in FlattenLogical(binary))
      {
        var operandResult = EvaluateStructureExpression(operand, evidence);
        accepted.AddRange(operandResult.Accepted);
        rejected.AddRange(operandResult.Rejected);
        missing.AddRange(operandResult.Missing);
        unknown |= operandResult.Unknown;
        if (!string.IsNullOrWhiteSpace(operandResult.Diagnostic))
        {
          diagnostics.Add(operandResult.Diagnostic!);
        }
      }

      var operandKeys = FlattenLogical(binary)
        .Select(FactIdentity.BuildNodeKey)
        .ToHashSet(StringComparer.Ordinal);
      var acceptedOperandKeys = accepted
        .Select(item => item.AnchorNodeKey)
        .Where(operandKeys.Contains)
        .ToHashSet(StringComparer.Ordinal);
      if (acceptedOperandKeys.Count != operandKeys.Count)
      {
        missing.Add(CoverageRequirement.RemovableOperands);
        diagnostics.Add("Every logical operand must be directly covered before a structure proof can complete.");
      }

      rejected.AddRange(hostEvidence.Where(item => !accepted.Contains(item)));
      var status = unknown
        ? CoverageProofStatus.Unknown
        : missing.Count == 0
          ? CoverageProofStatus.Complete
          : accepted.Count > 0
            ? CoverageProofStatus.Partial
            : CoverageProofStatus.Rejected;
      return new StructureExpressionResult(
        status,
        accepted,
        rejected,
        missing,
        unknown,
        diagnostics.Count == 0 ? null : string.Join(" ", diagnostics));
    }

    var exact = evidence
      .Where(item => string.Equals(
        item.AnchorNodeKey,
        FactIdentity.BuildNodeKey(expression),
        StringComparison.Ordinal))
      .ToArray();
    var uncertain = exact.Where(item => !item.IsAvailable).ToArray();
    if (uncertain.Length > 0)
    {
      return new StructureExpressionResult(
        CoverageProofStatus.Unknown,
        Array.Empty<CoverageEvidence>(),
        uncertain,
        new[] { CoverageRequirement.CompleteQuery },
        true,
        "Required expression evidence is not Available.");
    }

    var acceptedLeaf = exact
      .Where(item => item.Capability is FactCapability.AtomicTarget or
        FactCapability.GlobalTarget or
        FactCapability.ChildComposable)
      .ToArray();
    var rejectedLeaf = exact.Except(acceptedLeaf).ToArray();
    return acceptedLeaf.Length > 0
      ? new StructureExpressionResult(
        CoverageProofStatus.Complete,
        acceptedLeaf,
        rejectedLeaf,
        Array.Empty<CoverageRequirement>(),
        false,
        null)
      : new StructureExpressionResult(
        CoverageProofStatus.Rejected,
        Array.Empty<CoverageEvidence>(),
        rejectedLeaf,
        new[] { CoverageRequirement.AtomicEvidence },
        false,
        "Required expression has no exact atomic or composable evidence.");
  }

  private static IEnumerable<ExpressionSyntax> FlattenLogical(BinaryExpressionSyntax expression)
  {
    if (expression.Left is BinaryExpressionSyntax left && left.Kind() == expression.Kind())
    {
      foreach (var operand in FlattenLogical(left))
      {
        yield return operand;
      }
    }
    else
    {
      yield return expression.Left;
    }

    if (expression.Right is BinaryExpressionSyntax right && right.Kind() == expression.Kind())
    {
      foreach (var operand in FlattenLogical(right))
      {
        yield return operand;
      }
    }
    else
    {
      yield return expression.Right;
    }
  }

  private static IReadOnlyList<PreservedObligation> BuildPreservedObligations(SyntaxNode target)
  {
    return new[]
    {
      new PreservedObligation(
        "control-flow",
        $"Preserve control-flow semantics while rewriting {target.Kind()}.",
        FactIdentity.BuildNodeKey(target)),
      new PreservedObligation(
        "binding",
        $"Rebind surviving names after rewriting {target.Kind()}.",
        FactIdentity.BuildNodeKey(target))
    };
  }

  private sealed record StructureExpressionResult(
    CoverageProofStatus Status,
    IReadOnlyList<CoverageEvidence> Accepted,
    IReadOnlyList<CoverageEvidence> Rejected,
    IReadOnlyList<CoverageRequirement> Missing,
    bool Unknown,
    string? Diagnostic);
}
