using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome.Rewrite.Roslyn;

using TerrariaTools.Dome.Core;

/// <summary>
/// Roslyn 重写执行器。
/// </summary>
public sealed class RoslynRewriteExecutor
{
    /// <summary>
    /// 异步执行重写计划。
    /// </summary>
    /// <param name="source">源代码。</param>
    /// <param name="plan">审计计划。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>重写执行结果。</returns>
    public Task<RewriteExecutionResult> ExecuteAsync(string source, AuditPlan plan, CancellationToken cancellationToken)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot(cancellationToken);
        return ExecuteAsync(
            new RewriteExecutionDocumentContext(
                new SourceDocument(
                    plan.Changes.FirstOrDefault()?.Target.DocumentPath ?? "input.cs",
                    plan.Changes.FirstOrDefault()?.Target.DocumentPath ?? "input.cs",
                    source),
                root,
                null),
            plan,
            cancellationToken);
    }

    /// <summary>
    /// 异步执行带语义上下文的重写计划。
    /// </summary>
    /// <param name="documentContext">重写文档上下文。</param>
    /// <param name="plan">审计计划。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>重写执行结果。</returns>
    public Task<RewriteExecutionResult> ExecuteAsync(RewriteExecutionDocumentContext documentContext, AuditPlan plan, CancellationToken cancellationToken)
    {
        if (plan.Conflicts.Count > 0)
        {
            return Task.FromResult(RewriteExecutionResult.Failure("Rewrite cannot execute a plan with unresolved conflicts."));
        }

        var orderedChanges = plan.Changes
            .OrderBy(change => change.Target.DocumentPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(change => change.Target.MemberId.Value, StringComparer.Ordinal)
            .ThenByDescending(change => change.Target.SpanStart)
            .ThenBy(change => change.ExecutionOrder)
            .ToArray();

        var bindResult = BindPlan(documentContext, orderedChanges);
        if (!bindResult.IsSuccess || bindResult.Plan == null)
        {
            return Task.FromResult(RewriteExecutionResult.Failure(bindResult.Message));
        }

        var trackedRoot = documentContext.Root.TrackNodes(bindResult.Plan.Changes.Select(boundChange => boundChange.OriginalNode));
        SyntaxNode root = trackedRoot;
        foreach (var change in bindResult.Plan.Changes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentNode = root.GetCurrentNode(change.OriginalNode);
            if (currentNode == null)
            {
                return Task.FromResult(RewriteExecutionResult.Failure(
                    $"Target '{change.Change.Target.TargetKey}' could not be resolved during rewrite because the bound node is no longer available in the current syntax tree."));
            }

            var applyResult = ApplyChange(root, currentNode, change.Change);
            if (!applyResult.IsSuccess || applyResult.Root == null)
            {
                return Task.FromResult(RewriteExecutionResult.Failure(applyResult.Message));
            }

            root = applyResult.Root;
        }

        return Task.FromResult(RewriteExecutionResult.Success(root.ToFullString()));
    }

    /// <summary>
    /// 将计划变更绑定到当前语法树节点。
    /// </summary>
    private static RewriteBindingResult BindPlan(
        RewriteExecutionDocumentContext documentContext,
        IReadOnlyList<PlannedChange> orderedChanges)
    {
        var boundChanges = new List<BoundPlannedChange>(orderedChanges.Count);
        foreach (var change in orderedChanges)
        {
            var resolution = FindTargetNode(documentContext.Root, documentContext.SemanticModel, change.Target);
            if (!resolution.IsSuccess || resolution.Node == null)
            {
                return RewriteBindingResult.Failure(resolution.Message);
            }

            boundChanges.Add(new BoundPlannedChange(change, resolution.Node));
        }

        return RewriteBindingResult.Success(new BoundRewritePlan(boundChanges));
    }

    /// <summary>
    /// 查找目标节点。
    /// </summary>
    /// <param name="root">语法根节点。</param>
    /// <param name="target">计划目标。</param>
    /// <returns>目标解析结果。</returns>
    private static TargetResolutionResult FindTargetNode(
        SyntaxNode root,
        SemanticModel? semanticModel,
        PlanTarget target)
    {
        var stableResolution = TryResolveByResolutionKey(root, semanticModel, target);
        if (stableResolution.IsSuccess || target.ResolutionKey != null)
        {
            return stableResolution;
        }

        if (target.TargetKind == TargetKind.Class)
        {
            var classCandidates = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Where(@class => ClassMatches(@class, target.MemberId.Value))
                .Cast<SyntaxNode>()
                .ToArray();

            if (classCandidates.Length == 0)
            {
                return TargetResolutionResult.Failure($"Target '{target.TargetKey}' could not be resolved during rewrite because the class '{target.MemberId.Value}' was not found.");
            }

            if (classCandidates.Length == 1)
            {
                return TargetResolutionResult.Success(classCandidates[0]);
            }

            var classSpanCandidates = classCandidates
                .Where(node => node.SpanStart == target.SpanStart && node.Span.Length == target.SpanLength)
                .ToArray();

            if (classSpanCandidates.Length == 0)
            {
                return TargetResolutionResult.Failure($"Target '{target.TargetKey}' could not be resolved during rewrite because the span did not match the class '{target.MemberId.Value}'.");
            }

            var classTextMatch = classSpanCandidates.FirstOrDefault(node => string.Equals(node.ToString().Trim(), target.DisplayText, StringComparison.Ordinal));
            if (classTextMatch == null)
            {
                return TargetResolutionResult.Failure($"Target '{target.TargetKey}' could not be resolved during rewrite because the class text did not match '{target.DisplayText}'.");
            }

            return TargetResolutionResult.Success(classTextMatch);
        }

        if (target.TargetKind == TargetKind.Method)
        {
            var semanticMatchContext = TryCreateMethodSemanticMatchContext(root);

            var methodCandidates = semanticMatchContext.Root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(method => MemberMatches(method, target.MemberId.Value, semanticMatchContext.SemanticModel))
                .Select(method => ResolveMethodCandidate(root, method))
                .Where(static node => node != null)
                .Cast<SyntaxNode>()
                .ToArray();

            if (methodCandidates.Length == 0)
            {
                return TargetResolutionResult.Failure($"Target '{target.TargetKey}' could not be resolved during rewrite because the member '{target.MemberId.Value}' was not found.");
            }

            if (methodCandidates.Length == 1)
            {
                return TargetResolutionResult.Success(methodCandidates[0]);
            }

            var methodSpanCandidates = methodCandidates
                .Where(node => node.SpanStart == target.SpanStart && node.Span.Length == target.SpanLength)
                .ToArray();

            if (methodSpanCandidates.Length == 0)
            {
                return TargetResolutionResult.Failure($"Target '{target.TargetKey}' could not be resolved during rewrite because the span did not match the method '{target.MemberId.Value}'.");
            }

            var methodTextMatch = methodSpanCandidates.FirstOrDefault(node => string.Equals(node.ToString().Trim(), target.DisplayText, StringComparison.Ordinal));
            if (methodTextMatch == null)
            {
                return TargetResolutionResult.Failure($"Target '{target.TargetKey}' could not be resolved during rewrite because the method text did not match '{target.DisplayText}'.");
            }

            return TargetResolutionResult.Success(methodTextMatch);
        }

        var memberCandidates = root.DescendantNodes()
            .OfType<StatementSyntax>()
            .Where(statement => MemberMatches(statement, target.MemberId.Value))
            .ToArray();

        if (memberCandidates.Length == 0)
        {
            return TargetResolutionResult.Failure($"Target '{target.TargetKey}' could not be resolved during rewrite because the member '{target.MemberId.Value}' was not found.");
        }

        var spanCandidates = memberCandidates
            .Where(statement => statement.SpanStart == target.SpanStart && statement.Span.Length == target.SpanLength)
            .ToArray();

        if (spanCandidates.Length == 0)
        {
            return TargetResolutionResult.Failure($"Target '{target.TargetKey}' could not be resolved during rewrite because the span did not match any statement in member '{target.MemberId.Value}'.");
        }

        var textMatch = spanCandidates
            .FirstOrDefault(statement => string.Equals(statement.ToString().Trim(), target.DisplayText, StringComparison.Ordinal));

        if (textMatch == null)
        {
            return TargetResolutionResult.Failure($"Target '{target.TargetKey}' could not be resolved during rewrite because the statement text did not match '{target.DisplayText}'.");
        }

        return TargetResolutionResult.Success(textMatch);
    }

    /// <summary>
    /// 使用稳定解析键解析目标节点。
    /// </summary>
    private static TargetResolutionResult TryResolveByResolutionKey(
        SyntaxNode root,
        SemanticModel? semanticModel,
        PlanTarget target)
    {
        var candidate = ResolveNodeByResolutionKey(root, target);
        if (candidate == null)
        {
            return TargetResolutionResult.Failure($"Target '{target.TargetKey}' could not be resolved during rewrite because no node matched the resolution key.");
        }

        if (!SemanticIdentityMatches(root, semanticModel, target, candidate))
        {
            return TargetResolutionResult.Failure($"Target '{target.TargetKey}' could not be resolved during rewrite because the semantic identity did not match '{target.MemberId.Value}'.");
        }

        if (!string.Equals(candidate.ToString().Trim(), target.DisplayText, StringComparison.Ordinal) &&
            target.TargetKind == TargetKind.Statement)
        {
            return TargetResolutionResult.Failure($"Target '{target.TargetKey}' could not be resolved during rewrite because the statement text did not match '{target.DisplayText}'.");
        }

        return TargetResolutionResult.Success(candidate);
    }

    /// <summary>
    /// 检查语句是否匹配成员 ID。
    /// </summary>
    /// <param name="statement">语句语法节点。</param>
    /// <param name="memberId">成员 ID。</param>
    /// <returns>如果匹配则返回 true，否则返回 false。</returns>
    private static bool MemberMatches(StatementSyntax statement, string memberId)
    {
        var method = statement.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault();
        if (method != null)
        {
            var identifier = method switch
            {
                MethodDeclarationSyntax methodDeclaration => methodDeclaration.Identifier.Text,
                ConstructorDeclarationSyntax ctor => ctor.Identifier.Text,
                _ => null
            };

            return identifier != null && memberId.Contains(identifier, StringComparison.Ordinal);
        }

        var accessor = statement.Ancestors().OfType<AccessorDeclarationSyntax>().FirstOrDefault();
        return accessor == null || memberId.EndsWith(accessor.Keyword.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// 检查方法是否匹配成员 ID。
    /// </summary>
    /// <param name="method">方法声明语法节点。</param>
    /// <param name="memberId">成员 ID。</param>
    /// <returns>如果匹配则返回 true，否则返回 false。</returns>
    private static bool MemberMatches(MethodDeclarationSyntax method, string memberId, SemanticModel? semanticModel)
    {
        if (semanticModel?.GetDeclaredSymbol(method) is IMethodSymbol symbol)
        {
            return string.Equals(BuildMethodMemberId(symbol), memberId, StringComparison.Ordinal);
        }

        var exactMemberId = $"{GetContainingTypeName(method)}.{method.Identifier.Text}({BuildParameterList(method.ParameterList)})";
        if (string.Equals(memberId, exactMemberId, StringComparison.Ordinal))
        {
            return true;
        }

        var prefix = $"{GetContainingTypeName(method)}.{method.Identifier.Text}";
        if (!memberId.StartsWith(prefix, StringComparison.Ordinal) ||
            memberId.Length <= prefix.Length + 2 ||
            memberId[prefix.Length] != '(' ||
            memberId[^1] != ')')
        {
            return false;
        }

        var normalizedExpected = NormalizeParameterList(memberId.Substring(prefix.Length + 1, memberId.Length - prefix.Length - 2));
        var normalizedActual = NormalizeParameterList(method.ParameterList);
        return string.Equals(normalizedActual, normalizedExpected, StringComparison.Ordinal);
    }

    /// <summary>
    /// 为方法匹配创建语义上下文。
    /// </summary>
    /// <param name="root">当前语法根节点。</param>
    /// <returns>方法匹配上下文。</returns>
    private static MethodSemanticMatchContext TryCreateMethodSemanticMatchContext(SyntaxNode root)
    {
        var syntaxTree = root.SyntaxTree;
        if (syntaxTree == null)
        {
            syntaxTree = CSharpSyntaxTree.ParseText(root.ToFullString());
            root = syntaxTree.GetCompilationUnitRoot();
        }

        try
        {
            var compilation = CSharpCompilation.Create(
                "DomeRewriteResolution",
                new[] { syntaxTree },
                GetRewriteMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            return new MethodSemanticMatchContext(root, compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true));
        }
        catch
        {
            return new MethodSemanticMatchContext(root, null);
        }
    }

    /// <summary>
    /// 将语义匹配得到的方法节点解析回当前根节点中的对应节点。
    /// </summary>
    /// <param name="currentRoot">当前根节点。</param>
    /// <param name="matchedMethod">匹配到的方法节点。</param>
    /// <returns>当前根节点中的对应方法节点。</returns>
    private static MethodDeclarationSyntax? ResolveMethodCandidate(SyntaxNode currentRoot, MethodDeclarationSyntax matchedMethod)
    {
        if (ReferenceEquals(currentRoot.SyntaxTree, matchedMethod.SyntaxTree))
        {
            return matchedMethod;
        }

        return currentRoot.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method =>
                method.SpanStart == matchedMethod.SpanStart &&
                method.Span.Length == matchedMethod.Span.Length &&
                string.Equals(method.ToString(), matchedMethod.ToString(), StringComparison.Ordinal));
    }

    /// <summary>
    /// 根据稳定解析键解析当前根节点中的目标节点。
    /// </summary>
    private static SyntaxNode? ResolveNodeByResolutionKey(SyntaxNode root, PlanTarget target)
    {
        var resolutionKey = target.EffectiveResolutionKey;
        return target.TargetKind switch
        {
            TargetKind.Statement => root.DescendantNodes()
                .OfType<StatementSyntax>()
                .FirstOrDefault(statement =>
                    statement.SpanStart == resolutionKey.SpanStart &&
                    statement.Span.Length == resolutionKey.SpanLength),
            TargetKind.Class => root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(@class =>
                    @class.SpanStart == resolutionKey.SpanStart &&
                    @class.Span.Length == resolutionKey.SpanLength),
            TargetKind.Method => EnumerateMethodLikeDeclarations(root, target.MemberKind)
                .FirstOrDefault(node =>
                    node.SpanStart == resolutionKey.SpanStart &&
                    node.Span.Length == resolutionKey.SpanLength),
            _ => null
        };
    }

    /// <summary>
    /// 枚举方法类目标对应的声明节点。
    /// </summary>
    private static IEnumerable<SyntaxNode> EnumerateMethodLikeDeclarations(SyntaxNode root, MemberKind memberKind)
    {
        if (memberKind == MemberKind.Accessor)
        {
            foreach (var accessor in root.DescendantNodes().OfType<AccessorDeclarationSyntax>())
            {
                yield return accessor;
            }

            foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Where(static property => property.ExpressionBody != null))
            {
                yield return property;
            }

            yield break;
        }

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            yield return method;
        }

        foreach (var ctor in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
        {
            yield return ctor;
        }
    }

    /// <summary>
    /// 使用原始语义上下文校验稳定解析键对应的语义标识。
    /// </summary>
    private static bool SemanticIdentityMatches(SyntaxNode root, SemanticModel? semanticModel, PlanTarget target, SyntaxNode resolvedNode)
    {
        if (semanticModel == null)
        {
            return true;
        }

        if (!ReferenceEquals(root.SyntaxTree, resolvedNode.SyntaxTree))
        {
            return true;
        }

        var symbol = TryGetDeclaredSymbol(resolvedNode, semanticModel, target);
        if (symbol == null)
        {
            return true;
        }

        if (ContainsUnresolvedTypes(symbol))
        {
            return true;
        }

        var memberId = BuildMemberId(symbol);
        return string.Equals(memberId, target.MemberId.Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// 获取目标节点的声明符号。
    /// </summary>
    private static ISymbol? TryGetDeclaredSymbol(SyntaxNode node, SemanticModel semanticModel, PlanTarget target)
    {
        return node switch
        {
            MethodDeclarationSyntax method => semanticModel.GetDeclaredSymbol(method),
            ConstructorDeclarationSyntax ctor => semanticModel.GetDeclaredSymbol(ctor),
            AccessorDeclarationSyntax accessor => semanticModel.GetDeclaredSymbol(accessor),
            ClassDeclarationSyntax @class => semanticModel.GetDeclaredSymbol(@class),
            PropertyDeclarationSyntax property when target.MemberKind == MemberKind.Accessor => ResolveAccessorSymbol(property, semanticModel, target.MemberId.Value),
            _ => null
        };
    }

    /// <summary>
    /// 解析属性目标对应的访问器符号。
    /// </summary>
    private static ISymbol? ResolveAccessorSymbol(PropertyDeclarationSyntax property, SemanticModel semanticModel, string memberId)
    {
        var propertySymbol = semanticModel.GetDeclaredSymbol(property);
        if (propertySymbol == null)
        {
            return null;
        }

        if (memberId.EndsWith(".set", StringComparison.Ordinal))
        {
            return propertySymbol.SetMethod;
        }

        return propertySymbol.GetMethod;
    }

    /// <summary>
    /// 判断符号是否包含未解析类型。
    /// </summary>
    private static bool ContainsUnresolvedTypes(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol method => HasErrorType(method.ContainingType) ||
                                    HasErrorType(method.ReturnType) ||
                                    method.Parameters.Any(parameter => HasErrorType(parameter.Type)),
            INamedTypeSymbol namedType => HasErrorType(namedType),
            _ => false
        };
    }

    /// <summary>
    /// 判断类型符号是否未解析。
    /// </summary>
    private static bool HasErrorType(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol == null)
        {
            return false;
        }

        if (typeSymbol.TypeKind == TypeKind.Error)
        {
            return true;
        }

        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            return HasErrorType(arrayType.ElementType);
        }

        if (typeSymbol is IPointerTypeSymbol pointerType)
        {
            return HasErrorType(pointerType.PointedAtType);
        }

        if (typeSymbol is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            return namedType.TypeArguments.Any(HasErrorType);
        }

        return false;
    }

    /// <summary>
    /// 获取 rewrite 解析所需的元数据引用。
    /// </summary>
    /// <returns>元数据引用集合。</returns>
    private static IReadOnlyList<MetadataReference> GetRewriteMetadataReferences()
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            return trustedPlatformAssemblies
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(static path => MetadataReference.CreateFromFile(path))
                .ToArray();
        }

        return new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) };
    }

    /// <summary>
    /// 构建方法成员 ID。
    /// </summary>
    /// <param name="symbol">方法符号。</param>
    /// <returns>成员 ID 字符串。</returns>
    private static string BuildMethodMemberId(IMethodSymbol symbol)
    {
        return symbol.MethodKind switch
        {
            MethodKind.PropertyGet => $"{BuildSemanticTypeName(symbol.ContainingType)}.{symbol.AssociatedSymbol?.Name}.get",
            MethodKind.PropertySet => $"{BuildSemanticTypeName(symbol.ContainingType)}.{symbol.AssociatedSymbol?.Name}.set",
            MethodKind.Constructor => $"{BuildSemanticTypeName(symbol.ContainingType)}..ctor({BuildSemanticParameterList(symbol.Parameters)})",
            _ => $"{BuildSemanticTypeName(symbol.ContainingType)}.{symbol.Name}({BuildSemanticParameterList(symbol.Parameters)})"
        };
    }

    /// <summary>
    /// 构建声明符号成员 ID。
    /// </summary>
    private static string BuildMemberId(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol method => BuildMethodMemberId(method),
            INamedTypeSymbol typeSymbol => BuildSemanticTypeName(typeSymbol),
            _ => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
        };
    }

    /// <summary>
    /// 构建语义类型名。
    /// </summary>
    /// <param name="typeSymbol">类型符号。</param>
    /// <returns>类型名字符串。</returns>
    private static string BuildSemanticTypeName(INamedTypeSymbol? typeSymbol)
    {
        return typeSymbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? "Unknown";
    }

    /// <summary>
    /// 构建语义参数列表字符串。
    /// </summary>
    /// <param name="parameters">参数符号集合。</param>
    /// <returns>参数列表字符串。</returns>
    private static string BuildSemanticParameterList(ImmutableArray<IParameterSymbol> parameters)
    {
        return string.Join(", ", parameters.Select(parameter =>
            parameter.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
    }

    /// <summary>
    /// 检查类是否匹配类 ID。
    /// </summary>
    /// <param name="classDeclaration">类声明语法节点。</param>
    /// <param name="classId">类 ID。</param>
    /// <returns>如果匹配则返回 true，否则返回 false。</returns>
    private static bool ClassMatches(ClassDeclarationSyntax classDeclaration, string classId)
    {
        return string.Equals(classId, GetContainingTypeName(classDeclaration), StringComparison.Ordinal);
    }

    /// <summary>
    /// 获取包含类型名称。
    /// </summary>
    /// <param name="member">成员声明语法节点。</param>
    /// <returns>包含类型名称。</returns>
    private static string GetContainingTypeName(MemberDeclarationSyntax member)
    {
        if (member is TypeDeclarationSyntax typeDeclaration)
        {
            var containingTypes = typeDeclaration.Ancestors().OfType<TypeDeclarationSyntax>().Reverse().Select(type => type.Identifier.Text);
            var classNamespaces = member.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(ns => ns.Name.ToString());
            var segments = classNamespaces.Concat(containingTypes).Append(typeDeclaration.Identifier.Text);
            return string.Join(".", segments.Where(segment => !string.IsNullOrWhiteSpace(segment)));
        }

        var type = member.Ancestors().OfType<TypeDeclarationSyntax>().First();
        var namespaces = member.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(ns => ns.Name.ToString());
        var namespacePrefix = string.Join(".", namespaces);
        return string.IsNullOrEmpty(namespacePrefix) ? type.Identifier.Text : $"{namespacePrefix}.{type.Identifier.Text}";
    }

    /// <summary>
    /// 构建参数列表字符串。
    /// </summary>
    /// <param name="parameterList">参数列表语法节点。</param>
    /// <returns>参数列表字符串。</returns>
    private static string BuildParameterList(ParameterListSyntax parameterList)
    {
        return string.Join(", ", parameterList.Parameters.Select(parameter => parameter.Type?.ToString() ?? "object"));
    }

    /// <summary>
    /// 规范化参数列表语法节点。
    /// </summary>
    /// <param name="parameterList">参数列表语法节点。</param>
    /// <returns>规范化后的参数列表字符串。</returns>
    private static string NormalizeParameterList(ParameterListSyntax parameterList)
    {
        return string.Join(",", parameterList.Parameters.Select(parameter => NormalizeTypeSyntax(parameter.Type)));
    }

    /// <summary>
    /// 规范化参数列表字符串。
    /// </summary>
    /// <param name="parameterList">参数列表字符串。</param>
    /// <returns>规范化后的参数列表字符串。</returns>
    private static string NormalizeParameterList(string parameterList)
    {
        if (string.IsNullOrWhiteSpace(parameterList))
        {
            return string.Empty;
        }

        return string.Join(",", SplitParameterList(parameterList).Select(NormalizeTypeName));
    }

    /// <summary>
    /// 拆分参数列表字符串。
    /// </summary>
    /// <param name="parameterList">参数列表字符串。</param>
    /// <returns>参数类型字符串序列。</returns>
    private static IReadOnlyList<string> SplitParameterList(string parameterList)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;

        for (var index = 0; index < parameterList.Length; index++)
        {
            var current = parameterList[index];
            switch (current)
            {
                case '<':
                case '(':
                case '[':
                    depth++;
                    break;
                case '>':
                case ')':
                case ']':
                    depth--;
                    break;
                case ',' when depth == 0:
                    parts.Add(parameterList[start..index].Trim());
                    start = index + 1;
                    break;
            }
        }

        parts.Add(parameterList[start..].Trim());
        return parts;
    }

    /// <summary>
    /// 规范化类型名称字符串。
    /// </summary>
    /// <param name="typeName">类型名称字符串。</param>
    /// <returns>规范化后的类型名称。</returns>
    private static string NormalizeTypeName(string typeName)
    {
        var parsedType = SyntaxFactory.ParseTypeName(typeName.Replace("global::", string.Empty, StringComparison.Ordinal));
        return NormalizeTypeSyntax(parsedType);
    }

    /// <summary>
    /// 规范化类型语法节点。
    /// </summary>
    /// <param name="typeSyntax">类型语法节点。</param>
    /// <returns>规范化后的类型名称。</returns>
    private static string NormalizeTypeSyntax(TypeSyntax? typeSyntax)
    {
        return typeSyntax switch
        {
            null => "object",
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            GenericNameSyntax generic => $"{generic.Identifier.Text}<{string.Join(",", generic.TypeArgumentList.Arguments.Select(NormalizeTypeSyntax))}>",
            QualifiedNameSyntax qualified => NormalizeTypeSyntax(qualified.Right),
            AliasQualifiedNameSyntax aliasQualified => NormalizeTypeSyntax(aliasQualified.Name),
            PredefinedTypeSyntax predefined => predefined.Keyword.Text,
            NullableTypeSyntax nullable => $"{NormalizeTypeSyntax(nullable.ElementType)}?",
            ArrayTypeSyntax array => $"{NormalizeTypeSyntax(array.ElementType)}{string.Concat(array.RankSpecifiers.Select(rank => rank.ToString()))}",
            PointerTypeSyntax pointer => $"{NormalizeTypeSyntax(pointer.ElementType)}*",
            TupleTypeSyntax tuple => $"({string.Join(",", tuple.Elements.Select(static element => NormalizeTypeSyntax(element.Type)))})",
            OmittedTypeArgumentSyntax => "_",
            _ => typeSyntax.WithoutTrivia().ToString().Replace(" ", string.Empty, StringComparison.Ordinal)
        };
    }

    /// <summary>
    /// 注释掉语句。
    /// </summary>
    /// <param name="statement">语句语法节点。</param>
    /// <param name="change">计划更改。</param>
    /// <returns>注释后的语句语法节点。</returns>
    private static StatementSyntax CommentOut(StatementSyntax statement, PlannedChange change)
    {
        return SyntaxFactory.EmptyStatement()
            .WithLeadingTrivia(
                SyntaxFactory.Comment($"// {change.Reason.RuleId}: {statement.ToString().Trim()}"),
                SyntaxFactory.CarriageReturnLineFeed);
    }

    /// <summary>
    /// 使用默认值替换语句。
    /// </summary>
    /// <param name="statement">语句语法节点。</param>
    /// <param name="change">计划更改。</param>
    /// <returns>替换后的语句语法节点。</returns>
    private static StatementSyntax ReplaceWithDefault(StatementSyntax statement, PlannedChange change)
    {
        if (statement is not ExpressionStatementSyntax expressionStatement ||
            expressionStatement.Expression is not AssignmentExpressionSyntax assignment)
        {
            throw new InvalidOperationException($"Action '{PlanActionKind.ReplaceWithDefault}' is unsupported for target '{change.Target.TargetKey}' because the statement is not an assignment.");
        }

        var payload = change.Action.Payload ?? "default";
        var expression = SyntaxFactory.ParseExpression(payload);
        return statement.ReplaceNode(assignment.Right, expression);
    }

    /// <summary>
    /// 添加返回语句。
    /// </summary>
    /// <param name="statement">语句语法节点。</param>
    /// <param name="change">计划更改。</param>
    /// <returns>添加返回语句后的语句语法节点。</returns>
    private static StatementSyntax AddReturn(StatementSyntax statement, PlannedChange change)
    {
        var payload = change.Action.Payload;
        var returnStatement = string.IsNullOrWhiteSpace(payload)
            ? "return;"
            : $"return {payload};";
        return SyntaxFactory.ParseStatement(returnStatement)
            .WithTriviaFrom(statement);
    }

    /// <summary>
    /// 应用更改。
    /// </summary>
    /// <param name="root">语法根节点。</param>
    /// <param name="node">目标节点。</param>
    /// <param name="change">计划更改。</param>
    /// <returns>更改应用结果。</returns>
    private static ApplyChangeResult ApplyChange(SyntaxNode root, SyntaxNode node, PlannedChange change)
    {
        if (change.Target.TargetKind == TargetKind.Class)
        {
            if (node is not ClassDeclarationSyntax)
            {
                return ApplyChangeResult.Failure($"Action '{change.Action.Kind}' is unsupported for target '{change.Target.TargetKey}' because the target is not a class.");
            }

            try
            {
                var updatedRoot = change.Action.Kind switch
                {
                    PlanActionKind.Delete => root.RemoveNode(node, SyntaxRemoveOptions.KeepNoTrivia)
                                                 ?? throw new InvalidOperationException($"Action '{PlanActionKind.Delete}' invalidated target '{change.Target.TargetKey}'."),
                    _ => throw new InvalidOperationException($"Action '{change.Action.Kind}' is unsupported for class target '{change.Target.TargetKey}'.")
                };

                return ApplyChangeResult.Success(updatedRoot);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return ApplyChangeResult.Failure(ex.Message);
            }
        }

        if (change.Target.TargetKind == TargetKind.Method)
        {
            try
            {
                var updatedRoot = change.Action.Kind switch
                    {
                        PlanActionKind.Delete => root.RemoveNode(node, SyntaxRemoveOptions.KeepNoTrivia)
                                                     ?? throw new InvalidOperationException($"Action '{PlanActionKind.Delete}' invalidated target '{change.Target.TargetKey}'."),
                    PlanActionKind.AddReturn when node is MethodDeclarationSyntax methodNode => root.ReplaceNode(node, AddReturn(methodNode, change)),
                    _ => throw new InvalidOperationException($"Action '{change.Action.Kind}' is unsupported for method target '{change.Target.TargetKey}'.")
                };

                return ApplyChangeResult.Success(updatedRoot);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return ApplyChangeResult.Failure(ex.Message);
            }
        }

        if (node is not StatementSyntax statementNode)
        {
            return ApplyChangeResult.Failure($"Action '{change.Action.Kind}' is unsupported for target '{change.Target.TargetKey}' because the target is not a statement.");
        }

        try
        {
            var updatedRoot = change.Action.Kind switch
            {
                PlanActionKind.Delete => root.RemoveNode(node, SyntaxRemoveOptions.KeepNoTrivia)
                                             ?? throw new InvalidOperationException($"Action '{PlanActionKind.Delete}' invalidated target '{change.Target.TargetKey}'."),
                PlanActionKind.CommentOut => root.ReplaceNode(node, CommentOut(statementNode, change)),
                PlanActionKind.ReplaceWithDefault => root.ReplaceNode(node, ReplaceWithDefault(statementNode, change)),
                PlanActionKind.AddReturn => root.ReplaceNode(node, AddReturn(statementNode, change)),
                _ => throw new InvalidOperationException($"Action '{change.Action.Kind}' is unsupported for target '{change.Target.TargetKey}'.")
            };

            return ApplyChangeResult.Success(updatedRoot);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return ApplyChangeResult.Failure(ex.Message);
        }
    }

    /// <summary>
    /// 为方法添加返回语句。
    /// </summary>
    /// <param name="method">方法声明语法节点。</param>
    /// <param name="change">计划更改。</param>
    /// <returns>添加返回语句后的方法声明语法节点。</returns>
    private static MethodDeclarationSyntax AddReturn(MethodDeclarationSyntax method, PlannedChange change)
    {
        if (method.Body == null)
        {
            throw new InvalidOperationException($"Action '{PlanActionKind.AddReturn}' is unsupported for target '{change.Target.TargetKey}' because the method has no block body.");
        }

        if (method.Body.Statements.Count > 0)
        {
            throw new InvalidOperationException($"Action '{PlanActionKind.AddReturn}' is unsupported for target '{change.Target.TargetKey}' because the method body is not empty.");
        }

        var payload = change.Action.Payload;
        var returnStatement = string.IsNullOrWhiteSpace(payload)
            ? "return;"
            : $"return {payload};";
        return method.WithBody(
            method.Body.WithStatements(
                SyntaxFactory.SingletonList<StatementSyntax>(SyntaxFactory.ParseStatement(returnStatement))));
    }

    /// <summary>
    /// 目标解析结果记录。
    /// </summary>
    private sealed record TargetResolutionResult(bool IsSuccess, SyntaxNode? Node, string Message)
    {
        /// <summary>
        /// 创建成功结果。
        /// </summary>
        public static TargetResolutionResult Success(SyntaxNode node) => new(true, node, string.Empty);

        /// <summary>
        /// 创建失败结果。
        /// </summary>
        public static TargetResolutionResult Failure(string message) => new(false, null, message);
    }

    /// <summary>
    /// 方法语义匹配上下文。
    /// </summary>
    private sealed record MethodSemanticMatchContext(SyntaxNode Root, SemanticModel? SemanticModel);

    /// <summary>
    /// 绑定后的重写计划。
    /// </summary>
    private sealed record BoundRewritePlan(
        IReadOnlyList<BoundPlannedChange> Changes);

    /// <summary>
    /// 绑定后的单条计划变更。
    /// </summary>
    private sealed record BoundPlannedChange(
        PlannedChange Change,
        SyntaxNode OriginalNode);

    /// <summary>
    /// 重写绑定结果记录。
    /// </summary>
    private sealed record RewriteBindingResult(
        bool IsSuccess,
        BoundRewritePlan? Plan,
        string Message)
    {
        /// <summary>
        /// 创建成功结果。
        /// </summary>
        public static RewriteBindingResult Success(BoundRewritePlan plan) => new(true, plan, string.Empty);

        /// <summary>
        /// 创建失败结果。
        /// </summary>
        public static RewriteBindingResult Failure(string message) => new(false, null, message);
    }

    /// <summary>
    /// 更改应用结果记录。
    /// </summary>
    private sealed record ApplyChangeResult(bool IsSuccess, SyntaxNode? Root, string Message)
    {
        /// <summary>
        /// 创建成功结果。
        /// </summary>
        public static ApplyChangeResult Success(SyntaxNode root) => new(true, root, string.Empty);

        /// <summary>
        /// 创建失败结果。
        /// </summary>
        public static ApplyChangeResult Failure(string message) => new(false, null, message);
    }
}
