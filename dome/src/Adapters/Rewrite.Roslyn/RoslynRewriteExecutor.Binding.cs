using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ModelPlanning = TerrariaTools.Dome.Core.Planning;
using ModelPrimitives = TerrariaTools.Dome.Core.Common;

namespace TerrariaTools.Dome.Adapters.Rewrite.Roslyn;

public sealed partial class RoslynRewriteExecutor
{
    /// <summary>
    /// 绑定重写计划中的每一项变更。
    /// </summary>
    private static RewriteBindingResult BindPlan(
        RewriteDocumentContext documentContext,
        IReadOnlyList<ModelPlanning.PlannedChange> orderedChanges)
    {
        var boundChanges = new List<BoundPlannedChange>(orderedChanges.Count);
        foreach (var change in orderedChanges)
        {
            var resolution = FindTargetNode(documentContext.Root, documentContext.SemanticModel, change.Target, change.Locator);
            if (!resolution.IsSuccess || resolution.Node == null)
            {
                return RewriteBindingResult.Failure(resolution.Message);
            }

            boundChanges.Add(new BoundPlannedChange(change, resolution.Node));
        }

        return RewriteBindingResult.Success(new BoundRewritePlan(boundChanges));
    }

    /// <summary>
    /// 按目标信息定位需要重写的语法节点。
    /// </summary>
    private static TargetResolutionResult FindTargetNode(
        SyntaxNode root,
        SemanticModel? semanticModel,
        ModelPrimitives.TargetIdentity target,
        ModelPrimitives.TargetLocator locator)
    {
        var stableResolution = TryResolveByResolutionKey(root, semanticModel, target, locator);
        if (stableResolution.IsSuccess || locator.EffectiveResolutionKey != locator.ResolutionKey)
        {
            return stableResolution;
        }

        if (target.TargetKind == ModelPrimitives.TargetKind.Class)
        {
            var classCandidates = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Where(@class => ClassMatches(@class, target.MemberId.Value))
                .Cast<SyntaxNode>()
                .ToArray();

            if (classCandidates.Length == 0)
            {
                return TargetResolutionResult.Failure($"Target '{BuildTargetKey(target, locator)}' could not be resolved during rewrite because the class '{target.MemberId.Value}' was not found.");
            }

            if (classCandidates.Length == 1)
            {
                return TargetResolutionResult.Success(classCandidates[0]);
            }

            var classSpanCandidates = classCandidates
                .Where(node => node.SpanStart == locator.SpanStart && node.Span.Length == locator.SpanLength)
                .ToArray();

            if (classSpanCandidates.Length == 0)
            {
                return TargetResolutionResult.Failure($"Target '{BuildTargetKey(target, locator)}' could not be resolved during rewrite because the span did not match the class '{target.MemberId.Value}'.");
            }

            var classTextMatch = classSpanCandidates.FirstOrDefault(node => string.Equals(node.ToString().Trim(), locator.DisplayText, StringComparison.Ordinal));
            if (classTextMatch == null)
            {
                return TargetResolutionResult.Failure($"Target '{BuildTargetKey(target, locator)}' could not be resolved during rewrite because the class text did not match '{locator.DisplayText}'.");
            }

            return TargetResolutionResult.Success(classTextMatch);
        }

        if (target.TargetKind == ModelPrimitives.TargetKind.Method)
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
                return TargetResolutionResult.Failure($"Target '{BuildTargetKey(target, locator)}' could not be resolved during rewrite because the member '{target.MemberId.Value}' was not found.");
            }

            if (methodCandidates.Length == 1)
            {
                return TargetResolutionResult.Success(methodCandidates[0]);
            }

            var methodSpanCandidates = methodCandidates
                .Where(node => node.SpanStart == locator.SpanStart && node.Span.Length == locator.SpanLength)
                .ToArray();

            if (methodSpanCandidates.Length == 0)
            {
                return TargetResolutionResult.Failure($"Target '{BuildTargetKey(target, locator)}' could not be resolved during rewrite because the span did not match the method '{target.MemberId.Value}'.");
            }

            var methodTextMatch = methodSpanCandidates.FirstOrDefault(node => string.Equals(node.ToString().Trim(), locator.DisplayText, StringComparison.Ordinal));
            if (methodTextMatch == null)
            {
                return TargetResolutionResult.Failure($"Target '{BuildTargetKey(target, locator)}' could not be resolved during rewrite because the method text did not match '{locator.DisplayText}'.");
            }

            return TargetResolutionResult.Success(methodTextMatch);
        }

        if (target.TargetKind == ModelPrimitives.TargetKind.Field)
        {
            var fieldCandidates = root.DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .Where(variable => string.Equals(variable.Identifier.ValueText, GetMemberName(target.MemberId.Value), StringComparison.Ordinal))
                .Cast<SyntaxNode>()
                .ToArray();

            return ResolveMemberCandidate(target, locator, fieldCandidates, "field");
        }

        if (target.TargetKind == ModelPrimitives.TargetKind.Property)
        {
            var propertyCandidates = root.DescendantNodes()
                .OfType<PropertyDeclarationSyntax>()
                .Where(property => string.Equals(property.Identifier.ValueText, GetMemberName(target.MemberId.Value), StringComparison.Ordinal))
                .Cast<SyntaxNode>()
                .ToArray();

            return ResolveMemberCandidate(target, locator, propertyCandidates, "property");
        }

        var memberCandidates = root.DescendantNodes()
            .OfType<StatementSyntax>()
            .Where(statement => MemberMatches(statement, target.MemberId.Value))
            .ToArray();

        if (memberCandidates.Length == 0)
        {
            return TargetResolutionResult.Failure($"Target '{BuildTargetKey(target, locator)}' could not be resolved during rewrite because the member '{target.MemberId.Value}' was not found.");
        }

        var spanCandidates = memberCandidates
            .Where(statement => statement.SpanStart == locator.SpanStart && statement.Span.Length == locator.SpanLength)
            .ToArray();

        if (spanCandidates.Length == 0)
        {
            return TargetResolutionResult.Failure($"Target '{BuildTargetKey(target, locator)}' could not be resolved during rewrite because the span did not match any statement in member '{target.MemberId.Value}'.");
        }

        var textMatch = spanCandidates
            .FirstOrDefault(statement => string.Equals(statement.ToString().Trim(), locator.DisplayText, StringComparison.Ordinal));

        if (textMatch == null)
        {
            return TargetResolutionResult.Failure($"Target '{BuildTargetKey(target, locator)}' could not be resolved during rewrite because the statement text did not match '{locator.DisplayText}'.");
        }

        return TargetResolutionResult.Success(textMatch);
    }

    /// <summary>
    /// 尝试通过解析键解析目标节点。
    /// </summary>
    private static TargetResolutionResult TryResolveByResolutionKey(
        SyntaxNode root,
        SemanticModel? semanticModel,
        ModelPrimitives.TargetIdentity target,
        ModelPrimitives.TargetLocator locator)
    {
        var candidate = ResolveNodeByResolutionKey(root, target, locator);
        if (candidate == null)
        {
            return TargetResolutionResult.Failure($"Target '{BuildTargetKey(target, locator)}' could not be resolved during rewrite because no node matched the resolution key.");
        }

        if (!SemanticIdentityMatches(root, semanticModel, target, locator, candidate))
        {
            return TargetResolutionResult.Failure($"Target '{BuildTargetKey(target, locator)}' could not be resolved during rewrite because the semantic identity did not match '{target.MemberId.Value}'.");
        }

        if (!string.Equals(candidate.ToString().Trim(), locator.DisplayText, StringComparison.Ordinal) &&
            target.TargetKind == ModelPrimitives.TargetKind.Statement)
        {
            return TargetResolutionResult.Failure($"Target '{BuildTargetKey(target, locator)}' could not be resolved during rewrite because the statement text did not match '{locator.DisplayText}'.");
        }

        return TargetResolutionResult.Success(candidate);
    }

    /// <summary>
    /// 判断语句是否属于指定成员。
    /// </summary>
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
    /// 判断方法声明是否匹配指定成员标识。
    /// </summary>
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
    /// 为方法匹配创建语义分析上下文。
    /// </summary>
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
    /// 将匹配到的方法候选映射回当前语法树。
    /// </summary>
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
    /// 根据解析键在语法树中查找节点。
    /// </summary>
    private static SyntaxNode? ResolveNodeByResolutionKey(SyntaxNode root, ModelPrimitives.TargetIdentity target, ModelPrimitives.TargetLocator locator)
    {
        var resolutionKey = locator.EffectiveResolutionKey;
        return target.TargetKind switch
        {
            ModelPrimitives.TargetKind.Statement => root.DescendantNodes()
                .OfType<StatementSyntax>()
                .FirstOrDefault(statement =>
                    statement.SpanStart == resolutionKey.SpanStart &&
                    statement.Span.Length == resolutionKey.SpanLength),
            ModelPrimitives.TargetKind.Field => root.DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .FirstOrDefault(variable =>
                    variable.SpanStart == resolutionKey.SpanStart &&
                    variable.Span.Length == resolutionKey.SpanLength),
            ModelPrimitives.TargetKind.Property => root.DescendantNodes()
                .OfType<PropertyDeclarationSyntax>()
                .FirstOrDefault(property =>
                    property.SpanStart == resolutionKey.SpanStart &&
                    property.Span.Length == resolutionKey.SpanLength),
            ModelPrimitives.TargetKind.Class => root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(@class =>
                    @class.SpanStart == resolutionKey.SpanStart &&
                    @class.Span.Length == resolutionKey.SpanLength),
            ModelPrimitives.TargetKind.Method => EnumerateMethodLikeDeclarations(root, target.MemberKind)
                .FirstOrDefault(node =>
                    node.SpanStart == resolutionKey.SpanStart &&
                    node.Span.Length == resolutionKey.SpanLength),
            _ => null
        };
    }

    /// <summary>
    /// 枚举与方法类似的声明节点。
    /// </summary>
    private static IEnumerable<SyntaxNode> EnumerateMethodLikeDeclarations(SyntaxNode root, ModelPrimitives.MemberKind memberKind)
    {
        if (memberKind == ModelPrimitives.MemberKind.Accessor)
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
    /// 判断已解析节点的语义身份是否匹配目标。
    /// </summary>
    private static bool SemanticIdentityMatches(
        SyntaxNode root,
        SemanticModel? semanticModel,
        ModelPrimitives.TargetIdentity target,
        ModelPrimitives.TargetLocator locator,
        SyntaxNode resolvedNode)
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
    /// 尝试获取节点声明出的符号。
    /// </summary>
    private static ISymbol? TryGetDeclaredSymbol(SyntaxNode node, SemanticModel semanticModel, ModelPrimitives.TargetIdentity target)
    {
        return node switch
        {
            MethodDeclarationSyntax method => semanticModel.GetDeclaredSymbol(method),
            ConstructorDeclarationSyntax ctor => semanticModel.GetDeclaredSymbol(ctor),
            AccessorDeclarationSyntax accessor => semanticModel.GetDeclaredSymbol(accessor),
            ClassDeclarationSyntax @class => semanticModel.GetDeclaredSymbol(@class),
            PropertyDeclarationSyntax property when target.MemberKind == ModelPrimitives.MemberKind.Accessor => ResolveAccessorSymbol(property, semanticModel, target.MemberId.Value),
            VariableDeclaratorSyntax variable when variable.Parent?.Parent is FieldDeclarationSyntax => semanticModel.GetDeclaredSymbol(variable),
            PropertyDeclarationSyntax property => semanticModel.GetDeclaredSymbol(property),
            _ => null
        };
    }

    /// <summary>
    /// 从候选节点中解析出最终成员匹配结果。
    /// </summary>
    private static TargetResolutionResult ResolveMemberCandidate(
        ModelPrimitives.TargetIdentity target,
        ModelPrimitives.TargetLocator locator,
        IReadOnlyList<SyntaxNode> candidates,
        string label)
    {
        if (candidates.Count == 0)
        {
            return TargetResolutionResult.Failure($"Target '{BuildTargetKey(target, locator)}' could not be resolved during rewrite because the {label} '{target.MemberId.Value}' was not found.");
        }

        var spanCandidates = candidates
            .Where(node => node.SpanStart == locator.SpanStart && node.Span.Length == locator.SpanLength)
            .ToArray();

        if (spanCandidates.Length == 0)
        {
            return TargetResolutionResult.Failure($"Target '{BuildTargetKey(target, locator)}' could not be resolved during rewrite because the span did not match the {label} '{target.MemberId.Value}'.");
        }

        if (spanCandidates.Length == 1)
        {
            return TargetResolutionResult.Success(spanCandidates[0]);
        }

        var textMatch = spanCandidates.FirstOrDefault(node => string.Equals(node.ToString().Trim(), locator.DisplayText, StringComparison.Ordinal));
        if (textMatch == null)
        {
            return TargetResolutionResult.Failure($"Target '{BuildTargetKey(target, locator)}' could not be resolved during rewrite because the {label} text did not match '{locator.DisplayText}'.");
        }

        return TargetResolutionResult.Success(textMatch);
    }

    /// <summary>
    /// 从成员标识中提取成员名称。
    /// </summary>
    private static string GetMemberName(string memberId)
    {
        var parameterStart = memberId.IndexOf('(');
        var end = parameterStart >= 0 ? parameterStart : memberId.Length;
        var lastDot = memberId.LastIndexOf('.', end - 1, end);
        if (lastDot < 0 || lastDot + 1 >= end)
        {
            return memberId;
        }

        return memberId.Substring(lastDot + 1, end - lastDot - 1);
    }

    /// <summary>
    /// 根据成员标识解析属性访问器符号。
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
    /// 判断类型是否包含错误类型。
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
    /// 获取重写阶段所需的元数据引用。
    /// </summary>
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
    /// 构建方法成员标识。
    /// </summary>
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
    /// 构建通用成员标识。
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
    /// 构建语义类型名称。
    /// </summary>
    private static string BuildSemanticTypeName(INamedTypeSymbol? typeSymbol)
    {
        return typeSymbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? "Unknown";
    }

    /// <summary>
    /// 构建语义参数列表。
    /// </summary>
    private static string BuildSemanticParameterList(ImmutableArray<IParameterSymbol> parameters)
    {
        return string.Join(", ", parameters.Select(parameter =>
            parameter.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
    }

    /// <summary>
    /// 判断类型声明是否匹配指定类标识。
    /// </summary>
    private static bool ClassMatches(ClassDeclarationSyntax classDeclaration, string classId)
    {
        return string.Equals(classId, GetContainingTypeName(classDeclaration), StringComparison.Ordinal);
    }

    /// <summary>
    /// 获取成员所属类型的完整名称。
    /// </summary>
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
    /// 构建参数列表文本。
    /// </summary>
    private static string BuildParameterList(ParameterListSyntax parameterList)
    {
        return string.Join(", ", parameterList.Parameters.Select(parameter => parameter.Type?.ToString() ?? "object"));
    }

    /// <summary>
    /// 规范化参数列表语法节点。
    /// </summary>
    private static string NormalizeParameterList(ParameterListSyntax parameterList)
    {
        return string.Join(",", parameterList.Parameters.Select(parameter => NormalizeTypeSyntax(parameter.Type)));
    }

    /// <summary>
    /// 规范化参数列表文本。
    /// </summary>
    private static string NormalizeParameterList(string parameterList)
    {
        if (string.IsNullOrWhiteSpace(parameterList))
        {
            return string.Empty;
        }

        return string.Join(",", SplitParameterList(parameterList).Select(NormalizeTypeName));
    }

    /// <summary>
    /// 拆分参数列表文本。
    /// </summary>
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
    /// 规范化类型名称文本。
    /// </summary>
    private static string NormalizeTypeName(string typeName)
    {
        var parsedType = SyntaxFactory.ParseTypeName(typeName.Replace("global::", string.Empty, StringComparison.Ordinal));
        return NormalizeTypeSyntax(parsedType);
    }

    /// <summary>
    /// 规范化类型语法节点。
    /// </summary>
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
    /// 表示目标解析结果。
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
    /// 表示方法匹配阶段使用的语义上下文。
    /// </summary>
    private sealed record MethodSemanticMatchContext(SyntaxNode Root, SemanticModel? SemanticModel);

    /// <summary>
    /// 表示已绑定的重写计划。
    /// </summary>
    private sealed record BoundRewritePlan(
        IReadOnlyList<BoundPlannedChange> Changes);

    /// <summary>
    /// 表示已绑定到原始节点的计划变更。
    /// </summary>
    private sealed record BoundPlannedChange(
        ModelPlanning.PlannedChange Change,
        SyntaxNode OriginalNode);

    /// <summary>
    /// 表示重写绑定结果。
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
}



