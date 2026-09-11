using Microsoft.CodeAnalysis.Text;
using NLISSN.Core.Decision;
using NLISSN.Core.Rewrite;

namespace NLISSN.Application;

/// <summary>
/// Verifies that replayable text operations remain within final decision authority.
/// </summary>
public sealed class RewriteVerifier
{
    public RewriteVerificationResult Verify(
        IReadOnlyDictionary<string, string> originalSourcesByPath,
        IReadOnlyDictionary<string, string> rewrittenSourcesByPath,
        IReadOnlyList<RuleDecision> decisions,
        IReadOnlyDictionary<string, IReadOnlyList<RewritePlanEdit>> planOperationsByFile)
    {
        ArgumentNullException.ThrowIfNull(originalSourcesByPath);
        ArgumentNullException.ThrowIfNull(rewrittenSourcesByPath);
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(planOperationsByFile);

        var failures = new List<RewriteVerificationFailure>();
        var missingReplacementWitnesses = decisions
            .Where(decision => decision.Action == DecisionActionKind.Replace)
            .Where(decision => decision.VerificationWitness is null)
            .ToHashSet();

        foreach (var decision in missingReplacementWitnesses)
        {
            failures.Add(CreateFailure(
                RewriteVerificationFailureCode.MissingReplacementWitness,
                "A replacement decision must carry final decision witness evidence.",
                decision));
        }

        foreach (var (filePath, operations) in planOperationsByFile)
        {
            foreach (var operation in operations)
            {
                var operationSpan = TextSpan.FromBounds(
                    operation.Start,
                    operation.Start + operation.Length);
                var candidates = decisions
                    .Where(decision => IsInFile(decision, filePath))
                    .Where(decision => decision.FinalNode.Span.Contains(operationSpan))
                    .ToList();
                if (candidates.Count == 0)
                {
                    failures.Add(new RewriteVerificationFailure(
                        RewriteVerificationFailureCode.UnauthorizedPlanOperation,
                        $"Rewrite operation at '{operation.Start}' is outside every final decision anchor.",
                        null,
                        filePath,
                        operation.Start));
                    continue;
                }

                if (candidates.Any(candidate =>
                    candidate.VerificationWitness is not null &&
                    candidate.VerificationWitness.AuthorizedSpans.Any(span => span.Contains(operationSpan))))
                {
                    continue;
                }

                if (candidates.Any(candidate => candidate.VerificationWitness is null &&
                    !missingReplacementWitnesses.Contains(candidate)))
                {
                    continue;
                }

                if (candidates.All(missingReplacementWitnesses.Contains))
                {
                    continue;
                }

                var decision = candidates[0];
                failures.Add(CreateFailure(
                    RewriteVerificationFailureCode.UnauthorizedPlanOperation,
                    $"Rewrite operation at '{operation.Start}' is not covered by its final decision witness.",
                    decision));
            }
        }

        foreach (var decision in decisions)
        {
            var witness = decision.VerificationWitness;
            if (witness is null || witness.PreservationFrame.Count == 0 ||
                !originalSourcesByPath.TryGetValue(witness.FilePath, out var originalSource) ||
                !rewrittenSourcesByPath.TryGetValue(witness.FilePath, out var rewrittenSource))
            {
                continue;
            }

            var operations = planOperationsByFile.TryGetValue(witness.FilePath, out var fileOperations)
                ? fileOperations
                : Array.Empty<RewritePlanEdit>();
            var controlFlow = new RoslynControlFlowRewriteVerifier().Verify(
                originalSource,
                rewrittenSource,
                witness.FilePath,
                witness,
                witness.PreservationFrame,
                operations);
            failures.AddRange(controlFlow.Failures.Select(failure => CreateFailure(
                RewriteVerificationFailureCode.ControlFlowEffectViolation,
                failure.Message,
                decision)));
        }

        var unexpectedDiagnostics = PostRewriteDiagnostics.GetRewriteDiagnostics(
            originalSourcesByPath,
            rewrittenSourcesByPath);
        failures.AddRange(unexpectedDiagnostics.Select(diagnostic => new RewriteVerificationFailure(
            RewriteVerificationFailureCode.UnexpectedDiagnostic,
            $"Rewrite introduced compiler error '{diagnostic.Id}'.",
            null,
            diagnostic.FilePath,
            diagnostic.Start)));

        return new RewriteVerificationResult(failures, unexpectedDiagnostics);
    }

    private static bool IsInFile(RuleDecision decision, string filePath)
    {
        return string.Equals(
            decision.FinalNode.SyntaxTree.FilePath,
            filePath,
            StringComparison.Ordinal);
    }

    private static RewriteVerificationFailure CreateFailure(
        RewriteVerificationFailureCode code,
        string message,
        RuleDecision decision)
    {
        return new RewriteVerificationFailure(
            code,
            message,
            decision.RuleId,
            decision.FinalNode.SyntaxTree.FilePath,
            decision.FinalNode.SpanStart);
    }
}
