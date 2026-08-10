using Microsoft.CodeAnalysis;

namespace NLISSN.Core.Analysis.MethodLinkage;

internal static class MethodLinkageRoots
{
    internal static IReadOnlySet<IMethodSymbol> FindEntryReachability(MethodLinkageGraph graph)
    {
        return graph.EntryPoint is null
          ? graph.Methods
          : FindClosure(graph, new[] { graph.EntryPoint });
    }

    internal static IReadOnlySet<IMethodSymbol> FindExternalReferenceClosure(MethodLinkageGraph graph)
    {
        return FindClosure(graph, graph.ExternalRoots);
    }

    private static IReadOnlySet<IMethodSymbol> FindClosure(
      MethodLinkageGraph graph,
      IEnumerable<IMethodSymbol> roots)
    {
        var retained = new HashSet<IMethodSymbol>(roots, SymbolEqualityComparer.Default);
        var worklist = new Queue<IMethodSymbol>(retained);
        while (worklist.Count > 0)
        {
            var current = worklist.Dequeue();
            if (!graph.Edges.TryGetValue(current, out var callees))
            {
                continue;
            }

            foreach (var callee in callees)
            {
                if (retained.Add(callee))
                {
                    worklist.Enqueue(callee);
                }
            }
        }

        return retained;
    }
}
