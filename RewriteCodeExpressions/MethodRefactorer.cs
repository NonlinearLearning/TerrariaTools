using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Threading;
using System.Collections.Concurrent;

namespace TerrariaTools.RewriteCodeExpressions
{
    /// <summary>
    /// 提供方法重构功能，包括删除未引用方法和将仅内部引用的公开方法改为 private 方法，并对方法进行重新排序。
    /// </summary>
    public class MethodRefactorer
    {
        private readonly Solution _solution;
        private readonly ConcurrentDictionary<string, byte> _processedFiles = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<DocumentId, SemanticModel> _semanticModelCache = new ConcurrentDictionary<DocumentId, SemanticModel>();

        public MethodRefactorer(Solution solution)
        {
            _solution = solution;
        }

        /// <summary>
        /// 执行解决方案级别的方法重构。
        /// </summary>
        /// <param name="solutionPath">解决方案路径</param>
        /// <param name="loader">用于加载解决方案的加载器</param>
        public static async Task ExecuteSolutionRefactoringAsync(string solutionPath, Load loader)
        {
            bool anyFileChangedInPass;
            int passCount = 0;

            do
            {
                passCount++;
                anyFileChangedInPass = false;
                Console.WriteLine($"\n[信息] 正在启动第 {passCount} 轮方法重构迭代...");

                // 每轮迭代都重新加载解决方案，以确保获取最新的语义模型
                using var workspace = await loader.LoadSolutionAsync(solutionPath);
                if (workspace == null) break;

                var solution = workspace.CurrentSolution;
                var refactorer = new MethodRefactorer(solution);

                var allDocuments = solution.Projects
                    .SelectMany(p => p.Documents)
                    .Where(d => d.FilePath != null && d.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                Console.WriteLine($"[信息] 发现 {allDocuments.Count} 个待处理的 C# 文件。");

                int totalDeleted = 0;
                int totalPrivatized = 0;
                int totalBodyCleared = 0;
                int processedCount = 0;

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                };

                var startTime = DateTime.Now;
                var results = new ConcurrentBag<RefactorResult>();

                await Parallel.ForEachAsync(allDocuments, parallelOptions, async (doc, ct) =>
                {
                    try
                    {
                        var result = await refactorer.ProcessFileAsync(doc.FilePath!);
                        results.Add(result);

                        if (result.AnyChanged)
                        {
                            anyFileChangedInPass = true;
                            Interlocked.Add(ref totalDeleted, result.DeletedCount);
                            Interlocked.Add(ref totalPrivatized, result.PrivatizedCount);
                            Interlocked.Add(ref totalBodyCleared, result.BodyClearedCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[错误] 处理文件 {doc.FilePath} 时出错: {ex.Message}");
                    }

                    var currentCount = Interlocked.Increment(ref processedCount);
                    if (currentCount % 100 == 0 || currentCount == allDocuments.Count)
                    {
                        var elapsed = DateTime.Now - startTime;
                        var speed = currentCount / elapsed.TotalSeconds;
                        Console.WriteLine($"[{currentCount}/{allDocuments.Count}] 正在分析方法... 速度: {speed:F1} 文件/秒, 待处理变更: {totalDeleted + totalPrivatized + totalBodyCleared}");
                    }
                });

                if (anyFileChangedInPass)
                {
                    Console.WriteLine($"[信息] 正在将本轮方法变更保存到磁盘...");
                    var currentSolution = solution;
                    int savedCount = 0;
                    foreach (var res in results.Where(r => r.AnyChanged && r.DocumentId != null && r.NewRoot != null))
                    {
                        try
                        {
                            File.WriteAllText(res.FilePath!, res.NewRoot!.ToFullString(), System.Text.Encoding.UTF8);
                            savedCount++;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[错误] 写入文件 {res.FilePath} 失败: {ex.Message}");
                        }
                    }
                    Console.WriteLine($"[成功] 本轮迭代结束。已保存 {savedCount} 个文件的变更，共删除 {totalDeleted}，私有化 {totalPrivatized}，清空体 {totalBodyCleared}。");
                }
                else
                {
                    Console.WriteLine("[信息] 本轮未发现可重构的内容，迭代停止。");
                }
            } while (anyFileChangedInPass && passCount < 10);

            Console.WriteLine($"\n[完成] 方法重构全流程结束。总共执行了 {passCount} 轮迭代。");
        }

        private async Task<SemanticModel?> GetSemanticModelAsync(Document doc)
        {
            if (_semanticModelCache.TryGetValue(doc.Id, out var model)) return model;
            var newModel = await doc.GetSemanticModelAsync();
            if (newModel != null) _semanticModelCache.TryAdd(doc.Id, newModel);
            return newModel;
        }

        public class RefactorResult
        {
            public bool AnyChanged => DeletedCount > 0 || PrivatizedCount > 0 || BodyClearedCount > 0;
            public int DeletedCount;
            public int PrivatizedCount;
            public int BodyClearedCount;
            public SyntaxNode? NewRoot { get; set; }
            public DocumentId? DocumentId { get; set; }
            public string? FilePath { get; set; }
        }

        public async Task<RefactorResult> ProcessFileAsync(string filePath)
        {
            var result = new RefactorResult { FilePath = filePath };
            if (_processedFiles.ContainsKey(filePath)) return result;
            _processedFiles.TryAdd(filePath, 0);

            var document = _solution.GetDocumentIdsWithFilePath(filePath)
                .Select(id => _solution.GetDocument(id))
                .FirstOrDefault();

            if (document == null)
            {
                document = _solution.Projects.SelectMany(p => p.Documents)
                    .FirstOrDefault(d => string.Equals(d.Name, filePath, StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            }

            if (document == null) return result;
            result.DocumentId = document.Id;

            var root = await document.GetSyntaxRootAsync();
            var semanticModel = await document.GetSemanticModelAsync();
            if (root == null || semanticModel == null) return result;

            // 1. 跳过接口声明
            if (root.DescendantNodes().OfType<InterfaceDeclarationSyntax>().Any())
            {
                return result;
            }

            var classDecls = root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToList();
            var allMethodActions = new List<(MethodDeclarationSyntax Method, MethodAction Action, ClassDeclarationSyntax Class)>();

            foreach (var classDecl in classDecls)
            {
                var classSymbol = semanticModel.GetDeclaredSymbol(classDecl);
                if (classSymbol == null) continue;

                var methods = classDecl.Members.OfType<MethodDeclarationSyntax>().ToList();
                var methodTasks = methods.Select(m => AnalyzeMethodAsync(m, classSymbol, semanticModel, document)).ToList();
                var analyzedResults = await Task.WhenAll(methodTasks);

                var toDelete = new List<MethodDeclarationSyntax>();
                var toMakePrivate = new List<MethodDeclarationSyntax>();
                var toClearBody = new List<MethodDeclarationSyntax>();
                var withExternalRefs = new List<MethodDeclarationSyntax>();
                var others = new List<MethodDeclarationSyntax>();

                foreach (var res in analyzedResults)
                {
                    if (res.Action == MethodAction.Delete)
                    {
                        toDelete.Add(res.Method);
                        Interlocked.Increment(ref result.DeletedCount);
                    }
                    else if (res.Action == MethodAction.MakePrivate)
                    {
                        toMakePrivate.Add(res.Method);
                        Interlocked.Increment(ref result.PrivatizedCount);
                    }
                    else if (res.Action == MethodAction.ClearBody)
                    {
                        toClearBody.Add(res.Method);
                        Interlocked.Increment(ref result.BodyClearedCount);
                    }
                    else if (res.Action == MethodAction.KeepExternal)
                    {
                        withExternalRefs.Add(res.Method);
                    }
                    else
                    {
                        others.Add(res.Method);
                    }
                }

                if (toDelete.Any() || toMakePrivate.Any() || toClearBody.Any() || withExternalRefs.Any() || others.Any())
                {
                    // 在当前的 root 中定位最新的类节点
                    var latestClass = root.DescendantNodesAndSelf()
                        .OfType<ClassDeclarationSyntax>()
                        .FirstOrDefault(c => c.IsEquivalentTo(classDecl) ||
                                           (c.Identifier.Text == classDecl.Identifier.Text &&
                                            c.SpanStart == classDecl.SpanStart));

                    if (latestClass != null)
                    {
                        // 更新 root
                        var updatedRoot = await GetUpdatedRootAsync(root, latestClass,
                            toDelete, toMakePrivate, toClearBody,
                            withExternalRefs, others, semanticModel);

                        // 验证返回的 root 是否包含更新后的类
                        root = updatedRoot;
                    }
                }
            }

            var finalRoot = root.NormalizeWhitespace();
            var originalRoot = await document.GetSyntaxRootAsync();

            // 如果新旧根节点在逻辑上等价，则认为没有发生实际变更，避免陷入迭代死循环
            if (originalRoot != null && originalRoot.IsEquivalentTo(finalRoot, topLevel: false))
            {
                result.DeletedCount = 0;
                result.PrivatizedCount = 0;
                result.BodyClearedCount = 0;
                result.NewRoot = null;
                return result;
            }

            result.NewRoot = finalRoot;
            return result;
        }

        private enum MethodAction { Delete, MakePrivate, ClearBody, KeepExternal, KeepOther }
        private class MethodAnalysisResult
        {
            public MethodDeclarationSyntax Method { get; set; } = null!;
            public MethodAction Action { get; set; }
        }

        private async Task<bool> IsLocationInsideClassAsync(Document doc, TextSpan span, INamedTypeSymbol classSymbol)
        {
            var model = await GetSemanticModelAsync(doc);
            if (model == null) return false;

            var root = await doc.GetSyntaxRootAsync();
            if (root == null) return false;

            var node = root.FindNode(span);
            var enclosingSymbol = model.GetEnclosingSymbol(node.SpanStart);

            while (enclosingSymbol != null)
            {
                if (SymbolEqualityComparer.Default.Equals(enclosingSymbol, classSymbol))
                    return true;
                enclosingSymbol = enclosingSymbol.ContainingSymbol;
            }

            return false;
        }

        private async Task<MethodAnalysisResult> AnalyzeMethodAsync(MethodDeclarationSyntax methodDecl, INamedTypeSymbol classSymbol, SemanticModel semanticModel, Document document)
        {
            var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl);
            if (methodSymbol == null) return new MethodAnalysisResult { Method = methodDecl, Action = MethodAction.KeepOther };

            // 抽象方法不处理其删除逻辑（保留定义）
            if (methodSymbol.IsAbstract)
            {
                return new MethodAnalysisResult { Method = methodDecl, Action = MethodAction.KeepOther };
            }

            // 检查是否是程序的入口点 (Main 方法)
            if (methodSymbol.Name == "Main" && methodSymbol.IsStatic)
            {
                return new MethodAnalysisResult { Method = methodDecl, Action = MethodAction.KeepOther };
            }

            // 也可以通过 Compilation.GetEntryPoint 进行更精确的检查
            if (SymbolEqualityComparer.Default.Equals(methodSymbol, semanticModel.Compilation.GetEntryPoint(default)))
            {
                return new MethodAnalysisResult { Method = methodDecl, Action = MethodAction.KeepOther };
            }

            // 检查是否是接口实现
            bool isInterfaceImpl = methodSymbol.ExplicitInterfaceImplementations.Any() ||
                                 methodSymbol.ContainingType.AllInterfaces
                                    .SelectMany(i => i.GetMembers().OfType<IMethodSymbol>())
                                    .Any(m => SymbolEqualityComparer.Default.Equals(methodSymbol.ContainingType.FindImplementationForInterfaceMember(m), methodSymbol));

            // 使用 SymbolFinder 在全解决方案范围内查找引用，不进行作用域优化
            bool hasExternalReferences = false;
            bool hasInternalReferences = false;
            var references = await SymbolFinder.FindReferencesAsync(methodSymbol, _solution);

            foreach (var reference in references)
            {
                foreach (var location in reference.Locations)
                {
                    var doc = location.Document;
                    var docModel = await GetSemanticModelAsync(doc);
                    if (docModel == null) continue;

                    var root = await doc.GetSyntaxRootAsync();
                    var node = root?.FindNode(location.Location.SourceSpan);
                    if (node == null) continue;

                    // 1. 比较是否是当前方法本身的定义位置
                    if (doc.Id == document.Id)
                    {
                        var declaredSymbol = docModel.GetDeclaredSymbol(node);
                        if (SymbolEqualityComparer.Default.Equals(declaredSymbol, methodSymbol)) continue;
                    }

                    // 2. 检查引用是否在当前类内部（处理 partial 类跨文件的情况）
                    var enclosingSymbol = docModel.GetEnclosingSymbol(location.Location.SourceSpan.Start);
                    if (IsSymbolInsideClass(enclosingSymbol, classSymbol))
                    {
                        hasInternalReferences = true;
                    }
                    else
                    {
                        // 3. 引用在类外部，属于外部引用
                        hasExternalReferences = true;
                        break;
                    }
                }
                if (hasExternalReferences) break;
            }

            // 处理逻辑
            if (!hasExternalReferences && !hasInternalReferences)
            {
                return await GetNoReferenceActionAsync(methodDecl, methodSymbol);
            }
            else if (hasExternalReferences)
            {
                return new MethodAnalysisResult { Method = methodDecl, Action = MethodAction.KeepExternal };
            }
            // 如果只有内部引用，且方法是 public，且不是接口实现、虚方法、重写方法或抽象方法，则可以私有化
            else if (methodSymbol.DeclaredAccessibility == Accessibility.Public &&
                     !isInterfaceImpl &&
                     !methodSymbol.IsVirtual &&
                     !methodSymbol.IsOverride &&
                     !methodSymbol.IsAbstract)
            {
                return new MethodAnalysisResult { Method = methodDecl, Action = MethodAction.MakePrivate };
            }

            return new MethodAnalysisResult { Method = methodDecl, Action = MethodAction.KeepOther };
        }

        private async Task<MethodAnalysisResult> GetNoReferenceActionAsync(MethodDeclarationSyntax methodDecl, IMethodSymbol methodSymbol)
        {
            bool isInterfaceImpl = IsInterfaceImplementation(methodSymbol);
            if (methodSymbol.IsOverride || methodSymbol.IsVirtual || isInterfaceImpl)
            {
                if ((methodDecl.Body != null || methodDecl.ExpressionBody != null) && !IsBodyAlreadyCleared(methodDecl, methodSymbol))
                {
                    return new MethodAnalysisResult { Method = methodDecl, Action = MethodAction.ClearBody };
                }
                return new MethodAnalysisResult { Method = methodDecl, Action = MethodAction.KeepOther };
            }
            return new MethodAnalysisResult { Method = methodDecl, Action = MethodAction.Delete };
        }

        private bool IsBodyAlreadyCleared(MethodDeclarationSyntax methodDecl, IMethodSymbol methodSymbol)
        {
            // 表达式主体肯定没清空（因为清空后会变成 BlockBody）
            if (methodDecl.ExpressionBody != null) return false;
            // 没有 Body 说明是抽象或分部方法
            if (methodDecl.Body == null) return true;

            var statements = methodDecl.Body.Statements;
            var outParameters = methodSymbol.Parameters.Where(p => p.RefKind == RefKind.Out).ToList();
            bool returnsVoid = methodSymbol.ReturnsVoid;
            int expectedCount = outParameters.Count + (returnsVoid ? 0 : 1);

            // 如果语句数量不符，肯定没清空
            if (statements.Count != expectedCount) return false;

            // 1. 验证 out 参数赋值语句
            for (int i = 0; i < outParameters.Count; i++)
            {
                var stmt = statements[i];
                if (stmt is ExpressionStatementSyntax exprStmt &&
                    exprStmt.Expression is AssignmentExpressionSyntax assign &&
                    assign.Left is IdentifierNameSyntax id &&
                    id.Identifier.Text == outParameters[i].Name)
                {
                    // 进一步检查是否赋值为 null, false 或 (T)0
                    var right = assign.Right;
                    if (IsDefaultValueExpression(right)) continue;
                }
                return false;
            }

            // 2. 验证 return 语句
            if (!returnsVoid)
            {
                var lastStmt = statements.Last();
                if (lastStmt is ReturnStatementSyntax returnStmt && returnStmt.Expression != null)
                {
                    if (IsDefaultValueExpression(returnStmt.Expression)) return true;
                }
                return false;
            }

            return true;
        }

        private bool IsDefaultValueExpression(ExpressionSyntax expr)
        {
            // null, false, default
            if (expr is LiteralExpressionSyntax literal &&
                (literal.IsKind(SyntaxKind.NullLiteralExpression) ||
                 literal.IsKind(SyntaxKind.FalseLiteralExpression) ||
                 literal.IsKind(SyntaxKind.DefaultLiteralExpression)))
            {
                return true;
            }

            // (T)0
            if (expr is CastExpressionSyntax cast &&
                cast.Expression is LiteralExpressionSyntax numLiteral &&
                numLiteral.Token.ValueText == "0")
            {
                return true;
            }

            return false;
        }

        private async Task<bool> IsSymbolInsideClassAsync(Document doc, TextSpan span, INamedTypeSymbol classSymbol)
        {
            var model = await doc.GetSemanticModelAsync();
            if (model == null) return false;

            var enclosingSymbol = model.GetEnclosingSymbol(span.Start);
            return IsSymbolInsideClass(enclosingSymbol, classSymbol);
        }

        private bool IsSymbolInsideClass(ISymbol? symbol, INamedTypeSymbol classSymbol)
        {
            while (symbol != null)
            {
                if (SymbolEqualityComparer.Default.Equals(symbol, classSymbol))
                    return true;
                symbol = symbol.ContainingSymbol;
            }
            return false;
        }

        private bool IsInterfaceImplementation(IMethodSymbol methodSymbol)
        {
            var containingType = methodSymbol.ContainingType;
            foreach (var iface in containingType.AllInterfaces)
            {
                foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
                {
                    if (SymbolEqualityComparer.Default.Equals(containingType.FindImplementationForInterfaceMember(member), methodSymbol))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private async Task<SyntaxNode> GetUpdatedRootAsync(SyntaxNode root, ClassDeclarationSyntax currentClass,
            List<MethodDeclarationSyntax> toDelete,
            List<MethodDeclarationSyntax> toMakePrivate,
            List<MethodDeclarationSyntax> toClearBody,
            List<MethodDeclarationSyntax> withExternalRefs,
            List<MethodDeclarationSyntax> others,
            SemanticModel semanticModel)
        {
            // 1. 处理需要改为 private 的方法
            var updatedToMakePrivate = toMakePrivate.Select(m =>
            {
                var modifiers = m.Modifiers.Where(mod =>
                    !mod.IsKind(SyntaxKind.PublicKeyword) &&
                    !mod.IsKind(SyntaxKind.InternalKeyword) &&
                    !mod.IsKind(SyntaxKind.ProtectedKeyword) &&
                    !mod.IsKind(SyntaxKind.PrivateKeyword)
                ).ToList();
                modifiers.Insert(0, SyntaxFactory.Token(SyntaxKind.PrivateKeyword).WithTrailingTrivia(SyntaxFactory.Space));
                return m.WithModifiers(SyntaxFactory.TokenList(modifiers));
            }).ToList();

            // 2. 处理需要清空函数体的方法
            var updatedClearedBody = toClearBody.Select(m =>
            {
                var returnTypeSyntax = m.ReturnType;
                var methodSymbol = semanticModel.GetDeclaredSymbol(m);
                var statements = new List<StatementSyntax>();

                // 处理 out 参数赋值，C# 要求在方法返回前必须对 out 参数赋值
                if (methodSymbol != null)
                {
                    foreach (var parameter in methodSymbol.Parameters)
                    {
                        if (parameter.RefKind == RefKind.Out)
                        {
                            ExpressionSyntax defaultValue;
                            var paramType = parameter.Type;

                            if (paramType.SpecialType == SpecialType.System_Boolean)
                            {
                                defaultValue = SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression);
                            }
                            else if (IsNumericType(paramType))
                            {
                                // 数值类型返回 (T)0
                                var typeSyntax = SyntaxFactory.ParseTypeName(paramType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                                defaultValue = SyntaxFactory.CastExpression(
                                    typeSyntax,
                                    SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0))
                                );
                            }
                            else
                            {
                                // 其他对象类型返回 null
                                defaultValue = SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
                            }

                            statements.Add(SyntaxFactory.ExpressionStatement(
                                SyntaxFactory.AssignmentExpression(
                                    SyntaxKind.SimpleAssignmentExpression,
                                    SyntaxFactory.IdentifierName(parameter.Name),
                                    defaultValue)));
                        }
                    }
                }

                BlockSyntax newBody;

                // 如果返回类型不是 void，根据类型添加具体的返回值
                if (returnTypeSyntax is PredefinedTypeSyntax predefined && predefined.Keyword.IsKind(SyntaxKind.VoidKeyword))
                {
                    newBody = SyntaxFactory.Block(statements);
                }
                else
                {
                    var returnType = methodSymbol?.ReturnType;

                    ExpressionSyntax returnExpression;
                    if (returnType == null)
                    {
                        returnExpression = SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression, SyntaxFactory.Token(SyntaxKind.DefaultKeyword));
                    }
                    else if (returnType.SpecialType == SpecialType.System_Boolean)
                    {
                        // bool 返回 false
                        returnExpression = SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression);
                    }
                    else if (IsNumericType(returnType))
                    {
                        // 数值类型返回 (T)0
                        returnExpression = SyntaxFactory.CastExpression(
                            returnTypeSyntax,
                            SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0))
                        );
                    }
                    else if (returnType is ITypeParameterSymbol)
                    {
                        // 泛型类型使用 default
                        returnExpression = SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression, SyntaxFactory.Token(SyntaxKind.DefaultKeyword));
                    }
                    else
                    {
                        // 其他情况（类、可空、以及用户要求的 Struct）均返回 null
                        returnExpression = SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
                    }

                    statements.Add(SyntaxFactory.ReturnStatement(returnExpression));
                    newBody = SyntaxFactory.Block(statements);
                }

                return m.WithBody(newBody)
                        .WithExpressionBody(null)
                        .WithSemicolonToken(default);
            }).ToList();

            // 3. 重新组合成员
            var newMethods = new List<MethodDeclarationSyntax>();
            newMethods.AddRange(withExternalRefs);

            var remainingMethods = others
                .Concat(updatedToMakePrivate)
                .Concat(updatedClearedBody)
                .ToList();

            newMethods.AddRange(remainingMethods);

            // 按名称排序（可选，但通常有助于代码整洁）
            newMethods = newMethods.OrderBy(m => m.Identifier.Text).ToList();

            // 4. 构造新的类成员列表
            var nonMethodMembers = currentClass.Members.Where(m => !(m is MethodDeclarationSyntax)).ToList();
            var allNewMembers = SyntaxFactory.List<MemberDeclarationSyntax>(nonMethodMembers.Concat(newMethods));

            var newClassDecl = currentClass.WithMembers(allNewMembers);
            return root.ReplaceNode(currentClass, newClassDecl);
        }

        private bool IsNumericType(ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Decimal:
                case SpecialType.System_Char:
                    return true;
                default:
                    return false;
            }
        }

        private bool IsNullableType(ITypeSymbol type)
        {
            return type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        }
    }
}
