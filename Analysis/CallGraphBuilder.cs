using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Analysis
{
    /// <summary>
    /// 高效构建方法调用图的工具。
    /// 通过遍历语法树和语义模型，一次性建立方法之间的依赖关系。
    /// </summary>
    public class CallGraphBuilder
    {
        private readonly Solution _solution;
        private readonly ConcurrentDictionary<IMethodSymbol, HashSet<IMethodSymbol>> _callGraph = new(SymbolEqualityComparer.Default);
        private readonly ConcurrentDictionary<IMethodSymbol, HashSet<IMethodSymbol>> _reverseCallGraph = new(SymbolEqualityComparer.Default);
        private readonly ConcurrentBag<IMethodSymbol> _allMethods = new();
        private readonly ConcurrentDictionary<IMethodSymbol, bool> _allInterfaceImplementations = new(SymbolEqualityComparer.Default);
        private readonly ConcurrentDictionary<IMethodSymbol, ConcurrentBag<IMethodSymbol>> _interfaceToImplementations = new(SymbolEqualityComparer.Default);

        public CallGraphBuilder(Solution solution)
        {
            _solution = solution;
        }

        public async Task BuildAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            var projects = _solution.Projects.ToList();
            if (!projects.SelectMany(p => p.Documents).Any()) throw new Exception($"Debug: No documents found in solution. Projects: {projects.Count}");

            int totalProjects = projects.Count;
            int processedProjects = 0;

            foreach (var project in projects)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var compilation = await project.GetCompilationAsync(cancellationToken);
                if (compilation == null) continue;

                var documents = project.Documents
                    .Where(d => (d.FilePath != null && d.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) || (d.Name != null && d.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                await Parallel.ForEachAsync(documents, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken }, async (doc, ct) =>
                {
                    var root = await doc.GetSyntaxRootAsync(ct);
                    var semanticModel = await doc.GetSemanticModelAsync(ct);

                    if (semanticModel == null || root == null) return;

                    var walker = new MethodDependencyWalker(semanticModel, ct, _allInterfaceImplementations, _interfaceToImplementations);
                    walker.Visit(root);

                    // 收集该文档中定义的所有方法及其依赖
                    foreach (var (caller, callees) in walker.Dependencies)
                    {
                        var callerDef = caller.OriginalDefinition;
                        lock (_allMethods)
                        {
                            _allMethods.Add(callerDef);
                        }

                        foreach (var callee in callees)
                        {
                            AddDependency(callerDef, callee.OriginalDefinition);
                        }
                    }

                    // 收集虽然没有依赖但被声明的方法（孤立节点）
                    foreach (var method in walker.DeclaredMethods)
                    {
                        var methodDef = method.OriginalDefinition;
                        if (!_callGraph.ContainsKey(methodDef))
                        {
                            lock (_allMethods)
                            {
                                _allMethods.Add(methodDef);
                            }
                            _callGraph.TryAdd(methodDef, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
                        }
                    }
                });

                Interlocked.Increment(ref processedProjects);
                progress?.Report($"[构建依赖图] 已处理项目 {processedProjects}/{totalProjects}: {project.Name}");
            }
        }

        private void AddDependency(IMethodSymbol caller, IMethodSymbol callee)
        {
            // Forward edge: Caller -> Callee
            _callGraph.AddOrUpdate(caller,
                new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default) { callee },
                (key, existing) => { lock (existing) { existing.Add(callee); } return existing; });

            // Backward edge: Callee -> Caller (Used By)
            _reverseCallGraph.AddOrUpdate(callee,
                new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default) { caller },
                (key, existing) => { lock (existing) { existing.Add(caller); } return existing; });
        }

        public enum GraphMethodAction
        {
            None,
            Delete,
            Privatize,
            ClearBody,
            Decouple // 保持方法体但取消继承/重写关系
        }

        private bool IsVisibleToExternal(IMethodSymbol method)
        {
            var symbol = (ISymbol)method;
            while (symbol != null && symbol.Kind != SymbolKind.Namespace)
            {
                switch (symbol.DeclaredAccessibility)
                {
                    case Accessibility.Public:
                    case Accessibility.Protected:
                    case Accessibility.ProtectedOrInternal:
                        symbol = symbol.ContainingSymbol;
                        break;
                    default:
                        return false;
                }
            }
            return true;
        }

        public Dictionary<IMethodSymbol, GraphMethodAction> AnalyzeMethods(IEnumerable<IMethodSymbol>? roots = null, bool aggressive = false, bool enableRatioAnalysis = true)
        {
            var actions = new Dictionary<IMethodSymbol, GraphMethodAction>(SymbolEqualityComparer.Default);
            bool effectiveAggressive = aggressive || (roots != null);

            // 1. Mark used methods
            var usedMethods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            if (roots != null)
            {
                var queue = new Queue<IMethodSymbol>(roots);
                while (queue.Count > 0)
                {
                    var method = queue.Dequeue();
                    if (usedMethods.Add(method))
                    {
                        if (_callGraph.TryGetValue(method, out var callees))
                        {
                            foreach (var callee in callees)
                            {
                                queue.Enqueue(callee);
                            }
                        }
                    }
                }
            }
            else
            {
                foreach (var method in _allMethods)
                {
                    if (method.MethodKind == MethodKind.Constructor || method.MethodKind == MethodKind.StaticConstructor)
                    {
                        usedMethods.Add(method);
                        continue;
                    }

                    if (!aggressive)
                    {
                        if (method.DeclaredAccessibility == Accessibility.Public)
                        {
                             usedMethods.Add(method);
                        }
                    }

                    if (_reverseCallGraph.TryGetValue(method, out var callers) && callers.Count > 0)
                    {
                         usedMethods.Add(method);
                    }
                }
            }

            // 2. Determine Actions
            foreach (var method in _allMethods)
            {
                if (usedMethods.Contains(method))
                {
                    if (_reverseCallGraph.TryGetValue(method, out var callers))
                    {
                         if (ShouldPrivatize(method, callers, effectiveAggressive))
                         {
                             actions[method] = GraphMethodAction.Privatize;
                         }
                    }
                }
                else
                {
                    var action = DetermineAction(method, effectiveAggressive);
                    if (action != GraphMethodAction.None)
                    {
                        actions[method] = action;
                    }
                }
            }

            // 3. Apply Ratio-Based Refactoring
            if (enableRatioAnalysis)
            {
                ApplyRatioBasedRefactoring(actions);
            }

            return actions;
        }

        private void ApplyRatioBasedRefactoring(Dictionary<IMethodSymbol, GraphMethodAction> actions)
        {
            // 收集所有实现关系 (BaseMethod -> List of Implementations)
            var implementationGroups = new Dictionary<IMethodSymbol, List<IMethodSymbol>>(SymbolEqualityComparer.Default);

            foreach (var method in _allMethods)
            {
                var baseMethod = GetRootOverriddenMethod(method);
                if (baseMethod != null && !SymbolEqualityComparer.Default.Equals(baseMethod, method))
                {
                    if (!implementationGroups.TryGetValue(baseMethod, out var implementations))
                    {
                        implementations = new List<IMethodSymbol>();
                        implementationGroups[baseMethod] = implementations;
                    }
                    if (!implementations.Contains(method, SymbolEqualityComparer.Default))
                    {
                        implementations.Add(method);
                    }
                }
            }

            // 合并接口实现关系
            foreach (var kvp in _interfaceToImplementations)
            {
                var ifaceMethod = kvp.Key;
                if (!implementationGroups.TryGetValue(ifaceMethod, out var implementations))
                {
                    implementations = new List<IMethodSymbol>();
                    implementationGroups[ifaceMethod] = implementations;
                }
                foreach (var impl in kvp.Value)
                {
                    if (!implementations.Contains(impl, SymbolEqualityComparer.Default))
                    {
                        implementations.Add(impl);
                    }
                }
            }

            // 分析每一组实现
            foreach (var kvp in implementationGroups)
            {
                var baseMethod = kvp.Key;
                var implementations = kvp.Value;

                // 检查基类方法或接口方法是否在项目中，或者任何实现是否被保留
                bool shouldKeepGroup = false;

                // 1. 如果基类方法在项目中且被保留，则必须保留所有子类实现
                if (IsProjectMethod(baseMethod))
                {
                    var baseAction = actions.GetValueOrDefault(baseMethod, GraphMethodAction.None);
                    if (baseAction == GraphMethodAction.None || baseAction == GraphMethodAction.Privatize || baseAction == GraphMethodAction.ClearBody)
                    {
                        shouldKeepGroup = true;
                    }
                }
                // 2. 如果基类方法不在项目中（如库方法/外部接口），但它是接口或虚方法，
                //    我们需要确保如果子类实现了它，且子类本身被保留，则实现也必须保留。
                else if (baseMethod.ContainingType.TypeKind == TypeKind.Interface || baseMethod.IsVirtual || baseMethod.IsAbstract)
                {
                    // 对于外部接口或虚方法，我们默认认为需要保留其实现关系
                    shouldKeepGroup = true;
                }

                if (shouldKeepGroup)
                {
                    foreach (var impl in implementations)
                    {
                        var currentAction = actions.GetValueOrDefault(impl);
                        // 如果当前决定删除，但它是该组（接口/基类虚方法）的一部分，我们必须保留它（清空体）
                        // 注意：如果已经是 None (保留完整代码) 或 Privatize，则不需要改为 ClearBody
                        if (currentAction == GraphMethodAction.Delete)
                        {
                            actions[impl] = GraphMethodAction.ClearBody;
                        }
                    }
                }
            }

            // --- 第二轮：确保所有被保留方法所满足的接口契约也被保留 ---
            // 这是一个反馈环：如果一个方法被保留，它可能实现了某个接口，那么该接口在其他类中的实现也必须被保留
            bool changed;
            do
            {
                changed = false;
                var currentActions = new Dictionary<IMethodSymbol, GraphMethodAction>(actions, SymbolEqualityComparer.Default);
                foreach (var method in _allMethods)
                {
                    if (actions.TryGetValue(method, out var action) && (action == GraphMethodAction.None || action == GraphMethodAction.Privatize || action == GraphMethodAction.ClearBody))
                    {
                        // 这是一个被保留的方法，检查它是否实现了任何接口
                        var root = GetRootOverriddenMethod(method);
                        if (root != null && root.ContainingType.TypeKind == TypeKind.Interface)
                        {
                            // 它实现了接口 root，确保该接口的所有实现都被保留
                            if (implementationGroups.TryGetValue(root, out var otherImpls))
                            {
                                foreach (var otherImpl in otherImpls)
                                {
                                    if (!actions.TryGetValue(otherImpl, out var otherAction) || otherAction == GraphMethodAction.Delete)
                                    {
                                        actions[otherImpl] = GraphMethodAction.ClearBody;
                                        changed = true;
                                    }
                                }
                            }
                        }
                    }

                    // 新增：处理重写(Override)关系
                    // 如果一个重写方法被保留（None/ClearBody/Privatize），那么它重写的基类虚方法/抽象方法也必须被保留（至少作为声明存在）
                    if (actions.TryGetValue(method, out var myAction) && (myAction == GraphMethodAction.None || myAction == GraphMethodAction.ClearBody || myAction == GraphMethodAction.Privatize))
                    {
                        if (method.IsOverride && method.OverriddenMethod != null)
                        {
                            var baseMethod = method.OverriddenMethod;
                            // 只有当基类方法在项目中且被标记为删除时，才需要将其改为ClearBody以保持继承链
                            // 注意：如果 baseMethod 不在 actions 中，可能是因为它从未被访问，或者不在分析范围内。
                            // 但如果它在 actions 中且是 Delete，必须挽救。
                            // 如果不在 actions 中，我们尝试添加它（虽然通常都在）
                            if (actions.TryGetValue(baseMethod, out var baseAction))
                            {
                                if (baseAction == GraphMethodAction.Delete)
                                {
                                    actions[baseMethod] = GraphMethodAction.ClearBody;
                                    changed = true;
                                }
                            }
                        }
                    }
                }
            } while (changed);
        }

        private bool IsProjectMethod(IMethodSymbol method)
        {
            // 检查该方法是否有源码定义
            return method.DeclaringSyntaxReferences.Any();
        }

        private GraphMethodAction DetermineAction(IMethodSymbol method, bool aggressive)
        {
            // 0. Struct 不处理 (通常包含生命周期管理)
            if (method.ContainingType.TypeKind == TypeKind.Struct)
            {
                return GraphMethodAction.None;
            }

            // 1. 构造函数/析构函数通常不删除
            if (method.MethodKind == MethodKind.Constructor || method.MethodKind == MethodKind.StaticConstructor || method.MethodKind == MethodKind.Destructor)
            {
                return GraphMethodAction.None;
            }

            // 2. Main 函数作为入口点不删除
            if (method.Name == "Main" && method.IsStatic)
            {
                return GraphMethodAction.None;
            }

            // 3. Public 方法判定
            if (method.DeclaredAccessibility == Accessibility.Public)
            {
                if (!aggressive) return GraphMethodAction.None;
            }

            // 4. 接口实现 / 虚方法 / 重写方法 (Non-Public OR Aggressive)
            if (IsInterfaceImplementation(method) || method.IsVirtual || method.IsOverride || method.IsAbstract)
            {
                if (method.IsAbstract) return GraphMethodAction.None;
                return GraphMethodAction.ClearBody;
            }

            // 5. 特殊处理：如果该方法是接口实现的一部分（即使是通过基类实现的），也要保留
            if (IsInterfaceImplementation(method))
            {
                return GraphMethodAction.ClearBody;
            }

            // 6. 普通方法 -> Delete
            return GraphMethodAction.Delete;
        }

        private bool ShouldPrivatize(IMethodSymbol method, HashSet<IMethodSymbol> callers, bool aggressive)
        {
            // 0. Struct 的方法不进行私有化 (由于生命周期和拷贝语义，改动风险较大)
            if (method.ContainingType.TypeKind == TypeKind.Struct) return false;

            // 只有公开程度高于 Private 的才需要考虑私有化
            if (method.DeclaredAccessibility == Accessibility.Private) return false;

            if (!aggressive && IsVisibleToExternal(method))
            {
                return false;
            }

            // 构造函数不私有化 (通常有特殊用途)
            if (method.MethodKind == MethodKind.Constructor || method.MethodKind == MethodKind.StaticConstructor) return false;

            if (IsInterfaceImplementation(method)) return false;
            if (method.IsVirtual || method.IsOverride || method.IsAbstract) return false;
            if (method.Name == "Main" && method.IsStatic) return false;

            foreach (var caller in callers)
            {
                if (!SymbolEqualityComparer.Default.Equals(caller.ContainingType, method.ContainingType))
                {
                    return false;
                }
            }
            return true;
        }


        private IMethodSymbol? GetRootOverriddenMethod(IMethodSymbol method)
        {
            // 向上寻找最顶层的重写方法 (抽象方法或虚方法)
            var current = method;
            while (current.OverriddenMethod != null)
            {
                current = current.OverriddenMethod;
            }

            // 如果它实现了接口方法，也将其视为基方法 (处理比例分析)
            foreach (var iface in method.ContainingType.AllInterfaces)
            {
                foreach (var ifaceMethod in iface.GetMembers().OfType<IMethodSymbol>())
                {
                    var impl = method.ContainingType.FindImplementationForInterfaceMember(ifaceMethod);
                    if (SymbolEqualityComparer.Default.Equals(impl, method))
                    {
                        return ifaceMethod;
                    }
                }
            }

            return SymbolEqualityComparer.Default.Equals(current, method) ? null : current;
        }

        /// <summary>
        /// 获取所有在当前解决方案中定义的方法（不含外部引用）。
        /// </summary>
        public HashSet<IMethodSymbol> AllDeclaredMethods => new(_allMethods, SymbolEqualityComparer.Default);

        /// <summary>
        /// 获取从指定种子方法开始所有可达的方法。
        /// 仅包含当前解决方案中定义的方法，以保证统计口径与全局分析一致。
        /// </summary>
        public HashSet<IMethodSymbol> GetReachableMethods(IMethodSymbol seed)
        {
            var projectMethods = AllDeclaredMethods;
            var reachable = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            var stack = new Stack<IMethodSymbol>();

            // 确保种子本身也在统计范围内
            if (projectMethods.Contains(seed))
            {
                stack.Push(seed);
            }

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (reachable.Add(current))
                {
                    if (_callGraph.TryGetValue(current, out var callees))
                    {
                        foreach (var callee in callees)
                        {
                            // 仅追踪和收集项目内的方法
                            if (projectMethods.Contains(callee) && !reachable.Contains(callee))
                            {
                                stack.Push(callee);
                            }
                        }
                    }
                }
            }
            return reachable;
        }


        private bool IsInterfaceImplementation(IMethodSymbol method)
        {
            if (_allInterfaceImplementations.ContainsKey(method.OriginalDefinition)) return true;

            if (method.ExplicitInterfaceImplementations.Any()) return true;

            var containingType = method.ContainingType;
            if (containingType == null) return false;

            // 检查当前类及其所有基类实现的接口
            var interfaces = containingType.AllInterfaces;
            foreach (var i in interfaces)
            {
                foreach (var interfaceMethod in i.GetMembers().OfType<IMethodSymbol>())
                {
                    var implementation = containingType.FindImplementationForInterfaceMember(interfaceMethod);
                    if (implementation != null && SymbolEqualityComparer.Default.Equals(implementation, method))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private class MethodDependencyWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel _semanticModel;
            private readonly CancellationToken _cancellationToken;
            private readonly ConcurrentDictionary<IMethodSymbol, bool> _allInterfaceImplementations;
            private readonly ConcurrentDictionary<IMethodSymbol, ConcurrentBag<IMethodSymbol>> _interfaceToImplementations;

            public Dictionary<IMethodSymbol, HashSet<IMethodSymbol>> Dependencies { get; } = new(SymbolEqualityComparer.Default);
            public HashSet<IMethodSymbol> DeclaredMethods { get; } = new(SymbolEqualityComparer.Default);

            private IMethodSymbol? _currentMethod;

            public MethodDependencyWalker(SemanticModel semanticModel, CancellationToken cancellationToken, ConcurrentDictionary<IMethodSymbol, bool> allInterfaceImplementations, ConcurrentDictionary<IMethodSymbol, ConcurrentBag<IMethodSymbol>> interfaceToImplementations)
            {
                _semanticModel = semanticModel;
                _cancellationToken = cancellationToken;
                _allInterfaceImplementations = allInterfaceImplementations;
                _interfaceToImplementations = interfaceToImplementations;
            }

            public override void VisitClassDeclaration(ClassDeclarationSyntax node)
            {
                var symbol = _semanticModel.GetDeclaredSymbol(node, _cancellationToken);
                if (symbol != null)
                {
                    foreach (var iface in symbol.AllInterfaces)
                    {
                        foreach (var ifaceMember in iface.GetMembers().OfType<IMethodSymbol>())
                        {
                            var implementation = symbol.FindImplementationForInterfaceMember(ifaceMember);
                            if (implementation != null && implementation is IMethodSymbol implMethod)
                            {
                                var implDef = implMethod.OriginalDefinition;
                                var ifaceDef = ifaceMember.OriginalDefinition;
                                _allInterfaceImplementations.TryAdd(implDef, true);
                                var bag = _interfaceToImplementations.GetOrAdd(ifaceDef, _ => new ConcurrentBag<IMethodSymbol>());
                                bag.Add(implDef);
                            }
                        }
                    }
                }
                base.VisitClassDeclaration(node);
            }

            public override void VisitStructDeclaration(StructDeclarationSyntax node)
            {
                var symbol = _semanticModel.GetDeclaredSymbol(node, _cancellationToken);
                if (symbol != null)
                {
                    foreach (var iface in symbol.AllInterfaces)
                    {
                        foreach (var ifaceMember in iface.GetMembers().OfType<IMethodSymbol>())
                        {
                            var implementation = symbol.FindImplementationForInterfaceMember(ifaceMember);
                            if (implementation != null && implementation is IMethodSymbol implMethod)
                            {
                                var implDef = implMethod.OriginalDefinition;
                                var ifaceDef = ifaceMember.OriginalDefinition;
                                _allInterfaceImplementations.TryAdd(implDef, true);
                                var bag = _interfaceToImplementations.GetOrAdd(ifaceDef, _ => new ConcurrentBag<IMethodSymbol>());
                                bag.Add(implDef);
                            }
                        }
                    }
                }
                base.VisitStructDeclaration(node);
            }

            public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
            {
                var symbol = _semanticModel.GetDeclaredSymbol(node, _cancellationToken);
                if (symbol != null)
                {
                    _currentMethod = symbol;
                    DeclaredMethods.Add(symbol);
                    Dependencies[symbol] = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

                    base.VisitMethodDeclaration(node);

                    _currentMethod = null;
                }
            }

            public override void VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                // 处理方法调用
                var symbolInfo = _semanticModel.GetSymbolInfo(node, _cancellationToken);
                AddDependency(symbolInfo.Symbol);
                base.VisitInvocationExpression(node);
            }

            public override void VisitIdentifierName(IdentifierNameSyntax node)
            {
                // 处理方法作为委托、参数传递等情况 (例如 Action a = MethodA;)
                // 注意：InvocationExpression 也会包含 IdentifierName，为了避免重复，
                // 其实 VisitInvocationExpression 可以不处理，统一在这里处理。
                // 但为了保险，我们都检查一下。

                var symbolInfo = _semanticModel.GetSymbolInfo(node, _cancellationToken);
                AddDependency(symbolInfo.Symbol);
                base.VisitIdentifierName(node);
            }

            public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            {
                // 构造函数调用
                var symbolInfo = _semanticModel.GetSymbolInfo(node, _cancellationToken);
                AddDependency(symbolInfo.Symbol);
                base.VisitObjectCreationExpression(node);
            }

            public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
            {
                var symbol = _semanticModel.GetDeclaredSymbol(node, _cancellationToken);
                if (symbol != null && symbol.ContainingType != null)
                {
                    _currentMethod = symbol;
                    DeclaredMethods.Add(symbol);
                    Dependencies[symbol] = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

                    // 建立构造函数到所有接口实现的依赖
                    // 这确保了如果一个类被实例化，它的所有接口实现都会被保留（清空体或保留体）
                    var type = symbol.ContainingType;
                    foreach (var iface in type.AllInterfaces)
                    {
                        foreach (var ifaceMember in iface.GetMembers().OfType<IMethodSymbol>())
                        {
                            var implementation = type.FindImplementationForInterfaceMember(ifaceMember);
                            if (implementation != null)
                            {
                                AddDependency(implementation);
                            }
                        }
                    }

                    base.VisitConstructorDeclaration(node);
                    _currentMethod = null;
                }
            }

            private void AddDependency(ISymbol? symbol)
            {
                if (_currentMethod == null || symbol == null) return;

                // 我们只关心方法依赖
                if (symbol is IMethodSymbol targetMethod)
                {
                    // 处理扩展方法的 Reduced 形式，获取原始定义的静态方法
                    if (targetMethod.MethodKind == MethodKind.ReducedExtension && targetMethod.ReducedFrom != null)
                    {
                        targetMethod = targetMethod.ReducedFrom;
                    }

                    // 忽略自身递归调用 (递归不增加外部引用计数)
                    if (!SymbolEqualityComparer.Default.Equals(_currentMethod, targetMethod))
                    {
                        if (Dependencies.TryGetValue(_currentMethod, out var set))
                        {
                            set.Add(targetMethod);
                        }
                    }
                }
            }
        }
    }
}
