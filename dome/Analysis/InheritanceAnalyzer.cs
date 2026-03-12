using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuikGraph;

namespace TerrariaTools.Analysis.Dome
{
    /// <summary>
    /// 继承关系分析器，构建项目级的继承依赖图。
    /// 包含类型级（继承、实现）和成员级（重写、实现）的依赖关系。
    /// </summary>
    public class InheritanceAnalyzer
    {
        /// <summary>
        /// 基于 Compilation 构建完整的继承拓扑图
        /// </summary>
        public BidirectionalGraph<ISymbol, InheritanceEdge> Build(Compilation compilation)
        {
            var graph = new BidirectionalGraph<ISymbol, InheritanceEdge>();

            // 1. 一次性获取所有项目中的命名空间和类型，而不是遍历语法树
            var stack = new Stack<INamespaceSymbol>();
            stack.Push(compilation.GlobalNamespace);

            while (stack.Count > 0)
            {
                var ns = stack.Pop();
                foreach (var member in ns.GetMembers())
                {
                    if (member is INamespaceSymbol childNs)
                    {
                        stack.Push(childNs);
                    }
                    else if (member is INamedTypeSymbol typeSymbol)
                    {
                        AnalyzeTypeRecursive(typeSymbol, graph);
                    }
                }
            }

            return graph;
        }

        private void AnalyzeTypeRecursive(INamedTypeSymbol typeSymbol, BidirectionalGraph<ISymbol, InheritanceEdge> graph)
        {
            if (graph.ContainsVertex(typeSymbol)) return;
            graph.AddVertex(typeSymbol);

            // 1. 类型级: 继承 (Base Class)
            if (typeSymbol.BaseType != null && typeSymbol.BaseType.SpecialType != SpecialType.System_Object)
            {
                AnalyzeTypeRecursive(typeSymbol.BaseType, graph);
                graph.AddEdge(new InheritanceEdge(typeSymbol, typeSymbol.BaseType, InheritanceRelationType.Inherits));
            }

            // 2. 类型级: 接口实现 (Interface Implementation)
            foreach (var iface in typeSymbol.Interfaces)
            {
                AnalyzeTypeRecursive(iface, graph);
                graph.AddEdge(new InheritanceEdge(typeSymbol, iface, InheritanceRelationType.Implements));
            }

            // 3. 成员级: 方法和属性
            foreach (var member in typeSymbol.GetMembers())
            {
                if (member.IsImplicitlyDeclared) continue;

                if (member is IMethodSymbol method)
                {
                    AnalyzeMethod(method, typeSymbol, graph);
                }
                else if (member is IPropertySymbol property)
                {
                    AnalyzeProperty(property, typeSymbol, graph);
                }
            }

            // 4. 嵌套类型
            foreach (var nestedType in typeSymbol.GetTypeMembers())
            {
                AnalyzeTypeRecursive(nestedType, graph);
            }
        }

        public static IEnumerable<INamedTypeSymbol> GetBaseTypes(INamedTypeSymbol symbol)
        {
            var current = symbol.BaseType;
            while (current != null && current.SpecialType != SpecialType.System_Object)
            {
                yield return current;
                current = current.BaseType;
            }
        }

        public static async System.Threading.Tasks.Task<bool> HasDerivedClassesAsync(INamedTypeSymbol symbol, Solution solution)
        {
            var derivedClasses = await Microsoft.CodeAnalysis.FindSymbols.SymbolFinder.FindDerivedClassesAsync(symbol, solution);
            return derivedClasses.Any();
        }

        public static bool IsInInheritanceChain(IMethodSymbol symbol)
        {
            if (symbol.IsOverride || symbol.IsVirtual || symbol.IsAbstract) return true;
            
            // Check interface implementation
            if (symbol.ContainingType.AllInterfaces.Any(i => i.GetMembers().OfType<IMethodSymbol>().Any(m => 
                SymbolEqualityComparer.Default.Equals(symbol.ContainingType.FindImplementationForInterfaceMember(m), symbol))))
            {
                return true;
            }

            return false;
        }

        private void AnalyzeMethod(IMethodSymbol method, INamedTypeSymbol typeSymbol, BidirectionalGraph<ISymbol, InheritanceEdge> graph)
        {
            graph.AddVertex(method);

            // 3.1 重写 (Override)
            if (method.IsOverride && method.OverriddenMethod != null)
            {
                graph.AddVertex(method.OverriddenMethod);
                graph.AddEdge(new InheritanceEdge(method, method.OverriddenMethod, InheritanceRelationType.Overrides));
            }

            // 3.2 接口实现 (Interface Implementation)
            foreach (var iface in typeSymbol.AllInterfaces)
            {
                foreach (var ifaceMember in iface.GetMembers().OfType<IMethodSymbol>())
                {
                    var impl = typeSymbol.FindImplementationForInterfaceMember(ifaceMember);
                    if (impl != null && SymbolEqualityComparer.Default.Equals(impl, method))
                    {
                        graph.AddVertex(ifaceMember);
                        graph.AddEdge(new InheritanceEdge(method, ifaceMember, InheritanceRelationType.Implementation));
                    }
                }
            }
        }

        private void AnalyzeProperty(IPropertySymbol property, INamedTypeSymbol typeSymbol, BidirectionalGraph<ISymbol, InheritanceEdge> graph)
        {
             graph.AddVertex(property);

            // 3.3 重写 (Override)
            if (property.IsOverride && property.OverriddenProperty != null)
            {
                graph.AddVertex(property.OverriddenProperty);
                graph.AddEdge(new InheritanceEdge(property, property.OverriddenProperty, InheritanceRelationType.Overrides));
            }

            // 3.4 接口实现 (Interface Implementation)
            foreach (var iface in typeSymbol.AllInterfaces)
            {
                foreach (var ifaceMember in iface.GetMembers().OfType<IPropertySymbol>())
                {
                    var impl = typeSymbol.FindImplementationForInterfaceMember(ifaceMember);
                    if (impl != null && SymbolEqualityComparer.Default.Equals(impl, property))
                    {
                        graph.AddVertex(ifaceMember);
                        graph.AddEdge(new InheritanceEdge(property, ifaceMember, InheritanceRelationType.Implementation));
                    }
                }
            }
        }
    }

    public enum InheritanceRelationType
    {
        Inherits,
        Implements,
        Overrides,
        Implementation
    }

    public class InheritanceEdge : IEdge<ISymbol>
    {
        public ISymbol Source { get; }
        public ISymbol Target { get; }
        public InheritanceRelationType RelationType { get; }

        public InheritanceEdge(ISymbol source, ISymbol target, InheritanceRelationType relationType)
        {
            Source = source;
            Target = target;
            RelationType = relationType;
        }
    }
}
