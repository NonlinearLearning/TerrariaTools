using NLISSN.Core.Analysis.Structure;
using NLISSN.Core.Marking;

namespace NLISSN.Core.Pipeline;

/// <summary>
/// Names the semantic fact attached to a marked syntax structure.
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
/// Selects one exact structure role and its semantic meaning.
/// </summary>
public sealed record MarkedStructureSelector(
  RuleSyntaxStructureKind StructureKind,
  RuleSyntaxStructureRole Role,
  RuleSemanticTag SemanticTag);

/// <summary>
/// Specifies how many compatible producer ports a rule input accepts.
/// </summary>
public enum RuleInputCardinality
{
    All = 0,
    ExactlyOne = 1,
    Optional = 2
}

/// <summary>
/// Identifies an explicitly declared fact family without using a scheduling group key.
/// </summary>
public enum RuleFactDomain
{
    None = 0,
    SObject = 1,
    Class = 2
}

/// <summary>
/// Selects all completed facts from named stages in one declared fact family.
/// </summary>
public sealed record RuleTerminalFactSelector(
  RuleFactDomain Domain,
  IReadOnlyList<RuleKind> SourceStages);

/// <summary>
/// Owns non-structural terminal inputs required to resolve remaining rule facts.
/// </summary>
public sealed record RuleTerminalConsumesContract(
  IReadOnlyList<RuleTerminalFactSelector> Selectors)
{
    public static RuleTerminalConsumesContract Empty { get; } =
      new RuleTerminalConsumesContract(Array.Empty<RuleTerminalFactSelector>());
}

/// <summary>
/// Declares one marked structure consumed by a rule.
/// </summary>
public sealed record RuleConsumedStructure(
  MarkedStructureSelector Selector,
  RuleInputCardinality Cardinality);

/// <summary>
/// Owns all marked-structure inputs accepted by a rule.
/// </summary>
public sealed record RuleConsumesContract(IReadOnlyList<RuleConsumedStructure> Structures)
{
    public static RuleConsumesContract Empty { get; } = new RuleConsumesContract(Array.Empty<RuleConsumedStructure>());
}

/// <summary>
/// Owns all marked-structure outputs emitted by a rule.
/// </summary>
public sealed record RuleProducesContract(IReadOnlyList<MarkedStructureSelector> Structures)
{
    public static RuleProducesContract Empty { get; } = new RuleProducesContract(Array.Empty<MarkedStructureSelector>());
}

/// <summary>
/// Matches ports by exact syntax structure role and semantic tag.
/// </summary>
public static class RuleStructureContractMatcher
{
    public static bool IsCompatible(
      MarkedStructureSelector producer,
      MarkedStructureSelector consumer)
    {
        ArgumentNullException.ThrowIfNull(producer);
        ArgumentNullException.ThrowIfNull(consumer);

        return producer.StructureKind == consumer.StructureKind &&
          producer.Role == consumer.Role &&
          producer.SemanticTag == consumer.SemanticTag;
    }
}

/// <summary>
/// Creates reusable contracts for the public Roslyn structures currently supported by the catalog.
/// </summary>
public static class RuleStructureContractFactories
{
    public static RuleProducesContract CreateIfCompletionProduces(RuleSemanticTag semanticTag)
    {
        ArgumentNullException.ThrowIfNull(semanticTag);

        return new RuleProducesContract(new[]
        {
      new MarkedStructureSelector(
        RuleSyntaxStructureKind.If,
        RuleSyntaxStructureRole.Whole,
        semanticTag),
      new MarkedStructureSelector(
        RuleSyntaxStructureKind.If,
        RuleSyntaxStructureRole.ElseBranch,
        semanticTag)
    });
    }

    public static RuleConsumesContract CreateIfCompletionConsumes(
      RuleSemanticTag semanticTag,
      RuleInputCardinality cardinality)
    {
        ArgumentNullException.ThrowIfNull(semanticTag);

        return new RuleConsumesContract(new[]
        {
      new RuleConsumedStructure(
        new MarkedStructureSelector(
          RuleSyntaxStructureKind.If,
          RuleSyntaxStructureRole.Whole,
          semanticTag),
        cardinality),
      new RuleConsumedStructure(
        new MarkedStructureSelector(
          RuleSyntaxStructureKind.If,
          RuleSyntaxStructureRole.ElseBranch,
          semanticTag),
        cardinality)
    });
    }

    public static RuleProducesContract CreateIfStructureProduces(RuleSemanticTag semanticTag)
    {
        ArgumentNullException.ThrowIfNull(semanticTag);

        return new RuleProducesContract(new[]
        {
      new MarkedStructureSelector(
        RuleSyntaxStructureKind.If,
        RuleSyntaxStructureRole.Whole,
        semanticTag),
      new MarkedStructureSelector(
        RuleSyntaxStructureKind.If,
        RuleSyntaxStructureRole.ElseIf,
        semanticTag),
      new MarkedStructureSelector(
        RuleSyntaxStructureKind.If,
        RuleSyntaxStructureRole.ElseBranch,
        semanticTag)
    });
    }

    public static RuleConsumesContract CreateIfStructureConsumes(
      RuleSemanticTag semanticTag,
      RuleInputCardinality cardinality)
    {
        ArgumentNullException.ThrowIfNull(semanticTag);

        return new RuleConsumesContract(new[]
        {
      new RuleConsumedStructure(
        new MarkedStructureSelector(
          RuleSyntaxStructureKind.If,
          RuleSyntaxStructureRole.Whole,
          semanticTag),
        cardinality),
      new RuleConsumedStructure(
        new MarkedStructureSelector(
          RuleSyntaxStructureKind.If,
          RuleSyntaxStructureRole.ElseIf,
          semanticTag),
        cardinality),
      new RuleConsumedStructure(
        new MarkedStructureSelector(
          RuleSyntaxStructureKind.If,
          RuleSyntaxStructureRole.ElseBranch,
          semanticTag),
        cardinality)
    });
    }

    public static RuleProducesContract CreateVariableDeclaratorProduces(RuleSemanticTag semanticTag)
    {
        ArgumentNullException.ThrowIfNull(semanticTag);

        return new RuleProducesContract(new[]
        {
      new MarkedStructureSelector(
        RuleSyntaxStructureKind.VariableDeclarator,
        RuleSyntaxStructureRole.Whole,
        semanticTag)
    });
    }

    public static RuleConsumesContract CreateVariableDeclaratorConsumes(
      RuleSemanticTag semanticTag,
      RuleInputCardinality cardinality)
    {
        ArgumentNullException.ThrowIfNull(semanticTag);

        return new RuleConsumesContract(new[]
        {
      new RuleConsumedStructure(
        new MarkedStructureSelector(
          RuleSyntaxStructureKind.VariableDeclarator,
          RuleSyntaxStructureRole.Whole,
          semanticTag),
        cardinality)
    });
    }

    public static RuleProducesContract CreateLogicalBinaryProduces(RuleSemanticTag semanticTag)
    {
        ArgumentNullException.ThrowIfNull(semanticTag);

        return new RuleProducesContract(new[]
        {
      new MarkedStructureSelector(
        RuleSyntaxStructureKind.LogicalBinary,
        RuleSyntaxStructureRole.Whole,
        semanticTag)
    });
    }

    public static RuleConsumesContract CreateLogicalBinaryConsumes(
      RuleSemanticTag semanticTag,
      RuleInputCardinality cardinality)
    {
        ArgumentNullException.ThrowIfNull(semanticTag);

        return new RuleConsumesContract(new[]
        {
      new RuleConsumedStructure(
        new MarkedStructureSelector(
          RuleSyntaxStructureKind.LogicalBinary,
          RuleSyntaxStructureRole.Whole,
          semanticTag),
        cardinality)
    });
    }

    public static RuleProducesContract CreateExpressionOrStatementHostProduces(RuleSemanticTag semanticTag)
    {
        ArgumentNullException.ThrowIfNull(semanticTag);

        return new RuleProducesContract(new[]
        {
      new MarkedStructureSelector(
        RuleSyntaxStructureKind.ExpressionOrStatementHost,
        RuleSyntaxStructureRole.Whole,
        semanticTag)
    });
    }

    public static RuleConsumesContract CreateExpressionOrStatementHostConsumes(
      RuleSemanticTag semanticTag,
      RuleInputCardinality cardinality)
    {
        ArgumentNullException.ThrowIfNull(semanticTag);

        return new RuleConsumesContract(new[]
        {
      new RuleConsumedStructure(
        new MarkedStructureSelector(
          RuleSyntaxStructureKind.ExpressionOrStatementHost,
          RuleSyntaxStructureRole.Whole,
          semanticTag),
        cardinality)
    });
    }

    public static RuleProducesContract CreateDeclarationHostProduces(RuleSemanticTag semanticTag)
    {
        ArgumentNullException.ThrowIfNull(semanticTag);

        return new RuleProducesContract(new[]
        {
      new MarkedStructureSelector(
        RuleSyntaxStructureKind.DeclarationHost,
        RuleSyntaxStructureRole.Whole,
        semanticTag)
    });
    }

    public static RuleConsumesContract CreateDeclarationHostConsumes(
      RuleSemanticTag semanticTag,
      RuleInputCardinality cardinality)
    {
        ArgumentNullException.ThrowIfNull(semanticTag);

        return new RuleConsumesContract(new[]
        {
      new RuleConsumedStructure(
        new MarkedStructureSelector(
          RuleSyntaxStructureKind.DeclarationHost,
          RuleSyntaxStructureRole.Whole,
          semanticTag),
        cardinality)
    });
    }

    public static RuleProducesContract CreateMethodParameterUsageProduces(RuleSemanticTag semanticTag)
    {
        return CreateUsageProduces(RuleSyntaxStructureKind.MethodParameterUsage, semanticTag);
    }

    public static RuleConsumesContract CreateMethodParameterUsageConsumes(
      RuleSemanticTag semanticTag,
      RuleInputCardinality cardinality)
    {
        return CreateUsageConsumes(RuleSyntaxStructureKind.MethodParameterUsage, semanticTag, cardinality);
    }

    public static RuleProducesContract CreateLocalFunctionParameterUsageProduces(RuleSemanticTag semanticTag)
    {
        return CreateUsageProduces(RuleSyntaxStructureKind.LocalFunctionParameterUsage, semanticTag);
    }

    public static RuleConsumesContract CreateLocalFunctionParameterUsageConsumes(
      RuleSemanticTag semanticTag,
      RuleInputCardinality cardinality)
    {
        return CreateUsageConsumes(RuleSyntaxStructureKind.LocalFunctionParameterUsage, semanticTag, cardinality);
    }

    public static RuleProducesContract CreateIndexerParameterUsageProduces(RuleSemanticTag semanticTag)
    {
        return CreateUsageProduces(RuleSyntaxStructureKind.IndexerParameterUsage, semanticTag);
    }

    public static RuleConsumesContract CreateIndexerParameterUsageConsumes(
      RuleSemanticTag semanticTag,
      RuleInputCardinality cardinality)
    {
        return CreateUsageConsumes(RuleSyntaxStructureKind.IndexerParameterUsage, semanticTag, cardinality);
    }

    public static RuleProducesContract CreateExtensionMethodParameterUsageProduces(RuleSemanticTag semanticTag)
    {
        return CreateUsageProduces(RuleSyntaxStructureKind.ExtensionMethodParameterUsage, semanticTag);
    }

    public static RuleConsumesContract CreateExtensionMethodParameterUsageConsumes(
      RuleSemanticTag semanticTag,
      RuleInputCardinality cardinality)
    {
        return CreateUsageConsumes(RuleSyntaxStructureKind.ExtensionMethodParameterUsage, semanticTag, cardinality);
    }

    public static RuleProducesContract CreateDelegateUsageProduces(RuleSemanticTag semanticTag)
    {
        ArgumentNullException.ThrowIfNull(semanticTag);
        return new RuleProducesContract(new[]
        {
      new MarkedStructureSelector(RuleSyntaxStructureKind.DelegateUsage, RuleSyntaxStructureRole.Declaration, semanticTag),
      new MarkedStructureSelector(RuleSyntaxStructureKind.DelegateUsage, RuleSyntaxStructureRole.Binding, semanticTag),
      new MarkedStructureSelector(RuleSyntaxStructureKind.DelegateUsage, RuleSyntaxStructureRole.Callsite, semanticTag)
    });
    }

    public static RuleConsumesContract CreateDelegateUsageConsumes(
      RuleSemanticTag semanticTag,
      RuleInputCardinality cardinality)
    {
        ArgumentNullException.ThrowIfNull(semanticTag);
        return new RuleConsumesContract(new[]
        {
      new RuleConsumedStructure(
        new MarkedStructureSelector(RuleSyntaxStructureKind.DelegateUsage, RuleSyntaxStructureRole.Declaration, semanticTag),
        cardinality),
      new RuleConsumedStructure(
        new MarkedStructureSelector(RuleSyntaxStructureKind.DelegateUsage, RuleSyntaxStructureRole.Binding, semanticTag),
        cardinality),
      new RuleConsumedStructure(
        new MarkedStructureSelector(RuleSyntaxStructureKind.DelegateUsage, RuleSyntaxStructureRole.Callsite, semanticTag),
        cardinality)
    });
    }

    private static RuleProducesContract CreateUsageProduces(
      RuleSyntaxStructureKind kind,
      RuleSemanticTag semanticTag)
    {
        ArgumentNullException.ThrowIfNull(semanticTag);
        return new RuleProducesContract(new[]
        {
      new MarkedStructureSelector(kind, RuleSyntaxStructureRole.Declaration, semanticTag),
      new MarkedStructureSelector(kind, RuleSyntaxStructureRole.Callsite, semanticTag)
    });
    }

    private static RuleConsumesContract CreateUsageConsumes(
      RuleSyntaxStructureKind kind,
      RuleSemanticTag semanticTag,
      RuleInputCardinality cardinality)
    {
        ArgumentNullException.ThrowIfNull(semanticTag);
        return new RuleConsumesContract(new[]
        {
      new RuleConsumedStructure(
        new MarkedStructureSelector(kind, RuleSyntaxStructureRole.Declaration, semanticTag),
        cardinality),
      new RuleConsumedStructure(
        new MarkedStructureSelector(kind, RuleSyntaxStructureRole.Callsite, semanticTag),
        cardinality)
    });
    }
}

/// <summary>
/// Validates actual marks against their producer's declared structure ports.
/// </summary>
public static class RuleStructureContractValidator
{
    /// <summary>
    /// Derives the structural ports that are actually present in values supplied by a prior stage.
    /// </summary>
    public static RuleProducesContract CreateObservedProduces(
      IReadOnlyList<MarkRecord> marks,
      IReadOnlyList<RuleConsumedStructure> consumers)
    {
        ArgumentNullException.ThrowIfNull(marks);
        ArgumentNullException.ThrowIfNull(consumers);

        var selectors = consumers
          .Select(consumer => consumer.Selector)
          .Distinct()
          .Where(selector => marks.Any(mark => Matches(mark, selector)))
          .ToList();
        return new RuleProducesContract(selectors);
    }

    public static MarkedStructureSelector RequireProducedMark(
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

        foreach (var selector in produces.Structures)
        {
            if (Matches(mark, selector))
            {
                return selector;
            }
        }

        throw new InvalidOperationException(
          $"Rule '{mark.RuleId}' emitted a mark that does not match any declared produced structure.");
    }

    public static bool TryGetObservedProducedMark(
      RuleProducesContract produces,
      MarkRecord mark,
      out MarkedStructureSelector? selector)
    {
        ArgumentNullException.ThrowIfNull(produces);
        ArgumentNullException.ThrowIfNull(mark);

        selector = produces.Structures.FirstOrDefault(candidate => Matches(mark, candidate));
        return selector is not null;
    }

    private static bool Matches(MarkRecord mark, MarkedStructureSelector selector)
    {
        return mark.SemanticTag == selector.SemanticTag &&
          RuleSyntaxStructureCatalog.Validate(
            mark.SyntaxNode,
            selector.StructureKind,
            selector.Role).Status == RuleSyntaxStructureResolutionStatus.Resolved;
    }
}
