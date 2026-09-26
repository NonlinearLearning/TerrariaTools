using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLCPG.Analysis;
using NLCPG.Contracts;
using NLCPG.Model;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace NLISSN.Core.Analysis;

/// Builds rule structure views only through declared CPG relation profiles.
public sealed class NLCPGStructureViewBuilder
{
    private static readonly ConditionalWeakTable<CpgAnalysisContext, AnalysisRunCache> RunCaches = new();

    // Compatibility entry point for one syntax anchor. Multi-fragment callers must declare a relation profile.
    public NLCPGStructureView Build(SyntaxNode root, CpgAnalysisContext context)
    {
        return Build(root, context, cacheScopeKey: null);
    }

    public NLCPGStructureView Build(SyntaxNode root, CpgAnalysisContext context, string? cacheScopeKey)
    {
        return Build(
            new[] { root },
            context,
            CpgRelationProfile.StructuralContainment,
            CpgQueryDirection.Incoming,
            CreateCompatibilityBudget(),
            cacheScopeKey);
    }

    // The untyped multi-fragment entry is deliberately restricted to a single-fragment compatibility view.
    public NLCPGStructureView Build(IReadOnlyCollection<SyntaxNode> fragments, CpgAnalysisContext context)
    {
        return Build(fragments, context, cacheScopeKey: null);
    }

    public NLCPGStructureView Build(
        IReadOnlyCollection<SyntaxNode> fragments,
        CpgAnalysisContext context,
        string? cacheScopeKey)
    {
        if (fragments.Count != 1)
        {
            throw new ArgumentException(
                "Multi-fragment structure views require an explicit relation profile and traversal budget.",
                nameof(fragments));
        }

        return Build(
            fragments,
            context,
            CpgRelationProfile.StructuralContainment,
            CpgQueryDirection.Incoming,
            CreateCompatibilityBudget(),
            cacheScopeKey);
    }

    public NLCPGStructureView Build(
        IReadOnlyCollection<SyntaxNode> fragments,
        CpgAnalysisContext context,
        CpgRelationProfile profile,
        CpgQueryDirection direction,
        NLCPGTraversalBudget budget,
        string? cacheScopeKey = null)
    {
        var result = Query(fragments, context, profile, direction, budget, cacheScopeKey);
        if (result.Status != CpgQueryStatus.Complete || result.View is null)
        {
            throw new InvalidOperationException(
                $"Structure view query did not complete: {result.Status} ({result.TruncationReason ?? "no reason"}).");
        }

        return result.View;
    }

    public CpgStructureViewQueryResult Query(
        IReadOnlyCollection<SyntaxNode> fragments,
        CpgAnalysisContext context,
        CpgRelationProfile profile,
        CpgQueryDirection direction,
        NLCPGTraversalBudget budget,
        string? cacheScopeKey = null)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        ArgumentNullException.ThrowIfNull(context);
        if (fragments.Count == 0)
        {
            throw new ArgumentException("At least one syntax fragment is required.", nameof(fragments));
        }

        var fragmentList = fragments.ToArray();
        var cacheKey = BuildCacheKey(fragmentList, profile, direction, budget, cacheScopeKey);
        var runCache = RunCaches.GetValue(context, static _ => new AnalysisRunCache());
        if (runCache.TryGetView(cacheKey, out var cached))
        {
            return new CpgStructureViewQueryResult(cached, CpgQueryStatus.Complete, null);
        }

        var fragmentNodeSets = fragmentList
            .Select(fragment => ResolveGraphNodesInside(context.Graph, fragment))
            .ToArray();
        if (fragmentNodeSets.Any(nodes => nodes.Count == 0))
        {
            return new CpgStructureViewQueryResult(null, CpgQueryStatus.Disconnected, null);
        }

        var selectedNodeIds = fragmentNodeSets
            .SelectMany(nodes => nodes)
            .ToHashSet();
        var selectedEdges = new HashSet<NLCPGEdge>();
        var service = context.RelationQueryService ??
            new CpgRelationQueryService(context.Graph, context.AvailableCapabilities);
        for (var left = 0; left < fragmentNodeSets.Length; left += 1)
        {
            for (var right = left + 1; right < fragmentNodeSets.Length; right += 1)
            {
                var relation = service.Query(new CpgRelationQuery(
                    profile,
                    direction,
                    new CpgNodeSelector(NodeIds: fragmentNodeSets[left]),
                    new CpgNodeSelector(NodeIds: fragmentNodeSets[right]),
                    budget,
                    CpgRelationProfiles.GetRequiredCapabilities(profile),
                    CpgQueryPurpose.StructureView));
                if (relation.Status != CpgQueryStatus.Complete)
                {
                    return new CpgStructureViewQueryResult(
                        null,
                        relation.Status,
                        relation.TruncationReason);
                }

                selectedNodeIds.UnionWith(relation.Nodes.Select(node => node.NodeId!.Value));
                selectedEdges.UnionWith(relation.Edges);
            }
        }

        var allowedKinds = CpgRelationProfiles.GetAllowedEdgeKinds(profile);
        foreach (var edge in context.Graph.Edges)
        {
            if (allowedKinds.Contains(edge.Kind) &&
                selectedNodeIds.Contains(edge.SourceNodeId) &&
                selectedNodeIds.Contains(edge.TargetNodeId))
            {
                selectedEdges.Add(edge);
            }
        }

        var nodes = selectedNodeIds
            .Select(context.Graph.GetNode)
            .Where(node => node.HasValue)
            .Select(node => node.GetValueOrDefault())
            .OrderBy(node => node.SpanStart ?? int.MaxValue)
            .ThenBy(node => node.SpanEnd ?? int.MaxValue)
            .ThenBy(node => node.NodeId)
            .ToArray();
        var rootCandidates = nodes
            .Where(node => node.Kind == NLCPGNodeKind.SyntaxNode &&
                string.Equals(context.Graph.ResolveFilePath(node) ?? string.Empty, fragmentList[0].SyntaxTree.FilePath ?? string.Empty, StringComparison.Ordinal) &&
                node.SpanStart == fragmentList[0].SpanStart &&
                node.SpanEnd == fragmentList[0].Span.End &&
                string.Equals(context.Graph.ResolveDisplayKind(node), fragmentList[0].Kind().ToString(), StringComparison.Ordinal))
            .ToArray();
        if (rootCandidates.Length != 1)
        {
            return new CpgStructureViewQueryResult(null, CpgQueryStatus.Ambiguous, null);
        }

        var edges = selectedEdges
            .OrderBy(edge => edge.Kind)
            .ThenBy(edge => edge.StructuredLabel?.StableKey, StringComparer.Ordinal)
            .ThenBy(edge => edge.SourceNodeId)
            .ThenBy(edge => edge.TargetNodeId)
            .ToArray();
        var view = new NLCPGStructureView(rootCandidates[0], nodes, edges);
        runCache.RememberView(cacheKey, view);
        return new CpgStructureViewQueryResult(view, CpgQueryStatus.Complete, null);
    }

    private static IReadOnlySet<NodeId> ResolveGraphNodesInside(NLCPGGraph graph, SyntaxNode fragment)
    {
        var filePath = fragment.SyntaxTree.FilePath ?? string.Empty;
        return graph.Nodes
            .Where(node => node.NodeId.HasValue &&
                string.Equals(graph.ResolveFilePath(node) ?? string.Empty, filePath, StringComparison.Ordinal) &&
                node.SpanStart >= fragment.SpanStart &&
                node.SpanEnd <= fragment.Span.End)
            .Select(node => node.NodeId!.Value)
            .ToHashSet();
    }

    private static string BuildCacheKey(
        IReadOnlyList<SyntaxNode> fragments,
        CpgRelationProfile profile,
        CpgQueryDirection direction,
        NLCPGTraversalBudget budget,
        string? cacheScopeKey)
    {
        var fragmentKey = string.Join(
            "|",
            fragments.Select(fragment =>
                $"{fragment.SyntaxTree.FilePath}:{fragment.SpanStart}:{fragment.Span.Length}:{fragment.RawKind}"));
        return string.Join("|", cacheScopeKey ?? string.Empty, profile, direction, budget, fragmentKey);
    }

    private static NLCPGTraversalBudget CreateCompatibilityBudget()
    {
        return new NLCPGTraversalBudget(16, 1, 1, 4096, 8192);
    }

    private sealed class AnalysisRunCache
    {
        private readonly ConcurrentDictionary<string, NLCPGStructureView> _views = new(StringComparer.Ordinal);

        public bool TryGetView(string cacheKey, out NLCPGStructureView view)
        {
            return _views.TryGetValue(cacheKey, out view!);
        }

        public void RememberView(string cacheKey, NLCPGStructureView view)
        {
            _views.TryAdd(cacheKey, view);
        }
    }
}
