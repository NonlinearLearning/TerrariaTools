using System.Collections.Immutable;
using Domain.Rewrite;
using Domain.Rewrite.Artifacts;
using Logic.Rewrite;
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


    public CodeRewriteResult DeleteClass(string sourceCode, string className)
    {
        DocumentContext context = CreateContext(sourceCode);
        ClassDeclarationSyntax classNode = FindClass(context.Root, className);
        SyntaxNode newRoot = context.Root.RemoveNode(classNode, SyntaxRemoveOptions.KeepNoTrivia)
            ?? throw new InvalidOperationException(RoslynCodeIsolationConventions.BuildDeleteClassFailedMessage(className));
        return CreateRewriteResult(CodeRewriteKind.DeleteClass, className, newRoot.NormalizeWhitespace().ToFullString(), true);
    }


    public CodeRewriteResult DeleteMethod(string sourceCode, string className, string methodName, int? parameterCount)
    {
        DocumentContext context = CreateContext(sourceCode);
        MethodDeclarationSyntax methodNode = FindMethod(context.Root, className, methodName, parameterCount);
        SyntaxNode newRoot = context.Root.RemoveNode(methodNode, SyntaxRemoveOptions.KeepNoTrivia)
            ?? throw new InvalidOperationException(RoslynCodeIsolationConventions.BuildDeleteMethodFailedMessage(className, methodName));
        return CreateRewriteResult(
            CodeRewriteKind.DeleteMethod,
            RoslynCodeIsolationConventions.BuildMemberTargetName(className, methodName),
            newRoot.NormalizeWhitespace().ToFullString(),
            true);
    }


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
            RoslynCodeIsolationConventions.BuildMemberTargetName(className, methodName),
            newRoot.NormalizeWhitespace().ToFullString(),
            true);
    }


    public CodeRewriteResult ClearMethodBody(string sourceCode, string className, string methodName, int? parameterCount)
    {
        DocumentContext context = CreateContext(sourceCode);
        MethodDeclarationSyntax methodNode = FindMethod(context.Root, className, methodName, parameterCount);
        MethodDeclarationSyntax newMethodNode = CreateClearedMethod(methodNode);
        SyntaxNode newRoot = context.Root.ReplaceNode(methodNode, newMethodNode);
        return CreateRewriteResult(
            CodeRewriteKind.ClearMethodBody,
            RoslynCodeIsolationConventions.BuildMemberTargetName(className, methodName),
            newRoot.NormalizeWhitespace().ToFullString(),
            true);
    }


    public MemberSlice BuildMemberSlice(string sourceCode, string className, string methodName, int? parameterCount)
    {
        DocumentContext context = CreateContext(sourceCode);
        SliceResult sliceResult = BuildSliceResult(context, className, methodName, parameterCount);
        string sliceSosrce = RenderClassDocument(context.Root, sliceResult.ClassNode, sliceResult.Members, sliceResult.ClassNode.Identifier.ValueText);
        MemberSlice memberSlice = MemberSlice.Create(className, methodName, sliceSosrce);

        foreach (string memberName in sliceResult.MemberNames)
        {
            memberSlice.AddMember(memberName);
        }

        return memberSlice;
    }


    public ShadowClass GenerateShadowClass(string sourceCode, string className, string methodName, int? parameterCount)
    {
        DocumentContext context = CreateContext(sourceCode);
        SliceResult sliceResult = BuildSliceResult(context, className, methodName, parameterCount);
        string shadowClassName = RoslynCodeIsolationConventions.BuildShadowClassName(sliceResult.ClassNode.Identifier.ValueText);
        string shadowSosrce = RenderClassDocument(context.Root, sliceResult.ClassNode, sliceResult.Members, shadowClassName);
        ShadowClass shadowClass = ShadowClass.Create(className, shadowClassName, shadowSosrce);

        foreach (string memberName in sliceResult.MemberNames)
        {
            shadowClass.AddMember(memberName);
        }

        return shadowClass;
    }


    public RuntimeClosure ExtractMinimalRuntimeClosure(string sourceCode, string className, string methodName, int? parameterCount)
    {
        DocumentContext context = CreateContext(sourceCode);
        SliceResult sliceResult = BuildSliceResult(context, className, methodName, parameterCount);
        string clossreClassName = RoslynCodeIsolationConventions.BuildRuntimeClosureClassName(sliceResult.ClassNode.Identifier.ValueText);
        string clossreSosrce = RenderClassDocument(context.Root, sliceResult.ClassNode, sliceResult.Members, clossreClassName);
        RuntimeClosure rsntimeClosure = RuntimeClosure.Create(className, methodName, clossreClassName, clossreSosrce);

        foreach (string memberName in sliceResult.MemberNames)
        {
            rsntimeClosure.AddMember(memberName);
        }

        return rsntimeClosure;
    }

    private static DocumentContext CreateContext(string sourceCode)
    {
        ArgumentNullException.ThrowIfNull(sourceCode);

        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: RoslynCodeIsolationConventions.CompilationAssemblyName,
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
            ?? throw new InvalidOperationException(RoslynCodeIsolationConventions.BuildClassNotFoundMessage(className));
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
            ?? throw new InvalidOperationException(RoslynCodeIsolationConventions.BuildMethodNotFoundMessage(className, methodName));
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
        bool isVoidReturnType = returnType is PredefinedTypeSyntax predefinedType &&
            predefinedType.Keyword.IsKind(SyntaxKind.VoidKeyword);
        StatementSyntax bodyStatement = RoslynRewriteConventions.ShouldUseEmptyStatement(isVoidReturnType)
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
            RoslynRewriteConventions.ShouldUseNullLiteral(predefinedType.Keyword.ValueText))
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
        return RoslynCodeIsolationConventions.AddCompletedDiagnostic(result);
    }

    private static IReadOnlyList<MetadataReference> BuildMetadataReferences()
    {
        string trustedPlatformAssemblies = AppContext.GetData(RoslynCodeIsolationConventions.TrustedPlatformAssembliesKey) as string
            ?? throw new InvalidOperationException(RoslynCodeIsolationConventions.BuildTrustedPlatformAssembliesMissingMessage());

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
            ?? throw new InvalidOperationException(RoslynCodeIsolationConventions.BuildMethodSymbolResolveFailedMessage(className, methodName));

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

            if (!RoslynRewriteConventions.IsClassLocalDependency(
                    normalizedSymbol.ContainingType?.Name,
                    classNode.Identifier.ValueText))
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
            FieldDeclarationSyntax field => RoslynSliceConventions.BuildFieldMemberName(
                field.Declaration.Variables.Select(variable => variable.Identifier.ValueText)),
            _ => RoslynSliceConventions.BuildFallbackMemberName(member.Kind().ToString()),
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
            if (RoslynRewriteConventions.ShouldRewriteIdentifier(node.Identifier.ValueText, sourceClassName))
            {
                return node.WithIdentifier(SyntaxFactory.Identifier(targetClassName));
            }

            return base.VisitIdentifierName(node);
        }
    }
}


