using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using QuikGraph;
using QuikGraph.Algorithms.Search;

namespace TerrariaTools.Analysis
{
    /// <summary>
    /// 类型冲突分析器
    /// 检测不同程序集中的类型名称冲突，辅助解决命名空间污染问题。
    /// </summary>
    public sealed class TypeConflictAnalyzer : ITypeConflictAnalyzer
    {
        /// <summary>
        /// 程序集类型映射缓存，用于提高重复分析的效率。
        /// </summary>
        private static readonly Dictionary<AssemblyIdentity, Dictionary<string, HashSet<string>>> AssemblyTypeMapCache = new();

        /// <summary>
        /// 异步分析全局类型冲突
        /// </summary>
        /// <param name="solution">Roslyn 解决方案</param>
        /// <returns>存在冲突的类型标识符集合</returns>
        public async Task<HashSet<string>> AnalyzeGlobalConflictsAsync(Solution solution)
        {
            var conflictIdentifiers = new HashSet<string>(StringComparer.Ordinal);
            var typeCounts = new ConcurrentDictionary<string, ConcurrentBag<string>>(StringComparer.Ordinal);

            foreach (var project in solution.Projects)
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null)
                {
                    continue;
                }

                var assemblies = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default) { compilation.Assembly };
                foreach (var reference in compilation.References)
                {
                    if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol asm)
                    {
                        assemblies.Add(asm);
                    }
                }

                foreach (var assembly in assemblies)
                {
                    Dictionary<string, HashSet<string>> assemblyMap;
                    lock (AssemblyTypeMapCache)
                    {
                        if (!AssemblyTypeMapCache.TryGetValue(assembly.Identity, out assemblyMap!))
                        {
                            assemblyMap = ScanAssemblyTypes(assembly);
                            AssemblyTypeMapCache[assembly.Identity] = assemblyMap;
                        }
                    }

                    foreach (var kv in assemblyMap)
                    {
                        var bag = typeCounts.GetOrAdd(kv.Key, _ => new ConcurrentBag<string>());
                        foreach (var fullName in kv.Value)
                        {
                            bag.Add(fullName);
                        }
                    }
                }
            }

            foreach (var kv in typeCounts)
            {
                if (kv.Value.Distinct().Count() > 1)
                {
                    conflictIdentifiers.Add(kv.Key);
                }
            }

            return conflictIdentifiers;
        }

        /// <summary>
        /// 扫描程序集中的所有类型并建立名称映射
        /// </summary>
        /// <param name="assembly">程序集符号</param>
        /// <returns>类型名称到全名集合的映射字典</returns>
        private static Dictionary<string, HashSet<string>> ScanAssemblyTypes(IAssemblySymbol assembly)
        {
            var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var nsGraph = BuildNamespaceGraph(assembly.GlobalNamespace);
            var dfs = new DepthFirstSearchAlgorithm<INamespaceSymbol, Edge<INamespaceSymbol>>(nsGraph);

            dfs.DiscoverVertex += ns =>
            {
                foreach (var member in ns.GetMembers())
                {
                    if (member is INamedTypeSymbol type && !type.Name.Contains("<", StringComparison.Ordinal))
                    {
                        if (type.DeclaredAccessibility == Accessibility.Public || type.DeclaredAccessibility == Accessibility.Internal)
                        {
                            if (!map.TryGetValue(type.Name, out var set))
                            {
                                set = new HashSet<string>(StringComparer.Ordinal);
                                map[type.Name] = set;
                            }

                            set.Add(type.ToDisplayString());
                        }
                    }
                }
            };

            dfs.Compute(assembly.GlobalNamespace);

            return map;
        }

        /// <summary>
        /// 构建命名空间层级图
        /// </summary>
        /// <param name="root">根命名空间</param>
        /// <returns>命名空间邻接图</returns>
        private static AdjacencyGraph<INamespaceSymbol, Edge<INamespaceSymbol>> BuildNamespaceGraph(INamespaceSymbol root)
        {
            var graph = new AdjacencyGraph<INamespaceSymbol, Edge<INamespaceSymbol>>(allowParallelEdges: false);

            void AddNamespaceRecursive(INamespaceSymbol current)
            {
                graph.AddVertex(current);
                foreach (var child in current.GetNamespaceMembers())
                {
                    graph.AddVertex(child);
                    graph.AddEdge(new Edge<INamespaceSymbol>(current, child));
                    AddNamespaceRecursive(child);
                }
            }

            AddNamespaceRecursive(root);
            return graph;
        }
    }
}
