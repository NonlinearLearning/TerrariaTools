using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace TerrariaTools.Analysis
{
    /// <summary>
    /// 基于 Roslyn 的迭代依赖分析引擎。
    /// 实现了“重写思路.txt”中的静态分析和依赖图构建。
    /// 使用队列进行广度优先搜索 (BFS) 以避免深层递归导致的栈溢出。
    /// </summary>
    public class CodeDependencyAnalyzer
    {
        private readonly Solution _solution;
        private readonly DependencyGraph _graph = new DependencyGraph();
        private readonly HashSet<ISymbol> _processedSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        private readonly Queue<ISymbol> _pendingSymbols = new Queue<ISymbol>();

        public CodeDependencyAnalyzer(Solution solution)
        {
            _solution = solution ?? throw new ArgumentNullException(nameof(solution));
        }

        public DependencyGraph Graph => _graph;

        /// <summary>
        /// 从给定的种子符号开始分析依赖。
        /// </summary>
        public async Task AnalyzeRecursiveAsync(ISymbol seedSymbol)
        {
            if (seedSymbol == null) return;

            EnqueueSymbol(seedSymbol);

            while (_pendingSymbols.Count > 0)
            {
                var current = _pendingSymbols.Dequeue();
                await ProcessSymbolAsync(current);
            }
        }

        /// <summary>
        /// 从给定的多个种子符号开始分析依赖。
        /// </summary>
        public async Task AnalyzeRecursiveAsync(IEnumerable<ISymbol> seeds)
        {
            if (seeds == null) return;

            foreach (var seed in seeds)
            {
                EnqueueSymbol(seed);
            }

            while (_pendingSymbols.Count > 0)
            {
                var current = _pendingSymbols.Dequeue();
                await ProcessSymbolAsync(current);
            }
        }

        private void EnqueueSymbol(ISymbol symbol)
        {
            if (symbol == null || _processedSymbols.Contains(symbol))
                return;

            _processedSymbols.Add(symbol);
            _pendingSymbols.Enqueue(symbol);
            _graph.GetOrAddNode(symbol);
        }

        private async Task ProcessSymbolAsync(ISymbol symbol)
        {
            // 1. 获取符号的定义位置
            var reference = symbol.DeclaringSyntaxReferences.FirstOrDefault();

            // 对于没有源码定义的符号（外部符号），我们仅分析其泛型参数
            if (reference == null)
            {
                if (symbol is INamedTypeSymbol namedType)
                {
                    foreach (var arg in namedType.TypeArguments)
                    {
                        AddDependency(symbol, arg);
                    }
                }
                return;
            }

            // 1.0 处理包含类型的依赖：如果成员是必需的，其包含类型也是必需的
            if (symbol.ContainingType != null)
            {
                AddDependency(symbol, symbol.ContainingType);
            }

            // 1.1 处理各类型符号特有的显式依赖
            if (symbol is IMethodSymbol method)
            {
                AddDependency(symbol, method.ReturnType);
                foreach (var param in method.Parameters)
                {
                    AddDependency(symbol, param.Type);
                }

                // 处理方法的泛型参数及其约束
                foreach (var typeParam in method.TypeParameters)
                {
                    foreach (var constraint in typeParam.ConstraintTypes)
                    {
                        AddDependency(symbol, constraint);
                    }
                }

                if (method.IsOverride && method.OverriddenMethod != null)
                {
                    AddDependency(symbol, method.OverriddenMethod);
                }

                if (method.MethodKind == MethodKind.Constructor)
                {
                    foreach (var syntaxReference in method.DeclaringSyntaxReferences)
                    {
                        var syntax = syntaxReference.GetSyntax() as ConstructorDeclarationSyntax;
                        if (syntax?.Initializer != null)
                        {
                            var model = await _solution.GetDocument(syntax.SyntaxTree)!.GetSemanticModelAsync();
                            var constructorSymbol = model?.GetSymbolInfo(syntax.Initializer).Symbol;
                            if (constructorSymbol != null)
                            {
                                AddDependency(symbol, constructorSymbol);
                            }
                        }
                    }
                }
                foreach (var impl in method.ExplicitInterfaceImplementations)
                {
                    AddDependency(symbol, impl);
                }

                // 隐式接口实现：如果当前方法实现了某个接口成员，则该接口成员也是必要的
                if (method.ContainingType != null)
                {
                    foreach (var iface in method.ContainingType.AllInterfaces)
                    {
                        foreach (var interfaceMember in iface.GetMembers(method.Name))
                        {
                            var implementation = method.ContainingType.FindImplementationForInterfaceMember(interfaceMember);
                            if (SymbolEqualityComparer.Default.Equals(implementation, method))
                            {
                                AddDependency(symbol, interfaceMember);
                            }
                        }
                    }
                }
            }
            else if (symbol is IPropertySymbol property)
            {
                AddDependency(symbol, property.Type);
                if (property.OverriddenProperty != null)
                {
                    AddDependency(symbol, property.OverriddenProperty);
                }
                foreach (var impl in property.ExplicitInterfaceImplementations)
                {
                    AddDependency(symbol, impl);
                }
                // 隐式接口实现
                if (property.ContainingType != null)
                {
                    foreach (var iface in property.ContainingType.AllInterfaces)
                    {
                        foreach (var interfaceMember in iface.GetMembers(property.Name))
                        {
                            var implementation = property.ContainingType.FindImplementationForInterfaceMember(interfaceMember);
                            if (SymbolEqualityComparer.Default.Equals(implementation, property))
                            {
                                AddDependency(symbol, interfaceMember);
                            }
                        }
                    }
                }

                // 分析属性初始化器 (e.g. public int X { get; } = 10;)
                foreach (var syntaxRef in property.DeclaringSyntaxReferences)
                {
                    var syntax = await syntaxRef.GetSyntaxAsync();
                    if (syntax is PropertyDeclarationSyntax propDecl && propDecl.Initializer != null)
                    {
                         await AnalyzeExpressionAsync(symbol, propDecl.Initializer.Value, syntax.SyntaxTree);
                    }
                }
            }
            else if (symbol is IFieldSymbol field)
            {
                AddDependency(symbol, field.Type);

                // 分析字段初始化器 (e.g. private int _x = GetValue();)
                foreach (var syntaxRef in field.DeclaringSyntaxReferences)
                {
                    var syntax = await syntaxRef.GetSyntaxAsync();
                    if (syntax is VariableDeclaratorSyntax varDecl && varDecl.Initializer != null)
                    {
                        await AnalyzeExpressionAsync(symbol, varDecl.Initializer.Value, syntax.SyntaxTree);
                    }
                }
            }
            else if (symbol is IEventSymbol @event)
            {
                AddDependency(symbol, @event.Type);
                if (@event.OverriddenEvent != null)
                {
                    AddDependency(symbol, @event.OverriddenEvent);
                }
                foreach (var impl in @event.ExplicitInterfaceImplementations)
                {
                    AddDependency(symbol, impl);
                }
                // 隐式接口实现
                if (@event.ContainingType != null)
                {
                    foreach (var iface in @event.ContainingType.AllInterfaces)
                    {
                        foreach (var interfaceMember in iface.GetMembers(@event.Name))
                        {
                            var implementation = @event.ContainingType.FindImplementationForInterfaceMember(interfaceMember);
                            if (SymbolEqualityComparer.Default.Equals(implementation, @event))
                            {
                                AddDependency(symbol, interfaceMember);
                            }
                        }
                    }
                }
            }
            else if (symbol is INamedTypeSymbol type)
            {
                // 类、接口、结构、枚举
                if (type.BaseType != null)
                {
                    AddDependency(symbol, type.BaseType);
                }
                foreach (var iface in type.Interfaces)
                {
                    AddDependency(symbol, iface);
                }
                foreach (var arg in type.TypeArguments)
                {
                    AddDependency(symbol, arg);
                }

                // 处理类型的泛型参数及其约束
                foreach (var typeParam in type.TypeParameters)
                {
                    foreach (var constraint in typeParam.ConstraintTypes)
                    {
                        AddDependency(symbol, constraint);
                    }
                }

                // 关键增强：如果是接口，包含所有成员和继承的接口
                if (type.TypeKind == TypeKind.Interface)
                {
                    foreach (var member in type.GetMembers())
                    {
                        AddDependency(symbol, member);
                    }
                    foreach (var inheritedIface in type.AllInterfaces)
                    {
                        AddDependency(symbol, inheritedIface);
                    }
                }

                // 关键增强：如果是一个类或结构，它依赖于它所实现的所有接口成员的具体实现
                if (type.TypeKind == TypeKind.Class || type.TypeKind == TypeKind.Struct)
                {
                    // 必须保留静态构造函数，因为它包含类的初始化逻辑
                    var staticCtor = type.StaticConstructors.FirstOrDefault();
                    if (staticCtor != null)
                    {
                        AddDependency(symbol, staticCtor);
                    }

                    // 类必须保留其构造函数（如果是必需的）
                // 我们不包含所有构造函数，只包含无参构造函数（如果存在），以确保实例化可行
                // 或者如果类是静态的，则不需要构造函数
                    if (!type.IsStatic)
                    {
                        var ctor = type.Constructors.FirstOrDefault(c => c.Parameters.Length == 0 && !c.IsImplicitlyDeclared);
                        if (ctor != null)
                        {
                            AddDependency(symbol, ctor);
                        }
                    }
                }

                // 关键点：对于类，我们不需要自动包含其所有成员
                // 但为了编译正确，MemberSlicingRewriter 会保留接口实现
                // 所以我们在这里确保所有接口定义的成员所涉及的类型都能被找到
                foreach (var iface in type.AllInterfaces)
                {
                    AddDependency(symbol, iface);
                }
            }

            // 1.3 遍历语法树查找所有引用的符号 (仅针对有源码定义的符号)
            var syntaxNode = await reference.GetSyntaxAsync();
            var semanticModel = await _solution.GetDocument(syntaxNode.SyntaxTree)!.GetSemanticModelAsync();

            if (semanticModel != null)
            {
                IEnumerable<SyntaxNode> nodesToScan;

                if (symbol is INamedTypeSymbol && syntaxNode is TypeDeclarationSyntax typeSyntax)
                {
                    // 对于类型声明，只扫描特性和基类列表，避免扫描成员体（成员体会作为单独的符号被分析）
                    // 还要包含 TypeParameterList (虽然前面已经处理了泛型约束，但为了保险起见)
                    var list = new List<SyntaxNode>();
                    list.AddRange(typeSyntax.AttributeLists);
                    if (typeSyntax.BaseList != null) list.Add(typeSyntax.BaseList);
                    if (typeSyntax.TypeParameterList != null) list.Add(typeSyntax.TypeParameterList);
                    nodesToScan = list;
                }
                else
                {
                    // 对于方法、属性、字段等，扫描整个节点
                    nodesToScan = new[] { syntaxNode };
                }

                foreach (var node in nodesToScan)
                {
                    foreach (var descendant in node.DescendantNodes())
                    {
                        AnalyzeNode(symbol, descendant, semanticModel);
                    }
                }
            }
        }

        private async Task AnalyzeExpressionAsync(ISymbol fromSymbol, ExpressionSyntax expression, SyntaxTree syntaxTree)
        {
            var doc = _solution.GetDocument(syntaxTree);
            if (doc == null) return;
            var semanticModel = await doc.GetSemanticModelAsync();
            if (semanticModel == null) return;

            foreach (var descendant in expression.DescendantNodesAndSelf())
            {
                AnalyzeNode(fromSymbol, descendant, semanticModel);
            }
        }

        private void AnalyzeNode(ISymbol symbol, SyntaxNode descendant, SemanticModel semanticModel)
        {
            SymbolInfo symbolInfo = default;

            if (descendant is InvocationExpressionSyntax invocation)
            {
                symbolInfo = semanticModel.GetSymbolInfo(invocation);

                // 显式检查调用的表达式（例如方法名或委托）
                var methodGroupInfo = semanticModel.GetSymbolInfo(invocation.Expression);
                foreach (var s in GetSymbolsFromInfo(methodGroupInfo))
                {
                    if (!SymbolEqualityComparer.Default.Equals(s, symbol))
                    {
                        AddDependency(symbol, s);
                    }
                }
            }
            else if (descendant is ElementAccessExpressionSyntax elementAccess)
            {
                // 显式处理索引器访问的“左侧”对象
                var expressionInfo = semanticModel.GetSymbolInfo(elementAccess.Expression);
                foreach (var s in GetSymbolsFromInfo(expressionInfo))
                {
                    if (!SymbolEqualityComparer.Default.Equals(s, symbol))
                    {
                        AddDependency(symbol, s);
                    }
                }

                // 同时也获取索引器本身
                symbolInfo = semanticModel.GetSymbolInfo(elementAccess);
            }
            else if (descendant is SimpleNameSyntax simpleName)
            {
                symbolInfo = semanticModel.GetSymbolInfo(simpleName);
            }
            else if (descendant is MemberAccessExpressionSyntax memberAccess)
            {
                symbolInfo = semanticModel.GetSymbolInfo(memberAccess);
            }
            else if (descendant is ObjectCreationExpressionSyntax objectCreation)
            {
                symbolInfo = semanticModel.GetSymbolInfo(objectCreation);
            }
            else if (descendant is InitializerExpressionSyntax initializer)
            {
                // 处理集合初始化器中的 Add 方法引用
                foreach (var expression in initializer.Expressions)
                {
                    var collectionInfo = semanticModel.GetCollectionInitializerSymbolInfo(expression);
                    if (collectionInfo.Symbol != null)
                    {
                        AddDependency(symbol, collectionInfo.Symbol);
                    }
                }
            }
            else if (descendant is AssignmentExpressionSyntax assignment)
            {
                // 处理赋值左侧
                var leftInfo = semanticModel.GetSymbolInfo(assignment.Left);
                foreach (var s in GetSymbolsFromInfo(leftInfo))
                {
                    if (!SymbolEqualityComparer.Default.Equals(s, symbol))
                    {
                        AddDependency(symbol, s);
                    }
                }
                // 处理赋值右侧
                var rightInfo = semanticModel.GetSymbolInfo(assignment.Right);
                foreach (var s in GetSymbolsFromInfo(rightInfo))
                {
                    if (!SymbolEqualityComparer.Default.Equals(s, symbol))
                    {
                        AddDependency(symbol, s);
                    }
                }
            }
            else if (descendant is ArgumentSyntax argument)
            {
                symbolInfo = semanticModel.GetSymbolInfo(argument.Expression);
            }

            foreach (var s in GetSymbolsFromInfo(symbolInfo))
            {
                if (!SymbolEqualityComparer.Default.Equals(s, symbol))
                {
                    AddDependency(symbol, s);
                }
            }

            // 检查是否存在用户定义的隐式/显式转换
            if (descendant is ExpressionSyntax expr)
            {
                var conversion = semanticModel.GetConversion(expr);
                if (conversion.IsUserDefined && conversion.MethodSymbol != null)
                {
                    AddDependency(symbol, conversion.MethodSymbol);
                }
            }
        }

        private IEnumerable<ISymbol> GetSymbolsFromInfo(SymbolInfo info)
        {
            if (info.Symbol != null)
            {
                yield return info.Symbol;
            }
            else
            {
                foreach (var candidate in info.CandidateSymbols)
                {
                    yield return candidate;
                }
            }
        }

        private void AddDependency(ISymbol from, ISymbol to)
        {
            if (to == null) return;

            // 处理扩展方法的 Reduced 形式
            if (to is IMethodSymbol methodTo && methodTo.MethodKind == MethodKind.ReducedExtension)
            {
                to = methodTo.ReducedFrom!;
            }

            // 0. 处理数组类型
            if (to is IArrayTypeSymbol arrayType)
            {
                AddDependency(from, arrayType.ElementType);
                return;
            }

            // 1. 处理泛型相关的符号
            // 我们需要依赖原始定义（以获取源码）和类型参数（以满足类型的完整性）
            var target = to.OriginalDefinition;
            if (target == null) return;

            if (to is INamedTypeSymbol named && !named.IsDefinition)
            {
                foreach (var arg in named.TypeArguments)
                {
                    AddDependency(from, arg);
                }
            }
            else if (to is IMethodSymbol method && !method.IsDefinition)
            {
                foreach (var arg in method.TypeArguments)
                {
                    AddDependency(from, arg);
                }
            }

            // 1.5 处理 override 成员的隐式依赖
            // 如果 'from' 是一个 override 成员，它必须依赖于被它重写的基类成员
            // 否则基类成员可能被移除，导致编译错误 CS0115
            if (from is IMethodSymbol methodFrom && methodFrom.IsOverride && methodFrom.OverriddenMethod != null)
            {
                 // 递归添加对基类方法的依赖
                 // 注意：这里我们添加的是从 override 方法到基类方法的依赖
                 // 这意味着如果 override 方法被保留，基类方法也会被保留
                 var overridden = methodFrom.OverriddenMethod.OriginalDefinition;
                 _graph.AddDependency(from.OriginalDefinition, overridden);
                 EnqueueSymbol(overridden);
            }
            else if (from is IPropertySymbol propertyFrom && propertyFrom.IsOverride && propertyFrom.OverriddenProperty != null)
            {
                 var overridden = propertyFrom.OverriddenProperty.OriginalDefinition;
                 _graph.AddDependency(from.OriginalDefinition, overridden);
                 EnqueueSymbol(overridden);
            }
            else if (from is IEventSymbol eventFrom && eventFrom.IsOverride && eventFrom.OverriddenEvent != null)
            {
                 var overridden = eventFrom.OverriddenEvent.OriginalDefinition;
                 _graph.AddDependency(from.OriginalDefinition, overridden);
                 EnqueueSymbol(overridden);
            }

            // 1.6 处理接口实现的隐式依赖
            // 如果 'from' 是一个接口的实现成员，它必须依赖于该接口成员
            // 否则接口成员可能被移除，导致编译错误 CS0535
            if (from.ContainingType != null)
            {
                foreach (var iface in from.ContainingType.AllInterfaces)
                {
                    foreach (var member in iface.GetMembers(from.Name))
                    {
                        var impl = from.ContainingType.FindImplementationForInterfaceMember(member);
                        if (SymbolEqualityComparer.Default.Equals(impl, from))
                        {
                            var interfaceMember = member.OriginalDefinition;
                            _graph.AddDependency(from.OriginalDefinition, interfaceMember);
                            EnqueueSymbol(interfaceMember);
                        }
                    }
                }
            }

            // 2. 正常处理原始定义
            if (IsInterestingSymbol(target))
            {
                // 注意：from 也应该使用其原始定义来建立图，确保图的节点一致性
                var fromTarget = from.OriginalDefinition;
                _graph.AddDependency(fromTarget, target);
                EnqueueSymbol(target);
            }
        }

        private bool IsImplicitInterfaceImplementation(ISymbol symbol)
        {
            var containingType = symbol.ContainingType;
            if (containingType == null) return false;

            foreach (var iface in containingType.AllInterfaces)
            {
                foreach (var ifaceMember in iface.GetMembers(symbol.Name))
                {
                    var impl = containingType.FindImplementationForInterfaceMember(ifaceMember);
                    if (SymbolEqualityComparer.Default.Equals(impl, symbol))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 过滤掉不感兴趣的符号（如命名空间、内置基本类型等）。
        /// </summary>
        private bool IsInterestingSymbol(ISymbol symbol)
        {
            if (symbol == null || symbol is INamespaceSymbol) return false;

            // 仅关注当前解决方案中定义的符号
            return symbol.DeclaringSyntaxReferences.Any();
        }

        /// <summary>
        /// 执行后向切片 (Backward Slicing) 的简化版本：
        /// 查找所有直接或间接依赖于给定目标的符号。
        /// </summary>
        public IEnumerable<SymbolNode> GetBackwardSlice(ISymbol targetSymbol)
        {
            var targetNode = _graph.GetOrAddNode(targetSymbol);
            var result = new HashSet<SymbolNode>(_graph.NodeComparer);

            // 在图中寻找所有能到达 targetNode 的节点
            foreach (var node in _graph.AllNodes)
            {
                if (CanReach(node, targetNode, new HashSet<SymbolNode>(_graph.NodeComparer)))
                {
                    result.Add(node);
                }
            }
            return result;
        }

        private bool CanReach(SymbolNode current, SymbolNode target, HashSet<SymbolNode> visited)
        {
            if (current == target) return true;
            if (!visited.Add(current)) return false;

            foreach (var dep in current.Dependencies)
            {
                if (CanReach(dep, target, visited)) return true;
            }
            return false;
        }
    }
}
