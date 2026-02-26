using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Analysis
{
    /// <summary>
    /// 成员级切片重写器。
    /// 继承自 CSharpSyntaxRewriter，根据给定的“必要符号集”移除未使用的类成员。
    /// 实现了“重写思路.txt”中的死代码消除和精细化提取。
    /// </summary>
    public class MemberSlicingRewriter : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _semanticModel;
        private readonly Dictionary<ISymbol, CallGraphBuilder.GraphMethodAction> _actions;
        private readonly HashSet<ISymbol> _necessarySymbols;
        private readonly Dictionary<INamedTypeSymbol, Dictionary<ISymbol, HashSet<ISymbol>>> _typeInterfaceImplementationCache = new(SymbolEqualityComparer.Default);

        // 预定义高危冲突标识符集合，用于快速过滤
        // 默认包含 XNA 常见类型
        private readonly HashSet<string> _riskIdentifiers;
        private bool _hasModifiedAmbiguousSymbols = false;

        public bool HasModifiedAmbiguousSymbols => _hasModifiedAmbiguousSymbols;

        private static readonly HashSet<string> _defaultRiskIdentifiers = new HashSet<string>(StringComparer.Ordinal)
        {
            "Rectangle", "Point", "Color", "Vector2", "Matrix", "Quaternion", "Plane", "Vector3", "Vector4"
        };

        public MemberSlicingRewriter(SemanticModel semanticModel, IEnumerable<ISymbol> necessarySymbols, IEnumerable<string>? riskIdentifiers = null)
            : this(semanticModel, new Dictionary<ISymbol, CallGraphBuilder.GraphMethodAction>(SymbolEqualityComparer.Default), necessarySymbols, riskIdentifiers)
        {
        }

        public MemberSlicingRewriter(SemanticModel semanticModel, Dictionary<ISymbol, CallGraphBuilder.GraphMethodAction> actions, IEnumerable<ISymbol> necessarySymbols, IEnumerable<string>? riskIdentifiers = null)
        {
            _semanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
            _actions = actions ?? new Dictionary<ISymbol, CallGraphBuilder.GraphMethodAction>(SymbolEqualityComparer.Default);
            _necessarySymbols = new HashSet<ISymbol>(necessarySymbols, SymbolEqualityComparer.Default);

            _riskIdentifiers = riskIdentifiers != null
                ? new HashSet<string>(riskIdentifiers.Concat(_defaultRiskIdentifiers), StringComparer.Ordinal)
                : new HashSet<string>(_defaultRiskIdentifiers, StringComparer.Ordinal);
        }

        /// <summary>
        /// 访问方法声明。根据动作进行删除、私有化或清空体。
        /// </summary>
        public const string OriginalSymbolAnnotationKind = "OriginalSymbol";

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
             // 关键修正：如果节点不在原始语法树中，它是经过重写产生的临时节点。
            // 由于它已经脱离了关联的 _semanticModel，直接调用 GetSymbolInfo 会抛出 ArgumentException。
            if (node.SyntaxTree == null || node.SyntaxTree != _semanticModel.SyntaxTree)
            {
                return base.VisitIdentifierName(node);
            }
            // 优化 1: 快速名称过滤
            // 只有当标识符是已知可能冲突的类型名时，才进行昂贵的语义检查
            if (!_riskIdentifiers.Contains(node.Identifier.Text))
            {
                return base.VisitIdentifierName(node);
            }

            // 优化 2: 上下文过滤
            // 如果标识符已经是限定名的一部分（作为右侧），或者是别名定义，则无需处理
            if (node.Parent is QualifiedNameSyntax qns && qns.Right == node)
            {
                return base.VisitIdentifierName(node);
            }
            if (node.Parent is AliasQualifiedNameSyntax aqns && aqns.Name == node)
            {
                return base.VisitIdentifierName(node);
            }
            if (node.Parent is MemberAccessExpressionSyntax maes && maes.Name == node)
            {
                return base.VisitIdentifierName(node);
            }
            if (node.Parent is UsingDirectiveSyntax || node.Parent is NameEqualsSyntax)
            {
                return base.VisitIdentifierName(node);
            }

            // 为所有标识符添加语义标注，用于后处理修复
            // 特别针对 XNA 类型和潜在的歧义类型
            var symbolInfo = _semanticModel.GetSymbolInfo(node);
            var symbol = symbolInfo.Symbol;

            if (symbol is INamedTypeSymbol typeSymbol)
            {
                string ns = typeSymbol.ContainingNamespace?.ToDisplayString() ?? "";
                if (ns.StartsWith("Microsoft.Xna.Framework") ||
                    ns.StartsWith("System.Drawing") ||
                    _riskIdentifiers.Contains(typeSymbol.Name))
                {
                    _hasModifiedAmbiguousSymbols = true;
                    node = node.WithAdditionalAnnotations(new SyntaxAnnotation(OriginalSymbolAnnotationKind, typeSymbol.ToDisplayString()));
                }
            }

            return base.VisitIdentifierName(node);
        }

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var symbol = _semanticModel.GetDeclaredSymbol(node);
            if (symbol == null) return base.VisitMethodDeclaration(node);

            // 检查是否有预定义的动作
            if (_actions.TryGetValue(symbol, out var action))
            {
                // 关键修正：即使被标记为删除，如果是必要符号（如被字段初始化器引用），则不能删除
                if (action == CallGraphBuilder.GraphMethodAction.Delete && _necessarySymbols.Contains(symbol))
                {
                    // 如果是必要符号，我们倾向于保留它。
                    // 检查是否需要保留完整体或仅保留签名
                    // 对于被字段初始化器引用的方法，通常需要其逻辑来正确初始化
                    return base.VisitMethodDeclaration(node);
                }

                switch (action)
                {
                    case CallGraphBuilder.GraphMethodAction.Delete:
                        // 即使被标记为删除，如果它是必须实现的接口成员或抽象成员，也必须保留一个空实现
                        if (IsRequiredByInheritance(symbol))
                        {
                            Visit(node.ReturnType);
                            Visit(node.ParameterList);
                            return ClearMethodBody(node, symbol);
                        }
                        return null;

                    case CallGraphBuilder.GraphMethodAction.ClearBody:
                        Visit(node.ReturnType);
                        Visit(node.ParameterList);
                        return ClearMethodBody(node, symbol);

                    case CallGraphBuilder.GraphMethodAction.Privatize:
                        // 修改访问修饰符为 private
                        var privateModifiers = node.Modifiers
                            .Where(m => !IsAccessModifier(m.Kind()))
                            .Prepend(SyntaxFactory.Token(SyntaxKind.PrivateKeyword));
                        node = node.WithModifiers(SyntaxFactory.TokenList(privateModifiers));
                        break;

                    case CallGraphBuilder.GraphMethodAction.Decouple:
                        // 取消 override 关键字，保持方法体
                        var decoupledModifiers = node.Modifiers
                            .Where(m => !m.IsKind(SyntaxKind.OverrideKeyword) && !m.IsKind(SyntaxKind.AbstractKeyword) && !m.IsKind(SyntaxKind.VirtualKeyword));
                        node = node.WithModifiers(SyntaxFactory.TokenList(decoupledModifiers));
                        break;
                }
            }
            else if (!_necessarySymbols.Contains(symbol))
            {
                // 如果没有显式动作，且不在必要符号集中
                // 检查是否因继承/接口实现而必须保留
                if (IsRequiredByInheritance(symbol))
                {
                    Visit(node.ReturnType);
                    Visit(node.ParameterList);
                    return ClearMethodBody(node, symbol);
                }

                // 否则删除
                return null;
            }

            return base.VisitMethodDeclaration(node);
        }

        private MethodDeclarationSyntax ClearMethodBody(MethodDeclarationSyntax node, IMethodSymbol symbol)
        {
            // 如果方法本身带有 override 关键字，但在我们的基类中找不到对应的方法（可能是因为基类该方法被删除了），
            // 那么我们需要移除 override 关键字，否则会导致 CS0115 错误。
            if (symbol.IsOverride && symbol.OverriddenMethod == null)
            {
                var newModifiers = node.Modifiers.Where(m => !m.IsKind(SyntaxKind.OverrideKeyword));
                node = node.WithModifiers(SyntaxFactory.TokenList(newModifiers));
            }

            // 如果是 extern 方法（如 P/Invoke），保留原样，不清理
            if (node.Modifiers.Any(m => m.IsKind(SyntaxKind.ExternKeyword)))
            {
                return node;
            }

            // 清空函数体，仅保留 return 语句（如果是必要的话）或抛出异常
            // 针对 WindowsLaunch 等平台相关方法，我们希望它们在非 Windows 平台上不做任何事或返回默认值。
            if (node.Body != null || node.ExpressionBody != null)
            {
                var statements = new List<StatementSyntax>();

                // Handle out parameters
                foreach (var param in symbol.Parameters)
                {
                    if (param.RefKind == RefKind.Out)
                    {
                        statements.Add(SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                SyntaxFactory.IdentifierName(param.Name),
                                SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression)
                            )
                        ));
                    }
                }

                if (!symbol.ReturnsVoid)
                {
                    statements.Add(SyntaxFactory.ReturnStatement(
                        SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression)
                    ));
                }

                var newBody = SyntaxFactory.Block(statements);
                return node.WithBody(newBody).WithExpressionBody(null).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None));
            }
            return node;
        }

        private bool IsAccessModifier(SyntaxKind kind)
        {
            return kind == SyntaxKind.PublicKeyword ||
                   kind == SyntaxKind.InternalKeyword ||
                   kind == SyntaxKind.ProtectedKeyword ||
                   kind == SyntaxKind.PrivateKeyword;
        }

        /// <summary>
        /// 访问字段声明。仅保留在必要符号集中的变量。
        /// </summary>
        public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            // 一个字段声明可能包含多个变量（如 int a, b;）
            var variablesToKeep = node.Declaration.Variables.Where(v =>
            {
                var symbol = _semanticModel.GetDeclaredSymbol(v);
                return symbol != null && _necessarySymbols.Contains(symbol);
            }).ToList();

            if (variablesToKeep.Count == 0)
            {
                return null; // 整个声明都不需要
            }

            if (variablesToKeep.Count == node.Declaration.Variables.Count)
            {
                return base.VisitFieldDeclaration(node); // 全部保留
            }

            // 部分保留
            return node.WithDeclaration(node.Declaration.WithVariables(SyntaxFactory.SeparatedList(variablesToKeep)));
        }

        /// <summary>
        /// 访问属性声明。
        /// </summary>
        public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            var symbol = _semanticModel.GetDeclaredSymbol(node);
            if (symbol != null && !_necessarySymbols.Contains(symbol))
            {
                if (IsRequiredByInheritance(symbol))
                {
                    Visit(node.Type);
                    return StubProperty(node, symbol);
                }
                return null;
            }
            return base.VisitPropertyDeclaration(node);
        }

        /// <summary>
        /// 访问事件声明 (具有 add/remove 访问器)。
        /// </summary>
        public override SyntaxNode? VisitEventDeclaration(EventDeclarationSyntax node)
        {
            var symbol = _semanticModel.GetDeclaredSymbol(node);
            if (symbol != null && !_necessarySymbols.Contains(symbol))
            {
                if (IsRequiredByInheritance(symbol))
                {
                    Visit(node.Type);
                    return StubEvent(node, symbol);
                }
                return null;
            }
            return base.VisitEventDeclaration(node);
        }

        /// <summary>
        /// 访问事件字段声明 (简单的 event EventHandler MyEvent;)。
        /// </summary>
        public override SyntaxNode? VisitEventFieldDeclaration(EventFieldDeclarationSyntax node)
        {
            var variablesToKeep = new List<VariableDeclaratorSyntax>();
            var variablesToStub = new List<VariableDeclaratorSyntax>();

            foreach (var v in node.Declaration.Variables)
            {
                var symbol = _semanticModel.GetDeclaredSymbol(v);
                if (symbol != null)
                {
                    if (_necessarySymbols.Contains(symbol))
                    {
                        variablesToKeep.Add(v);
                    }
                    else if (IsRequiredByInheritance(symbol))
                    {
                        variablesToStub.Add(v);
                    }
                }
            }

            if (variablesToKeep.Count == 0 && variablesToStub.Count == 0)
            {
                return null;
            }

            // 如果有需要保留的变量，我们保留整个字段声明（包含所有变量，或者仅保留必要的）
            // 但如果有些变量需要 Stub，而有些需要保留，这在 event field 中很难处理，因为一个声明只能有一种形式。
            // 简化处理：如果有一个变量需要保留，就保留整个声明。
            // 如果所有变量都不需要保留但有些需要 Stub，则将它们转换为完整的 EventDeclaration。

            if (variablesToKeep.Count > 0)
            {
                if (variablesToKeep.Count == node.Declaration.Variables.Count)
                {
                    return base.VisitEventFieldDeclaration(node);
                }
                return node.WithDeclaration(node.Declaration.WithVariables(SyntaxFactory.SeparatedList(variablesToKeep)));
            }

            // 只有需要 Stub 的变量。由于 EventFieldDeclaration 只能是简单的声明，
            // 我们需要将其转换为 EventDeclaration 才能添加 add/remove 块。
            // 为简单起见，我们只处理第一个需要 Stub 的变量（通常一个 event 声明只有一个变量）
            var firstStub = variablesToStub.First();
            var eventSymbol = _semanticModel.GetDeclaredSymbol(firstStub) as IEventSymbol;
            bool isInterface = eventSymbol?.ContainingType?.TypeKind == TypeKind.Interface;

            var accessors = new List<AccessorDeclarationSyntax>();
            if (isInterface)
            {
                accessors.Add(SyntaxFactory.AccessorDeclaration(SyntaxKind.AddAccessorDeclaration).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));
                accessors.Add(SyntaxFactory.AccessorDeclaration(SyntaxKind.RemoveAccessorDeclaration).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));
            }
            else
            {
                accessors.Add(SyntaxFactory.AccessorDeclaration(SyntaxKind.AddAccessorDeclaration).WithBody(SyntaxFactory.Block()));
                accessors.Add(SyntaxFactory.AccessorDeclaration(SyntaxKind.RemoveAccessorDeclaration).WithBody(SyntaxFactory.Block()));
            }

            var eventModifiers = node.Modifiers;
            // 处理 override 丢失情况
            if (eventSymbol != null && eventSymbol.IsOverride && eventSymbol.OverriddenEvent == null)
            {
                eventModifiers = SyntaxFactory.TokenList(eventModifiers.Where(m => !m.IsKind(SyntaxKind.OverrideKeyword)));
            }

            var eventDecl = SyntaxFactory.EventDeclaration(
                node.AttributeLists,
                eventModifiers,
                node.EventKeyword,
                node.Declaration.Type,
                null!,
                firstStub.Identifier,
                SyntaxFactory.AccessorList(SyntaxFactory.List(accessors))
            );

            return eventDecl;
        }

        private bool IsBaseClass(INamedTypeSymbol symbol)
        {
            // 如果该类被任何其他类继承，则它是一个基类。
            // 简单的方法是检查是否有任何已知的必要符号属于它的子类。
            // 但更彻底的方法是查看整个编译。
            // 不过，由于我们要“不动基类”，我们可以保守一点：
            // 如果该类不是 sealed，且有任何 virtual/abstract 成员，或者被标记为 necessary 且不是 sealed，
            // 我们可以假设它可能是基类。

            // 实际上，语义模型可以告诉我们一个类是否在当前编译中被继承。
            // 但为了“不动基类”的最直接理解，如果这个类被其他类继承，我们就不切片它。

            // 查找当前编译中是否有类继承自此 symbol
            // 注意：这可能比较耗时，但为了“不动基类”的准确性是必要的。
            // 优化：检查 symbol.IsSealed。如果 sealed，肯定不是基类。
            if (symbol.IsSealed) return false;

            // 在 Terraria 这种大型项目中，很多类都是基类。
            // 我们可以通过检查是否有任何 symbol 的 BaseType 指向此 symbol 来判断。
            // 但由于性能考虑，我们也可以看它是否有 virtual/abstract 成员。

            // 更简单的判断：如果这个类在 necessarySymbols 中，并且有子类在 necessarySymbols 中。
            // 或者，我们直接放宽政策：如果一个类不是 sealed 的，我们就认为它是潜在的基类，减少对它的改动。
            return !symbol.IsSealed;
        }

        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var symbol = _semanticModel.GetDeclaredSymbol(node);
            if (symbol != null && IsBaseClass(symbol))
            {
                // 如果是基类，我们完全跳过成员切片逻辑，直接返回原始节点（但要处理内部的标识符标注等）
                // 这样基类的属性、字段、方法都会被原样保留。
                return base.VisitClassDeclaration(node);
            }

            var newNode = (ClassDeclarationSyntax?)base.VisitClassDeclaration(node);

            if (newNode == null) return null;

            // 如果类没有任何成员（排除了自动生成的构造函数等隐含成员后的语法成员）
            // 且类本身也不在必要符号集中（没有作为类型被引用）
            if (!newNode.Members.Any() && symbol != null && !_necessarySymbols.Contains(symbol))
            {
                return null;
            }

            return newNode;
        }

        public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node)
        {
            var symbol = _semanticModel.GetDeclaredSymbol(node);
            var newNode = (StructDeclarationSyntax?)base.VisitStructDeclaration(node);

            if (newNode == null) return null;

            if (newNode.Members.Count == 0 && symbol != null && !_necessarySymbols.Contains(symbol))
            {
                return null;
            }

            return newNode;
        }

        public override SyntaxNode? VisitEnumDeclaration(EnumDeclarationSyntax node)
        {
            var symbol = _semanticModel.GetDeclaredSymbol(node);
            if (symbol != null && !_necessarySymbols.Contains(symbol))
            {
                return null;
            }
            return base.VisitEnumDeclaration(node);
        }

        public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            return base.VisitExpressionStatement(node);
        }

        public override SyntaxNode? VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        {
            return base.VisitLocalDeclarationStatement(node);
        }

        private bool ContainsPrunedReference(SyntaxNode node)
        {
            // 关键修正：CSharpSyntaxRewriter 在重写过程中会创建新的节点。
            // 如果一个节点是新创建的（没有 SyntaxTree），它就无法使用原始的 _semanticModel 进行查询。
            if (node.SyntaxTree == null || node.SyntaxTree != _semanticModel.SyntaxTree)
            {
                // 如果节点不在原始树中，我们无法进行语义判断。
                // 此时有两种策略：
                // 1. 假设它不包含被剪裁的引用（因为它是新生成的代码，通常不应该引用被删除的代码）
                // 2. 检查它是否带有我们标记的特定 Annotation（如果有的话）
                // 这里我们采用保守策略：如果节点不在树中，说明它是经过 VisitIdentifierName 或其他方法修改过的，
                // 我们信任之前的修改逻辑，不将其视为死代码。
                return false;
            }

            foreach (var descendant in node.DescendantNodes())
            {
                // 二次确认：即使父节点在树中，子节点也可能因为局部重写而不在树中
                if (descendant.SyntaxTree == null || descendant.SyntaxTree != _semanticModel.SyntaxTree)
                    continue;

                var symbolInfo = _semanticModel.GetSymbolInfo(descendant);
                var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

                if (symbol != null && IsSymbolPruned(symbol))
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsSymbolPruned(ISymbol symbol)
        {
            // 只有源代码中的符号才会被剪裁
            if (symbol.Locations.Any(l => l.IsInSource))
            {
                var original = symbol.OriginalDefinition;

                // 我们只剪裁我们追踪的顶层成员。
                // 忽略局部变量、参数、类型参数等。
                if (!(original is IMethodSymbol ||
                      original is IFieldSymbol ||
                      original is IPropertySymbol ||
                      original is IEventSymbol ||
                      original is INamedTypeSymbol))
                {
                    return false;
                }

                // 如果是方法，检查它是否被标记为删除或不在必要集合中
                if (original is IMethodSymbol method)
                {
                    if (_actions.TryGetValue(method, out var action) && action == CallGraphBuilder.GraphMethodAction.Delete)
                    {
                        return !_necessarySymbols.Contains(method);
                    }
                }

                return !_necessarySymbols.Contains(original);
            }
            return false;
        }

        public override SyntaxNode? VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            var symbol = _semanticModel.GetDeclaredSymbol(node);
            var newNode = (InterfaceDeclarationSyntax?)base.VisitInterfaceDeclaration(node);

            if (newNode == null) return null;

            if (newNode.Members.Count == 0 && symbol != null && !_necessarySymbols.Contains(symbol))
            {
                return null;
            }

            return newNode;
        }

        private Dictionary<string, UsingDirectiveSyntax> _requiredAliases = new Dictionary<string, UsingDirectiveSyntax>();

        public override SyntaxNode? VisitCompilationUnit(CompilationUnitSyntax node)
        {
             // 1. 先使用 Walker 扫描一遍，收集所有需要的别名
            // 此时 node 还在语法树中，语义模型有效
            var walker = new UsageWalker(_semanticModel, _necessarySymbols, _riskIdentifiers);
            walker.Visit(node);

            foreach (var alias in walker.RequiredAliases)
            {
                if (!_requiredAliases.ContainsKey(alias.Key))
                {
                    _requiredAliases[alias.Key] = alias.Value;
                }
            }

            // 2. 递归处理子节点
            // 必须在原始 node 上调用 base.VisitCompilationUnit，
            // 这样子节点（如 VisitIdentifierName）在执行时依然处于原始语法树中。
            var result = (CompilationUnitSyntax?)base.VisitCompilationUnit(node);
            if (result == null) return null;

            // 3. 过滤平台相关代码 (Option 12 修复)
            // 如果生成的 CompilationUnit 包含特定的 using 指令，但我们不想引入这些依赖（如 System.Windows.Forms），
            // 我们可以在这里进行清理。
            // 对于 WindowsLaunch 和 CrashDump 等文件，它们可能包含特定平台的 P/Invoke 或引用。
            // 简单的方法是移除所有不兼容的 Using 和 Attribute。

            // 移除 System.Windows.Forms 引用，防止在非 Windows 平台编译失败
            var usings = result.Usings.Where(u => {
                var name = u.Name.ToString();
                return !name.StartsWith("System.Windows.Forms") &&
                       !name.StartsWith("Microsoft.Xna.Framework.Input.Touch"); // 示例：移除其他可能不需要的引用
            });
            result = result.WithUsings(SyntaxFactory.List(usings));

            // 3. 最后注入收集到的所有别名
            if (_requiredAliases.Any())
            {
                // 确保不注入已移除的命名空间别名
                var validAliases = _requiredAliases.Values.Where(a => !a.Name.ToString().StartsWith("System.Windows.Forms"));
                var newUsings = result.Usings.AddRange(validAliases);
                result = result.WithUsings(newUsings);
            }

            return result;
        }

        private class UsageWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel _semanticModel;
            private readonly HashSet<ISymbol> _necessarySymbols;
            private readonly HashSet<string> _riskIdentifiers;
            private readonly Dictionary<string, UsingDirectiveSyntax> _requiredAliases = new Dictionary<string, UsingDirectiveSyntax>();

            public Dictionary<string, UsingDirectiveSyntax> RequiredAliases => _requiredAliases;

            public UsageWalker(SemanticModel semanticModel, HashSet<ISymbol> necessarySymbols, HashSet<string> riskIdentifiers)
            {
                _semanticModel = semanticModel;
                _necessarySymbols = necessarySymbols;
                _riskIdentifiers = riskIdentifiers;
            }

            public override void VisitIdentifierName(IdentifierNameSyntax node)
            {
                // 跳过限定名称的一部分 (e.g. A.B.C 中的 B 和 C)
                if (node.Parent is MemberAccessExpressionSyntax ma && ma.Name == node)
                {
                    base.VisitIdentifierName(node);
                    return;
                }
                if (node.Parent is QualifiedNameSyntax qn && qn.Right == node)
                {
                    base.VisitIdentifierName(node);
                    return;
                }

                // 优化：仅对有风险的标识符进行语义检查
                if (!_riskIdentifiers.Contains(node.Identifier.Text))
                {
                    base.VisitIdentifierName(node);
                    return;
                }

                var symbolInfo = _semanticModel.GetSymbolInfo(node);
                var symbols = (symbolInfo.Symbol != null
                    ? new[] { symbolInfo.Symbol }.AsEnumerable()
                    : symbolInfo.CandidateSymbols).ToList();

                // 优先选择 XNA 命名空间下的符号
                var preferredSymbol = symbols.FirstOrDefault(s => {
                    var type = (s as INamedTypeSymbol) ?? (s as IMethodSymbol)?.ContainingType;
                    var ns = type?.ContainingNamespace?.ToDisplayString() ?? "";
                    return ns.StartsWith("Microsoft.Xna.Framework") || ns.Contains("Xna");
                }) ?? symbols.FirstOrDefault();

                if (preferredSymbol != null)
                {
                    INamedTypeSymbol? typeSymbol = preferredSymbol as INamedTypeSymbol;
                    if (typeSymbol == null && preferredSymbol is IMethodSymbol method)
                    {
                        typeSymbol = method.ContainingType;
                    }

                    if (typeSymbol != null)
                    {
                        string ns = typeSymbol.ContainingNamespace?.ToDisplayString() ?? "";
                        if (ns.StartsWith("Microsoft.Xna.Framework") || ns.Contains("Xna") || _riskIdentifiers.Contains(typeSymbol.Name))
                        {
                            string typeName = typeSymbol.Name;
                            if (!_requiredAliases.ContainsKey(typeName))
                            {
                                // Option 12 修复：过滤掉 System.Windows.Forms 等不兼容的类型别名
                                var nsStr = typeSymbol.ContainingNamespace?.ToDisplayString() ?? "";
                                if (!nsStr.StartsWith("System.Windows.Forms"))
                                {
                                    var alias = SyntaxFactory.UsingDirective(
                                        SyntaxFactory.NameEquals(SyntaxFactory.IdentifierName(typeName)),
                                        SyntaxFactory.ParseName(typeSymbol.ToDisplayString())
                                    );
                                    _requiredAliases[typeName] = alias;
                                }
                            }
                        }
                    }
                }

                base.VisitIdentifierName(node);
            }
        }

        private EventDeclarationSyntax StubEvent(EventDeclarationSyntax node, IEventSymbol symbol)
        {
            // 如果事件本身带有 override 关键字，但在我们的基类中找不到对应的事件（可能是因为基类该事件被删除了），
            // 那么我们需要移除 override 关键字，否则会导致 CS0115 错误。
            if (symbol.IsOverride && symbol.OverriddenEvent == null)
            {
                var newModifiers = node.Modifiers.Where(m => !m.IsKind(SyntaxKind.OverrideKeyword));
                node = node.WithModifiers(SyntaxFactory.TokenList(newModifiers));
            }

            bool isInterface = symbol.ContainingType?.TypeKind == TypeKind.Interface;
            var accessors = new List<AccessorDeclarationSyntax>();

            if (isInterface)
            {
                accessors.Add(SyntaxFactory.AccessorDeclaration(SyntaxKind.AddAccessorDeclaration).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));
                accessors.Add(SyntaxFactory.AccessorDeclaration(SyntaxKind.RemoveAccessorDeclaration).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));
            }
            else
            {
                accessors.Add(SyntaxFactory.AccessorDeclaration(SyntaxKind.AddAccessorDeclaration).WithBody(SyntaxFactory.Block()));
                accessors.Add(SyntaxFactory.AccessorDeclaration(SyntaxKind.RemoveAccessorDeclaration).WithBody(SyntaxFactory.Block()));
            }

            return node.WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.List(accessors)));
        }

        private bool IsRequiredByInheritance(ISymbol symbol)
        {
            if (symbol == null || symbol.ContainingType == null) return false;

            // 1. 显式重写：如果是 override，则必须保留以维持类结构
            if (symbol.IsOverride) return true;

            // 2. 基类契约：如果是 virtual 或 abstract，则它是供子类重写的“基类属性/方法”，必须保留
            // 这解决了“不移基类的属性”的要求，确保子类的 override 始终能找到对应的基类成员。
            if (symbol.IsVirtual || symbol.IsAbstract) return true;

            // 3. 访问级别保护：如果是 protected 且类非密封，保留以供潜在子类使用
            if (symbol.DeclaredAccessibility == Accessibility.Protected ||
                symbol.DeclaredAccessibility == Accessibility.ProtectedOrInternal)
            {
                if (!symbol.ContainingType.IsSealed) return true;
            }

            var type = symbol.ContainingType;
            var map = GetInterfaceImplementationMap(type);

            // 4. 编译安全性优先：智能接口实现识别 (基于缓存)
            if (map.ContainsKey(symbol)) return true;

            return false;
        }

        private Dictionary<ISymbol, HashSet<ISymbol>> GetInterfaceImplementationMap(INamedTypeSymbol type)
        {
            if (_typeInterfaceImplementationCache.TryGetValue(type, out var map))
            {
                return map;
            }

            map = new Dictionary<ISymbol, HashSet<ISymbol>>(SymbolEqualityComparer.Default);
            foreach (var iface in type.AllInterfaces)
            {
                foreach (var interfaceMember in iface.GetMembers())
                {
                    // 使用语义模型的精确匹配：查找该接口成员在当前类中的实现
                    var implementation = type.FindImplementationForInterfaceMember(interfaceMember);
                    if (implementation != null)
                    {
                        if (!map.TryGetValue(implementation, out var ifaces))
                        {
                            ifaces = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
                            map[implementation] = ifaces;
                        }
                        ifaces.Add(iface);
                    }
                }
            }

            _typeInterfaceImplementationCache[type] = map;
            return map;
        }

        private PropertyDeclarationSyntax StubProperty(PropertyDeclarationSyntax node, IPropertySymbol symbol)
        {
            // 如果属性本身带有 override 关键字，但在我们的基类中找不到对应的属性（可能是因为基类该属性被删除了），
            // 那么我们需要移除 override 关键字，否则会导致 CS0115 错误。
            if (symbol.IsOverride && symbol.OverriddenProperty == null)
            {
                var newModifiers = node.Modifiers.Where(m => !m.IsKind(SyntaxKind.OverrideKeyword));
                node = node.WithModifiers(SyntaxFactory.TokenList(newModifiers));
            }

            // 如果是抽象属性，直接返回原节点（通常只有声明，没有实现），不添加任何 body
            if (symbol.IsAbstract)
            {
                return node;
            }

            // If it's an auto-property { get; set; }, we can keep it as is or clear initializer.
            // But if it has logic, we should replace it.
            // Simplest way for property stub:
            // Convert to { get { return default; } set {} } if applicable.

            var accessors = new List<AccessorDeclarationSyntax>();

            if (symbol.GetMethod != null)
            {
                var body = SyntaxFactory.Block(
                    SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression))
                );
                accessors.Add(SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithBody(body));
            }
            if (symbol.SetMethod != null)
            {
                accessors.Add(SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithBody(SyntaxFactory.Block()));
            }

            return node.WithExpressionBody(null)
                       .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None))
                       .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.List(accessors)))
                       .WithInitializer(null);
        }


    }
}
