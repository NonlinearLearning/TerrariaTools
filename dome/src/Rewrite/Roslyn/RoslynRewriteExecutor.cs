using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome.Rewrite.Roslyn;

using TerrariaTools.Dome.Core;

/// <summary>
/// Roslyn 閲嶅啓鎵ц鍣ㄣ€?
/// </summary>
public sealed class RoslynRewriteExecutor : IRewriteExecutor
{
    /// <summary>
    /// 寮傛鎵ц閲嶅啓璁″垝銆?
    /// </summary>
    /// <param name="source">婧愪唬鐮併€?/param>
    /// <param name="plan">瀹¤璁″垝銆?/param>
    /// <param name="cancellationToken">鍙栨秷浠ょ墝銆?/param>
    /// <returns>閲嶅啓鎵ц缁撴灉銆?/returns>
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
    /// 寮傛鎵ц甯﹁涔変笂涓嬫枃鐨勯噸鍐欒鍒掋€?
    /// </summary>
    /// <param name="documentContext">閲嶅啓鏂囨。涓婁笅鏂囥€?/param>
    /// <param name="plan">瀹¤璁″垝銆?/param>
    /// <param name="cancellationToken">鍙栨秷浠ょ墝銆?/param>
    /// <returns>閲嶅啓鎵ц缁撴灉銆?/returns>
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
    /// 灏嗚鍒掑彉鏇寸粦瀹氬埌褰撳墠璇硶鏍戣妭鐐广€?
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
    /// 鏌ユ壘鐩爣鑺傜偣銆?
    /// </summary>
    /// <param name="root">璇硶鏍硅妭鐐广€?/param>
    /// <param name="target">璁″垝鐩爣銆?/param>
    /// <returns>鐩爣瑙ｆ瀽缁撴灉銆?/returns>
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

        if (target.TargetKind == TargetKind.Field)
        {
            var fieldCandidates = root.DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .Where(variable => string.Equals(variable.Identifier.ValueText, GetMemberName(target.MemberId.Value), StringComparison.Ordinal))
                .Cast<SyntaxNode>()
                .ToArray();

            return ResolveMemberCandidate(target, fieldCandidates, "field");
        }

        if (target.TargetKind == TargetKind.Property)
        {
            var propertyCandidates = root.DescendantNodes()
                .OfType<PropertyDeclarationSyntax>()
                .Where(property => string.Equals(property.Identifier.ValueText, GetMemberName(target.MemberId.Value), StringComparison.Ordinal))
                .Cast<SyntaxNode>()
                .ToArray();

            return ResolveMemberCandidate(target, propertyCandidates, "property");
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
    /// 浣跨敤绋冲畾瑙ｆ瀽閿В鏋愮洰鏍囪妭鐐广€?
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
    /// 妫€鏌ヨ鍙ユ槸鍚﹀尮閰嶆垚鍛?ID銆?
    /// </summary>
    /// <param name="statement">璇彞璇硶鑺傜偣銆?/param>
    /// <param name="memberId">鎴愬憳 ID銆?/param>
    /// <returns>濡傛灉鍖归厤鍒欒繑鍥?true锛屽惁鍒欒繑鍥?false銆?/returns>
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
    /// 妫€鏌ユ柟娉曟槸鍚﹀尮閰嶆垚鍛?ID銆?
    /// </summary>
    /// <param name="method">鏂规硶澹版槑璇硶鑺傜偣銆?/param>
    /// <param name="memberId">鎴愬憳 ID銆?/param>
    /// <returns>濡傛灉鍖归厤鍒欒繑鍥?true锛屽惁鍒欒繑鍥?false銆?/returns>
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
    /// 涓烘柟娉曞尮閰嶅垱寤鸿涔変笂涓嬫枃銆?
    /// </summary>
    /// <param name="root">褰撳墠璇硶鏍硅妭鐐广€?/param>
    /// <returns>鏂规硶鍖归厤涓婁笅鏂囥€?/returns>
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
    /// 灏嗚涔夊尮閰嶅緱鍒扮殑鏂规硶鑺傜偣瑙ｆ瀽鍥炲綋鍓嶆牴鑺傜偣涓殑瀵瑰簲鑺傜偣銆?
    /// </summary>
    /// <param name="currentRoot">褰撳墠鏍硅妭鐐广€?/param>
    /// <param name="matchedMethod">鍖归厤鍒扮殑鏂规硶鑺傜偣銆?/param>
    /// <returns>褰撳墠鏍硅妭鐐逛腑鐨勫搴旀柟娉曡妭鐐广€?/returns>
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
    /// 鏍规嵁绋冲畾瑙ｆ瀽閿В鏋愬綋鍓嶆牴鑺傜偣涓殑鐩爣鑺傜偣銆?
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
            TargetKind.Field => root.DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .FirstOrDefault(variable =>
                    variable.SpanStart == resolutionKey.SpanStart &&
                    variable.Span.Length == resolutionKey.SpanLength),
            TargetKind.Property => root.DescendantNodes()
                .OfType<PropertyDeclarationSyntax>()
                .FirstOrDefault(property =>
                    property.SpanStart == resolutionKey.SpanStart &&
                    property.Span.Length == resolutionKey.SpanLength),
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
    /// 鏋氫妇鏂规硶绫荤洰鏍囧搴旂殑澹版槑鑺傜偣銆?
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
    /// 浣跨敤鍘熷璇箟涓婁笅鏂囨牎楠岀ǔ瀹氳В鏋愰敭瀵瑰簲鐨勮涔夋爣璇嗐€?
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
    /// 鑾峰彇鐩爣鑺傜偣鐨勫０鏄庣鍙枫€?
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
            VariableDeclaratorSyntax variable when variable.Parent?.Parent is FieldDeclarationSyntax => semanticModel.GetDeclaredSymbol(variable),
            PropertyDeclarationSyntax property => semanticModel.GetDeclaredSymbol(property),
            _ => null
        };
    }

    private static TargetResolutionResult ResolveMemberCandidate(PlanTarget target, IReadOnlyList<SyntaxNode> candidates, string label)
    {
        if (candidates.Count == 0)
        {
            return TargetResolutionResult.Failure($"Target '{target.TargetKey}' could not be resolved during rewrite because the {label} '{target.MemberId.Value}' was not found.");
        }

        var spanCandidates = candidates
            .Where(node => node.SpanStart == target.SpanStart && node.Span.Length == target.SpanLength)
            .ToArray();

        if (spanCandidates.Length == 0)
        {
            return TargetResolutionResult.Failure($"Target '{target.TargetKey}' could not be resolved during rewrite because the span did not match the {label} '{target.MemberId.Value}'.");
        }

        if (spanCandidates.Length == 1)
        {
            return TargetResolutionResult.Success(spanCandidates[0]);
        }

        var textMatch = spanCandidates.FirstOrDefault(node => string.Equals(node.ToString().Trim(), target.DisplayText, StringComparison.Ordinal));
        if (textMatch == null)
        {
            return TargetResolutionResult.Failure($"Target '{target.TargetKey}' could not be resolved during rewrite because the {label} text did not match '{target.DisplayText}'.");
        }

        return TargetResolutionResult.Success(textMatch);
    }

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
    /// 瑙ｆ瀽灞炴€х洰鏍囧搴旂殑璁块棶鍣ㄧ鍙枫€?
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
    /// 鍒ゆ柇绗﹀彿鏄惁鍖呭惈鏈В鏋愮被鍨嬨€?
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
    /// 鍒ゆ柇绫诲瀷绗﹀彿鏄惁鏈В鏋愩€?
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
    /// 鑾峰彇 rewrite 瑙ｆ瀽鎵€闇€鐨勫厓鏁版嵁寮曠敤銆?
    /// </summary>
    /// <returns>鍏冩暟鎹紩鐢ㄩ泦鍚堛€?/returns>
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
    /// 鏋勫缓鏂规硶鎴愬憳 ID銆?
    /// </summary>
    /// <param name="symbol">鏂规硶绗﹀彿銆?/param>
    /// <returns>鎴愬憳 ID 瀛楃涓层€?/returns>
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
    /// 鏋勫缓澹版槑绗﹀彿鎴愬憳 ID銆?
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
    /// 鏋勫缓璇箟绫诲瀷鍚嶃€?
    /// </summary>
    /// <param name="typeSymbol">绫诲瀷绗﹀彿銆?/param>
    /// <returns>绫诲瀷鍚嶅瓧绗︿覆銆?/returns>
    private static string BuildSemanticTypeName(INamedTypeSymbol? typeSymbol)
    {
        return typeSymbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? "Unknown";
    }

    /// <summary>
    /// 鏋勫缓璇箟鍙傛暟鍒楄〃瀛楃涓层€?
    /// </summary>
    /// <param name="parameters">鍙傛暟绗﹀彿闆嗗悎銆?/param>
    /// <returns>鍙傛暟鍒楄〃瀛楃涓层€?/returns>
    private static string BuildSemanticParameterList(ImmutableArray<IParameterSymbol> parameters)
    {
        return string.Join(", ", parameters.Select(parameter =>
            parameter.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
    }

    /// <summary>
    /// 妫€鏌ョ被鏄惁鍖归厤绫?ID銆?
    /// </summary>
    /// <param name="classDeclaration">绫诲０鏄庤娉曡妭鐐广€?/param>
    /// <param name="classId">绫?ID銆?/param>
    /// <returns>濡傛灉鍖归厤鍒欒繑鍥?true锛屽惁鍒欒繑鍥?false銆?/returns>
    private static bool ClassMatches(ClassDeclarationSyntax classDeclaration, string classId)
    {
        return string.Equals(classId, GetContainingTypeName(classDeclaration), StringComparison.Ordinal);
    }

    /// <summary>
    /// 鑾峰彇鍖呭惈绫诲瀷鍚嶇О銆?
    /// </summary>
    /// <param name="member">鎴愬憳澹版槑璇硶鑺傜偣銆?/param>
    /// <returns>鍖呭惈绫诲瀷鍚嶇О銆?/returns>
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
    /// 鏋勫缓鍙傛暟鍒楄〃瀛楃涓层€?
    /// </summary>
    /// <param name="parameterList">鍙傛暟鍒楄〃璇硶鑺傜偣銆?/param>
    /// <returns>鍙傛暟鍒楄〃瀛楃涓层€?/returns>
    private static string BuildParameterList(ParameterListSyntax parameterList)
    {
        return string.Join(", ", parameterList.Parameters.Select(parameter => parameter.Type?.ToString() ?? "object"));
    }

    /// <summary>
    /// 瑙勮寖鍖栧弬鏁板垪琛ㄨ娉曡妭鐐广€?
    /// </summary>
    /// <param name="parameterList">鍙傛暟鍒楄〃璇硶鑺傜偣銆?/param>
    /// <returns>瑙勮寖鍖栧悗鐨勫弬鏁板垪琛ㄥ瓧绗︿覆銆?/returns>
    private static string NormalizeParameterList(ParameterListSyntax parameterList)
    {
        return string.Join(",", parameterList.Parameters.Select(parameter => NormalizeTypeSyntax(parameter.Type)));
    }

    /// <summary>
    /// 瑙勮寖鍖栧弬鏁板垪琛ㄥ瓧绗︿覆銆?
    /// </summary>
    /// <param name="parameterList">鍙傛暟鍒楄〃瀛楃涓层€?/param>
    /// <returns>瑙勮寖鍖栧悗鐨勫弬鏁板垪琛ㄥ瓧绗︿覆銆?/returns>
    private static string NormalizeParameterList(string parameterList)
    {
        if (string.IsNullOrWhiteSpace(parameterList))
        {
            return string.Empty;
        }

        return string.Join(",", SplitParameterList(parameterList).Select(NormalizeTypeName));
    }

    /// <summary>
    /// 鎷嗗垎鍙傛暟鍒楄〃瀛楃涓层€?
    /// </summary>
    /// <param name="parameterList">鍙傛暟鍒楄〃瀛楃涓层€?/param>
    /// <returns>鍙傛暟绫诲瀷瀛楃涓插簭鍒椼€?/returns>
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
    /// 瑙勮寖鍖栫被鍨嬪悕绉板瓧绗︿覆銆?
    /// </summary>
    /// <param name="typeName">绫诲瀷鍚嶇О瀛楃涓层€?/param>
    /// <returns>瑙勮寖鍖栧悗鐨勭被鍨嬪悕绉般€?/returns>
    private static string NormalizeTypeName(string typeName)
    {
        var parsedType = SyntaxFactory.ParseTypeName(typeName.Replace("global::", string.Empty, StringComparison.Ordinal));
        return NormalizeTypeSyntax(parsedType);
    }

    /// <summary>
    /// 瑙勮寖鍖栫被鍨嬭娉曡妭鐐广€?
    /// </summary>
    /// <param name="typeSyntax">绫诲瀷璇硶鑺傜偣銆?/param>
    /// <returns>瑙勮寖鍖栧悗鐨勭被鍨嬪悕绉般€?/returns>
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
    /// 娉ㄩ噴鎺夎鍙ャ€?
    /// </summary>
    /// <param name="statement">璇彞璇硶鑺傜偣銆?/param>
    /// <param name="change">璁″垝鏇存敼銆?/param>
    /// <returns>娉ㄩ噴鍚庣殑璇彞璇硶鑺傜偣銆?/returns>
    private static StatementSyntax CommentOut(StatementSyntax statement, PlannedChange change)
    {
        return SyntaxFactory.EmptyStatement()
            .WithLeadingTrivia(
                SyntaxFactory.Comment($"// {change.Reason.RuleId}: {statement.ToString().Trim()}"),
                SyntaxFactory.CarriageReturnLineFeed);
    }

    /// <summary>
    /// 浣跨敤榛樿鍊兼浛鎹㈣鍙ャ€?
    /// </summary>
    /// <param name="statement">璇彞璇硶鑺傜偣銆?/param>
    /// <param name="change">璁″垝鏇存敼銆?/param>
    /// <returns>鏇挎崲鍚庣殑璇彞璇硶鑺傜偣銆?/returns>
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
    /// 娣诲姞杩斿洖璇彞銆?
    /// </summary>
    /// <param name="statement">璇彞璇硶鑺傜偣銆?/param>
    /// <param name="change">璁″垝鏇存敼銆?/param>
    /// <returns>娣诲姞杩斿洖璇彞鍚庣殑璇彞璇硶鑺傜偣銆?/returns>
    private static StatementSyntax AddReturn(StatementSyntax statement, PlannedChange change)
    {
        var payload = change.Action.Payload;
        var returnStatement = string.IsNullOrWhiteSpace(payload)
            ? "return;"
            : $"return {payload};";
        return SyntaxFactory.ParseStatement(returnStatement)
            .WithTriviaFrom(statement);
    }

    private static MethodDeclarationSyntax AddReturn(MethodDeclarationSyntax method, PlannedChange change)
    {
        var payload = change.Action.Payload;
        var returnStatement = string.IsNullOrWhiteSpace(payload)
            ? "return;"
            : $"return {payload};";
        var statement = SyntaxFactory.ParseStatement(returnStatement);
        var body = method.Body ?? SyntaxFactory.Block();
        return method.WithBody(body.AddStatements(statement)).WithExpressionBody(null).WithSemicolonToken(default);
    }

    /// <summary>
    /// 搴旂敤鏇存敼銆?
    /// </summary>
    /// <param name="root">璇硶鏍硅妭鐐广€?/param>
    /// <param name="node">鐩爣鑺傜偣銆?/param>
    /// <param name="change">璁″垝鏇存敼銆?/param>
    /// <returns>鏇存敼搴旂敤缁撴灉銆?/returns>
    private static ApplyChangeResult ApplyChange(SyntaxNode root, SyntaxNode node, PlannedChange change)
    {
        try
        {
            var updatedRoot = change.Target.TargetKind switch
            {
                TargetKind.Class => ApplyClassChange(root, node, change),
                TargetKind.Method => ApplyMethodChange(root, node, change),
                TargetKind.Field => ApplyFieldChange(root, node, change),
                TargetKind.Property => ApplyPropertyChange(root, node, change),
                _ => ApplyStatementChange(root, node, change)
            };

            return ApplyChangeResult.Success(updatedRoot);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return ApplyChangeResult.Failure(ex.Message);
        }
    }

    private static SyntaxNode ApplyClassChange(SyntaxNode root, SyntaxNode node, PlannedChange change)
    {
        if (node is not ClassDeclarationSyntax classNode)
        {
            throw new InvalidOperationException($"Action '{change.Action.Kind}' is unsupported for target '{change.Target.TargetKey}' because the target is not a class.");
        }

        return change.Action.Kind switch
        {
            PlanActionKind.Delete => root.RemoveNode(node, SyntaxRemoveOptions.KeepNoTrivia)
                                     ?? throw new InvalidOperationException($"Action '{PlanActionKind.Delete}' invalidated target '{change.Target.TargetKey}'."),
            PlanActionKind.ReorderPublicMethods => root.ReplaceNode(node, ReorderPublicMethods(classNode)),
            _ => throw new InvalidOperationException($"Action '{change.Action.Kind}' is unsupported for class target '{change.Target.TargetKey}'.")
        };
    }

    private static SyntaxNode ApplyMethodChange(SyntaxNode root, SyntaxNode node, PlannedChange change)
    {
        return change.Action.Kind switch
        {
            PlanActionKind.Delete => root.RemoveNode(node, SyntaxRemoveOptions.KeepNoTrivia)
                                     ?? throw new InvalidOperationException($"Action '{PlanActionKind.Delete}' invalidated target '{change.Target.TargetKey}'."),
            PlanActionKind.AddReturn when node is MethodDeclarationSyntax methodNode => root.ReplaceNode(node, AddReturn(methodNode, change)),
            PlanActionKind.ChangeVisibilityToPrivate when node is MethodDeclarationSyntax methodNode => root.ReplaceNode(node, ChangeVisibilityToPrivate(methodNode)),
            _ => throw new InvalidOperationException($"Action '{change.Action.Kind}' is unsupported for method target '{change.Target.TargetKey}'.")
        };
    }

    private static SyntaxNode ApplyFieldChange(SyntaxNode root, SyntaxNode node, PlannedChange change)
    {
        if (change.Action.Kind != PlanActionKind.Delete || node is not VariableDeclaratorSyntax variable)
        {
            throw new InvalidOperationException($"Action '{change.Action.Kind}' is unsupported for field target '{change.Target.TargetKey}'.");
        }

        if (variable.Parent is not VariableDeclarationSyntax declaration || declaration.Parent is not FieldDeclarationSyntax fieldDeclaration)
        {
            throw new InvalidOperationException($"Action '{change.Action.Kind}' is unsupported for field target '{change.Target.TargetKey}' because the target is not a field declarator.");
        }

        if (declaration.Variables.Count == 1)
        {
            return root.RemoveNode(fieldDeclaration, SyntaxRemoveOptions.KeepNoTrivia)
                   ?? throw new InvalidOperationException($"Action '{PlanActionKind.Delete}' invalidated target '{change.Target.TargetKey}'.");
        }

        return root.ReplaceNode(declaration, declaration.WithVariables(declaration.Variables.Remove(variable)));
    }

    private static SyntaxNode ApplyPropertyChange(SyntaxNode root, SyntaxNode node, PlannedChange change)
    {
        if (change.Action.Kind != PlanActionKind.Delete || node is not PropertyDeclarationSyntax)
        {
            throw new InvalidOperationException($"Action '{change.Action.Kind}' is unsupported for property target '{change.Target.TargetKey}'.");
        }

        return root.RemoveNode(node, SyntaxRemoveOptions.KeepNoTrivia)
               ?? throw new InvalidOperationException($"Action '{PlanActionKind.Delete}' invalidated target '{change.Target.TargetKey}'.");
    }

    private static SyntaxNode ApplyStatementChange(SyntaxNode root, SyntaxNode node, PlannedChange change)
    {
        if (node is not StatementSyntax statementNode)
        {
            throw new InvalidOperationException($"Action '{change.Action.Kind}' is unsupported for target '{change.Target.TargetKey}' because the target is not a statement.");
        }

        return change.Action.Kind switch
        {
            PlanActionKind.Delete => root.RemoveNode(node, SyntaxRemoveOptions.KeepNoTrivia)
                                     ?? throw new InvalidOperationException($"Action '{PlanActionKind.Delete}' invalidated target '{change.Target.TargetKey}'."),
            PlanActionKind.CommentOut => root.ReplaceNode(node, CommentOut(statementNode, change)),
            PlanActionKind.ReplaceWithDefault => root.ReplaceNode(node, ReplaceWithDefault(statementNode, change)),
            PlanActionKind.AddReturn => root.ReplaceNode(node, AddReturn(statementNode, change)),
            _ => throw new InvalidOperationException($"Action '{change.Action.Kind}' is unsupported for target '{change.Target.TargetKey}'.")
        };
    }

    private static MethodDeclarationSyntax ChangeVisibilityToPrivate(MethodDeclarationSyntax method)
    {
        var rewrittenModifiers = new List<SyntaxToken>(method.Modifiers.Count);
        var replacedAccessModifier = false;

        foreach (var modifier in method.Modifiers)
        {
            if (modifier.IsKind(SyntaxKind.PublicKeyword) ||
                modifier.IsKind(SyntaxKind.PrivateKeyword) ||
                modifier.IsKind(SyntaxKind.ProtectedKeyword) ||
                modifier.IsKind(SyntaxKind.InternalKeyword))
            {
                if (replacedAccessModifier)
                {
                    continue;
                }

                rewrittenModifiers.Add(
                    SyntaxFactory.Token(
                        modifier.LeadingTrivia,
                        SyntaxKind.PrivateKeyword,
                        SyntaxFactory.TriviaList(SyntaxFactory.Space)));
                replacedAccessModifier = true;
                continue;
            }

            rewrittenModifiers.Add(modifier);
        }

        if (!replacedAccessModifier)
        {
            rewrittenModifiers.Insert(
                0,
                SyntaxFactory.Token(
                    method.GetLeadingTrivia(),
                    SyntaxKind.PrivateKeyword,
                    SyntaxFactory.TriviaList(SyntaxFactory.Space)));
        }

        return method.WithModifiers(SyntaxFactory.TokenList(rewrittenModifiers));
    }

    private static ClassDeclarationSyntax ReorderPublicMethods(ClassDeclarationSyntax classNode)
    {
        var ordinaryMethods = classNode.Members.OfType<MethodDeclarationSyntax>().Where(static method => !method.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.StaticKeyword))).ToArray();
        if (ordinaryMethods.Length < 2)
        {
            return classNode;
        }

        var orderedMethods = ordinaryMethods
            .Where(IsPublicMethod)
            .OrderBy(static method => method.Identifier.ValueText, StringComparer.Ordinal)
            .ThenBy(static method => method.ParameterList.Parameters.Count)
            .Concat(ordinaryMethods.Where(static method => !IsPublicMethod(method)))
            .ToArray();

        var originalMethods = new HashSet<MethodDeclarationSyntax>(ordinaryMethods, ReferenceEqualityComparer.Instance);
        var queue = new Queue<MethodDeclarationSyntax>(orderedMethods);
        var rewrittenMembers = classNode.Members.Select(member => member is MethodDeclarationSyntax method && originalMethods.Contains(method) ? (MemberDeclarationSyntax)queue.Dequeue() : member);
        return classNode.WithMembers(SyntaxFactory.List(rewrittenMembers));
    }

    private static bool IsPublicMethod(MethodDeclarationSyntax method)
        => method.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PublicKeyword));

    private static int GetActionPriority(PlanActionKind kind)
        => kind == PlanActionKind.ReorderPublicMethods ? 1 : 0;

    /// <summary>
    /// 鐩爣瑙ｆ瀽缁撴灉璁板綍銆?
    /// </summary>
    private sealed record TargetResolutionResult(bool IsSuccess, SyntaxNode? Node, string Message)
    {
        /// <summary>
        /// 鍒涘缓鎴愬姛缁撴灉銆?
        /// </summary>
        public static TargetResolutionResult Success(SyntaxNode node) => new(true, node, string.Empty);

        /// <summary>
        /// 鍒涘缓澶辫触缁撴灉銆?
        /// </summary>
        public static TargetResolutionResult Failure(string message) => new(false, null, message);
    }

    /// <summary>
    /// 鏂规硶璇箟鍖归厤涓婁笅鏂囥€?
    /// </summary>
    private sealed record MethodSemanticMatchContext(SyntaxNode Root, SemanticModel? SemanticModel);

    /// <summary>
    /// 缁戝畾鍚庣殑閲嶅啓璁″垝銆?
    /// </summary>
    private sealed record BoundRewritePlan(
        IReadOnlyList<BoundPlannedChange> Changes);

    /// <summary>
    /// 缁戝畾鍚庣殑鍗曟潯璁″垝鍙樻洿銆?
    /// </summary>
    private sealed record BoundPlannedChange(
        PlannedChange Change,
        SyntaxNode OriginalNode);

    /// <summary>
    /// 閲嶅啓缁戝畾缁撴灉璁板綍銆?
    /// </summary>
    private sealed record RewriteBindingResult(
        bool IsSuccess,
        BoundRewritePlan? Plan,
        string Message)
    {
        /// <summary>
        /// 鍒涘缓鎴愬姛缁撴灉銆?
        /// </summary>
        public static RewriteBindingResult Success(BoundRewritePlan plan) => new(true, plan, string.Empty);

        /// <summary>
        /// 鍒涘缓澶辫触缁撴灉銆?
        /// </summary>
        public static RewriteBindingResult Failure(string message) => new(false, null, message);
    }

    /// <summary>
    /// 鏇存敼搴旂敤缁撴灉璁板綍銆?
    /// </summary>
    private sealed record ApplyChangeResult(bool IsSuccess, SyntaxNode? Root, string Message)
    {
        /// <summary>
        /// 鍒涘缓鎴愬姛缁撴灉銆?
        /// </summary>
        public static ApplyChangeResult Success(SyntaxNode root) => new(true, root, string.Empty);

        /// <summary>
        /// 鍒涘缓澶辫触缁撴灉銆?
        /// </summary>
        public static ApplyChangeResult Failure(string message) => new(false, null, message);
    }
}


