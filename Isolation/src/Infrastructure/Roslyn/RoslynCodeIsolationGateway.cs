using System.Collections.Immutable;
using Domain.Rewrite;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Infrastructure.Roslyn;

/// <summary>
/// 基于 Roslyn 的代码隔离实现。
/// </summary>
public sealed class RoslynCodeIsolationGateway : ICodeIsolationGateway
{
    private static readonly Lazy<IReadOnlyList<MetadataReference>> MetadataReferences = new(BuildMetadataReferences);

    /// <inheritdoc />
    public CodeRewriteResult DeleteClass(string sourceCode, string className)
    {
        DocumentContext context = CreateContext(sourceCode);
        ClassDeclarationSyntax classNode = FindClass(context.Root, className);
        SyntaxNode newRoot = context.Root.RemoveNode(classNode, SyntaxRemoveOptions.KeepNoTrivia)
            ?? throw new InvalidOperationException($"删除类型失败：{className}");
        return CreateRewriteResult(CodeRewriteKind.DeleteClass, className, newRoot.NormalizeWhitespace().ToFullString(), true);
    }

    /// <inheritdoc />
    public CodeRewriteResult DeleteMethod(string sourceCode, string className, string methodName, int? parameterCount)
    {
        DocumentContext context = CreateContext(sourceCode);
        MethodDeclarationSyntax methodNode = FindMethod(context.Root, className, methodName, parameterCount);
        SyntaxNode newRoot = context.Root.RemoveNode(methodNode, SyntaxRemoveOptions.KeepNoTrivia)
            ?? throw new InvalidOperationException($"删除方法失败：{className}.{methodName}");
        return CreateRewriteResult(
            CodeRewriteKind.DeleteMethod,
            $"{className}.{methodName}",
            newRoot.NormalizeWhitespace().ToFullString(),
            true);
    }

    /// <inheritdoc />
    public CodeRewriteResult PrivatizeMethod(string sourceCode, string className, string methodName, int? parameterCount)
    {
        DocumentContext context = CreateContext(sourceCode);
        MethodDeclarationSyntax methodNode = FindMethod(context.Root, className, methodName, parameterCount);
        SyntaxTokenList modifiers = methodNode.Modifiers;
        modifiers = new SyntaxTokenList(modifiers.Where(token => !IsAccessibilityToken(token)));

        if (!modifiers.Any(token => token.IsKind(SyntaxKind.PrivateKeyword)))
        {
            modifiers = modifiers.Insert(0, SyntaxFactory.Token(SyntaxKind.PrivateKeyword));
        }

        MethodDeclarationSyntax newMethodNode = methodNode.WithModifiers(modifiers);
        SyntaxNode newRoot = context.Root.ReplaceNode(methodNode, newMethodNode);
        return CreateRewriteResult(
            CodeRewriteKind.PrivatizeMethod,
            $"{className}.{methodName}",
            newRoot.NormalizeWhitespace().ToFullString(),
            true);
    }

    /// <inheritdoc />
    public CodeRewriteResult ClearMethodBody(string sourceCode, string className, string methodName, int? parameterCount)
    {
        DocumentContext context = CreateContext(sourceCode);
        MethodDeclarationSyntax methodNode = FindMethod(context.Root, className, methodName, parameterCount);
        MethodDeclarationSyntax newMethodNode = CreateClearedMethod(methodNode);
        SyntaxNode newRoot = context.Root.ReplaceNode(methodNode, newMethodNode);
        return CreateRewriteResult(
            CodeRewriteKind.ClearMethodBody,
            $"{className}.{methodName}",
            newRoot.NormalizeWhitespace().ToFullString(),
            true);
    }

    /// <inheritdoc />
    public MemberSlice BuildMemberSlice(string sourceCode, string className, string methodName, int? parameterCount)
    {
        DocumentContext context = CreateContext(sourceCode);
        SliceResult sliceResult = BuildSliceResult(context, className, methodName, parameterCount);
        string sliceSource = RenderClassDocument(context.Root, sliceResult.ClassNode, sliceResult.Members, sliceResult.ClassNode.Identifier.ValueText);
        MemberSlice memberSlice = MemberSlice.Create(className, methodName, sliceSource);

        foreach (string memberName in sliceResult.MemberNames)
        {
            memberSlice.AddMember(memberName);
        }

        return memberSlice;
    }

    /// <inheritdoc />
    public ShadowClass GenerateShadowClass(string sourceCode, string className, string methodName, int? parameterCount)
    {
        DocumentContext context = CreateContext(sourceCode);
        SliceResult sliceResult = BuildSliceResult(context, className, methodName, parameterCount);
        string shadowClassName = $"{sliceResult.ClassNode.Identifier.ValueText}Shadow";
        string shadowSource = RenderClassDocument(context.Root, sliceResult.ClassNode, sliceResult.Members, shadowClassName);
        ShadowClass shadowClass = ShadowClass.Create(className, shadowClassName, shadowSource);

        foreach (string memberName in sliceResult.MemberNames)
        {
            shadowClass.AddMember(memberName);
        }

        return shadowClass;
    }

    /// <inheritdoc />
    public RuntimeClosure ExtractMinimalRuntimeClosure(string sourceCode, string className, string methodName, int? parameterCount)
    {
        DocumentContext context = CreateContext(sourceCode);
        SliceResult sliceResult = BuildSliceResult(context, className, methodName, parameterCount);
        string closureClassName = $"{sliceResult.ClassNode.Identifier.ValueText}RuntimeClosure";
        string closureSource = RenderClassDocument(context.Root, sliceResult.ClassNode, sliceResult.Members, closureClassName);
        RuntimeClosure runtimeClosure = RuntimeClosure.Create(className, methodName, closureClassName, closureSource);

        foreach (string memberName in sliceResult.MemberNames)
        {
            runtimeClosure.AddMember(memberName);
        }

        return runtimeClosure;
    }

    private static DocumentContext CreateContext(string sourceCode)
    {
        ArgumentNullException.ThrowIfNull(sourceCode);

        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "Isolation.Roslyn.CodeIsolation",
            syntaxTrees: new[] { syntaxTree },
            references: MetadataReferences.Value,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
        CompilationUnitSyntax root = syntaxTree.GetCompilationUnitRoot();
        return new DocumentContext(root, semanticModel);
    }

    private static ClassDeclarationSyntax FindClass(CompilationUnitSyntax root, string className)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);

        return root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(node => string.Equals(node.Identifier.ValueText, className, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"未找到类型：{className}");
    }

    private static MethodDeclarationSyntax FindMethod(
        CompilationUnitSyntax root,
        string className,
        string methodName,
        int? parameterCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        ClassDeclarationSyntax classNode = FindClass(root, className);

        return classNode.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(node =>
                string.Equals(node.Identifier.ValueText, methodName, StringComparison.Ordinal) &&
                (!parameterCount.HasValue || node.ParameterList.Parameters.Count == parameterCount.Value))
            ?? throw new InvalidOperationException($"未找到方法：{className}.{methodName}");
    }

    private static bool IsAccessibilityToken(SyntaxToken token)
    {
        return token.IsKind(SyntaxKind.PublicKeyword) ||
            token.IsKind(SyntaxKind.ProtectedKeyword) ||
            token.IsKind(SyntaxKind.InternalKeyword) ||
            token.IsKind(SyntaxKind.PrivateKeyword);
    }

    private static MethodDeclarationSyntax CreateClearedMethod(MethodDeclarationSyntax methodNode)
    {
        TypeSyntax returnType = methodNode.ReturnType;
        StatementSyntax bodyStatement = returnType is PredefinedTypeSyntax predefinedType &&
            predefinedType.Keyword.IsKind(SyntaxKind.VoidKeyword)
            ? SyntaxFactory.EmptyStatement()
            : SyntaxFactory.ReturnStatement(CreateDefaultExpression(returnType));

        BlockSyntax body = SyntaxFactory.Block(bodyStatement);
        ArrowExpressionClauseSyntax? expressionBody = null;
        SyntaxToken semicolonToken = default;

        return methodNode
            .WithBody(body)
            .WithExpressionBody(expressionBody)
            .WithSemicolonToken(semicolonToken);
    }

    private static ExpressionSyntax CreateDefaultExpression(TypeSyntax returnType)
    {
        if (returnType is RefTypeSyntax refTypeSyntax)
        {
            return CreateDefaultExpression(refTypeSyntax.Type);
        }

        if (returnType is PredefinedTypeSyntax predefinedType &&
            predefinedType.Keyword.IsKind(SyntaxKind.StringKeyword))
        {
            return SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
        }

        return SyntaxFactory.DefaultExpression(returnType.WithoutTrivia());
    }

    private static CodeRewriteResult CreateRewriteResult(
        CodeRewriteKind rewriteKind,
        string targetName,
        string sourceCode,
        bool changed)
    {
        CodeRewriteResult result = CodeRewriteResult.Create(rewriteKind, targetName, sourceCode, changed);
        result.AddDiagnostic("Roslyn rewrite completed.");
        return result;
    }

    private static IReadOnlyList<MetadataReference> BuildMetadataReferences()
    {
        string trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("未找到 TRUSTED_PLATFORM_ASSEMBLIES。");

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    }

    private static SliceResult BuildSliceResult(
        DocumentContext context,
        string className,
        string methodName,
        int? parameterCount)
    {
        ClassDeclarationSyntax classNode = FindClass(context.Root, className);
        MethodDeclarationSyntax rootMethod = FindMethod(context.Root, className, methodName, parameterCount);
        Dictionary<ISymbol, MemberDeclarationSyntax> memberBySymbol = BuildMemberMap(classNode, context.SemanticModel);

        IMethodSymbol rootMethodSymbol = context.SemanticModel.GetDeclaredSymbol(rootMethod)
            ?? throw new InvalidOperationException($"无法解析方法符号：{className}.{methodName}");

        HashSet<ISymbol> includedSymbols = new(SymbolEqualityComparer.Default);
        Queue<ISymbol> pendingSymbols = new();
        pendingSymbols.Enqueue(rootMethodSymbol);

        while (pendingSymbols.Count > 0)
        {
            ISymbol currentSymbol = pendingSymbols.Dequeue();
            if (!includedSymbols.Add(currentSymbol))
            {
                continue;
            }

            if (!memberBySymbol.TryGetValue(currentSymbol, out MemberDeclarationSyntax? memberNode))
            {
                continue;
            }

            foreach (ISymbol dependency in FindDependencies(memberNode, context.SemanticModel, classNode))
            {
                if (!includedSymbols.Contains(dependency))
                {
                    pendingSymbols.Enqueue(dependency);
                }
            }
        }

        List<MemberDeclarationSyntax> members = classNode.Members
            .Where(member =>
            {
                ISymbol? symbol = context.SemanticModel.GetDeclaredSymbol(member);
                return symbol != null && includedSymbols.Contains(symbol);
            })
            .ToList();

        List<string> memberNames = members.Select(GetMemberName).ToList();
        return new SliceResult(classNode, members, memberNames);
    }

    private static Dictionary<ISymbol, MemberDeclarationSyntax> BuildMemberMap(
        ClassDeclarationSyntax classNode,
        SemanticModel semanticModel)
    {
        Dictionary<ISymbol, MemberDeclarationSyntax> memberBySymbol = new(SymbolEqualityComparer.Default);

        foreach (MemberDeclarationSyntax member in classNode.Members)
        {
            ISymbol? symbol = semanticModel.GetDeclaredSymbol(member);
            if (symbol != null && !memberBySymbol.ContainsKey(symbol))
            {
                memberBySymbol.Add(symbol, member);
            }
        }

        return memberBySymbol;
    }

    private static IEnumerable<ISymbol> FindDependencies(
        MemberDeclarationSyntax memberNode,
        SemanticModel semanticModel,
        ClassDeclarationSyntax classNode)
    {
        foreach (SyntaxNode syntaxNode in memberNode.DescendantNodesAndSelf())
        {
            ISymbol? referencedSymbol = semanticModel.GetSymbolInfo(syntaxNode).Symbol;
            if (referencedSymbol == null)
            {
                continue;
            }

            ISymbol normalizedSymbol = referencedSymbol is IMethodSymbol methodSymbol
                ? methodSymbol.ReducedFrom ?? methodSymbol
                : referencedSymbol;

            if (normalizedSymbol.ContainingType == null ||
                !string.Equals(normalizedSymbol.ContainingType.Name, classNode.Identifier.ValueText, StringComparison.Ordinal))
            {
                continue;
            }

            yield return normalizedSymbol;
        }
    }

    private static string GetMemberName(MemberDeclarationSyntax member)
    {
        return member switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            FieldDeclarationSyntax field => string.Join(
                ",",
                field.Declaration.Variables.Select(variable => variable.Identifier.ValueText)),
            _ => member.Kind().ToString(),
        };
    }

    private static string RenderClassDocument(
        CompilationUnitSyntax root,
        ClassDeclarationSyntax originalClassNode,
        IReadOnlyCollection<MemberDeclarationSyntax> members,
        string targetClassName)
    {
        ClassDeclarationSyntax classNode = originalClassNode
            .WithIdentifier(SyntaxFactory.Identifier(targetClassName))
            .WithMembers(SyntaxFactory.List(members.Select(member => RewriteMemberForTargetClass(member, originalClassNode.Identifier.ValueText, targetClassName))));

        MemberDeclarationSyntax container = classNode;
        if (originalClassNode.Parent is NamespaceDeclarationSyntax namespaceDeclaration)
        {
            container = namespaceDeclaration.WithMembers(SyntaxFactory.SingletonList(container));
        }
        else if (originalClassNode.Parent is FileScopedNamespaceDeclarationSyntax fileScopedNamespaceDeclaration)
        {
            container = fileScopedNamespaceDeclaration.WithMembers(SyntaxFactory.SingletonList(container));
        }

        CompilationUnitSyntax document = SyntaxFactory.CompilationUnit()
            .WithUsings(root.Usings)
            .WithMembers(SyntaxFactory.SingletonList(container))
            .NormalizeWhitespace();

        return document.ToFullString();
    }

    private static MemberDeclarationSyntax RewriteMemberForTargetClass(
        MemberDeclarationSyntax member,
        string sourceClassName,
        string targetClassName)
    {
        if (member is ConstructorDeclarationSyntax constructor)
        {
            return constructor.WithIdentifier(SyntaxFactory.Identifier(targetClassName));
        }

        SyntaxNode rewrittenNode = new ClassNameRewriter(sourceClassName, targetClassName).Visit(member) ?? member;
        return (MemberDeclarationSyntax)rewrittenNode;
    }

    private sealed record DocumentContext(CompilationUnitSyntax Root, SemanticModel SemanticModel);

    private sealed record SliceResult(
        ClassDeclarationSyntax ClassNode,
        IReadOnlyCollection<MemberDeclarationSyntax> Members,
        IReadOnlyCollection<string> MemberNames);

    private sealed class ClassNameRewriter : CSharpSyntaxRewriter
    {
        private readonly string sourceClassName;
        private readonly string targetClassName;

        public ClassNameRewriter(string sourceClassName, string targetClassName)
        {
            this.sourceClassName = sourceClassName;
            this.targetClassName = targetClassName;
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (string.Equals(node.Identifier.ValueText, sourceClassName, StringComparison.Ordinal))
            {
                return node.WithIdentifier(SyntaxFactory.Identifier(targetClassName));
            }

            return base.VisitIdentifierName(node);
        }
    }
}
