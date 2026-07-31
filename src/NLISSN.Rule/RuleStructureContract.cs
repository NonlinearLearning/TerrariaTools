using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Marking;

namespace NLISSN.Core.Pipeline;

/// <summary>
/// Names the semantic fact attached to a marked syntax node.
/// </summary>
public sealed record RuleSemanticTag
{
  public RuleSemanticTag(string value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      throw new ArgumentException("A rule semantic tag cannot be empty.", nameof(value));
    }

    Value = value;
  }

  public string Value { get; }
}

/// <summary>
/// Identifies the proof domains contributing to a fact without participating in graph routing.
/// </summary>
[Flags]
public enum RuleEvidenceOrigin
{
  None = 0,
  AtomicExpression = 1,
  DeclarationExpression = 2,
  DeclarationType = 4,
  DeclarationName = 8
}

/// <summary>
/// Owns the stable semantic ports used by the fact-composed rule graph.
/// </summary>
public static class RuleFactPorts
{
  public static RuleSemanticTag TargetExpression { get; } = new("Target.Expression");

  public static RuleSemanticTag TargetTypeSyntax { get; } = new("Target.TypeSyntax");

  public static RuleSemanticTag TargetDeclaration { get; } = new("Target.Declaration");

  public static RuleSemanticTag FlowLocalDefinition { get; } = new("Flow.LocalDefinition");

  public static RuleSemanticTag FlowSymbolReference { get; } = new("Flow.SymbolReference");

  public static RuleSemanticTag FlowAssignmentTarget { get; } = new("Flow.AssignmentTarget");

  public static RuleSemanticTag RelationDeclarationHost { get; } = new("Relation.DeclarationHost");

  public static RuleSemanticTag RelationParameterUsage { get; } = new("Relation.ParameterUsage");

  public static RuleSemanticTag RelationDelegateUsage { get; } = new("Relation.DelegateUsage");

  public static RuleSemanticTag RelationExtensionUsage { get; } = new("Relation.ExtensionUsage");

  public static RuleSemanticTag LiftExpressionHost { get; } = new("Lift.ExpressionHost");

  public static RuleSemanticTag LiftLogicalReduction { get; } = new("Lift.LogicalReduction");

  public static RuleSemanticTag LiftIfStructure { get; } = new("Lift.IfStructure");

  public static RuleSemanticTag LiftSwitchStructure { get; } = new("Lift.SwitchStructure");

  public static RuleSemanticTag LiftControlStructure { get; } = new("Lift.ControlStructure");
}

/// <summary>
/// Specifies how a rule accepts the producers of each declared syntax input.
/// </summary>
public enum RuleInputCardinality
{
  All = 0,
  ExactlyOne = 1,
  Optional = 2
}

/// <summary>
/// Declares the syntax kinds and semantic tag accepted from an upstream rule.
/// </summary>
public sealed record RuleConsumedSyntax
{
  public RuleConsumedSyntax(IReadOnlyList<SyntaxKind> syntaxKinds, RuleSemanticTag semanticTag)
  {
    SyntaxKinds = RuleSyntaxContractValidation.ValidateSyntaxKinds(syntaxKinds);
    SemanticTag = semanticTag ?? throw new ArgumentNullException(nameof(semanticTag));
  }

  public IReadOnlyList<SyntaxKind> SyntaxKinds { get; }

  public RuleSemanticTag SemanticTag { get; }
}

/// <summary>
/// Declares the syntax kinds and semantic tag emitted by a rule.
/// </summary>
public sealed record RuleProducedSyntax
{
  public RuleProducedSyntax(IReadOnlyList<SyntaxKind> syntaxKinds, RuleSemanticTag semanticTag)
  {
    SyntaxKinds = RuleSyntaxContractValidation.ValidateSyntaxKinds(syntaxKinds);
    SemanticTag = semanticTag ?? throw new ArgumentNullException(nameof(semanticTag));
  }

  public IReadOnlyList<SyntaxKind> SyntaxKinds { get; }

  public RuleSemanticTag SemanticTag { get; }
}

/// <summary>
/// Owns the direct syntax inputs accepted by a rule.
/// </summary>
public sealed record RuleConsumesContract(IReadOnlyList<RuleConsumedSyntax> Inputs)
{
  public static RuleConsumesContract Empty { get; } = new(Array.Empty<RuleConsumedSyntax>());
}

/// <summary>
/// Owns the direct syntax outputs emitted by a rule.
/// </summary>
public sealed record RuleProducesContract(IReadOnlyList<RuleProducedSyntax> Outputs)
{
  public static RuleProducesContract Empty { get; } = new(Array.Empty<RuleProducedSyntax>());
}

/// <summary>
/// Matches direct syntax contracts by semantic tag and accepted syntax kinds.
/// </summary>
public static class RuleSyntaxContractMatcher
{
  public static bool IsCompatible(RuleProducedSyntax producer, RuleConsumedSyntax consumer)
  {
    ArgumentNullException.ThrowIfNull(producer);
    ArgumentNullException.ThrowIfNull(consumer);

    return producer.SemanticTag == consumer.SemanticTag &&
      producer.SyntaxKinds.All(consumer.SyntaxKinds.Contains);
  }
}

/// <summary>
/// Validates marks against direct syntax-tag producer contracts.
/// </summary>
public static class RuleSyntaxContractValidator
{
  public static RuleProducedSyntax RequireProducedMark(
    RuleProducesContract produces,
    MarkRecord mark)
  {
    ArgumentNullException.ThrowIfNull(produces);
    ArgumentNullException.ThrowIfNull(mark);

    if (mark.SemanticTag is not { } semanticTag)
    {
      throw new InvalidOperationException(
        $"Rule '{mark.RuleId}' emitted a mark without a semantic tag.");
    }

    var output = produces.Outputs.FirstOrDefault(candidate =>
      candidate.SemanticTag == semanticTag &&
      candidate.SyntaxKinds.Contains((SyntaxKind)mark.SyntaxNode.RawKind));
    return output ?? throw new InvalidOperationException(
      $"Rule '{mark.RuleId}' emitted a mark that does not match any declared syntax output.");
  }

  public static bool TryGetObservedProducedMark(
    RuleProducesContract produces,
    MarkRecord mark,
    out RuleProducedSyntax? output)
  {
    ArgumentNullException.ThrowIfNull(produces);
    ArgumentNullException.ThrowIfNull(mark);

    output = mark.SemanticTag is null
      ? null
      : produces.Outputs.FirstOrDefault(candidate =>
        candidate.SemanticTag == mark.SemanticTag &&
        candidate.SyntaxKinds.Contains((SyntaxKind)mark.SyntaxNode.RawKind));
    return output is not null;
  }

  public static RuleProducesContract CreateObservedProduces(
    IReadOnlyList<MarkRecord> marks,
    IReadOnlyList<RuleConsumedSyntax> consumers)
  {
    ArgumentNullException.ThrowIfNull(marks);
    ArgumentNullException.ThrowIfNull(consumers);

    var outputs = consumers
      .Distinct()
      .Where(input => marks.Any(mark =>
        mark.SemanticTag == input.SemanticTag &&
        input.SyntaxKinds.Contains((SyntaxKind)mark.SyntaxNode.RawKind)))
      .Select(input => new RuleProducedSyntax(input.SyntaxKinds, input.SemanticTag))
      .ToList();
    return new RuleProducesContract(outputs);
  }
}

internal static class RuleSyntaxContractValidation
{
  public static IReadOnlyList<SyntaxKind> ValidateSyntaxKinds(IReadOnlyList<SyntaxKind> syntaxKinds)
  {
    ArgumentNullException.ThrowIfNull(syntaxKinds);
    if (syntaxKinds.Count == 0)
    {
      throw new ArgumentException("A syntax contract must declare at least one syntax kind.", nameof(syntaxKinds));
    }

    if (syntaxKinds.Distinct().Count() != syntaxKinds.Count)
    {
      throw new ArgumentException("A syntax contract cannot declare duplicate syntax kinds.", nameof(syntaxKinds));
    }

    return syntaxKinds.ToArray();
  }
}
