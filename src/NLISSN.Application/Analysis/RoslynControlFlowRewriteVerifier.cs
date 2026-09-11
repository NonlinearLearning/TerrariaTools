using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using NLISSN.Core.Rewrite;

namespace NLISSN.Application;

/// <summary>
/// Verifies reachability of explicitly preserved method-local anchors after a rewrite.
/// </summary>
public sealed class RoslynControlFlowRewriteVerifier
{
    public ControlFlowVerificationResult Verify(
        string originalSource,
        string rewrittenSource,
        string filePath,
        RewriteDecisionWitness witness,
        IReadOnlyList<TextSpan> preservedOriginalSpans,
        IReadOnlyList<RewritePlanEdit> operations)
    {
        ArgumentNullException.ThrowIfNull(witness);
        var failures = new List<ControlFlowVerificationFailure>();
        var tree = CSharpSyntaxTree.ParseText(rewrittenSource, path: filePath);
        var compilation = RoslynCompilationFactory.CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();

        foreach (var originalSpan in preservedOriginalSpans)
        {
            var mappedSpan = MapSpan(originalSpan, operations);
            if (mappedSpan is null)
            {
                failures.Add(new ControlFlowVerificationFailure(
                    ControlFlowVerificationFailureCode.PreservedSpanModified,
                    "A preservation-frame span was modified by the rewrite.",
                    originalSpan));
                continue;
            }

            var node = root.FindNode(mappedSpan.Value, getInnermostNodeForTie: true);
            var controlFlowGraph = CreateControlFlowGraph(semanticModel, node);
            if (controlFlowGraph is null)
            {
                failures.Add(new ControlFlowVerificationFailure(
                    ControlFlowVerificationFailureCode.ControlFlowGraphUnavailable,
                    "Could not create a method or constructor control-flow graph for a preserved anchor.",
                    mappedSpan.Value));
                continue;
            }

            if (witness.Effects.Contains(RewriteControlFlowEffect.PreserveFinally) &&
                !ContainsFinallyRegion(controlFlowGraph.Root))
            {
                failures.Add(new ControlFlowVerificationFailure(
                    ControlFlowVerificationFailureCode.FinallyRouteMissing,
                    "A rule declared PreserveFinally, but the affected method CFG has no finally region.",
                    mappedSpan.Value));
            }

            var preservedOperation = FindOperation(semanticModel, node);
            var block = controlFlowGraph.Blocks.FirstOrDefault(candidate =>
                candidate.Operations.Any(operation => operation.DescendantsAndSelf()
                    .Any(descendant => ReferenceEquals(descendant, preservedOperation) ||
                        descendant.Syntax.Span.IntersectsWith(mappedSpan.Value))) ||
                (candidate.BranchValue is not null &&
                 (ReferenceEquals(candidate.BranchValue, preservedOperation) ||
                  candidate.BranchValue.Syntax.Span.Contains(mappedSpan.Value))));
            if (block is null)
            {
                failures.Add(new ControlFlowVerificationFailure(
                    ControlFlowVerificationFailureCode.PreservedAnchorUnreachable,
                    "Could not map a preserved sibling anchor to a control-flow block.",
                    mappedSpan.Value));
                continue;
            }

            if (!CanReach(controlFlowGraph, controlFlowGraph.Blocks.Single(candidate => candidate.Kind == BasicBlockKind.Entry), block))
            {
                failures.Add(new ControlFlowVerificationFailure(
                    ControlFlowVerificationFailureCode.PreservedAnchorUnreachable,
                    "A preserved sibling anchor is not reachable from method entry after the rewrite.",
                    mappedSpan.Value));
            }
        }

        return new ControlFlowVerificationResult(failures);
    }

    private static TextSpan? MapSpan(TextSpan originalSpan, IReadOnlyList<RewritePlanEdit> operations)
    {
        var delta = 0;
        foreach (var operation in operations.OrderBy(operation => operation.Start))
        {
            var operationSpan = TextSpan.FromBounds(operation.Start, operation.Start + operation.Length);
            if (operationSpan.Start < originalSpan.End && originalSpan.Start < operationSpan.End)
            {
                return null;
            }

            if (operation.Start >= originalSpan.End)
            {
                break;
            }

            delta += operation.ReplacementText.Length - operation.Length;
        }

        return new TextSpan(originalSpan.Start + delta, originalSpan.Length);
    }

    private static ControlFlowGraph? CreateControlFlowGraph(SemanticModel semanticModel, SyntaxNode node)
    {
        var body = node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>()
            .Select(method => method.Body)
            .FirstOrDefault(block => block is not null);
        if (body is null)
        {
            return null;
        }

        return semanticModel.GetOperation(body)?.Parent switch
        {
            IMethodBodyOperation methodBody => ControlFlowGraph.Create(methodBody),
            IConstructorBodyOperation constructorBody => ControlFlowGraph.Create(constructorBody),
            _ => null
        };
    }

    private static IOperation? FindOperation(SemanticModel semanticModel, SyntaxNode node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            var operation = semanticModel.GetOperation(current);
            if (operation is not null)
            {
                return operation;
            }
        }

        return null;
    }

    private static bool CanReach(ControlFlowGraph graph, BasicBlock source, BasicBlock target)
    {
        var pending = new Queue<BasicBlock>();
        var visited = new HashSet<int>();
        pending.Enqueue(source);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!visited.Add(current.Ordinal))
            {
                continue;
            }

            if (current.Ordinal == target.Ordinal)
            {
                return true;
            }

            Enqueue(current.FallThroughSuccessor, pending);
            Enqueue(current.ConditionalSuccessor, pending);
        }

        return false;
    }

    private static bool ContainsFinallyRegion(ControlFlowRegion region)
    {
        return region.Kind == ControlFlowRegionKind.Finally ||
            region.NestedRegions.Any(ContainsFinallyRegion);
    }


    private static void Enqueue(ControlFlowBranch? branch, Queue<BasicBlock> pending)
    {
        if (branch?.Destination is not null)
        {
            pending.Enqueue(branch.Destination);
        }
    }
}

public sealed record ControlFlowVerificationResult(IReadOnlyList<ControlFlowVerificationFailure> Failures)
{
    public bool IsSuccess => Failures.Count == 0;
}

public sealed record ControlFlowVerificationFailure(
    ControlFlowVerificationFailureCode Code,
    string Message,
    TextSpan Span);

public enum ControlFlowVerificationFailureCode
{
    PreservedSpanModified,
    ControlFlowGraphUnavailable,
    PreservedAnchorUnreachable,
    FinallyRouteMissing,
}
