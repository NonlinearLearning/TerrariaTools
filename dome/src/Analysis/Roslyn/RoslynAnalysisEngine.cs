using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome.Analysis.Roslyn;

using TerrariaTools.Dome.Core;

/// <summary>
/// Roslyn分析文档记录，包含源文档、语法根、语义模型和分析目标。
/// </summary>
/// <param name="Document">源文档。</param>
/// <param name="Root">编译单元语法根。</param>
/// <param name="SemanticModel">语义模型。</param>
/// <param name="Targets">分析目标列表。</param>
public sealed record RoslynAnalysisDocument(
    SourceDocument Document,
    CompilationUnitSyntax Root,
    SemanticModel SemanticModel,
    IReadOnlyList<AnalysisTarget> Targets);

/// <summary>
/// Roslyn分析结果记录，包含分析视图和文档列表。
/// </summary>
/// <param name="View">分析视图。</param>
/// <param name="Documents">Roslyn分析文档列表。</param>
public sealed record RoslynAnalysisResult(
    AnalysisView View,
    IReadOnlyList<RoslynAnalysisDocument> Documents,
    FunctionIndex FunctionIndex,
    FunctionFactsIndex FunctionFacts);

/// <summary>
/// Roslyn分析引擎，负责协调文档的解析、编译和依赖分析。
/// 类依赖图是读取sln项目就进行全部分析，函数依赖分析需要支持性能更好的动态文件范围分析和全sln项目分析
/// 而语句分析初版需要支持最小作用域分析,然后支持引用关系穿透,即跨越作用域但此阶段不会跨越函数作用域和类作用域
/// 下一阶段是支持域分析穿透支持到函数和类成员属性.下阶段更为复杂需要支持多作用跨越和不同作用域的混合分析
/// </summary>
public sealed class RoslynAnalysisEngine
{
    /// <summary>
    /// 异步执行分析。
    /// </summary>
    /// <param name="documents">源文档列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>Roslyn分析结果。</returns>
    public Task<RoslynAnalysisResult> AnalyzeAsync(
        IReadOnlyList<SourceDocument> documents,
        CancellationToken cancellationToken)
    {
        return AnalyzeAsync(
            new SourceOnlyAnalysisInput(
                documents.Count == 0 ? string.Empty : Path.GetDirectoryName(documents[0].SourcePath) ?? string.Empty,
                documents),
            cancellationToken);
    }

    public async Task<RoslynAnalysisResult> AnalyzeAsync(
        AnalysisInput input,
        CancellationToken cancellationToken)
    {
        return input switch
        {
            SourceOnlyAnalysisInput sourceOnly => await AnalyzeSourceOnlyAsync(sourceOnly, cancellationToken),
            WorkspaceAnalysisInput workspace => await AnalyzeWorkspaceAsync(workspace, cancellationToken),
            _ => throw new NotSupportedException($"Unsupported analysis input '{input.GetType().Name}'.")
        };
    }

    /// <summary>
    /// 仅分析源码模式。
    /// </summary>
    private Task<RoslynAnalysisResult> AnalyzeSourceOnlyAsync(
        SourceOnlyAnalysisInput input,
        CancellationToken cancellationToken)
    {
        var documents = input.Documents;
        var trees = documents
            .Select(document => CSharpSyntaxTree.ParseText(document.SourceText, path: document.SourcePath))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "DomeAnalysis",
            trees,
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });

        var analyzedDocuments = new List<RoslynAnalysisDocument>(documents.Count);
        var allTargets = new List<AnalysisTarget>();
        var allEdges = new List<AnalysisEdge>();
        var typeNodes = new Dictionary<string, TypeNodeRef>(StringComparer.Ordinal);
        var typeEdges = new HashSet<TypeDependencyEdge>();
        var functionNodes = new Dictionary<string, FunctionNodeRef>(StringComparer.Ordinal);

        for (var index = 0; index < trees.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tree = trees[index];
            var root = tree.GetCompilationUnitRoot(cancellationToken);
            var semanticModel = compilation.GetSemanticModel(tree);

            RegisterTypeGraphDocuments(documents[index], root, semanticModel, typeNodes, typeEdges);
            RegisterFunctionNodes(documents[index], root, semanticModel, functionNodes);
            RegisterTypeBodyGraphs(root, semanticModel, typeEdges);

            var targets = AnalyzeDocument(documents[index], root, semanticModel, allEdges);
            analyzedDocuments.Add(new RoslynAnalysisDocument(documents[index], root, semanticModel, targets));
            allTargets.AddRange(targets);
        }

        //抽象写法
        //语句级分析不能使用sln项目级的全量分析
        var functionIndex = BuildFunctionIndex(functionNodes.Values);
        var functionFacts = BuildFunctionFactsIndex(functionNodes.Values, analyzedDocuments);
        var view = new AnalysisView(
            allTargets,
            allEdges,
            new TypeDependencyGraph(
                typeNodes.Values.OrderBy(node => node.TypeId, StringComparer.Ordinal).ToArray(),
                typeEdges.OrderBy(edge => edge.SourceTypeId, StringComparer.Ordinal)
                    .ThenBy(edge => edge.TargetTypeId, StringComparer.Ordinal)
                    .ThenBy(edge => edge.Kind)
                    .ToArray()),
            new FunctionDependencyGraph(Array.Empty<FunctionNodeRef>(), Array.Empty<FunctionDependencyEdge>()),
            new StatementDependencyGraph(Array.Empty<string>(), Array.Empty<StatementDependencyEdge>()),
            StatementGraphMaterialization.SnapshotOnly,
            FunctionGraphMaterialization.None);

        return Task.FromResult(new RoslynAnalysisResult(view, analyzedDocuments, functionIndex, functionFacts));
    }

    /// <summary>
    /// 分析工作区模式。
    /// </summary>
    private async Task<RoslynAnalysisResult> AnalyzeWorkspaceAsync(
        WorkspaceAnalysisInput input,
        CancellationToken cancellationToken)
    {
        var analyzedDocuments = new List<RoslynAnalysisDocument>(input.Documents.Count);
        var allTargets = new List<AnalysisTarget>();
        var allEdges = new List<AnalysisEdge>();
        var typeNodes = new Dictionary<string, TypeNodeRef>(StringComparer.Ordinal);
        var typeEdges = new HashSet<TypeDependencyEdge>();
        var functionNodes = new Dictionary<string, FunctionNodeRef>(StringComparer.Ordinal);

        foreach (var documentContext in input.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var root = documentContext.Root as CompilationUnitSyntax
                ?? throw new InvalidOperationException("WorkspaceDocumentContext.Root must be a CompilationUnitSyntax.");
            var sourceDocument = documentContext.SourceDocument;
            var semanticModel = documentContext.SemanticModel;

            RegisterTypeGraphDocuments(sourceDocument, root, semanticModel, typeNodes, typeEdges);
            RegisterFunctionNodes(sourceDocument, root, semanticModel, functionNodes);
            RegisterTypeBodyGraphs(root, semanticModel, typeEdges);

            var targets = AnalyzeDocument(sourceDocument, root, semanticModel, allEdges);
            analyzedDocuments.Add(new RoslynAnalysisDocument(sourceDocument, root, semanticModel, targets));
            allTargets.AddRange(targets);
        }

        var functionIndex = BuildFunctionIndex(functionNodes.Values);
        var functionFacts = BuildFunctionFactsIndex(functionNodes.Values, analyzedDocuments);
        var view = new AnalysisView(
            allTargets,
            allEdges,
            new TypeDependencyGraph(
                typeNodes.Values.OrderBy(node => node.TypeId, StringComparer.Ordinal).ToArray(),
                typeEdges.OrderBy(edge => edge.SourceTypeId, StringComparer.Ordinal)
                    .ThenBy(edge => edge.TargetTypeId, StringComparer.Ordinal)
                    .ThenBy(edge => edge.Kind)
                    .ToArray()),
            new FunctionDependencyGraph(Array.Empty<FunctionNodeRef>(), Array.Empty<FunctionDependencyEdge>()),
            new StatementDependencyGraph(Array.Empty<string>(), Array.Empty<StatementDependencyEdge>()),
            StatementGraphMaterialization.SnapshotOnly,
            FunctionGraphMaterialization.None);

        return new RoslynAnalysisResult(view, analyzedDocuments, functionIndex, functionFacts);
    }

    /// <summary>
    /// 根据分析结果创建分析上下文。
    /// 上下文需要根据文件数量以及作用范围来维护上下文,要么就是不变上下放弃维护
    /// 需要确定上下文需要传递什么数据和大小
    /// </summary>
    /// <param name="result">Roslyn分析结果。</param>
    /// <returns>分析上下文。</returns>
    public AnalysisSnapshot CreateSnapshot(RoslynAnalysisResult result)
    {
        return new AnalysisSnapshot(
            result.View,
            result.FunctionIndex,
            result.FunctionFacts,
            BuildStatementFactsIndex(result.View.Targets));
    }

    /// <summary>
    /// 创建分析服务。
    /// </summary>
    /// <param name="result">Roslyn分析结果。</param>
    /// <returns>分析服务。</returns>
    public AnalysisServices CreateServices(RoslynAnalysisResult result)
    {
        return CreateContext(result).Services;
    }

    /// <summary>
    /// 创建分析上下文。
    /// </summary>
    /// <param name="result">Roslyn分析结果。</param>
    /// <returns>分析上下文。</returns>
    public AnalysisContext CreateContext(RoslynAnalysisResult result)
    {
        var snapshot = CreateSnapshot(result);
        var services = CreateServicesCore(result.Documents, snapshot);
        return AnalysisContext.Create(snapshot, services);
    }

    /// <summary>
    /// 核心创建分析服务逻辑。
    /// </summary>
    private static AnalysisServices CreateServicesCore(
        IReadOnlyList<RoslynAnalysisDocument> documents,
        AnalysisSnapshot snapshot)
    {
        var overrideMembers = new HashSet<string>(StringComparer.Ordinal);
        var interfaceMembers = new HashSet<string>(StringComparer.Ordinal);
        var inheritanceTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var edge in snapshot.View.TypeGraph.Edges)
        {
            if (edge.Kind is TypeDependencyKind.Inherits or TypeDependencyKind.Implements)
            {
                inheritanceTypes.Add(edge.SourceTypeId);
            }

            if (edge.Kind == TypeDependencyKind.Implements && !string.IsNullOrEmpty(edge.MemberId))
            {
                interfaceMembers.Add(edge.MemberId);
            }
        }

        foreach (var document in documents)
        {
            foreach (var method in document.Root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var symbol = document.SemanticModel.GetDeclaredSymbol(method);
                if (symbol == null)
                {
                    continue;
                }

                if (symbol.IsOverride)
                {
                    overrideMembers.Add(MetadataMemberIdBuilder.Build(symbol).Value);
                }

                foreach (var iface in symbol.ContainingType.AllInterfaces)
                {
                    foreach (var ifaceMember in iface.GetMembers().OfType<IMethodSymbol>())
                    {
                        var implementation = symbol.ContainingType.FindImplementationForInterfaceMember(ifaceMember);
                        if (IsMethodImplementationMatch(symbol, implementation))
                        {
                            interfaceMembers.Add(MetadataMemberIdBuilder.Build(symbol).Value);
                        }
                    }
                }
            }

            foreach (var accessor in document.Root.DescendantNodes().OfType<AccessorDeclarationSyntax>())
            {
                var symbol = document.SemanticModel.GetDeclaredSymbol(accessor);
                if (symbol == null)
                {
                    continue;
                }

                if (symbol.IsOverride)
                {
                    overrideMembers.Add(MetadataMemberIdBuilder.Build(symbol).Value);
                }

                foreach (var iface in symbol.ContainingType.AllInterfaces)
                {
                    foreach (var ifaceMember in iface.GetMembers().OfType<IMethodSymbol>())
                    {
                        var implementation = symbol.ContainingType.FindImplementationForInterfaceMember(ifaceMember);
                        if (IsMethodImplementationMatch(symbol, implementation))
                        {
                            interfaceMembers.Add(MetadataMemberIdBuilder.Build(symbol).Value);
                        }
                    }
                }
            }
        }

        var memberToFunctions = new Dictionary<string, HashSet<MemberId>>(StringComparer.Ordinal);
        var memberToTypes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var fact in snapshot.FunctionFacts.FactsByMemberId.Values)
        {
            foreach (var calledMemberId in fact.CalledMemberIds)
            {
                RegisterReference(memberToFunctions, calledMemberId.Value, fact.Node.MemberId);
                RegisterReference(memberToTypes, calledMemberId.Value, fact.Node.DeclaringTypeId);
            }
        }

        var typeToFunctions = new Dictionary<string, HashSet<MemberId>>(StringComparer.Ordinal);
        var typeToTypes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var edge in snapshot.View.TypeGraph.Edges)
        {
            if (!string.IsNullOrEmpty(edge.MemberId))
            {
                RegisterReference(typeToFunctions, edge.TargetTypeId, new MemberId(edge.MemberId));
            }

            RegisterReference(typeToTypes, edge.TargetTypeId, edge.SourceTypeId);
        }

        return new AnalysisServices(
            new InheritanceQueryService(overrideMembers, interfaceMembers, inheritanceTypes),
            new ReferenceQueryService(memberToFunctions, memberToTypes, typeToFunctions, typeToTypes),
            new StatementAnalysisService(snapshot.StatementFacts),
            new FunctionGraphProvider(snapshot.FunctionIndex, snapshot.FunctionFacts));
    }

    /// <summary>
    /// 构建函数索引。
    /// </summary>
    private static FunctionIndex BuildFunctionIndex(IEnumerable<FunctionNodeRef> nodes)
    {
        var nodeArray = nodes.ToArray();
        var nodesByMemberId = nodeArray.ToDictionary(node => node.MemberId.Value, StringComparer.Ordinal);
        var memberIdsByDocumentPath = nodeArray
            .GroupBy(node => node.DocumentPath, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(node => node.MemberId.Value)
                    .OrderBy(memberId => memberId, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        return new FunctionIndex(nodesByMemberId, memberIdsByDocumentPath);
    }

    /// <summary>
    /// 构建函数事实索引。
    /// </summary>
    private static FunctionFactsIndex BuildFunctionFactsIndex(
        IEnumerable<FunctionNodeRef> nodes,
        IEnumerable<RoslynAnalysisDocument> documents)
    {
        var nodeArray = nodes.ToArray();
        var calledMembersByMemberId = documents
            .SelectMany(CreateFunctionFactsForDocument)
            .GroupBy(item => item.MemberId.Value, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<MemberId>)group
                    .SelectMany(item => item.CalledMemberIds)
                    .DistinctBy(memberId => memberId.Value)
                    .OrderBy(memberId => memberId.Value, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
        var factsByMemberId = nodeArray.ToDictionary(
            node => node.MemberId.Value,
            node => new FunctionFact(
                node,
                calledMembersByMemberId.TryGetValue(node.MemberId.Value, out var calledMemberIds)
                    ? calledMemberIds
                    : Array.Empty<MemberId>()),
            StringComparer.Ordinal);
        var memberIdsByDocumentPath = nodeArray
            .GroupBy(node => node.DocumentPath, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(node => node.MemberId.Value)
                    .OrderBy(memberId => memberId, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
        var incomingCallersByMemberId = new Dictionary<string, IReadOnlyList<MemberId>>(StringComparer.Ordinal);
        foreach (var fact in factsByMemberId.Values)
        {
            foreach (var calledMemberId in fact.CalledMemberIds)
            {
                if (!incomingCallersByMemberId.TryGetValue(calledMemberId.Value, out var callers))
                {
                    callers = Array.Empty<MemberId>();
                }

                incomingCallersByMemberId[calledMemberId.Value] = callers
                    .Append(fact.Node.MemberId)
                    .DistinctBy(memberId => memberId.Value)
                    .OrderBy(memberId => memberId.Value, StringComparer.Ordinal)
                    .ToArray();
            }
        }

        return new FunctionFactsIndex(factsByMemberId, memberIdsByDocumentPath, incomingCallersByMemberId);
    }

    /// <summary>
    /// 为文档创建函数事实。
    /// </summary>
    private static IEnumerable<(MemberId MemberId, IReadOnlyList<MemberId> CalledMemberIds)> CreateFunctionFactsForDocument(
        RoslynAnalysisDocument document)
    {
        foreach (var method in document.Root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var symbol = document.SemanticModel.GetDeclaredSymbol(method);
            if (symbol == null)
            {
                continue;
            }

            yield return (
                MetadataMemberIdBuilder.Build(symbol),
                CollectCalledMemberIds(GetBodyOrExpression(method), document.SemanticModel));
        }

        foreach (var ctor in document.Root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
        {
            var symbol = document.SemanticModel.GetDeclaredSymbol(ctor);
            if (symbol == null)
            {
                continue;
            }

            yield return (
                MetadataMemberIdBuilder.Build(symbol),
                CollectCalledMemberIds(GetBodyOrExpression(ctor), document.SemanticModel));
        }

        foreach (var accessor in document.Root.DescendantNodes().OfType<AccessorDeclarationSyntax>())
        {
            var symbol = document.SemanticModel.GetDeclaredSymbol(accessor);
            if (symbol == null)
            {
                continue;
            }

            yield return (
                MetadataMemberIdBuilder.Build(symbol),
                CollectCalledMemberIds(GetBodyOrExpression(accessor), document.SemanticModel));
        }
    }

    /// <summary>
    /// 注册引用关系。
    /// </summary>
    private static void RegisterReference<TValue>(
        IDictionary<string, HashSet<TValue>> map,
        string key,
        TValue value)
        where TValue : notnull
    {
        if (!map.TryGetValue(key, out var values))
        {
            values = new HashSet<TValue>();
            map[key] = values;
        }

        values.Add(value);
    }

    /// <summary>
    /// 构建语句依赖图。
    /// </summary>
    private static StatementFactsIndex BuildStatementFactsIndex(IReadOnlyList<AnalysisTarget> targets)
    {
        var buckets = targets
            .Where(target => target.Target.TargetKind == TargetKind.Statement)
            .GroupBy(target => target.Target.MemberId.Value, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<StatementFact>)group
                    .OrderBy(target => target.Target.SpanStart)
                    .ThenBy(target => target.Target.TargetKey, StringComparer.Ordinal)
                    .Select(target => new StatementFact(
                        target.Target.TargetKey,
                        target.Target.MemberId,
                        target.StatementKind,
                        target.DefinesSymbols,
                        target.UsesSymbols,
                        target.InvokedMemberIds,
                        target.ScopeMode,
                        target.ScopeId,
                        target.ParentScopeId,
                        target.Target.SpanStart,
                        target.Target.SpanLength))
                    .ToArray(),
                StringComparer.Ordinal);

        return new StatementFactsIndex(buckets);
    }

    /// <summary>
    /// 分析单个文档，提取分析目标。
    /// </summary>
    private static IReadOnlyList<AnalysisTarget> AnalyzeDocument(
        SourceDocument document,
        CompilationUnitSyntax root,
        SemanticModel model,
        ICollection<AnalysisEdge> edges)
    {
        var targets = new List<AnalysisTarget>();
        var previousTargetsByScope = new Dictionary<string, AnalysisTarget>(StringComparer.Ordinal);

        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            foreach (var variable in field.Declaration.Variables.Where(variable => variable.Initializer != null))
            {
                var memberSymbol = model.GetDeclaredSymbol(variable);
                if (memberSymbol == null)
                {
                    continue;
                }

                var currentTarget = CreateInitializerTarget(document, field, variable.Initializer!, memberSymbol, model, MemberKind.Field);
                targets.Add(currentTarget);
                AddTargetEdges(currentTarget, ResolvePreviousTarget(previousTargetsByScope, currentTarget), edges);
            }
        }

        foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Where(property => property.Initializer != null))
        {
            var memberSymbol = model.GetDeclaredSymbol(property);
            if (memberSymbol == null)
            {
                continue;
            }

            var currentTarget = CreateInitializerTarget(document, property, property.Initializer!, memberSymbol, model, MemberKind.Property);
            targets.Add(currentTarget);
            AddTargetEdges(currentTarget, ResolvePreviousTarget(previousTargetsByScope, currentTarget), edges);
        }

        foreach (var classDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var classSymbol = model.GetDeclaredSymbol(classDeclaration);
            if (classSymbol == null)
            {
                continue;
            }

            targets.Add(CreateClassTarget(document, classDeclaration, classSymbol));
        }

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var memberSymbol = model.GetDeclaredSymbol(method);
            if (memberSymbol == null)
            {
                continue;
            }

            foreach (var statement in EnumerateStatements(method.Body))
            {
                var currentTarget = CreateStatementTarget(document, statement, memberSymbol, model, MemberKind.Method);
                targets.Add(currentTarget);
                AddTargetEdges(currentTarget, ResolvePreviousTarget(previousTargetsByScope, currentTarget), edges);
            }
        }

        foreach (var ctor in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
        {
            var memberSymbol = model.GetDeclaredSymbol(ctor);
            if (memberSymbol == null)
            {
                continue;
            }

            foreach (var statement in EnumerateStatements(ctor.Body))
            {
                var currentTarget = CreateStatementTarget(document, statement, memberSymbol, model, MemberKind.Constructor);
                targets.Add(currentTarget);
                AddTargetEdges(currentTarget, ResolvePreviousTarget(previousTargetsByScope, currentTarget), edges);
            }
        }

        foreach (var accessor in root.DescendantNodes().OfType<AccessorDeclarationSyntax>())
        {
            var memberSymbol = model.GetDeclaredSymbol(accessor);
            if (memberSymbol == null)
            {
                continue;
            }

            foreach (var statement in EnumerateStatements(accessor.Body))
            {
                var currentTarget = CreateStatementTarget(document, statement, memberSymbol, model, MemberKind.Accessor);
                targets.Add(currentTarget);
                AddTargetEdges(currentTarget, ResolvePreviousTarget(previousTargetsByScope, currentTarget), edges);
            }
        }

        return targets;
    }

    /// <summary>
    /// 创建类目标。
    /// </summary>
    private static AnalysisTarget CreateClassTarget(
        SourceDocument document,
        ClassDeclarationSyntax classDeclaration,
        INamedTypeSymbol classSymbol)
    {
        var typeId = MetadataTypeIdBuilder.Build(classSymbol);
        return new AnalysisTarget(
            new PlanTarget(
                document.RelativePath,
                new MemberId(typeId),
                MemberKind.Class,
                TargetKind.Class,
                classDeclaration.SpanStart,
                classDeclaration.Span.Length,
                typeId),
            classSymbol.IsAbstract ||
            classSymbol.DeclaredAccessibility == Accessibility.Public ||
            classSymbol.TypeParameters.Length > 0,
            Array.Empty<DirectiveAction>(),
            Array.Empty<SymbolRef>(),
            Array.Empty<SymbolRef>(),
            Array.Empty<MemberId>(),
            StatementKindRef.Unknown,
            false,
            false,
            false,
            Array.Empty<string>(),
            StatementScopeMode.MinimalBlock,
            null,
            null);
    }

    /// <summary>
    /// 解析前一个目标，用于建立执行顺序。
    /// </summary>
    private static AnalysisTarget? ResolvePreviousTarget(
        IDictionary<string, AnalysisTarget> previousTargetsByScope,
        AnalysisTarget currentTarget)
    {
        AnalysisTarget? previousTarget = null;

        if (!string.IsNullOrEmpty(currentTarget.ScopeId))
        {
            previousTargetsByScope.TryGetValue(currentTarget.ScopeId, out previousTarget);
        }

        if (!string.IsNullOrEmpty(currentTarget.ScopeId))
        {
            previousTargetsByScope[currentTarget.ScopeId] = currentTarget;
        }

        return previousTarget;
    }

    /// <summary>
    /// 注册类型图文档。
    /// </summary>
    private static void RegisterTypeGraphDocuments(
        SourceDocument document,
        CompilationUnitSyntax root,
        SemanticModel model,
        IDictionary<string, TypeNodeRef> typeNodes,
        ISet<TypeDependencyEdge> typeEdges)
    {
        foreach (var typeDeclaration in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            var typeSymbol = model.GetDeclaredSymbol(typeDeclaration) as INamedTypeSymbol;
            if (typeSymbol == null)
            {
                continue;
            }

            var typeId = MetadataTypeIdBuilder.Build(typeSymbol);
            typeNodes[typeId] = new TypeNodeRef(typeId, typeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), document.RelativePath);

            if (typeSymbol.BaseType != null && typeSymbol.BaseType.SpecialType != SpecialType.System_Object)
            {
                typeEdges.Add(new TypeDependencyEdge(typeId, MetadataTypeIdBuilder.Build(typeSymbol.BaseType), TypeDependencyKind.Inherits));
            }

            foreach (var iface in typeSymbol.Interfaces)
            {
                typeEdges.Add(new TypeDependencyEdge(typeId, MetadataTypeIdBuilder.Build(iface), TypeDependencyKind.Implements));
            }

            foreach (var field in typeSymbol.GetMembers().OfType<IFieldSymbol>())
            {
                typeEdges.Add(new TypeDependencyEdge(typeId, MetadataTypeIdBuilder.Build(field.Type), TypeDependencyKind.FieldType, MetadataMemberIdBuilder.Build(field).Value));
            }

            foreach (var property in typeSymbol.GetMembers().OfType<IPropertySymbol>())
            {
                typeEdges.Add(new TypeDependencyEdge(typeId, MetadataTypeIdBuilder.Build(property.Type), TypeDependencyKind.PropertyType, MetadataMemberIdBuilder.Build(property).Value));
            }

            foreach (var method in typeSymbol.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.Constructor or MethodKind.PropertyGet or MethodKind.PropertySet))
                {
                    continue;
                }

                foreach (var parameter in method.Parameters)
                {
                    typeEdges.Add(new TypeDependencyEdge(typeId, MetadataTypeIdBuilder.Build(parameter.Type), TypeDependencyKind.ParameterType, MetadataMemberIdBuilder.Build(method).Value));
                }

                if (method.MethodKind != MethodKind.Constructor)
                {
                    typeEdges.Add(new TypeDependencyEdge(typeId, MetadataTypeIdBuilder.Build(method.ReturnType), TypeDependencyKind.ReturnType, MetadataMemberIdBuilder.Build(method).Value));
                }
            }
        }
    }

    /// <summary>
    /// 注册函数节点。
    /// </summary>
    private static void RegisterFunctionNodes(
        SourceDocument document,
        CompilationUnitSyntax root,
        SemanticModel model,
        IDictionary<string, FunctionNodeRef> functionNodes)
    {
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            RegisterFunctionNode(model.GetDeclaredSymbol(method), MemberKind.Method, document.RelativePath, functionNodes);
        }

        foreach (var ctor in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
        {
            RegisterFunctionNode(model.GetDeclaredSymbol(ctor), MemberKind.Constructor, document.RelativePath, functionNodes);
        }

        foreach (var accessor in root.DescendantNodes().OfType<AccessorDeclarationSyntax>())
        {
            RegisterFunctionNode(model.GetDeclaredSymbol(accessor), MemberKind.Accessor, document.RelativePath, functionNodes);
        }
    }

    private static void RegisterTypeBodyGraphs(
        CompilationUnitSyntax root,
        SemanticModel model,
        ISet<TypeDependencyEdge> typeEdges)
    {
        var ignoredFunctionEdges = new HashSet<FunctionDependencyEdge>();

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var symbol = model.GetDeclaredSymbol(method);
            if (symbol != null)
            {
                RegisterFunctionBodyDependencies(symbol, GetBodyOrExpression(method), model, typeEdges, ignoredFunctionEdges);
            }
        }

        foreach (var ctor in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
        {
            var symbol = model.GetDeclaredSymbol(ctor);
            if (symbol != null)
            {
                RegisterFunctionBodyDependencies(symbol, GetBodyOrExpression(ctor), model, typeEdges, ignoredFunctionEdges);
            }
        }

        foreach (var accessor in root.DescendantNodes().OfType<AccessorDeclarationSyntax>())
        {
            var symbol = model.GetDeclaredSymbol(accessor);
            if (symbol != null)
            {
                RegisterFunctionBodyDependencies(symbol, GetBodyOrExpression(accessor), model, typeEdges, ignoredFunctionEdges);
            }
        }

        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            foreach (var variable in field.Declaration.Variables.Where(variable => variable.Initializer != null))
            {
                var fieldSymbol = model.GetDeclaredSymbol(variable) as IFieldSymbol;
                if (fieldSymbol?.ContainingType != null)
                {
                    RegisterTypeBodyDependencies(fieldSymbol.ContainingType, variable.Initializer!.Value, model, typeEdges);
                }
            }
        }

        foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Where(property => property.Initializer != null))
        {
            var propertySymbol = model.GetDeclaredSymbol(property) as IPropertySymbol;
            if (propertySymbol?.ContainingType != null)
            {
                RegisterTypeBodyDependencies(propertySymbol.ContainingType, property.Initializer!.Value, model, typeEdges);
            }
        }
    }

    /// <summary>
    /// 注册单个函数节点。
    /// </summary>
    private static void RegisterFunctionNode(
        ISymbol? symbol,
        MemberKind memberKind,
        string documentPath,
        IDictionary<string, FunctionNodeRef> functionNodes)
    {
        if (symbol == null)
        {
            return;
        }

        var memberId = MetadataMemberIdBuilder.Build(symbol);
        var declarationSyntax = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        var methodSymbol = symbol as IMethodSymbol;
        var returnsVoid = methodSymbol?.ReturnsVoid ?? false;
        var hasBody = declarationSyntax switch
        {
            MethodDeclarationSyntax methodDeclaration => methodDeclaration.Body != null,
            ConstructorDeclarationSyntax constructorDeclaration => constructorDeclaration.Body != null,
            AccessorDeclarationSyntax accessorDeclaration => accessorDeclaration.Body != null,
            _ => false
        };
        var hasStatements = declarationSyntax switch
        {
            MethodDeclarationSyntax methodDeclaration => methodDeclaration.Body?.Statements.Count > 0,
            ConstructorDeclarationSyntax constructorDeclaration => constructorDeclaration.Body?.Statements.Count > 0,
            AccessorDeclarationSyntax accessorDeclaration => accessorDeclaration.Body?.Statements.Count > 0,
            _ => false
        };
        var returnTypeDisplay = methodSymbol?.ReturnType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? "void";
        functionNodes[memberId.Value] = new FunctionNodeRef(
            memberId,
            memberKind,
            MetadataTypeIdBuilder.Build(symbol.ContainingType),
            symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            documentPath,
            declarationSyntax?.SpanStart ?? -1,
            declarationSyntax?.Span.Length ?? 0,
            symbol.DeclaredAccessibility == Accessibility.Private,
            returnsVoid,
            hasBody,
            hasStatements,
            returnTypeDisplay);
    }

    /// <summary>
    /// 注册函数体和类型体的依赖图。
    /// </summary>
    private static void RegisterBodyGraphs(
        CompilationUnitSyntax root,
        SemanticModel model,
        ISet<TypeDependencyEdge> typeEdges,
        ISet<FunctionDependencyEdge> functionEdges)
    {
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var symbol = model.GetDeclaredSymbol(method);
            if (symbol != null)
            {
                RegisterFunctionBodyDependencies(symbol, method.Body ?? (SyntaxNode?)method.ExpressionBody, model, typeEdges, functionEdges);
            }
        }

        foreach (var ctor in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
        {
            var symbol = model.GetDeclaredSymbol(ctor);
            if (symbol != null)
            {
                RegisterFunctionBodyDependencies(symbol, ctor.Body ?? (SyntaxNode?)ctor.ExpressionBody, model, typeEdges, functionEdges);
            }
        }

        foreach (var accessor in root.DescendantNodes().OfType<AccessorDeclarationSyntax>())
        {
            var symbol = model.GetDeclaredSymbol(accessor);
            if (symbol != null)
            {
                RegisterFunctionBodyDependencies(symbol, accessor.Body ?? (SyntaxNode?)accessor.ExpressionBody, model, typeEdges, functionEdges);
            }
        }

        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            foreach (var variable in field.Declaration.Variables.Where(variable => variable.Initializer != null))
            {
                var fieldSymbol = model.GetDeclaredSymbol(variable) as IFieldSymbol;
                if (fieldSymbol?.ContainingType != null)
                {
                    RegisterTypeBodyDependencies(fieldSymbol.ContainingType, variable.Initializer!.Value, model, typeEdges);
                }
            }
        }

        foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Where(property => property.Initializer != null))
        {
            var propertySymbol = model.GetDeclaredSymbol(property) as IPropertySymbol;
            if (propertySymbol?.ContainingType != null)
            {
                RegisterTypeBodyDependencies(propertySymbol.ContainingType, property.Initializer!.Value, model, typeEdges);
            }
        }
    }

    /// <summary>
    /// 注册函数体依赖。
    /// </summary>
    private static void RegisterFunctionBodyDependencies(
        ISymbol currentMember,
        SyntaxNode? bodyOrExpression,
        SemanticModel model,
        ISet<TypeDependencyEdge> typeEdges,
        ISet<FunctionDependencyEdge> functionEdges)
    {
        if (bodyOrExpression == null)
        {
            return;
        }

        var currentMemberId = MetadataMemberIdBuilder.Build(currentMember);
        var currentTypeId = MetadataTypeIdBuilder.Build(currentMember.ContainingType);

        foreach (var invocation in bodyOrExpression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (model.GetSymbolInfo(invocation).Symbol is IMethodSymbol targetMethod)
            {
                functionEdges.Add(new FunctionDependencyEdge(currentMemberId, MetadataMemberIdBuilder.Build(targetMethod), FunctionDependencyKind.Calls));
                RegisterTypeReferenceEdge(currentTypeId, targetMethod.ContainingType, TypeDependencyKind.MemberBodyReference, typeEdges, currentMemberId);
            }
        }

        foreach (var creation in bodyOrExpression.DescendantNodesAndSelf().OfType<BaseObjectCreationExpressionSyntax>())
        {
            if (model.GetSymbolInfo(creation).Symbol is IMethodSymbol ctorSymbol)
            {
                functionEdges.Add(new FunctionDependencyEdge(currentMemberId, MetadataMemberIdBuilder.Build(ctorSymbol), FunctionDependencyKind.Creates));
                RegisterTypeReferenceEdge(currentTypeId, ctorSymbol.ContainingType, TypeDependencyKind.ObjectCreation, typeEdges, currentMemberId);
            }
        }

        foreach (var identifier in bodyOrExpression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            RegisterMemberReference(identifier, currentMemberId, currentTypeId, model, typeEdges, functionEdges);
        }

        foreach (var memberAccess in bodyOrExpression.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
        {
            if (model.GetSymbolInfo(memberAccess).Symbol?.IsStatic == true)
            {
                RegisterTypeReferenceEdge(currentTypeId, model.GetSymbolInfo(memberAccess).Symbol?.ContainingType, TypeDependencyKind.StaticMemberAccess, typeEdges, currentMemberId);
            }
        }
    }

    /// <summary>
    /// 注册成员引用。
    /// </summary>
    private static void RegisterMemberReference(
        IdentifierNameSyntax identifier,
        MemberId currentMemberId,
        string currentTypeId,
        SemanticModel model,
        ISet<TypeDependencyEdge> typeEdges,
        ISet<FunctionDependencyEdge> functionEdges)
    {
        var symbol = model.GetSymbolInfo(identifier).Symbol;
        if (symbol is not (IFieldSymbol or IPropertySymbol))
        {
            return;
        }

        if (symbol.ContainingType == null)
        {
            return;
        }

        var isWrite = IsWriteTargetIdentifier(identifier);
        functionEdges.Add(new FunctionDependencyEdge(
            currentMemberId,
            MetadataMemberIdBuilder.Build(symbol),
            isWrite ? FunctionDependencyKind.WritesMember : FunctionDependencyKind.ReadsMember));
        RegisterTypeReferenceEdge(currentTypeId, symbol.ContainingType, TypeDependencyKind.MemberBodyReference, typeEdges, currentMemberId);

        if (symbol is IPropertySymbol property)
        {
            var accessor = isWrite ? property.SetMethod : property.GetMethod;
            if (accessor != null)
            {
                functionEdges.Add(new FunctionDependencyEdge(currentMemberId, MetadataMemberIdBuilder.Build(accessor), FunctionDependencyKind.UsesPropertyAccessor));
            }
        }
    }

    /// <summary>
    /// 注册类型引用边。
    /// </summary>
    private static void RegisterTypeReferenceEdge(
        string currentTypeId,
        ITypeSymbol? targetType,
        TypeDependencyKind kind,
        ISet<TypeDependencyEdge> typeEdges,
        MemberId currentMemberId)
    {
        if (targetType == null)
        {
            return;
        }

        typeEdges.Add(new TypeDependencyEdge(currentTypeId, MetadataTypeIdBuilder.Build(targetType), kind, currentMemberId.Value));
    }

    /// <summary>
    /// 注册类型体依赖。
    /// </summary>
    private static void RegisterTypeBodyDependencies(
        INamedTypeSymbol containingType,
        SyntaxNode node,
        SemanticModel model,
        ISet<TypeDependencyEdge> typeEdges)
    {
        var currentTypeId = MetadataTypeIdBuilder.Build(containingType);

        foreach (var creation in node.DescendantNodesAndSelf().OfType<BaseObjectCreationExpressionSyntax>())
        {
            if (model.GetSymbolInfo(creation).Symbol is IMethodSymbol ctorSymbol)
            {
            typeEdges.Add(new TypeDependencyEdge(currentTypeId, MetadataTypeIdBuilder.Build(ctorSymbol.ContainingType), TypeDependencyKind.ObjectCreation));
        }
        }

        foreach (var memberAccess in node.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
        {
            if (model.GetSymbolInfo(memberAccess).Symbol is not ISymbol memberSymbol)
            {
                continue;
            }

            var kind = memberSymbol.IsStatic ? TypeDependencyKind.StaticMemberAccess : TypeDependencyKind.MemberBodyReference;
            typeEdges.Add(new TypeDependencyEdge(currentTypeId, MetadataTypeIdBuilder.Build(memberSymbol.ContainingType), kind));
        }
    }

    private static SyntaxNode? GetBodyOrExpression(BaseMethodDeclarationSyntax declaration)
    {
        return declaration switch
        {
            MethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody?.Expression,
            ConstructorDeclarationSyntax ctor => (SyntaxNode?)ctor.Body ?? ctor.ExpressionBody?.Expression,
            _ => null
        };
    }

    private static SyntaxNode? GetBodyOrExpression(AccessorDeclarationSyntax accessor) =>
        (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody?.Expression;

    private static IReadOnlyList<MemberId> CollectCalledMemberIds(SyntaxNode? bodyOrExpression, SemanticModel model)
    {
        if (bodyOrExpression == null)
        {
            return Array.Empty<MemberId>();
        }

        return bodyOrExpression.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Select(invocation => model.GetSymbolInfo(invocation).Symbol)
            .OfType<IMethodSymbol>()
            .Select(MetadataMemberIdBuilder.Build)
            .DistinctBy(memberId => memberId.Value)
            .OrderBy(memberId => memberId.Value, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// 枚举语句块中的语句。
    /// </summary>
    private static IEnumerable<StatementSyntax> EnumerateStatements(BlockSyntax? body)
    {
        if (body == null)
        {
            yield break;
        }

        foreach (var statement in body.DescendantNodes(descendIntoChildren: _ => true).OfType<StatementSyntax>())
        {
            if (statement is BlockSyntax)
            {
                continue;
            }

            yield return statement;
        }
    }

    /// <summary>
    /// 创建初始化器目标。
    /// </summary>
    private static AnalysisTarget CreateInitializerTarget(
        SourceDocument document,
        CSharpSyntaxNode declarationNode,
        EqualsValueClauseSyntax initializer,
        ISymbol memberSymbol,
        SemanticModel model,
        MemberKind memberKind)
    {
        var memberId = MetadataMemberIdBuilder.Build(memberSymbol);
        var definesSymbols = GetDefinedSymbols(initializer, model, memberId, memberSymbol);
        var usesSymbols = GetUsedSymbols(initializer, model, memberId, memberSymbol);
        return new AnalysisTarget(
            new PlanTarget(
                document.RelativePath,
                memberId,
                memberKind,
                TargetKind.Statement,
                declarationNode.SpanStart,
                declarationNode.Span.Length,
                declarationNode.ToString().Trim()),
            IsHighRiskMember(memberSymbol),
            Array.Empty<DirectiveAction>(),
            definesSymbols,
            usesSymbols,
            Array.Empty<MemberId>(),
            StatementKindRef.Initializer,
            IsSanitizingNode(initializer, model, memberId),
            false,
            false,
            Array.Empty<string>(),
            StatementScopeMode.MinimalBlock,
            memberId.Value,
            null);
    }

    /// <summary>
    /// 创建语句目标。
    /// </summary>
    private static AnalysisTarget CreateStatementTarget(
        SourceDocument document,
        StatementSyntax statement,
        ISymbol memberSymbol,
        SemanticModel model,
        MemberKind memberKind)
    {
        var memberId = MetadataMemberIdBuilder.Build(memberSymbol);
        var definesSymbols = GetDefinedSymbols(statement, model, memberId);
        var usesSymbols = GetUsedSymbols(statement, model, memberId);
        var invokedMemberIds = GetInvokedMemberIds(statement, model);
        var statementKind = ClassifyStatementKind(statement);
        var isObjectInitializerAssignment = IsObjectInitializerAssignment(statement);
        var markedExpressionKinds = GetMarkedExpressionKinds(statement);
        var scopeInfo = GetScopeInfo(statement);
        return new AnalysisTarget(
            new PlanTarget(
                document.RelativePath,
                memberId,
                memberKind,
                TargetKind.Statement,
                statement.SpanStart,
                statement.Span.Length,
                statement.ToString().Trim()),
            IsHighRiskMember(memberSymbol) || isObjectInitializerAssignment,
            DirectiveReader.Read(statement),
            definesSymbols,
            usesSymbols,
            invokedMemberIds,
            statementKind,
            IsSanitizingNode(statement, model, memberId),
            isObjectInitializerAssignment,
            markedExpressionKinds.Length > 0,
            markedExpressionKinds,
            scopeInfo.ScopeMode,
            scopeInfo.ScopeId,
            scopeInfo.ParentScopeId);
    }

    /// <summary>
    /// 获取语句直接调用到的方法标识。
    /// </summary>
    private static IReadOnlyList<MemberId> GetInvokedMemberIds(StatementSyntax statement, SemanticModel model)
    {
        InvocationExpressionSyntax? invocation = statement switch
        {
            ExpressionStatementSyntax { Expression: InvocationExpressionSyntax expressionInvocation } => expressionInvocation,
            ReturnStatementSyntax { Expression: InvocationExpressionSyntax returnInvocation } => returnInvocation,
            _ => null
        };

        if (invocation == null)
        {
            return Array.Empty<MemberId>();
        }

        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol)
        {
            return Array.Empty<MemberId>();
        }

        return new[] { MetadataMemberIdBuilder.Build(methodSymbol) };
    }

    /// <summary>
    /// 计算语句的最小作用域与父块穿透信息。
    /// </summary>
    private static (StatementScopeMode ScopeMode, string ScopeId, string? ParentScopeId) GetScopeInfo(
        StatementSyntax statement)
    {
        var currentBlock = statement.Parent as BlockSyntax;
        if (currentBlock == null)
        {
            return (StatementScopeMode.MinimalBlock, CreateFallbackScopeId(statement), null);
        }

        var scopeId = CreateScopeId(currentBlock);
        var parentBlock = GetParentBlock(currentBlock);
        var parentScopeId = parentBlock == null ? null : CreateScopeId(parentBlock);

        if (parentBlock == null)
        {
            return (StatementScopeMode.MinimalBlock, scopeId, null);
        }

        return (StatementScopeMode.MinimalBlock, scopeId, parentScopeId);
    }

    private static string CreateFallbackScopeId(StatementSyntax statement) =>
        $"fallback|{statement.SyntaxTree.FilePath}|{statement.SpanStart}|{statement.Span.Length}";

    private static string CreateScopeId(BlockSyntax block) =>
        $"block|{block.SyntaxTree.FilePath}|{block.SpanStart}|{block.Span.Length}";

    private static BlockSyntax? GetParentBlock(BlockSyntax block)
    {
        var current = block.Parent;
        while (current != null)
        {
            if (current is BlockSyntax parentBlock)
            {
                return parentBlock;
            }

            if (current is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax)
            {
                return null;
            }

            current = current.Parent;
        }

        return null;
    }

    /// <summary>
    /// 获取标记的表达式类型。
    /// </summary>
    private static string[] GetMarkedExpressionKinds(StatementSyntax statement)
    {
        return statement.DescendantNodes()
            .OfType<ExpressionSyntax>()
            .Select(expression => expression.Kind())
            .Where(kind => kind is
                SyntaxKind.IdentifierName or
                SyntaxKind.SimpleMemberAccessExpression or
                SyntaxKind.InvocationExpression or
                SyntaxKind.LogicalAndExpression or
                SyntaxKind.LogicalOrExpression or
                SyntaxKind.PreIncrementExpression or
                SyntaxKind.PreDecrementExpression or
                SyntaxKind.PostIncrementExpression or
                SyntaxKind.PostDecrementExpression or
                SyntaxKind.ParenthesizedExpression)
            .Select(kind => kind.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// 获取定义的符号。
    /// </summary>
    private static IReadOnlyList<SymbolRef> GetDefinedSymbols(
        SyntaxNode node,
        SemanticModel model,
        MemberId declaringMemberId,
        ISymbol? memberSymbol = null)
    {
        var symbols = new List<SymbolRef>();

        if (node is LocalDeclarationStatementSyntax localDeclaration)
        {
            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                var projected = SymbolRefProjector.ProjectDeclared(localDeclaration, variable, model, declaringMemberId);
                if (projected != null)
                {
                    symbols.Add(projected);
                }
            }
        }
        else if (node is ExpressionStatementSyntax expressionStatement &&
                 expressionStatement.Expression is AssignmentExpressionSyntax assignment &&
                 assignment.Left is IdentifierNameSyntax identifier)
        {
            var projected = SymbolRefProjector.ProjectUsed(identifier, model, declaringMemberId);
            if (projected != null)
            {
                symbols.Add(projected);
            }
        }
        else if (memberSymbol is IFieldSymbol or IPropertySymbol)
        {
            var projected = SymbolRefProjector.Project(memberSymbol, declaringMemberId);
            if (projected != null)
            {
                symbols.Add(projected);
            }
        }

        return symbols;
    }

    /// <summary>
    /// 获取使用的符号。
    /// </summary>
    private static IReadOnlyList<SymbolRef> GetUsedSymbols(
        SyntaxNode node,
        SemanticModel model,
        MemberId declaringMemberId,
        ISymbol? memberSymbol = null)
    {
        var definedKeys = GetDefinedSymbols(node, model, declaringMemberId, memberSymbol)
            .Select(symbol => symbol.SymbolKey)
            .ToHashSet(StringComparer.Ordinal);

        return node.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Select(identifier => new
            {
                Identifier = identifier,
                Symbol = model.GetSymbolInfo(identifier).Symbol
            })
            .Where(candidate => ShouldTrackDataflowSymbol(candidate.Symbol))
            .Select(candidate => SymbolRefProjector.Project(candidate.Symbol, declaringMemberId))
            .Where(symbol => symbol != null)
            .Cast<SymbolRef>()
            .Where(symbol => !definedKeys.Contains(symbol.SymbolKey))
            .DistinctBy(symbol => symbol.SymbolKey)
            .ToArray();
    }

    /// <summary>
    /// 分类语句类型。
    /// </summary>
    private static StatementKindRef ClassifyStatementKind(StatementSyntax statement)
    {
        if (IsObjectInitializerAssignment(statement))
        {
            return StatementKindRef.ObjectInitializerAssignment;
        }

        return statement switch
        {
            IfStatementSyntax => StatementKindRef.If,
            WhileStatementSyntax => StatementKindRef.While,
            ForStatementSyntax => StatementKindRef.For,
            ReturnStatementSyntax => StatementKindRef.Return,
            LocalDeclarationStatementSyntax => StatementKindRef.Declaration,
            ExpressionStatementSyntax expressionStatement when expressionStatement.Expression is AssignmentExpressionSyntax => StatementKindRef.Assignment,
            _ => StatementKindRef.Unknown
        };
    }

    /// <summary>
    /// 检查是否为对象初始化赋值。
    /// </summary>
    private static bool IsObjectInitializerAssignment(StatementSyntax statement)
    {
        if (statement is not LocalDeclarationStatementSyntax localDeclaration)
        {
            return false;
        }

        return localDeclaration.Declaration.Variables.Any(variable =>
            variable.Initializer?.Value is ObjectCreationExpressionSyntax { Initializer: not null });
    }

    /// <summary>
    /// 检查节点是否具有净化作用（即赋值为字面量或非跟踪符号）。
    /// </summary>
    private static bool IsSanitizingNode(SyntaxNode node, SemanticModel model, MemberId declaringMemberId)
    {
        ExpressionSyntax? right = null;

        if (node is LocalDeclarationStatementSyntax localDeclaration)
        {
            right = localDeclaration.Declaration.Variables.FirstOrDefault()?.Initializer?.Value;
        }
        else if (node is ExpressionStatementSyntax expressionStatement &&
                 expressionStatement.Expression is AssignmentExpressionSyntax assignment)
        {
            right = assignment.Right;
        }
        else if (node is EqualsValueClauseSyntax initializer)
        {
            right = initializer.Value;
        }

        if (right == null)
        {
            return false;
        }

        if (right is LiteralExpressionSyntax)
        {
            return true;
        }

        return !right.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Select(identifier => model.GetSymbolInfo(identifier).Symbol)
            .Any(ShouldTrackDataflowSymbol);
    }

    /// <summary>
    /// 检查是否应跟踪数据流符号。
    /// </summary>
    private static bool ShouldTrackDataflowSymbol(ISymbol? symbol)
    {
        return symbol switch
        {
            ILocalSymbol or IParameterSymbol => true,
            IFieldSymbol field => !field.IsStatic,
            IPropertySymbol property => !property.IsStatic,
            _ => false
        };
    }

    /// <summary>
    /// 添加分析目标的边。
    /// </summary>
    private static void AddTargetEdges(
        AnalysisTarget currentTarget,
        AnalysisTarget? previousTarget,
        ICollection<AnalysisEdge> edges)
    {
        foreach (var symbol in currentTarget.DefinesSymbols)
        {
            edges.Add(new AnalysisEdge(currentTarget.Target.TargetKey, currentTarget.Target.TargetKey, AnalysisEdgeKind.Defines, symbol.SymbolKey));
        }

        foreach (var symbol in currentTarget.UsesSymbols)
        {
            edges.Add(new AnalysisEdge(currentTarget.Target.TargetKey, currentTarget.Target.TargetKey, AnalysisEdgeKind.Uses, symbol.SymbolKey));
        }

        if (previousTarget != null)
        {
            edges.Add(new AnalysisEdge(previousTarget.Target.TargetKey, currentTarget.Target.TargetKey, AnalysisEdgeKind.Precedes));
        }
    }

    /// <summary>
    /// 检查标识符是否为写入目标。
    /// </summary>
    private static bool IsWriteTargetIdentifier(IdentifierNameSyntax node)
    {
        if (node.Parent is AssignmentExpressionSyntax assignment && assignment.Left == node)
        {
            return true;
        }

        if (node.Parent is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name == node &&
            memberAccess.Parent is AssignmentExpressionSyntax memberAssignment &&
            memberAssignment.Left == memberAccess)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 检查成员是否为高风险成员（如虚方法、抽象方法或接口实现）。
    /// </summary>
    private static bool IsHighRiskMember(ISymbol memberSymbol)
    {
        if (memberSymbol is not IMethodSymbol method)
        {
            return false;
        }

        if (method.IsVirtual || method.IsOverride || method.IsAbstract)
        {
            return true;
        }

        foreach (var iface in method.ContainingType.AllInterfaces)
        {
            foreach (var ifaceMember in iface.GetMembers().OfType<IMethodSymbol>())
            {
                var implementation = method.ContainingType.FindImplementationForInterfaceMember(ifaceMember);
                if (SymbolEqualityComparer.Default.Equals(implementation, method))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 检查方法是否匹配实现。
    /// </summary>
    private static bool IsMethodImplementationMatch(IMethodSymbol method, ISymbol? implementation)
    {
        if (implementation is not IMethodSymbol candidate)
        {
            return false;
        }

        var current = method;
        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate, current))
            {
                return true;
            }

            current = current.OverriddenMethod;
        }

        return false;
    }
}
