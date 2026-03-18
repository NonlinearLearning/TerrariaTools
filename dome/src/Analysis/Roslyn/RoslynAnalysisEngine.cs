using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using ModelRules = TerrariaTools.Dome.Model.Rules;

namespace TerrariaTools.Dome.Analysis.Roslyn;

public sealed class RoslynAnalysisEngine : ApplicationAbstractions.IAnalysisEngine
{
    public Task<ApplicationAbstractions.AnalysisEngineResult> AnalyzeAsync(
        ApplicationAbstractions.SourceDocumentSet sourceSet,
        CancellationToken cancellationToken)
    {
        var compilation = CreateCompilation(sourceSet);
        var context = new BuildContext(sourceSet, compilation);
        context.Build(cancellationToken);

        var view = new ModelAnalysis.AnalysisResultModel(
            context.Targets
                .OrderBy(target => target.Target.DocumentPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(target => target.Locator.SpanStart)
                .ToArray(),
            Array.Empty<ModelAnalysis.AnalysisEdge>(),
            new ModelAnalysis.TypeDependencyGraph(
                context.TypeNodes.Values
                    .OrderBy(node => node.TypeId, StringComparer.Ordinal)
                    .ToArray(),
                context.TypeEdges
                    .OrderBy(edge => edge.SourceTypeId, StringComparer.Ordinal)
                    .ThenBy(edge => edge.TargetTypeId, StringComparer.Ordinal)
                    .ThenBy(edge => edge.Kind)
                    .ToArray()),
            new ModelAnalysis.FunctionDependencyGraph(
                context.FunctionNodes.Values
                    .OrderBy(node => node.MemberId.Value, StringComparer.Ordinal)
                    .ToArray(),
                context.FunctionFacts.Values
                    .SelectMany(static fact => fact.CalledMemberIds.Select(called => new ModelAnalysis.FunctionDependencyEdge(
                        fact.Node.MemberId,
                        called,
                        ModelPrimitives.FunctionDependencyKind.Calls)))
                    .OrderBy(edge => edge.SourceMemberId.Value, StringComparer.Ordinal)
                    .ThenBy(edge => edge.TargetMemberId.Value, StringComparer.Ordinal)
                    .ToArray()),
            new ModelAnalysis.StatementDependencyGraph(
                context.StatementFacts.Values
                    .OrderBy(fact => fact.SpanStart)
                    .Select(fact => fact.TargetKey)
                    .ToArray(),
                context.StatementFacts.Values
                    .GroupBy(fact => fact.MemberId.Value, StringComparer.Ordinal)
                    .SelectMany(static group =>
                    {
                        var ordered = group.OrderBy(fact => fact.SpanStart).ToArray();
                        return ordered.Skip(1).Select((fact, index) => new ModelAnalysis.StatementDependencyEdge(
                            ordered[index].TargetKey,
                            fact.TargetKey,
                            ModelAnalysis.StatementDependencyKind.Precedes));
                    })
                    .ToArray()),
            ModelPrimitives.StatementGraphMaterialization.SnapshotOnly,
            ModelPrimitives.FunctionGraphMaterialization.None);

        var snapshot = new ModelAnalysis.AnalysisExecutionSnapshot(
            view,
            new ModelAnalysis.FunctionIndex(
                context.FunctionNodes,
                context.FunctionMembersByDocumentPath.ToDictionary(
                    static pair => pair.Key,
                    static pair => (IReadOnlyList<string>)pair.Value.OrderBy(memberId => memberId, StringComparer.Ordinal).ToArray(),
                    StringComparer.Ordinal)),
            new ModelAnalysis.FunctionFactsIndex(
                context.FunctionFacts,
                context.FunctionMembersByDocumentPath.ToDictionary(
                    static pair => pair.Key,
                    static pair => (IReadOnlyList<string>)pair.Value.OrderBy(memberId => memberId, StringComparer.Ordinal).ToArray(),
                    StringComparer.Ordinal),
                context.IncomingCallersByMemberId.ToDictionary(
                    static pair => pair.Key,
                    static pair => (IReadOnlyList<ModelPrimitives.MemberId>)pair.Value.OrderBy(memberId => memberId.Value, StringComparer.Ordinal).ToArray(),
                    StringComparer.Ordinal)),
            new ModelAnalysis.StatementFactsIndex(context.StatementFacts));

        var services = new ModelAnalysis.AnalysisServices(
            new InheritanceQueryService(context.OverrideMembers, context.InterfaceMembers, context.InheritanceTypes),
            context.ReferenceQueryService,
            new StatementAnalysisService(snapshot.StatementFacts),
            new FunctionGraphProvider(snapshot.FunctionIndex, snapshot.FunctionFacts),
            EmptySymbolDependencyGraphProvider.Instance,
            new NativeMethodCallQueryService(snapshot.FunctionFacts),
            new NativeDataFlowSummaryService(context.StatementDataFlowsByTargetKey),
            EmptySwitchFlowSummaryService.Instance,
            new NativeCallChainAnalysisService(snapshot.FunctionIndex, snapshot.FunctionFacts),
            new NativeAdvancedAnalysisSummaryService(context.TypeInfos, context.SymbolInfos),
            new NativeMemberCleanupQueryService(
                snapshot.FunctionIndex,
                context.SymbolInfos,
                context.TypeInfos,
                context.ReferenceQueryService,
                context.PublicMethodsByTypeId));

        return Task.FromResult(new ApplicationAbstractions.AnalysisEngineResult(
            view,
            snapshot,
            services,
            new ModelAnalysis.AnalysisPerformanceSummary(
                sourceSet.Documents.Count,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero)));
    }

    private static CSharpCompilation CreateCompilation(ApplicationAbstractions.SourceDocumentSet sourceSet)
    {
        var trees = sourceSet.Documents
            .Select(document => CSharpSyntaxTree.ParseText(document.SourceText, path: document.SourcePath))
            .ToArray();

        MetadataReference[] references =
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location)
        ];

        return CSharpCompilation.Create(
            "Dome.StandardAnalysis",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private sealed class BuildContext
    {
        private readonly Dictionary<string, DocumentContext> _documentsByPath;
        private readonly Dictionary<string, HashSet<string>> _publicMethodsByTypeId = new(StringComparer.Ordinal);

        public BuildContext(ApplicationAbstractions.SourceDocumentSet sourceSet, CSharpCompilation compilation)
        {
            SourceSet = sourceSet;
            Compilation = compilation;
            TypeNodes = new Dictionary<string, ModelAnalysis.TypeNodeRef>(StringComparer.Ordinal);
            TypeEdges = [];
            Targets = [];
            FunctionNodes = new Dictionary<string, ModelAnalysis.FunctionNodeRef>(StringComparer.Ordinal);
            FunctionFacts = new Dictionary<string, ModelAnalysis.FunctionFact>(StringComparer.Ordinal);
            StatementFacts = new Dictionary<string, ModelAnalysis.StatementFact>(StringComparer.Ordinal);
            FunctionMembersByDocumentPath = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            IncomingCallersByMemberId = new Dictionary<string, HashSet<ModelPrimitives.MemberId>>(StringComparer.Ordinal);
            OverrideMembers = new HashSet<string>(StringComparer.Ordinal);
            InterfaceMembers = new HashSet<string>(StringComparer.Ordinal);
            InheritanceTypes = new HashSet<string>(StringComparer.Ordinal);
            SymbolInfos = new Dictionary<string, ModelAnalysis.MemberCleanupSymbolInfo>(StringComparer.Ordinal);
            TypeInfos = new Dictionary<string, ModelAnalysis.MemberCleanupTypeInfo>(StringComparer.Ordinal);
            MemberToFunctions = new Dictionary<string, HashSet<ModelPrimitives.MemberId>>(StringComparer.Ordinal);
            MemberToTypes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            TypeToFunctions = new Dictionary<string, HashSet<ModelPrimitives.MemberId>>(StringComparer.Ordinal);
            TypeToTypes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            StatementDataFlowsByTargetKey = new Dictionary<string, ModelAnalysis.DataFlowSummary>(StringComparer.Ordinal);
            _documentsByPath = sourceSet.Documents.ToDictionary(
                document => Path.GetFullPath(document.SourcePath),
                document =>
                {
                    var tree = compilation.SyntaxTrees.Single(candidate => string.Equals(
                        Path.GetFullPath(candidate.FilePath ?? string.Empty),
                        Path.GetFullPath(document.SourcePath),
                        StringComparison.OrdinalIgnoreCase));
                    return new DocumentContext(document, tree, compilation.GetSemanticModel(tree));
                },
                StringComparer.OrdinalIgnoreCase);
            ReferenceQueryService = new ReferenceQueryService(MemberToFunctions, MemberToTypes, TypeToFunctions, TypeToTypes);
        }

        public ApplicationAbstractions.SourceDocumentSet SourceSet { get; }
        public CSharpCompilation Compilation { get; }
        public Dictionary<string, ModelAnalysis.TypeNodeRef> TypeNodes { get; }
        public List<ModelAnalysis.TypeDependencyEdge> TypeEdges { get; }
        public List<ModelAnalysis.AnalysisTarget> Targets { get; }
        public Dictionary<string, ModelAnalysis.FunctionNodeRef> FunctionNodes { get; }
        public Dictionary<string, ModelAnalysis.FunctionFact> FunctionFacts { get; }
        public Dictionary<string, ModelAnalysis.StatementFact> StatementFacts { get; }
        public Dictionary<string, HashSet<string>> FunctionMembersByDocumentPath { get; }
        public Dictionary<string, HashSet<ModelPrimitives.MemberId>> IncomingCallersByMemberId { get; }
        public HashSet<string> OverrideMembers { get; }
        public HashSet<string> InterfaceMembers { get; }
        public HashSet<string> InheritanceTypes { get; }
        public Dictionary<string, ModelAnalysis.MemberCleanupSymbolInfo> SymbolInfos { get; }
        public Dictionary<string, ModelAnalysis.MemberCleanupTypeInfo> TypeInfos { get; }
        public Dictionary<string, HashSet<ModelPrimitives.MemberId>> MemberToFunctions { get; }
        public Dictionary<string, HashSet<string>> MemberToTypes { get; }
        public Dictionary<string, HashSet<ModelPrimitives.MemberId>> TypeToFunctions { get; }
        public Dictionary<string, HashSet<string>> TypeToTypes { get; }
        public Dictionary<string, ModelAnalysis.DataFlowSummary> StatementDataFlowsByTargetKey { get; }
        public ReferenceQueryService ReferenceQueryService { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<ModelPrimitives.MemberId>> PublicMethodsByTypeId =>
            _publicMethodsByTypeId.ToDictionary(
                static pair => pair.Key,
                static pair => (IReadOnlyList<ModelPrimitives.MemberId>)pair.Value
                    .OrderBy(memberId => memberId, StringComparer.Ordinal)
                    .Select(memberId => new ModelPrimitives.MemberId(memberId))
                    .ToArray(),
                StringComparer.Ordinal);

        public void Build(CancellationToken cancellationToken)
        {
            foreach (var document in _documentsByPath.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BuildTypeMetadata(document);
            }

            foreach (var document in _documentsByPath.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BuildMemberMetadata(document);
            }
        }

        private void BuildTypeMetadata(DocumentContext document)
        {
            foreach (var typeDeclaration in document.Root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (document.SemanticModel.GetDeclaredSymbol(typeDeclaration) is not INamedTypeSymbol typeSymbol)
                {
                    continue;
                }

                var typeId = MetadataTypeIdBuilder.Build(typeSymbol);
                TypeNodes[typeId] = new ModelAnalysis.TypeNodeRef(typeId, typeSymbol.Name, document.Document.RelativePath);

                var inInheritanceChain = false;
                if (typeSymbol.BaseType is { SpecialType: not SpecialType.System_Object } baseType)
                {
                    inInheritanceChain = true;
                    TypeEdges.Add(new ModelAnalysis.TypeDependencyEdge(
                        typeId,
                        MetadataTypeIdBuilder.Build(baseType),
                        ModelAnalysis.TypeDependencyKind.Inherits));
                }

                foreach (var interfaceType in typeSymbol.Interfaces)
                {
                    inInheritanceChain = true;
                    TypeEdges.Add(new ModelAnalysis.TypeDependencyEdge(
                        typeId,
                        MetadataTypeIdBuilder.Build(interfaceType),
                        ModelAnalysis.TypeDependencyKind.Implements));
                }

                if (inInheritanceChain)
                {
                    InheritanceTypes.Add(typeId);
                }

                TypeInfos[typeId] = new ModelAnalysis.MemberCleanupTypeInfo(
                    typeId,
                    document.Document.RelativePath,
                    typeSymbol.Name,
                    typeSymbol.DeclaredAccessibility == Accessibility.Public,
                    typeSymbol.IsAbstract,
                    typeSymbol.IsStatic,
                    typeDeclaration.Modifiers.Any(static modifier => modifier.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)) ||
                    typeSymbol.DeclaringSyntaxReferences.Length > 1,
                    typeSymbol.ContainingType is not null,
                    typeSymbol.TypeKind == TypeKind.Interface,
                    inInheritanceChain);

                Targets.Add(CreateTarget(
                    document.Document.RelativePath,
                    typeId,
                    ModelPrimitives.MemberKind.Class,
                    ModelPrimitives.TargetKind.Class,
                    typeDeclaration.Identifier.SpanStart,
                    typeDeclaration.Identifier.Span.Length,
                    typeSymbol.Name,
                    inInheritanceChain));
            }
        }

        private void BuildMemberMetadata(DocumentContext document)
        {
            foreach (var field in document.Root.DescendantNodes().OfType<FieldDeclarationSyntax>())
            {
                foreach (var variable in field.Declaration.Variables)
                {
                    if (document.SemanticModel.GetDeclaredSymbol(variable) is not IFieldSymbol fieldSymbol)
                    {
                        continue;
                    }

                    var symbolId = BuildMemberId(fieldSymbol);
                    RecordSymbolInfo(symbolId, ModelPrimitives.MemberKind.Field, fieldSymbol.ContainingType, document.Document.RelativePath, fieldSymbol.Name, fieldSymbol.DeclaredAccessibility, fieldSymbol.IsStatic, false, false, false, false, false);
                    Targets.Add(CreateTarget(document.Document.RelativePath, symbolId, ModelPrimitives.MemberKind.Field, ModelPrimitives.TargetKind.Field, variable.Identifier.SpanStart, variable.Identifier.Span.Length, variable.Identifier.Text));
                }
            }

            foreach (var property in document.Root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
            {
                if (document.SemanticModel.GetDeclaredSymbol(property) is not IPropertySymbol propertySymbol)
                {
                    continue;
                }

                var symbolId = BuildMemberId(propertySymbol);
                RecordSymbolInfo(symbolId, ModelPrimitives.MemberKind.Property, propertySymbol.ContainingType, document.Document.RelativePath, propertySymbol.Name, propertySymbol.DeclaredAccessibility, propertySymbol.IsStatic, propertySymbol.IsAbstract, propertySymbol.IsVirtual, propertySymbol.IsOverride, false, false);
                Targets.Add(CreateTarget(document.Document.RelativePath, symbolId, ModelPrimitives.MemberKind.Property, ModelPrimitives.TargetKind.Property, property.Identifier.SpanStart, property.Identifier.Span.Length, property.Identifier.Text));
            }

            foreach (var method in document.Root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (document.SemanticModel.GetDeclaredSymbol(method) is not IMethodSymbol methodSymbol)
                {
                    continue;
                }

                var memberIdValue = BuildMemberId(methodSymbol);
                var memberId = new ModelPrimitives.MemberId(memberIdValue);
                var typeId = MetadataTypeIdBuilder.Build(methodSymbol.ContainingType);
                var isHighRisk = IsHighRiskMethod(methodSymbol);
                if (methodSymbol.IsOverride)
                {
                    OverrideMembers.Add(memberIdValue);
                }

                if (ImplementsInterfaceMember(methodSymbol))
                {
                    InterfaceMembers.Add(memberIdValue);
                }

                if (methodSymbol.DeclaredAccessibility == Accessibility.Public)
                {
                    if (!_publicMethodsByTypeId.TryGetValue(typeId, out var methods))
                    {
                        methods = new HashSet<string>(StringComparer.Ordinal);
                        _publicMethodsByTypeId[typeId] = methods;
                    }

                    methods.Add(memberIdValue);
                }

                RecordSymbolInfo(memberIdValue, ModelPrimitives.MemberKind.Method, methodSymbol.ContainingType, document.Document.RelativePath, methodSymbol.Name, methodSymbol.DeclaredAccessibility, methodSymbol.IsStatic, methodSymbol.IsAbstract, methodSymbol.IsVirtual, methodSymbol.IsOverride, methodSymbol.IsExtern, true);
                FunctionNodes[memberIdValue] = new ModelAnalysis.FunctionNodeRef(
                    memberId,
                    ModelPrimitives.MemberKind.Method,
                    typeId,
                    methodSymbol.Name,
                    document.Document.RelativePath,
                    method.Identifier.SpanStart,
                    method.Identifier.Span.Length,
                    methodSymbol.DeclaredAccessibility == Accessibility.Private,
                    methodSymbol.ReturnsVoid,
                    method.Body is not null || method.ExpressionBody is not null,
                    method.Body is { Statements.Count: > 0 },
                    methodSymbol.ReturnType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
                AddToDocumentMap(FunctionMembersByDocumentPath, document.Document.RelativePath, memberIdValue);
                Targets.Add(CreateTarget(document.Document.RelativePath, memberIdValue, ModelPrimitives.MemberKind.Method, ModelPrimitives.TargetKind.Method, method.Identifier.SpanStart, method.Identifier.Span.Length, methodSymbol.Name, isHighRisk));

                var calledMemberIds = new HashSet<ModelPrimitives.MemberId>(new MemberIdEqualityComparer());
                RecordReferences(document, method, typeId, memberId, calledMemberIds);
                FunctionFacts[memberIdValue] = new ModelAnalysis.FunctionFact(
                    FunctionNodes[memberIdValue],
                    calledMemberIds.OrderBy(item => item.Value, StringComparer.Ordinal).ToArray());

                foreach (var statement in method.DescendantNodes().OfType<StatementSyntax>())
                {
                    if (statement is BlockSyntax)
                    {
                        continue;
                    }

                    var target = BuildStatementTarget(document, memberId, isHighRisk, statement);
                    if (target == null)
                    {
                        continue;
                    }

                    Targets.Add(target);
                    var targetKey = $"{target.Target.IdentityKey}|{target.Locator.EffectiveResolutionKey.SpanStart}|{target.Locator.EffectiveResolutionKey.SpanLength}";
                    StatementFacts[targetKey] = new ModelAnalysis.StatementFact(
                        targetKey,
                        memberId,
                        target.StatementKind,
                        target.DefinesSymbols,
                        target.UsesSymbols,
                        target.InvokedMemberIds,
                        target.ScopeMode,
                        target.ScopeId,
                        target.ParentScopeId,
                        target.Locator.SpanStart,
                        target.Locator.SpanLength);
                    StatementDataFlowsByTargetKey[targetKey] = new ModelAnalysis.DataFlowSummary(
                        target.UsesSymbols.Select(symbol => symbol.DisplayName).Distinct(StringComparer.Ordinal).ToArray(),
                        target.DefinesSymbols.Select(symbol => symbol.DisplayName).Distinct(StringComparer.Ordinal).ToArray());
                }
            }
        }

        private ModelAnalysis.AnalysisTarget? BuildStatementTarget(
            DocumentContext document,
            ModelPrimitives.MemberId methodId,
            bool isHighRiskMethod,
            StatementSyntax statement)
        {
            var directives = DirectiveReader.Read(statement);
            var defines = GetDefinedSymbols(document.SemanticModel, statement, methodId);
            var uses = GetUsedSymbols(document.SemanticModel, statement, methodId, defines.Select(symbol => symbol.SymbolKey).ToHashSet(StringComparer.Ordinal));
            var invoked = GetInvokedMembers(document.SemanticModel, statement);
            var markedExpressionKinds = GetMarkedExpressionKinds(statement);

            return new ModelAnalysis.AnalysisTarget(
                new ModelPrimitives.TargetIdentity(
                    document.Document.RelativePath,
                    methodId,
                    ModelPrimitives.MemberKind.Method,
                    ModelPrimitives.TargetKind.Statement),
                new ModelPrimitives.TargetLocator(
                    statement.SpanStart,
                    statement.Span.Length,
                    statement.ToString().Trim(),
                    new ModelPrimitives.TargetResolutionKey(statement.SpanStart, statement.Span.Length)),
                isHighRiskMethod,
                directives,
                defines,
                uses,
                invoked,
                MapStatementKind(statement),
                IsSanitizingAssignment(statement, defines, uses),
                statement.DescendantNodes().OfType<InitializerExpressionSyntax>().Any(),
                directives.Count > 0 && markedExpressionKinds.Count > 0 && uses.Count > 0,
                markedExpressionKinds,
                ModelPrimitives.StatementScopeMode.MinimalBlock,
                GetScopeId(statement.Parent as BlockSyntax),
                GetScopeId((statement.Parent as BlockSyntax)?.Parent as BlockSyntax));
        }

        private void RecordReferences(
            DocumentContext document,
            MethodDeclarationSyntax method,
            string declaringTypeId,
            ModelPrimitives.MemberId currentMemberId,
            HashSet<ModelPrimitives.MemberId> calledMemberIds)
        {
            foreach (var invocation in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (document.SemanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol invokedSymbol)
                {
                    continue;
                }

                var targetMethodId = BuildMemberId(invokedSymbol);
                calledMemberIds.Add(new ModelPrimitives.MemberId(targetMethodId));
                AddReference(MemberToFunctions, targetMethodId, currentMemberId);
                AddReference(MemberToTypes, targetMethodId, declaringTypeId);
                AddIncomingCaller(targetMethodId, currentMemberId);
            }

            foreach (var identifier in method.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                var symbol = document.SemanticModel.GetSymbolInfo(identifier).Symbol;
                if (symbol == null || symbol is ILocalSymbol or IParameterSymbol)
                {
                    continue;
                }

                var symbolId = BuildMemberId(symbol);
                AddReference(MemberToFunctions, symbolId, currentMemberId);
                AddReference(MemberToTypes, symbolId, declaringTypeId);
            }

            foreach (var node in method.DescendantNodes().OfType<TypeSyntax>())
            {
                var typeSymbol = document.SemanticModel.GetTypeInfo(node).Type;
                if (typeSymbol == null)
                {
                    continue;
                }

                var typeId = MetadataTypeIdBuilder.Build(typeSymbol);
                AddReference(TypeToFunctions, typeId, currentMemberId);
                AddReference(TypeToTypes, typeId, declaringTypeId);
            }
        }

        private void RecordSymbolInfo(
            string symbolId,
            ModelPrimitives.MemberKind memberKind,
            INamedTypeSymbol containingType,
            string documentPath,
            string name,
            Accessibility accessibility,
            bool isStatic,
            bool isAbstract,
            bool isVirtual,
            bool isOverride,
            bool isExtern,
            bool isOrdinaryMethod)
        {
            var typeId = MetadataTypeIdBuilder.Build(containingType);
            var typeInfo = TypeInfos.TryGetValue(typeId, out var value)
                ? value
                : new ModelAnalysis.MemberCleanupTypeInfo(typeId, documentPath, containingType.Name, false, false, false, false, false, false, false);

            SymbolInfos[symbolId] = new ModelAnalysis.MemberCleanupSymbolInfo(
                symbolId,
                memberKind,
                typeId,
                documentPath,
                name,
                accessibility == Accessibility.Public,
                accessibility == Accessibility.Private,
                isStatic,
                isAbstract,
                isVirtual,
                isOverride,
                isExtern,
                isOrdinaryMethod,
                typeInfo.IsPartial,
                typeInfo.IsNested,
                typeInfo.IsInterface,
                isStatic && string.Equals(name, "Main", StringComparison.Ordinal));
        }

        private static ModelAnalysis.AnalysisTarget CreateTarget(
            string documentPath,
            string memberId,
            ModelPrimitives.MemberKind memberKind,
            ModelPrimitives.TargetKind targetKind,
            int spanStart,
            int spanLength,
            string displayText,
            bool isHighRisk = false) =>
            new(
                new ModelPrimitives.TargetIdentity(documentPath, new ModelPrimitives.MemberId(memberId), memberKind, targetKind),
                new ModelPrimitives.TargetLocator(spanStart, spanLength, displayText, new ModelPrimitives.TargetResolutionKey(spanStart, spanLength)),
                isHighRisk,
                Array.Empty<ModelRules.DirectiveAction>(),
                Array.Empty<ModelAnalysis.SymbolRef>(),
                Array.Empty<ModelAnalysis.SymbolRef>(),
                Array.Empty<ModelPrimitives.MemberId>(),
                ModelPrimitives.StatementKindRef.Unknown,
                false,
                false,
                false,
                Array.Empty<string>(),
                ModelPrimitives.StatementScopeMode.MinimalBlock,
                null,
                null);

        private static IReadOnlyList<ModelAnalysis.SymbolRef> GetDefinedSymbols(
            SemanticModel model,
            StatementSyntax statement,
            ModelPrimitives.MemberId declaringMemberId)
        {
            var symbols = new Dictionary<string, ModelAnalysis.SymbolRef>(StringComparer.Ordinal);
            if (statement is LocalDeclarationStatementSyntax localDeclaration)
            {
                foreach (var variable in localDeclaration.Declaration.Variables)
                {
                    var projected = SymbolRefProjector.ProjectDeclared(localDeclaration, variable, model, declaringMemberId);
                    if (projected != null)
                    {
                        symbols[projected.SymbolKey] = projected;
                    }
                }
            }
            else if (statement is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment })
            {
                var projected = SymbolRefProjector.Project(model.GetSymbolInfo(assignment.Left).Symbol, declaringMemberId);
                if (projected != null)
                {
                    symbols[projected.SymbolKey] = projected;
                }
            }

            return symbols.Values.ToArray();
        }

        private static IReadOnlyList<ModelAnalysis.SymbolRef> GetUsedSymbols(
            SemanticModel model,
            StatementSyntax statement,
            ModelPrimitives.MemberId declaringMemberId,
            IReadOnlySet<string> definedSymbolKeys)
        {
            var symbols = new Dictionary<string, ModelAnalysis.SymbolRef>(StringComparer.Ordinal);
            foreach (var identifier in statement.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                var projected = SymbolRefProjector.ProjectUsed(identifier, model, declaringMemberId);
                if (projected == null || definedSymbolKeys.Contains(projected.SymbolKey))
                {
                    continue;
                }

                symbols[projected.SymbolKey] = projected;
            }

            return symbols.Values.ToArray();
        }

        private static IReadOnlyList<ModelPrimitives.MemberId> GetInvokedMembers(SemanticModel model, StatementSyntax statement) =>
            statement.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Select(invocation => model.GetSymbolInfo(invocation).Symbol as IMethodSymbol)
                .Where(static symbol => symbol != null)
                .Select(symbol => new ModelPrimitives.MemberId(BuildMemberId(symbol!)))
                .Distinct(new MemberIdEqualityComparer())
                .ToArray();

        private static IReadOnlyList<string> GetMarkedExpressionKinds(StatementSyntax statement) =>
            statement.DescendantNodes()
                .OfType<ExpressionSyntax>()
                .Select(expression => expression.Kind().ToString())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        private static bool IsSanitizingAssignment(
            StatementSyntax statement,
            IReadOnlyList<ModelAnalysis.SymbolRef> definedSymbols,
            IReadOnlyList<ModelAnalysis.SymbolRef> usedSymbols)
        {
            if (definedSymbols.Count == 0)
            {
                return false;
            }

            if (statement is LocalDeclarationStatementSyntax localDeclaration)
            {
                return localDeclaration.Declaration.Variables.All(variable =>
                    variable.Initializer?.Value is LiteralExpressionSyntax or DefaultExpressionSyntax);
            }

            if (statement is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment })
            {
                return usedSymbols.Count == 0 || assignment.Right is LiteralExpressionSyntax or DefaultExpressionSyntax;
            }

            return false;
        }

        private static ModelPrimitives.StatementKindRef MapStatementKind(StatementSyntax statement) =>
            statement switch
            {
                LocalDeclarationStatementSyntax => ModelPrimitives.StatementKindRef.Declaration,
                ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax } expressionStatement
                    when expressionStatement.Expression.DescendantNodes().OfType<InitializerExpressionSyntax>().Any() =>
                    ModelPrimitives.StatementKindRef.ObjectInitializerAssignment,
                ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax } => ModelPrimitives.StatementKindRef.Assignment,
                IfStatementSyntax => ModelPrimitives.StatementKindRef.If,
                WhileStatementSyntax => ModelPrimitives.StatementKindRef.While,
                ForStatementSyntax => ModelPrimitives.StatementKindRef.For,
                ReturnStatementSyntax => ModelPrimitives.StatementKindRef.Return,
                _ => ModelPrimitives.StatementKindRef.Unknown
            };

        private static string? GetScopeId(BlockSyntax? block) => block == null ? null : $"{block.SpanStart}:{block.Span.Length}";

        private static bool IsHighRiskMethod(IMethodSymbol methodSymbol) =>
            methodSymbol.IsOverride || ImplementsInterfaceMember(methodSymbol);

        private static bool ImplementsInterfaceMember(IMethodSymbol methodSymbol)
        {
            foreach (var interfaceType in methodSymbol.ContainingType.AllInterfaces)
            {
                foreach (var interfaceMember in interfaceType.GetMembers().OfType<IMethodSymbol>())
                {
                    if (SymbolEqualityComparer.Default.Equals(methodSymbol.ContainingType.FindImplementationForInterfaceMember(interfaceMember), methodSymbol))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string BuildMemberId(ISymbol symbol) =>
            symbol switch
            {
                IMethodSymbol method => $"{MetadataTypeIdBuilder.Build(method.ContainingType)}.{method.Name}({string.Join(", ", method.Parameters.Select(parameter => parameter.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)))})",
                IFieldSymbol field => $"{MetadataTypeIdBuilder.Build(field.ContainingType)}.{field.Name}",
                IPropertySymbol property => $"{MetadataTypeIdBuilder.Build(property.ContainingType)}.{property.Name}",
                INamedTypeSymbol namedType => MetadataTypeIdBuilder.Build(namedType),
                _ => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
            };

        private static void AddReference<TValue>(IDictionary<string, HashSet<TValue>> map, string key, TValue value)
            where TValue : notnull
        {
            if (!map.TryGetValue(key, out var values))
            {
                values = new HashSet<TValue>();
                map[key] = values;
            }

            values.Add(value);
        }

        private static void AddToDocumentMap(IDictionary<string, HashSet<string>> map, string documentPath, string memberId)
        {
            if (!map.TryGetValue(documentPath, out var members))
            {
                members = new HashSet<string>(StringComparer.Ordinal);
                map[documentPath] = members;
            }

            members.Add(memberId);
        }

        private void AddIncomingCaller(string targetMemberId, ModelPrimitives.MemberId caller)
        {
            if (!IncomingCallersByMemberId.TryGetValue(targetMemberId, out var callers))
            {
                callers = new HashSet<ModelPrimitives.MemberId>(new MemberIdEqualityComparer());
                IncomingCallersByMemberId[targetMemberId] = callers;
            }

            callers.Add(caller);
        }
    }

    private sealed record DocumentContext(
        ApplicationAbstractions.SourceDocument Document,
        SyntaxTree Tree,
        SemanticModel SemanticModel)
    {
        public CompilationUnitSyntax Root => (CompilationUnitSyntax)Tree.GetRoot();
    }

    private sealed class NativeMemberCleanupQueryService : ModelAnalysis.IMemberCleanupQueryService
    {
        private readonly ModelAnalysis.FunctionIndex _functionIndex;
        private readonly IReadOnlyDictionary<string, ModelAnalysis.MemberCleanupSymbolInfo> _symbols;
        private readonly IReadOnlyDictionary<string, ModelAnalysis.MemberCleanupTypeInfo> _types;
        private readonly ModelAnalysis.IReferenceQueryService _references;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<ModelPrimitives.MemberId>> _publicMethodsByTypeId;

        public NativeMemberCleanupQueryService(
            ModelAnalysis.FunctionIndex functionIndex,
            IReadOnlyDictionary<string, ModelAnalysis.MemberCleanupSymbolInfo> symbols,
            IReadOnlyDictionary<string, ModelAnalysis.MemberCleanupTypeInfo> types,
            ModelAnalysis.IReferenceQueryService references,
            IReadOnlyDictionary<string, IReadOnlyList<ModelPrimitives.MemberId>> publicMethodsByTypeId)
        {
            _functionIndex = functionIndex;
            _symbols = symbols;
            _types = types;
            _references = references;
            _publicMethodsByTypeId = publicMethodsByTypeId;
        }

        public ModelAnalysis.MemberCleanupSymbolInfo? GetSymbolInfo(string symbolId) =>
            _symbols.TryGetValue(symbolId, out var info) ? info : null;

        public ModelAnalysis.MemberCleanupTypeInfo? GetTypeInfo(string typeId) =>
            _types.TryGetValue(typeId, out var info) ? info : null;

        public bool HasAnyReferences(string symbolId) => _references.HasReferences(symbolId);

        public bool HasInternalMethodReferences(ModelPrimitives.MemberId memberId)
        {
            if (!_symbols.TryGetValue(memberId.Value, out var info))
            {
                return false;
            }

            return _references.GetReferencingFunctions(memberId.Value)
                .Any(caller => _functionIndex.NodesByMemberId.TryGetValue(caller.Value, out var node) &&
                               string.Equals(node.DeclaringTypeId, info.DeclaringTypeId, StringComparison.Ordinal));
        }

        public bool HasExternalMethodReferences(ModelPrimitives.MemberId memberId)
        {
            if (!_symbols.TryGetValue(memberId.Value, out var info))
            {
                return false;
            }

            return _references.GetReferencingFunctions(memberId.Value)
                .Any(caller => _functionIndex.NodesByMemberId.TryGetValue(caller.Value, out var node) &&
                               !string.Equals(node.DeclaringTypeId, info.DeclaringTypeId, StringComparison.Ordinal));
        }

        public IReadOnlyList<ModelPrimitives.MemberId> GetReorderablePublicMethods(string typeId) =>
            _publicMethodsByTypeId.TryGetValue(typeId, out var methods) ? methods : Array.Empty<ModelPrimitives.MemberId>();
    }

    private sealed class EmptySymbolDependencyGraphProvider : ModelAnalysis.ISymbolDependencyGraphProvider
    {
        public static EmptySymbolDependencyGraphProvider Instance { get; } = new();

        public ModelAnalysis.SymbolDependencySlice GetForwardSlice(
            IReadOnlyList<string> rootSymbolIds,
            ModelAnalysis.SymbolDependencyQueryOptions options) =>
            new(Array.Empty<ModelAnalysis.SymbolDependencyNode>(), Array.Empty<ModelAnalysis.SymbolDependencyEdge>(), Array.Empty<ModelAnalysis.SymbolDependencyPath>());
    }

    private sealed class EmptySwitchFlowSummaryService : ModelAnalysis.ISwitchFlowSummaryService
    {
        public static EmptySwitchFlowSummaryService Instance { get; } = new();
        public ModelAnalysis.SwitchFlowSummary Analyze(string targetKey) => new(Array.Empty<ModelAnalysis.SwitchCaseSummary>());
    }

    private sealed class NativeMethodCallQueryService(ModelAnalysis.FunctionFactsIndex functionFacts) : ModelAnalysis.IMethodCallQueryService
    {
        public IReadOnlyList<ModelPrimitives.MemberId> GetReachableMethods(IReadOnlyList<ModelPrimitives.MemberId> rootMemberIds)
        {
            var queue = new Queue<ModelPrimitives.MemberId>(rootMemberIds);
            var visited = new HashSet<ModelPrimitives.MemberId>(new MemberIdEqualityComparer());
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!visited.Add(current))
                {
                    continue;
                }

                if (!functionFacts.FactsByMemberId.TryGetValue(current.Value, out var fact))
                {
                    continue;
                }

                foreach (var calledMemberId in fact.CalledMemberIds)
                {
                    if (!visited.Contains(calledMemberId))
                    {
                        queue.Enqueue(calledMemberId);
                    }
                }
            }

            return visited.OrderBy(memberId => memberId.Value, StringComparer.Ordinal).ToArray();
        }
    }

    private sealed class NativeDataFlowSummaryService(
        IReadOnlyDictionary<string, ModelAnalysis.DataFlowSummary> summariesByTargetKey) : ModelAnalysis.IDataFlowSummaryService
    {
        public ModelAnalysis.DataFlowSummary Analyze(string targetKey) =>
            summariesByTargetKey.TryGetValue(targetKey, out var summary)
                ? summary
                : new ModelAnalysis.DataFlowSummary(Array.Empty<string>(), Array.Empty<string>());
    }

    private sealed class NativeCallChainAnalysisService(
        ModelAnalysis.FunctionIndex functionIndex,
        ModelAnalysis.FunctionFactsIndex functionFacts) : ModelAnalysis.ICallChainAnalysisService
    {
        public ModelAnalysis.CallChainAnalysisSummary Analyze(string memberId)
        {
            var entries = new List<ModelAnalysis.CallChainEntry>();
            var queue = new Queue<string>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            queue.Enqueue(memberId);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!visited.Add(current))
                {
                    continue;
                }

                if (functionIndex.NodesByMemberId.TryGetValue(current, out var node))
                {
                    entries.Add(new ModelAnalysis.CallChainEntry(current, node.DisplayName));
                }
                else
                {
                    entries.Add(new ModelAnalysis.CallChainEntry(current, current));
                }

                if (!functionFacts.IncomingCallersByMemberId.TryGetValue(current, out var callers))
                {
                    continue;
                }

                foreach (var caller in callers)
                {
                    if (!visited.Contains(caller.Value))
                    {
                        queue.Enqueue(caller.Value);
                    }
                }
            }

            return new ModelAnalysis.CallChainAnalysisSummary(entries);
        }
    }

    private sealed class NativeAdvancedAnalysisSummaryService(
        IReadOnlyDictionary<string, ModelAnalysis.MemberCleanupTypeInfo> typeInfos,
        IReadOnlyDictionary<string, ModelAnalysis.MemberCleanupSymbolInfo> symbolInfos) : ModelAnalysis.IAdvancedAnalysisSummaryService
    {
        public ModelAnalysis.AdvancedAnalysisSummary BuildSummary()
        {
            var persistentTypeCount = typeInfos.Values.Count(type => type.IsPublic || type.IsInInheritanceChain);
            var riskyTypeCount = symbolInfos.Values.Count(symbol => symbol.IsExtern || symbol.IsEntryPointLike);
            var notes = typeInfos.Values
                .Where(type => type.IsInInheritanceChain)
                .Select(type => $"Inheritance:{type.TypeId}")
                .Take(16)
                .ToArray();
            return new ModelAnalysis.AdvancedAnalysisSummary(
                persistentTypeCount,
                riskyTypeCount,
                notes);
        }
    }

    private sealed class MemberIdEqualityComparer : IEqualityComparer<ModelPrimitives.MemberId>
    {
        public bool Equals(ModelPrimitives.MemberId x, ModelPrimitives.MemberId y) =>
            string.Equals(x.Value, y.Value, StringComparison.Ordinal);

        public int GetHashCode(ModelPrimitives.MemberId obj) => StringComparer.Ordinal.GetHashCode(obj.Value);
    }
}
