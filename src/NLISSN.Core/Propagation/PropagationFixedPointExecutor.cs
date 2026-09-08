using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;

namespace NLISSN.Core.Propagation;

/// <summary>
/// Evaluates propagation facts until no rule can admit a new fact.
/// </summary>
internal sealed class PropagationFixedPointExecutor
{
    internal IReadOnlyList<PropagatedMarkRecord> Run(
      AnalysisSession session,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<RuleDefinitionPropagate> rules)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(seedMarks);
        ArgumentNullException.ThrowIfNull(rules);

        var sourceFacts = new Dictionary<PropagationFactKey, PropagationSourceFact>();
        var admittedFacts = new Dictionary<PropagationFactKey, PropagatedMarkRecord>();
        var worklist = new PriorityQueue<PropagationSourceFact, PropagationWorkItemPriority>();
        foreach (var seedMark in seedMarks)
        {
            var key = PropagationFactKey.Create(seedMark.RuleId, seedMark);
            if (!sourceFacts.TryAdd(key, new PropagationSourceFact(seedMark, 0)))
            {
                continue;
            }

            worklist.Enqueue(sourceFacts[key], PropagationWorkItemPriority.Create(key));
        }

        while (worklist.TryDequeue(out var sourceFact, out _))
        {
            session.Runtime.ExecutionOptions.CancellationToken.ThrowIfCancellationRequested();
            foreach (var rule in GetCompatibleRules(sourceFact.Mark, rules))
            {
                var inputs = GetCompatibleInputs(rule, sourceFacts)
                  .Select(fact => fact.Mark)
                  .ToList();
                var candidates = PropagationEngine.EvaluateRule(session, rule, inputs);
                foreach (var candidate in candidates)
                {
                    var sourceKey = PropagationFactKey.Create(
                      candidate.SourceMark.RuleId,
                      candidate.SourceMark);
                    if (!sourceFacts.TryGetValue(sourceKey, out var candidateSource))
                    {
                        throw new InvalidOperationException(
                          $"Rule '{rule.RuleId}' emitted a propagation fact from an unknown source mark.");
                    }

                    var admitted = candidate with
                    {
                        SourceMark = candidateSource.Mark,
                        Depth = candidateSource.Depth + 1
                    };
                    ValidateCurrentSyntaxTree(session, rule, admitted.Mark.SyntaxNode);
                    var admittedKey = PropagationFactKey.Create(admitted);
                    if (!admittedFacts.TryAdd(admittedKey, admitted))
                    {
                        continue;
                    }

                    sourceFacts[admittedKey] = new PropagationSourceFact(admitted.Mark, admitted.Depth);
                    worklist.Enqueue(
                      sourceFacts[admittedKey],
                      PropagationWorkItemPriority.Create(admittedKey));
                    session.Evidence.RecordPropagation(
                      rule.RuleId,
                      new[] { admitted.SourceMark },
                      new[] { admitted });
                }
            }
        }

        return admittedFacts
          .OrderBy(entry => PropagationWorkItemPriority.Create(entry.Key))
          .Select(entry => entry.Value)
          .ToList();
    }

    private static IEnumerable<RuleDefinitionPropagate> GetCompatibleRules(
      MarkRecord sourceMark,
      IReadOnlyList<RuleDefinitionPropagate> rules)
    {
        return rules
          .Where(rule => rule.Consumes.Inputs.Any(input => IsCompatible(sourceMark, input)))
          .OrderBy(rule => rule.RuleId, StringComparer.Ordinal);
    }

    private static IEnumerable<PropagationSourceFact> GetCompatibleInputs(
      RuleDefinitionPropagate rule,
      IReadOnlyDictionary<PropagationFactKey, PropagationSourceFact> sourceFacts)
    {
        return sourceFacts
          .Where(entry => rule.Consumes.Inputs.Any(input => IsCompatible(entry.Value.Mark, input)))
          .OrderBy(entry => PropagationWorkItemPriority.Create(entry.Key))
          .Select(entry => entry.Value);
    }

    private static bool IsCompatible(MarkRecord mark, RuleConsumedSyntax input)
    {
        if (mark.FactKind is null && mark.SemanticTag is null)
        {
            return false;
        }

        var factKind = RuleFactKindDescriptor.Resolve(mark.FactKind, mark.SemanticTag);
        var output = factKind is { } knownFactKind
          ? new RuleProducedSyntax(
            new[] { (SyntaxKind)mark.SyntaxNode.RawKind },
            knownFactKind)
          : new RuleProducedSyntax(
            new[] { (SyntaxKind)mark.SyntaxNode.RawKind },
            mark.SemanticTag!);
        return RuleSyntaxContractMatcher.IsCompatible(output, input);
    }

    private static void ValidateCurrentSyntaxTree(
      AnalysisSession session,
      RuleDefinitionPropagate rule,
      SyntaxNode syntaxNode)
    {
        if (!ReferenceEquals(syntaxNode.SyntaxTree, session.Root.SyntaxTree))
        {
            throw new InvalidOperationException(
              $"Rule '{rule.RuleId}' emitted a propagation fact outside the current syntax tree.");
        }
    }

    private sealed record PropagationSourceFact(MarkRecord Mark, int Depth);

    private sealed record PropagationWorkItemPriority(
      string FilePath,
      int SpanStart,
      int SpanLength,
      int RawKind,
      string RuleId,
      RuleFactKind? FactKind,
      string? SemanticTag) : IComparable<PropagationWorkItemPriority>
    {
        public static PropagationWorkItemPriority Create(PropagationFactKey key)
        {
            return new PropagationWorkItemPriority(
              key.FilePath,
              key.SpanStart,
              key.SpanLength,
              key.RawKind,
              key.RuleId,
              key.FactKind,
              key.SemanticTag);
        }

        public int CompareTo(PropagationWorkItemPriority? other)
        {
            if (other is null)
            {
                return 1;
            }

            var filePathComparison = StringComparer.Ordinal.Compare(FilePath, other.FilePath);
            if (filePathComparison != 0)
            {
                return filePathComparison;
            }

            var spanStartComparison = SpanStart.CompareTo(other.SpanStart);
            if (spanStartComparison != 0)
            {
                return spanStartComparison;
            }

            var spanLengthComparison = SpanLength.CompareTo(other.SpanLength);
            if (spanLengthComparison != 0)
            {
                return spanLengthComparison;
            }

            var rawKindComparison = RawKind.CompareTo(other.RawKind);
            if (rawKindComparison != 0)
            {
                return rawKindComparison;
            }

            var ruleIdComparison = StringComparer.Ordinal.Compare(RuleId, other.RuleId);
            if (ruleIdComparison != 0)
            {
                return ruleIdComparison;
            }

            if (FactKind is { } leftFactKind && other.FactKind is { } rightFactKind)
            {
                return leftFactKind.CompareTo(rightFactKind);
            }

            if (FactKind is not null)
            {
                return -1;
            }

            if (other.FactKind is not null)
            {
                return 1;
            }

            return StringComparer.Ordinal.Compare(SemanticTag, other.SemanticTag);
        }
    }
}
