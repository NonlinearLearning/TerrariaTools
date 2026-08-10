using Microsoft.CodeAnalysis;

namespace NLISSN.Core.Analysis.MethodLinkage;

/// 从一个 Compilation 构建方法调用闭包，供 MethodGlobal 规则共同使用。
public static class MethodLinkageAnalysis
{
    public static MethodLinkageResult Create(Compilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var graph = MethodLinkageGraphBuilder.Build(compilation);
        if (graph.HasErrors)
        {
            return new MethodLinkageResult(
              hasErrors: true,
              graph.PrivateCandidates.Count,
              EmptyMethodSet(),
              EmptyMethodSet());
        }

        var reachableFromEntry = MethodLinkageRoots.FindEntryReachability(graph);
        var externallyRetained = MethodLinkageRoots.FindExternalReferenceClosure(graph);
        return new MethodLinkageResult(
          hasErrors: false,
          graph.PrivateCandidates.Count,
          Exclude(graph.Methods, reachableFromEntry),
          Exclude(graph.PrivateCandidates, externallyRetained));
    }

    private static IReadOnlySet<IMethodSymbol> Exclude(
      IEnumerable<IMethodSymbol> candidates,
      IReadOnlySet<IMethodSymbol> retained)
    {
        return new HashSet<IMethodSymbol>(
          candidates.Where(candidate => !retained.Contains(candidate)),
          SymbolEqualityComparer.Default);
    }

    private static IReadOnlySet<IMethodSymbol> EmptyMethodSet()
    {
        return new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
    }
}
