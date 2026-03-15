using System.Diagnostics;
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
    AnalysisResultModel View,
    IReadOnlyList<RoslynAnalysisDocument> Documents,
    FunctionIndex FunctionIndex,
    FunctionFactsIndex FunctionFacts,
    AnalysisPerformanceSummary PerformanceSummary);

/// <summary>
/// 函数事实种子，记录函数与其调用成员集合。
/// </summary>
/// <param name="MemberId">函数成员标识。</param>
/// <param name="CalledMemberIds">被调用成员标识集合。</param>
internal sealed record FunctionFactSeed(
    MemberId MemberId,
    IReadOnlyList<MemberId> CalledMemberIds);

/// <summary>
/// 数据流事实，包含定义集合、使用集合与净化赋值标记。
/// </summary>
/// <param name="DefinesSymbols">定义的符号集合。</param>
/// <param name="UsesSymbols">使用的符号集合。</param>
/// <param name="IsSanitizingAssignment">是否为净化赋值。</param>
internal sealed record DataflowFacts(
    IReadOnlyList<SymbolRef> DefinesSymbols,
    IReadOnlyList<SymbolRef> UsesSymbols,
    bool IsSanitizingAssignment);

/// <summary>
/// 语句检查结果，包含数据流、调用成员与表达式标记信息。
/// </summary>
/// <param name="DataflowFacts">数据流事实。</param>
/// <param name="InvokedMemberIds">直接调用的成员标识集合。</param>
/// <param name="MarkedExpressionKinds">命中的表达式语法种类集合。</param>
internal sealed record StatementInspectionResult(
    DataflowFacts DataflowFacts,
    IReadOnlyList<MemberId> InvokedMemberIds,
    IReadOnlyList<string> MarkedExpressionKinds);

/// <summary>
/// 分析性能摘要，统计关键阶段耗时。
/// </summary>
/// <param name="DocumentCount">文档数量。</param>
/// <param name="SyntaxIndexTime">语法索引阶段耗时。</param>
/// <param name="TypeGraphTime">类型图构建阶段耗时。</param>
/// <param name="FunctionNodeTime">函数节点注册阶段耗时。</param>
/// <param name="TypeBodyGraphTime">类型体依赖分析阶段耗时。</param>
/// <param name="TargetAnalysisTime">目标分析阶段耗时。</param>
/// <param name="FunctionFactsTime">函数事实生成阶段耗时。</param>
/// <param name="MergeTime">结果合并阶段耗时。</param>
public sealed record AnalysisPerformanceSummary(
    int DocumentCount,
    TimeSpan SyntaxIndexTime,
    TimeSpan TypeGraphTime,
    TimeSpan FunctionNodeTime,
    TimeSpan TypeBodyGraphTime,
    TimeSpan TargetAnalysisTime,
    TimeSpan FunctionFactsTime,
    TimeSpan MergeTime);

/// <summary>
/// 单文档分析耗时明细（Ticks）。
/// </summary>
/// <param name="SyntaxIndexTicks">语法索引阶段耗时。</param>
/// <param name="TypeGraphTicks">类型图构建阶段耗时。</param>
/// <param name="FunctionNodeTicks">函数节点注册阶段耗时。</param>
/// <param name="TypeBodyGraphTicks">类型体依赖分析阶段耗时。</param>
/// <param name="TargetAnalysisTicks">目标分析阶段耗时。</param>
/// <param name="FunctionFactsTicks">函数事实生成阶段耗时。</param>
internal sealed record DocumentAnalysisTimings(
    long SyntaxIndexTicks,
    long TypeGraphTicks,
    long FunctionNodeTicks,
    long TypeBodyGraphTicks,
    long TargetAnalysisTicks,
    long FunctionFactsTicks);

/// <summary>
/// 文档语法索引，缓存常用语法节点集合以降低重复遍历成本。
/// </summary>
/// <param name="BaseTypes">基础类型声明集合。</param>
/// <param name="Fields">字段声明集合。</param>
/// <param name="PropertiesWithInitializer">带初始化器的属性声明集合。</param>
/// <param name="PropertiesWithExpressionBody">表达式体属性声明集合。</param>
/// <param name="Classes">类声明集合。</param>
/// <param name="Methods">方法声明集合。</param>
/// <param name="Constructors">构造函数声明集合。</param>
/// <param name="Accessors">访问器声明集合。</param>
/// <param name="Operators">运算符声明集合。</param>
/// <param name="ConversionOperators">转换运算符声明集合。</param>
internal sealed record DocumentSyntaxIndex(
    IReadOnlyList<BaseTypeDeclarationSyntax> BaseTypes,
    IReadOnlyList<FieldDeclarationSyntax> Fields,
    IReadOnlyList<PropertyDeclarationSyntax> PropertiesWithInitializer,
    IReadOnlyList<PropertyDeclarationSyntax> PropertiesWithExpressionBody,
    IReadOnlyList<ClassDeclarationSyntax> Classes,
    IReadOnlyList<MethodDeclarationSyntax> Methods,
    IReadOnlyList<ConstructorDeclarationSyntax> Constructors,
    IReadOnlyList<AccessorDeclarationSyntax> Accessors,
    IReadOnlyList<OperatorDeclarationSyntax> Operators,
    IReadOnlyList<ConversionOperatorDeclarationSyntax> ConversionOperators);

/// <summary>
/// 单文档分析打包结果，包含产物与性能数据。
/// </summary>
/// <param name="Document">源文档。</param>
/// <param name="Root">编译单元语法根。</param>
/// <param name="SemanticModel">语义模型。</param>
/// <param name="Targets">分析目标集合。</param>
/// <param name="AnalysisEdges">分析边集合。</param>
/// <param name="TypeNodes">类型节点集合。</param>
/// <param name="TypeEdges">类型依赖边集合。</param>
/// <param name="FunctionNodes">函数节点集合。</param>
/// <param name="FunctionFacts">函数事实集合。</param>
/// <param name="Timings">文档分析耗时明细。</param>
internal sealed record DocumentAnalysisBundle(
    SourceDocument Document,
    CompilationUnitSyntax Root,
    SemanticModel SemanticModel,
    IReadOnlyList<AnalysisTarget> Targets,
    IReadOnlyList<AnalysisEdge> AnalysisEdges,
    IReadOnlyList<TypeNodeRef> TypeNodes,
    IReadOnlyList<TypeDependencyEdge> TypeEdges,
    IReadOnlyList<FunctionNodeRef> FunctionNodes,
    IReadOnlyList<FunctionFactSeed> FunctionFacts,
    DocumentAnalysisTimings Timings);

/// <summary>
/// 文档级符号缓存，复用成员标识与类型标识计算结果。
/// </summary>
internal sealed class DocumentSymbolCache
{
    private readonly Dictionary<ISymbol, MemberId> _memberIds = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<ITypeSymbol, string> _typeIds = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// 获取成员标识，并在首次访问时写入缓存。
    /// </summary>
    /// <param name="symbol">成员符号。</param>
    /// <returns>成员标识。</returns>
    public MemberId GetMemberId(ISymbol symbol)
    {
        if (!_memberIds.TryGetValue(symbol, out var memberId))
        {
            memberId = MetadataMemberIdBuilder.Build(symbol);
            _memberIds[symbol] = memberId;
        }

        return memberId;
    }

    /// <summary>
    /// 获取类型标识，并在首次访问时写入缓存。
    /// </summary>
    /// <param name="symbol">类型符号。</param>
    /// <returns>类型标识。</returns>
    public string GetTypeId(ITypeSymbol? symbol)
    {
        if (symbol == null)
        {
            return MetadataTypeIdBuilder.Build(null);
        }

        if (!_typeIds.TryGetValue(symbol, out var typeId))
        {
            typeId = MetadataTypeIdBuilder.Build(symbol);
            _typeIds[symbol] = typeId;
        }

        return typeId;
    }
}

/// <summary>
/// Roslyn分析引擎，负责协调文档的解析、编译和依赖分析。
/// 类依赖图是读取sln项目就进行全部分析，函数依赖分析需要支持性能更好的动态文件范围分析和全sln项目分析
/// 而语句分析初版需要支持最小作用域分析,然后支持引用关系穿透,即跨越作用域但此阶段不会跨越函数作用域和类作用域
/// 下一阶段是支持域分析穿透支持到函数和类成员属性.下阶段更为复杂需要支持多作用跨越和不同作用域的混合分析
/// </summary>
public sealed class RoslynAnalysisEngine
{
    private static readonly string[] KnownPersistentOwnerTypeMarkers =
    [
        "Manager",
        "Registry",
        "Resolver"
    ];

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

    /// <summary>
    /// 按输入类型选择分析入口。
    /// </summary>
    /// <param name="input">分析输入。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>Roslyn分析结果。</returns>
    public async Task<RoslynAnalysisResult> AnalyzeAsync(
        AnalysisInput input,
        CancellationToken cancellationToken)
    {
        return input switch
        {
            SourceOnlyAnalysisInput sourceOnly => await AnalyzeSourceOnlyAsync(sourceOnly, cancellationToken),
            WorkspaceAnalysisContextInput workspace => await AnalyzeWorkspaceAsync(workspace, cancellationToken),
            _ => throw new NotSupportedException($"Unsupported analysis input '{input.GetType().Name}'.")
        };
    }

    /// <summary>
    /// 仅分析源码模式。
    /// </summary>
    private async Task<RoslynAnalysisResult> AnalyzeSourceOnlyAsync(
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

        var bundles = await Task.WhenAll(
            trees.Select((tree, index) =>
                Task.Run(
                    () => AnalyzeDocumentBundle(documents[index], tree.GetCompilationUnitRoot(cancellationToken), compilation.GetSemanticModel(tree), cancellationToken),
                    cancellationToken)));

        var mergeStart = Stopwatch.GetTimestamp();
        var analyzedDocuments = new List<RoslynAnalysisDocument>(bundles.Length);
        var allTargets = new List<AnalysisTarget>();
        var allEdges = new List<AnalysisEdge>();
        var typeNodes = new Dictionary<string, TypeNodeRef>(StringComparer.Ordinal);
        var typeEdges = new HashSet<TypeDependencyEdge>();
        var functionNodes = new Dictionary<string, FunctionNodeRef>(StringComparer.Ordinal);

        foreach (var bundle in bundles)
        {
            analyzedDocuments.Add(new RoslynAnalysisDocument(bundle.Document, bundle.Root, bundle.SemanticModel, bundle.Targets));
            allTargets.AddRange(bundle.Targets);
            allEdges.AddRange(bundle.AnalysisEdges);

            foreach (var node in bundle.TypeNodes)
            {
                typeNodes[node.TypeId] = node;
            }

            foreach (var edge in bundle.TypeEdges)
            {
                typeEdges.Add(edge);
            }

            foreach (var node in bundle.FunctionNodes)
            {
                functionNodes[node.MemberId.Value] = node;
            }
        }

        //抽象写法
        //语句级分析不能使用sln项目级的全量分析
        var functionIndex = BuildFunctionIndex(functionNodes.Values);
        var functionFacts = BuildFunctionFactsIndex(functionNodes.Values, bundles);
        var mergeElapsed = Stopwatch.GetElapsedTime(mergeStart);
        var view = new AnalysisResultModel(
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

        return new RoslynAnalysisResult(view, analyzedDocuments, functionIndex, functionFacts, SummarizePerformance(bundles, mergeElapsed));
    }

    /// <summary>
    /// 分析工作区模式。
    /// </summary>
    private async Task<RoslynAnalysisResult> AnalyzeWorkspaceAsync(
        WorkspaceAnalysisContextInput input,
        CancellationToken cancellationToken)
    {
        var bundles = await Task.WhenAll(
            input.Documents.Select(documentContext =>
            {
                var root = documentContext.Root as CompilationUnitSyntax
                    ?? throw new InvalidOperationException("WorkspaceAnalysisDocumentContext.Root must be a CompilationUnitSyntax.");
                return Task.Run(
                    () => AnalyzeDocumentBundle(documentContext.SourceDocument, root, documentContext.SemanticModel, cancellationToken),
                    cancellationToken);
            }));

        var mergeStart = Stopwatch.GetTimestamp();
        var analyzedDocuments = new List<RoslynAnalysisDocument>(bundles.Length);
        var allTargets = new List<AnalysisTarget>();
        var allEdges = new List<AnalysisEdge>();
        var typeNodes = new Dictionary<string, TypeNodeRef>(StringComparer.Ordinal);
        var typeEdges = new HashSet<TypeDependencyEdge>();
        var functionNodes = new Dictionary<string, FunctionNodeRef>(StringComparer.Ordinal);

        foreach (var bundle in bundles)
        {
            analyzedDocuments.Add(new RoslynAnalysisDocument(bundle.Document, bundle.Root, bundle.SemanticModel, bundle.Targets));
            allTargets.AddRange(bundle.Targets);
            allEdges.AddRange(bundle.AnalysisEdges);

            foreach (var node in bundle.TypeNodes)
            {
                typeNodes[node.TypeId] = node;
            }

            foreach (var edge in bundle.TypeEdges)
            {
                typeEdges.Add(edge);
            }

            foreach (var node in bundle.FunctionNodes)
            {
                functionNodes[node.MemberId.Value] = node;
            }
        }

        var functionIndex = BuildFunctionIndex(functionNodes.Values);
        var functionFacts = BuildFunctionFactsIndex(functionNodes.Values, bundles);
        var mergeElapsed = Stopwatch.GetElapsedTime(mergeStart);
        var view = new AnalysisResultModel(
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

        return new RoslynAnalysisResult(view, analyzedDocuments, functionIndex, functionFacts, SummarizePerformance(bundles, mergeElapsed));
    }

    /// <summary>
    /// 分析单个文档并汇总中间产物。
    /// </summary>
    /// <param name="sourceDocument">源文档。</param>
    /// <param name="root">编译单元语法根。</param>
    /// <param name="semanticModel">语义模型。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>文档分析打包结果。</returns>
    private static DocumentAnalysisBundle AnalyzeDocumentBundle(
        SourceDocument sourceDocument,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var symbolCache = new DocumentSymbolCache();

        var syntaxIndexStart = Stopwatch.GetTimestamp();
        var syntaxIndex = CreateSyntaxIndex(root);
        var syntaxIndexElapsedTicks = Stopwatch.GetElapsedTime(syntaxIndexStart).Ticks;
        var localTypeNodes = new Dictionary<string, TypeNodeRef>(StringComparer.Ordinal);
        var localTypeEdges = new HashSet<TypeDependencyEdge>();
        var localFunctionNodes = new Dictionary<string, FunctionNodeRef>(StringComparer.Ordinal);
        var localAnalysisEdges = new List<AnalysisEdge>();

        var typeGraphStart = Stopwatch.GetTimestamp();
        RegisterTypeGraphDocuments(sourceDocument, syntaxIndex, semanticModel, symbolCache, localTypeNodes, localTypeEdges);
        var typeGraphElapsedTicks = Stopwatch.GetElapsedTime(typeGraphStart).Ticks;
        var functionNodeStart = Stopwatch.GetTimestamp();
        RegisterFunctionNodes(sourceDocument, syntaxIndex, semanticModel, symbolCache, localFunctionNodes);
        var functionNodeElapsedTicks = Stopwatch.GetElapsedTime(functionNodeStart).Ticks;
        var typeBodyGraphStart = Stopwatch.GetTimestamp();
        RegisterTypeBodyGraphs(syntaxIndex, semanticModel, symbolCache, localTypeEdges);
        var typeBodyGraphElapsedTicks = Stopwatch.GetElapsedTime(typeBodyGraphStart).Ticks;
        var targetAnalysisStart = Stopwatch.GetTimestamp();
        var targets = AnalyzeDocument(sourceDocument, syntaxIndex, semanticModel, symbolCache, localAnalysisEdges);
        var targetAnalysisElapsedTicks = Stopwatch.GetElapsedTime(targetAnalysisStart).Ticks;
        var functionFactsStart = Stopwatch.GetTimestamp();
        var functionFacts = CreateFunctionFacts(syntaxIndex, semanticModel, symbolCache);
        var functionFactsElapsedTicks = Stopwatch.GetElapsedTime(functionFactsStart).Ticks;

        return new DocumentAnalysisBundle(
            sourceDocument,
            root,
            semanticModel,
            targets,
            localAnalysisEdges,
            localTypeNodes.Values.ToArray(),
            localTypeEdges.ToArray(),
            localFunctionNodes.Values.ToArray(),
            functionFacts,
            new DocumentAnalysisTimings(
                syntaxIndexElapsedTicks,
                typeGraphElapsedTicks,
                functionNodeElapsedTicks,
                typeBodyGraphElapsedTicks,
                targetAnalysisElapsedTicks,
                functionFactsElapsedTicks));
    }

    /// <summary>
    /// 汇总文档分析耗时并生成性能摘要。
    /// </summary>
    /// <param name="bundles">文档分析打包结果集合。</param>
    /// <param name="mergeTime">合并阶段耗时。</param>
    /// <returns>性能摘要。</returns>
    private static AnalysisPerformanceSummary SummarizePerformance(
        IReadOnlyList<DocumentAnalysisBundle> bundles,
        TimeSpan mergeTime)
    {
        long syntaxIndexTicks = 0;
        long typeGraphTicks = 0;
        long functionNodeTicks = 0;
        long typeBodyGraphTicks = 0;
        long targetAnalysisTicks = 0;
        long functionFactsTicks = 0;

        foreach (var bundle in bundles)
        {
            syntaxIndexTicks += bundle.Timings.SyntaxIndexTicks;
            typeGraphTicks += bundle.Timings.TypeGraphTicks;
            functionNodeTicks += bundle.Timings.FunctionNodeTicks;
            typeBodyGraphTicks += bundle.Timings.TypeBodyGraphTicks;
            targetAnalysisTicks += bundle.Timings.TargetAnalysisTicks;
            functionFactsTicks += bundle.Timings.FunctionFactsTicks;
        }

        return new AnalysisPerformanceSummary(
            bundles.Count,
            TimeSpan.FromTicks(syntaxIndexTicks),
            TimeSpan.FromTicks(typeGraphTicks),
            TimeSpan.FromTicks(functionNodeTicks),
            TimeSpan.FromTicks(typeBodyGraphTicks),
            TimeSpan.FromTicks(targetAnalysisTicks),
            TimeSpan.FromTicks(functionFactsTicks),
            mergeTime);
    }

    /// <summary>
    /// 根据分析结果创建分析上下文。
    /// 上下文需要根据文件数量以及作用范围来维护上下文,要么就是不变上下放弃维护
    /// 需要确定上下文需要传递什么数据和大小
    /// </summary>
    /// <param name="result">Roslyn分析结果。</param>
    /// <returns>分析上下文。</returns>
    public AnalysisExecutionSnapshot CreateSnapshot(RoslynAnalysisResult result)
    {
        return new AnalysisExecutionSnapshot(
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
        var services = BuildAnalysisServices(result.Documents, snapshot);
        return AnalysisContext.Create(snapshot, services);
    }

    /// <summary>
    /// 核心创建分析服务逻辑。
    /// </summary>
    private static AnalysisServices BuildAnalysisServices(
        IReadOnlyList<RoslynAnalysisDocument> documents,
        AnalysisExecutionSnapshot snapshot)
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

        foreach (var document in documents)
        {
            RegisterMethodGroupReferences(
                document.Root.DescendantNodes().OfType<MethodDeclarationSyntax>(),
                document.SemanticModel,
                memberToFunctions,
                memberToTypes,
                static method => GetBodyOrExpression(method));
            RegisterMethodGroupReferences(
                document.Root.DescendantNodes().OfType<ConstructorDeclarationSyntax>(),
                document.SemanticModel,
                memberToFunctions,
                memberToTypes,
                static ctor => GetReferenceScope(ctor));
            RegisterMethodGroupReferences(
                document.Root.DescendantNodes().OfType<AccessorDeclarationSyntax>(),
                document.SemanticModel,
                memberToFunctions,
                memberToTypes,
                static accessor => GetBodyOrExpression(accessor));
            RegisterPropertyMethodGroupReferences(
                document.Root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Where(static property => property.ExpressionBody != null),
                document.SemanticModel,
                memberToFunctions,
                memberToTypes);
            RegisterInitializerMethodReferences(
                document.Root,
                document.SemanticModel,
                memberToFunctions,
                memberToTypes);
        }

        var typeToFunctions = new Dictionary<string, HashSet<MemberId>>(StringComparer.Ordinal);
        var typeToTypes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var edge in snapshot.View.TypeGraph.Edges)
        {
            if (!IsPersistentTypeReference(edge.Kind) &&
                !IsNestedTypeBodyReference(edge))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(edge.MemberId))
            {
                RegisterReference(typeToFunctions, edge.TargetTypeId, new MemberId(edge.MemberId));
            }

            RegisterReference(typeToTypes, edge.TargetTypeId, edge.SourceTypeId);
        }

        foreach (var document in documents)
        {
            RegisterPersistentTypeReferences(
                document.Root.DescendantNodes().OfType<MethodDeclarationSyntax>(),
                document.SemanticModel,
                typeToFunctions,
                typeToTypes,
                static method => GetBodyOrExpression(method));
            RegisterPersistentTypeReferences(
                document.Root.DescendantNodes().OfType<ConstructorDeclarationSyntax>(),
                document.SemanticModel,
                typeToFunctions,
                typeToTypes,
                static ctor => GetReferenceScope(ctor));
            RegisterPersistentTypeReferences(
                document.Root.DescendantNodes().OfType<AccessorDeclarationSyntax>(),
                document.SemanticModel,
                typeToFunctions,
                typeToTypes,
                static accessor => GetBodyOrExpression(accessor));
            RegisterPersistentInitializerTypeReferences(document.Root, document.SemanticModel, typeToFunctions, typeToTypes);
        }

        return new AnalysisServices(
            new InheritanceQueryService(overrideMembers, interfaceMembers, inheritanceTypes),
            new ReferenceQueryService(memberToFunctions, memberToTypes, typeToFunctions, typeToTypes),
            new StatementAnalysisService(snapshot.StatementFacts),
            new FunctionGraphProvider(snapshot.FunctionIndex, snapshot.FunctionFacts));
    }

    /// <summary>
    /// 判断类型依赖边是否属于持久引用。
    /// </summary>
    /// <param name="kind">类型依赖种类。</param>
    /// <returns>若属于持久引用则为 <see langword="true"/>。</returns>
    private static bool IsPersistentTypeReference(TypeDependencyKind kind) =>
        kind is TypeDependencyKind.Inherits
            or TypeDependencyKind.Implements
            or TypeDependencyKind.FieldType
            or TypeDependencyKind.PropertyType
            or TypeDependencyKind.ParameterType
            or TypeDependencyKind.ReturnType
            or TypeDependencyKind.StaticMemberAccess;

    /// <summary>
    /// 判断是否为嵌套类型体引用。
    /// </summary>
    /// <param name="edge">类型依赖边。</param>
    /// <returns>若为嵌套类型体引用则为 <see langword="true"/>。</returns>
    private static bool IsNestedTypeBodyReference(TypeDependencyEdge edge) =>
        edge.Kind is TypeDependencyKind.ObjectCreation or TypeDependencyKind.MemberBodyReference &&
        IsNestedTypeReference(edge.SourceTypeId, edge.TargetTypeId);

    /// <summary>
    /// 判断目标类型是否为源类型的嵌套类型。
    /// </summary>
    /// <param name="sourceTypeId">源类型标识。</param>
    /// <param name="targetTypeId">目标类型标识。</param>
    /// <returns>若为嵌套关系则为 <see langword="true"/>。</returns>
    private static bool IsNestedTypeReference(string sourceTypeId, string targetTypeId) =>
        targetTypeId.StartsWith(sourceTypeId + ".", StringComparison.Ordinal);

    /// <summary>
    /// 注册方法组引用到成员和类型反向索引。
    /// </summary>
    private static void RegisterMethodGroupReferences<TDeclaration>(
        IEnumerable<TDeclaration> declarations,
        SemanticModel model,
        Dictionary<string, HashSet<MemberId>> memberToFunctions,
        Dictionary<string, HashSet<string>> memberToTypes,
        Func<TDeclaration, SyntaxNode?> getBodyOrExpression)
        where TDeclaration : SyntaxNode
    {
        foreach (var declaration in declarations)
        {
            if (model.GetDeclaredSymbol(declaration) is not IMethodSymbol currentMethod)
            {
                continue;
            }

            var currentMemberId = MetadataMemberIdBuilder.Build(currentMethod);
            var currentTypeId = MetadataTypeIdBuilder.Build(currentMethod.ContainingType);
            foreach (var referencedMethodId in CollectReferencedMethodIds(getBodyOrExpression(declaration), model))
            {
                RegisterReference(memberToFunctions, referencedMethodId.Value, currentMemberId);
                RegisterReference(memberToTypes, referencedMethodId.Value, currentTypeId);
            }
        }
    }

    /// <summary>
    /// 注册表达式体属性中的方法组引用。
    /// </summary>
    private static void RegisterPropertyMethodGroupReferences(
        IEnumerable<PropertyDeclarationSyntax> properties,
        SemanticModel model,
        Dictionary<string, HashSet<MemberId>> memberToFunctions,
        Dictionary<string, HashSet<string>> memberToTypes)
    {
        foreach (var property in properties)
        {
            if (model.GetDeclaredSymbol(property) is not IPropertySymbol { GetMethod: not null } propertySymbol)
            {
                continue;
            }

            var currentMemberId = MetadataMemberIdBuilder.Build(propertySymbol.GetMethod);
            var currentTypeId = MetadataTypeIdBuilder.Build(propertySymbol.ContainingType);
            foreach (var referencedMethodId in CollectReferencedMethodIds(GetBodyOrExpression(property), model))
            {
                RegisterReference(memberToFunctions, referencedMethodId.Value, currentMemberId);
                RegisterReference(memberToTypes, referencedMethodId.Value, currentTypeId);
            }
        }
    }

    /// <summary>
    /// 注册字段与属性初始化器中的方法引用。
    /// </summary>
    private static void RegisterInitializerMethodReferences(
        CompilationUnitSyntax root,
        SemanticModel model,
        Dictionary<string, HashSet<MemberId>> memberToFunctions,
        Dictionary<string, HashSet<string>> memberToTypes)
    {
        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            foreach (var variable in field.Declaration.Variables.Where(variable => variable.Initializer != null))
            {
                if (model.GetDeclaredSymbol(variable) is not IFieldSymbol fieldSymbol)
                {
                    continue;
                }

                var currentMemberId = MetadataMemberIdBuilder.Build(fieldSymbol);
                var currentTypeId = MetadataTypeIdBuilder.Build(fieldSymbol.ContainingType);
                foreach (var referencedMethodId in CollectInitializerReferencedMethodIds(variable.Initializer!.Value, model))
                {
                    RegisterReference(memberToFunctions, referencedMethodId.Value, currentMemberId);
                    RegisterReference(memberToTypes, referencedMethodId.Value, currentTypeId);
                }
            }
        }

        foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Where(static property => property.Initializer != null))
        {
            if (model.GetDeclaredSymbol(property) is not IPropertySymbol propertySymbol)
            {
                continue;
            }

            var currentMemberId = MetadataMemberIdBuilder.Build(propertySymbol);
            var currentTypeId = MetadataTypeIdBuilder.Build(propertySymbol.ContainingType);
            foreach (var referencedMethodId in CollectInitializerReferencedMethodIds(property.Initializer!.Value, model))
            {
                RegisterReference(memberToFunctions, referencedMethodId.Value, currentMemberId);
                RegisterReference(memberToTypes, referencedMethodId.Value, currentTypeId);
            }
        }
    }

    /// <summary>
    /// 收集初始化器中的被引用方法标识集合。
    /// </summary>
    private static IReadOnlyList<MemberId> CollectInitializerReferencedMethodIds(
        SyntaxNode initializerValue,
        SemanticModel model)
    {
        var referenced = new Dictionary<string, MemberId>(StringComparer.Ordinal);
        var symbolCache = new DocumentSymbolCache();

        foreach (var memberId in CollectCalledMemberIds(initializerValue, model, symbolCache))
        {
            referenced[memberId.Value] = memberId;
        }

        foreach (var memberId in CollectReferencedMethodIds(initializerValue, model))
        {
            referenced[memberId.Value] = memberId;
        }

        return referenced.Values
            .OrderBy(memberId => memberId.Value, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// 注册持久类型引用到类型反向索引。
    /// </summary>
    private static void RegisterPersistentTypeReferences<TDeclaration>(
        IEnumerable<TDeclaration> declarations,
        SemanticModel model,
        Dictionary<string, HashSet<MemberId>> typeToFunctions,
        Dictionary<string, HashSet<string>> typeToTypes,
        Func<TDeclaration, SyntaxNode?> getBodyOrExpression)
        where TDeclaration : SyntaxNode
    {
        foreach (var declaration in declarations)
        {
            if (model.GetDeclaredSymbol(declaration) is not IMethodSymbol currentMethod)
            {
                continue;
            }

            var currentMemberId = MetadataMemberIdBuilder.Build(currentMethod);
            var currentTypeId = MetadataTypeIdBuilder.Build(currentMethod.ContainingType);
            foreach (var referencedTypeId in CollectPersistentReferencedTypeIds(getBodyOrExpression(declaration), model))
            {
                RegisterReference(typeToFunctions, referencedTypeId, currentMemberId);
                RegisterReference(typeToTypes, referencedTypeId, currentTypeId);
            }
        }
    }

    /// <summary>
    /// 注册持久化字段初始化器中的类型引用。
    /// </summary>
    private static void RegisterPersistentInitializerTypeReferences(
        CompilationUnitSyntax root,
        SemanticModel model,
        Dictionary<string, HashSet<MemberId>> typeToFunctions,
        Dictionary<string, HashSet<string>> typeToTypes)
    {
        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            foreach (var variable in field.Declaration.Variables.Where(variable => variable.Initializer != null))
            {
                if (model.GetDeclaredSymbol(variable) is not IFieldSymbol fieldSymbol ||
                    !fieldSymbol.IsStatic)
                {
                    continue;
                }

                if (!LooksLikePersistentOwner(fieldSymbol.ContainingType) &&
                    !LooksLikeKnownPersistentField(fieldSymbol))
                {
                    continue;
                }

                var memberId = MetadataMemberIdBuilder.Build(fieldSymbol);
                var typeId = MetadataTypeIdBuilder.Build(fieldSymbol.ContainingType);
                foreach (var referencedTypeId in CollectCreatedTypeIds(variable.Initializer!.Value, model))
                {
                    RegisterReference(typeToFunctions, referencedTypeId, memberId);
                    RegisterReference(typeToTypes, referencedTypeId, typeId);
                }
            }
        }
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
        IEnumerable<DocumentAnalysisBundle> bundles)
    {
        var nodeArray = nodes.ToArray();
        var calledMembersByMemberId = bundles
            .SelectMany(bundle => bundle.FunctionFacts)
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
    private static IReadOnlyList<FunctionFactSeed> CreateFunctionFacts(
        DocumentSyntaxIndex syntaxIndex,
        SemanticModel semanticModel,
        DocumentSymbolCache symbolCache)
    {
        var results = new List<FunctionFactSeed>(
            syntaxIndex.Methods.Count +
            syntaxIndex.Constructors.Count +
            syntaxIndex.Accessors.Count +
            syntaxIndex.Operators.Count +
            syntaxIndex.ConversionOperators.Count +
            syntaxIndex.PropertiesWithExpressionBody.Count);

        foreach (var method in syntaxIndex.Methods)
        {
            var symbol = semanticModel.GetDeclaredSymbol(method);
            if (symbol == null)
            {
                continue;
            }

            results.Add(new FunctionFactSeed(
                symbolCache.GetMemberId(symbol),
                CollectCalledMemberIds(GetBodyOrExpression(method), semanticModel, symbolCache)));
        }

        foreach (var ctor in syntaxIndex.Constructors)
        {
            var symbol = semanticModel.GetDeclaredSymbol(ctor);
            if (symbol == null)
            {
                continue;
            }

            results.Add(new FunctionFactSeed(
                symbolCache.GetMemberId(symbol),
                CollectCalledMemberIds(GetReferenceScope(ctor), semanticModel, symbolCache)));
        }

        foreach (var accessor in syntaxIndex.Accessors)
        {
            var symbol = semanticModel.GetDeclaredSymbol(accessor);
            if (symbol == null)
            {
                continue;
            }

            results.Add(new FunctionFactSeed(
                symbolCache.GetMemberId(symbol),
                CollectCalledMemberIds(GetBodyOrExpression(accessor), semanticModel, symbolCache)));
        }

        foreach (var @operator in syntaxIndex.Operators)
        {
            var symbol = semanticModel.GetDeclaredSymbol(@operator);
            if (symbol == null)
            {
                continue;
            }

            results.Add(new FunctionFactSeed(
                symbolCache.GetMemberId(symbol),
                CollectCalledMemberIds(GetBodyOrExpression(@operator), semanticModel, symbolCache)));
        }

        foreach (var conversionOperator in syntaxIndex.ConversionOperators)
        {
            var symbol = semanticModel.GetDeclaredSymbol(conversionOperator);
            if (symbol == null)
            {
                continue;
            }

            results.Add(new FunctionFactSeed(
                symbolCache.GetMemberId(symbol),
                CollectCalledMemberIds(GetBodyOrExpression(conversionOperator), semanticModel, symbolCache)));
        }

        foreach (var property in syntaxIndex.PropertiesWithExpressionBody)
        {
            var propertySymbol = semanticModel.GetDeclaredSymbol(property) as IPropertySymbol;
            if (propertySymbol?.GetMethod == null)
            {
                continue;
            }

            results.Add(new FunctionFactSeed(
                symbolCache.GetMemberId(propertySymbol.GetMethod),
                CollectCalledMemberIds(GetBodyOrExpression(property), semanticModel, symbolCache)));
        }

        return results;
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
        DocumentSyntaxIndex syntaxIndex,
        SemanticModel model,
        DocumentSymbolCache symbolCache,
        ICollection<AnalysisEdge> edges)
    {
        var targets = new List<AnalysisTarget>();
        var previousTargetsByScope = new Dictionary<string, AnalysisTarget>(StringComparer.Ordinal);
        var scopeCache = new Dictionary<BlockSyntax, (string ScopeId, string? ParentScopeId)>(ReferenceEqualityComparer.Instance);

        foreach (var field in syntaxIndex.Fields)
        {
            foreach (var variable in field.Declaration.Variables.Where(variable => variable.Initializer != null))
            {
                var memberSymbol = model.GetDeclaredSymbol(variable);
                if (memberSymbol == null)
                {
                    continue;
                }

                var memberId = symbolCache.GetMemberId(memberSymbol);
                var currentTarget = CreateInitializerTarget(document, field, variable.Initializer!, memberSymbol, memberId, IsHighRiskMember(memberSymbol), model, MemberKind.Field, symbolCache);
                targets.Add(currentTarget);
                AddTargetEdges(currentTarget, ResolvePreviousTarget(previousTargetsByScope, currentTarget), edges);
            }
        }

        foreach (var property in syntaxIndex.PropertiesWithInitializer)
        {
            var memberSymbol = model.GetDeclaredSymbol(property);
            if (memberSymbol == null)
            {
                continue;
            }

            var memberId = symbolCache.GetMemberId(memberSymbol);
            var currentTarget = CreateInitializerTarget(document, property, property.Initializer!, memberSymbol, memberId, IsHighRiskMember(memberSymbol), model, MemberKind.Property, symbolCache);
            targets.Add(currentTarget);
            AddTargetEdges(currentTarget, ResolvePreviousTarget(previousTargetsByScope, currentTarget), edges);
        }

        foreach (var classDeclaration in syntaxIndex.Classes)
        {
            var classSymbol = model.GetDeclaredSymbol(classDeclaration);
            if (classSymbol == null)
            {
                continue;
            }

            targets.Add(CreateClassTarget(document, classDeclaration, classSymbol, symbolCache));
        }

        foreach (var method in syntaxIndex.Methods)
        {
            var memberSymbol = model.GetDeclaredSymbol(method);
            if (memberSymbol == null)
            {
                continue;
            }

            var memberId = symbolCache.GetMemberId(memberSymbol);
            var isHighRiskMember = IsHighRiskMember(memberSymbol);
            foreach (var statement in EnumerateStatements(method.Body))
            {
                var currentTarget = CreateStatementTarget(document, statement, memberId, isHighRiskMember, model, MemberKind.Method, scopeCache, symbolCache);
                targets.Add(currentTarget);
                AddTargetEdges(currentTarget, ResolvePreviousTarget(previousTargetsByScope, currentTarget), edges);
            }
        }

        foreach (var ctor in syntaxIndex.Constructors)
        {
            var memberSymbol = model.GetDeclaredSymbol(ctor);
            if (memberSymbol == null)
            {
                continue;
            }

            var memberId = symbolCache.GetMemberId(memberSymbol);
            var isHighRiskMember = IsHighRiskMember(memberSymbol);
            foreach (var statement in EnumerateStatements(ctor.Body))
            {
                var currentTarget = CreateStatementTarget(document, statement, memberId, isHighRiskMember, model, MemberKind.Constructor, scopeCache, symbolCache);
                targets.Add(currentTarget);
                AddTargetEdges(currentTarget, ResolvePreviousTarget(previousTargetsByScope, currentTarget), edges);
            }
        }

        foreach (var accessor in syntaxIndex.Accessors)
        {
            var memberSymbol = model.GetDeclaredSymbol(accessor);
            if (memberSymbol == null)
            {
                continue;
            }

            var memberId = symbolCache.GetMemberId(memberSymbol);
            var isHighRiskMember = IsHighRiskMember(memberSymbol);
            foreach (var statement in EnumerateStatements(accessor.Body))
            {
                var currentTarget = CreateStatementTarget(document, statement, memberId, isHighRiskMember, model, MemberKind.Accessor, scopeCache, symbolCache);
                targets.Add(currentTarget);
                AddTargetEdges(currentTarget, ResolvePreviousTarget(previousTargetsByScope, currentTarget), edges);
            }
        }

        foreach (var @operator in syntaxIndex.Operators)
        {
            var memberSymbol = model.GetDeclaredSymbol(@operator);
            if (memberSymbol == null)
            {
                continue;
            }

            var memberId = symbolCache.GetMemberId(memberSymbol);
            var isHighRiskMember = IsHighRiskMember(memberSymbol);
            foreach (var statement in EnumerateStatements(@operator.Body))
            {
                var currentTarget = CreateStatementTarget(document, statement, memberId, isHighRiskMember, model, MemberKind.Method, scopeCache, symbolCache);
                targets.Add(currentTarget);
                AddTargetEdges(currentTarget, ResolvePreviousTarget(previousTargetsByScope, currentTarget), edges);
            }
        }

        foreach (var conversionOperator in syntaxIndex.ConversionOperators)
        {
            var memberSymbol = model.GetDeclaredSymbol(conversionOperator);
            if (memberSymbol == null)
            {
                continue;
            }

            var memberId = symbolCache.GetMemberId(memberSymbol);
            var isHighRiskMember = IsHighRiskMember(memberSymbol);
            foreach (var statement in EnumerateStatements(conversionOperator.Body))
            {
                var currentTarget = CreateStatementTarget(document, statement, memberId, isHighRiskMember, model, MemberKind.Method, scopeCache, symbolCache);
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
        INamedTypeSymbol classSymbol,
        DocumentSymbolCache symbolCache)
    {
        var typeId = symbolCache.GetTypeId(classSymbol);
        return new AnalysisTarget(
            new PlanTarget(
                document.RelativePath,
                new MemberId(typeId),
                MemberKind.Class,
                TargetKind.Class,
                classDeclaration.SpanStart,
                classDeclaration.Span.Length,
                GetDisplayText(document, classDeclaration.SpanStart, classDeclaration.Span.Length),
                new TargetResolutionKey(classDeclaration.SpanStart, classDeclaration.Span.Length)),
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
        DocumentSyntaxIndex syntaxIndex,
        SemanticModel model,
        DocumentSymbolCache symbolCache,
        IDictionary<string, TypeNodeRef> typeNodes,
        ISet<TypeDependencyEdge> typeEdges)
    {
        foreach (var typeDeclaration in syntaxIndex.BaseTypes)
        {
            var typeSymbol = model.GetDeclaredSymbol(typeDeclaration) as INamedTypeSymbol;
            if (typeSymbol == null)
            {
                continue;
            }

            var typeId = symbolCache.GetTypeId(typeSymbol);
            typeNodes[typeId] = new TypeNodeRef(typeId, typeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), document.RelativePath);

            if (typeSymbol.BaseType != null && typeSymbol.BaseType.SpecialType != SpecialType.System_Object)
            {
                typeEdges.Add(new TypeDependencyEdge(typeId, symbolCache.GetTypeId(typeSymbol.BaseType), TypeDependencyKind.Inherits));
            }

            foreach (var iface in typeSymbol.Interfaces)
            {
                typeEdges.Add(new TypeDependencyEdge(typeId, symbolCache.GetTypeId(iface), TypeDependencyKind.Implements));
            }

            foreach (var field in typeSymbol.GetMembers().OfType<IFieldSymbol>())
            {
                RegisterDeclaredTypeDependency(typeId, field.Type, TypeDependencyKind.FieldType, symbolCache.GetMemberId(field).Value, typeEdges, symbolCache);
            }

            foreach (var property in typeSymbol.GetMembers().OfType<IPropertySymbol>())
            {
                RegisterDeclaredTypeDependency(typeId, property.Type, TypeDependencyKind.PropertyType, symbolCache.GetMemberId(property).Value, typeEdges, symbolCache);
            }

            foreach (var method in typeSymbol.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.Constructor or MethodKind.PropertyGet or MethodKind.PropertySet))
                {
                    continue;
                }

                foreach (var parameter in method.Parameters)
                {
                    RegisterDeclaredTypeDependency(typeId, parameter.Type, TypeDependencyKind.ParameterType, symbolCache.GetMemberId(method).Value, typeEdges, symbolCache);
                }

                if (method.MethodKind != MethodKind.Constructor)
                {
                    RegisterDeclaredTypeDependency(typeId, method.ReturnType, TypeDependencyKind.ReturnType, symbolCache.GetMemberId(method).Value, typeEdges, symbolCache);
                }
            }
        }
    }

    /// <summary>
    /// 注册声明类型依赖边。
    /// </summary>
    private static void RegisterDeclaredTypeDependency(
        string sourceTypeId,
        ITypeSymbol referencedType,
        TypeDependencyKind kind,
        string memberId,
        ISet<TypeDependencyEdge> typeEdges,
        DocumentSymbolCache symbolCache)
    {
        foreach (var targetType in EnumerateReferencedTypeSymbols(referencedType))
        {
            typeEdges.Add(new TypeDependencyEdge(sourceTypeId, symbolCache.GetTypeId(targetType), kind, memberId));
        }
    }

    /// <summary>
    /// 枚举引用类型及其嵌套类型参数。
    /// </summary>
    private static IEnumerable<ITypeSymbol> EnumerateReferencedTypeSymbols(ITypeSymbol typeSymbol)
    {
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var pending = new Stack<ITypeSymbol>();
        pending.Push(typeSymbol);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current))
            {
                continue;
            }

            yield return current;

            switch (current)
            {
                case IArrayTypeSymbol arrayType:
                    pending.Push(arrayType.ElementType);
                    break;
                case INamedTypeSymbol namedType:
                    foreach (var typeArgument in namedType.TypeArguments)
                    {
                        pending.Push(typeArgument);
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// 注册函数节点。
    /// </summary>
    private static void RegisterFunctionNodes(
        SourceDocument document,
        DocumentSyntaxIndex syntaxIndex,
        SemanticModel model,
        DocumentSymbolCache symbolCache,
        IDictionary<string, FunctionNodeRef> functionNodes)
    {
        foreach (var method in syntaxIndex.Methods)
        {
            RegisterFunctionNode(model.GetDeclaredSymbol(method), MemberKind.Method, document.RelativePath, symbolCache, functionNodes);
        }

        foreach (var ctor in syntaxIndex.Constructors)
        {
            RegisterFunctionNode(model.GetDeclaredSymbol(ctor), MemberKind.Constructor, document.RelativePath, symbolCache, functionNodes);
        }

        foreach (var accessor in syntaxIndex.Accessors)
        {
            RegisterFunctionNode(model.GetDeclaredSymbol(accessor), MemberKind.Accessor, document.RelativePath, symbolCache, functionNodes);
        }

        foreach (var @operator in syntaxIndex.Operators)
        {
            RegisterFunctionNode(model.GetDeclaredSymbol(@operator), MemberKind.Method, document.RelativePath, symbolCache, functionNodes);
        }

        foreach (var conversionOperator in syntaxIndex.ConversionOperators)
        {
            RegisterFunctionNode(model.GetDeclaredSymbol(conversionOperator), MemberKind.Method, document.RelativePath, symbolCache, functionNodes);
        }

        foreach (var property in syntaxIndex.PropertiesWithExpressionBody)
        {
            var propertySymbol = model.GetDeclaredSymbol(property) as IPropertySymbol;
            RegisterFunctionNode(propertySymbol?.GetMethod, MemberKind.Accessor, document.RelativePath, symbolCache, functionNodes, property);
        }
    }

    /// <summary>
    /// 注册类型体中的依赖边。
    /// </summary>
    private static void RegisterTypeBodyGraphs(
        DocumentSyntaxIndex syntaxIndex,
        SemanticModel model,
        DocumentSymbolCache symbolCache,
        ISet<TypeDependencyEdge> typeEdges)
    {
        var ignoredFunctionEdges = new HashSet<FunctionDependencyEdge>();

        foreach (var method in syntaxIndex.Methods)
        {
            var symbol = model.GetDeclaredSymbol(method);
            if (symbol != null)
            {
                RegisterFunctionBodyDependencies(symbol, GetBodyOrExpression(method), model, symbolCache, typeEdges, ignoredFunctionEdges);
            }
        }

        foreach (var ctor in syntaxIndex.Constructors)
        {
            var symbol = model.GetDeclaredSymbol(ctor);
            if (symbol != null)
            {
                RegisterFunctionBodyDependencies(symbol, GetReferenceScope(ctor), model, symbolCache, typeEdges, ignoredFunctionEdges);
            }
        }

        foreach (var accessor in syntaxIndex.Accessors)
        {
            var symbol = model.GetDeclaredSymbol(accessor);
            if (symbol != null)
            {
                RegisterFunctionBodyDependencies(symbol, GetBodyOrExpression(accessor), model, symbolCache, typeEdges, ignoredFunctionEdges);
            }
        }

        foreach (var @operator in syntaxIndex.Operators)
        {
            var symbol = model.GetDeclaredSymbol(@operator);
            if (symbol != null)
            {
                RegisterFunctionBodyDependencies(symbol, GetBodyOrExpression(@operator), model, symbolCache, typeEdges, ignoredFunctionEdges);
            }
        }

        foreach (var conversionOperator in syntaxIndex.ConversionOperators)
        {
            var symbol = model.GetDeclaredSymbol(conversionOperator);
            if (symbol != null)
            {
                RegisterFunctionBodyDependencies(symbol, GetBodyOrExpression(conversionOperator), model, symbolCache, typeEdges, ignoredFunctionEdges);
            }
        }

        foreach (var property in syntaxIndex.PropertiesWithExpressionBody)
        {
            var propertySymbol = model.GetDeclaredSymbol(property) as IPropertySymbol;
            if (propertySymbol?.GetMethod != null)
            {
                RegisterFunctionBodyDependencies(propertySymbol.GetMethod, GetBodyOrExpression(property), model, symbolCache, typeEdges, ignoredFunctionEdges);
            }
        }

        foreach (var field in syntaxIndex.Fields)
        {
            foreach (var variable in field.Declaration.Variables.Where(variable => variable.Initializer != null))
            {
                var fieldSymbol = model.GetDeclaredSymbol(variable) as IFieldSymbol;
                if (fieldSymbol?.ContainingType != null)
                {
                    RegisterTypeBodyDependencies(fieldSymbol.ContainingType, variable.Initializer!.Value, model, symbolCache, typeEdges);
                }
            }
        }

        foreach (var property in syntaxIndex.PropertiesWithInitializer)
        {
            var propertySymbol = model.GetDeclaredSymbol(property) as IPropertySymbol;
            if (propertySymbol?.ContainingType != null)
            {
                RegisterTypeBodyDependencies(propertySymbol.ContainingType, property.Initializer!.Value, model, symbolCache, typeEdges);
            }
        }
    }

    /// <summary>
    /// 创建文档语法索引。
    /// </summary>
    private static DocumentSyntaxIndex CreateSyntaxIndex(CompilationUnitSyntax root)
    {
        var baseTypes = new List<BaseTypeDeclarationSyntax>();
        var fields = new List<FieldDeclarationSyntax>();
        var propertiesWithInitializer = new List<PropertyDeclarationSyntax>();
        var propertiesWithExpressionBody = new List<PropertyDeclarationSyntax>();
        var classes = new List<ClassDeclarationSyntax>();
        var methods = new List<MethodDeclarationSyntax>();
        var constructors = new List<ConstructorDeclarationSyntax>();
        var accessors = new List<AccessorDeclarationSyntax>();
        var operators = new List<OperatorDeclarationSyntax>();
        var conversionOperators = new List<ConversionOperatorDeclarationSyntax>();

        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case BaseTypeDeclarationSyntax baseType:
                    baseTypes.Add(baseType);
                    if (baseType is ClassDeclarationSyntax classDeclaration)
                    {
                        classes.Add(classDeclaration);
                    }
                    break;
                case FieldDeclarationSyntax field:
                    fields.Add(field);
                    break;
                case PropertyDeclarationSyntax property:
                    if (property.Initializer != null)
                    {
                        propertiesWithInitializer.Add(property);
                    }

                    if (property.ExpressionBody != null)
                    {
                        propertiesWithExpressionBody.Add(property);
                    }
                    break;
                case MethodDeclarationSyntax method:
                    methods.Add(method);
                    break;
                case ConstructorDeclarationSyntax constructor:
                    constructors.Add(constructor);
                    break;
                case AccessorDeclarationSyntax accessor:
                    accessors.Add(accessor);
                    break;
                case OperatorDeclarationSyntax @operator:
                    operators.Add(@operator);
                    break;
                case ConversionOperatorDeclarationSyntax conversionOperator:
                    conversionOperators.Add(conversionOperator);
                    break;
            }
        }

        return new DocumentSyntaxIndex(
            baseTypes,
            fields,
            propertiesWithInitializer,
            propertiesWithExpressionBody,
            classes,
            methods,
            constructors,
            accessors,
            operators,
            conversionOperators);
    }

    /// <summary>
    /// 注册单个函数节点。
    /// </summary>
    private static void RegisterFunctionNode(
        ISymbol? symbol,
        MemberKind memberKind,
        string documentPath,
        DocumentSymbolCache symbolCache,
        IDictionary<string, FunctionNodeRef> functionNodes,
        SyntaxNode? declarationSyntaxOverride = null)
    {
        if (symbol == null)
        {
            return;
        }

        var memberId = symbolCache.GetMemberId(symbol);
        var declarationSyntax = declarationSyntaxOverride ?? symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        var methodSymbol = symbol as IMethodSymbol;
        var returnsVoid = methodSymbol?.ReturnsVoid ?? false;
        var hasBody = declarationSyntax switch
        {
            MethodDeclarationSyntax methodDeclaration => methodDeclaration.Body != null,
            ConstructorDeclarationSyntax constructorDeclaration => constructorDeclaration.Body != null,
            AccessorDeclarationSyntax accessorDeclaration => accessorDeclaration.Body != null,
            OperatorDeclarationSyntax operatorDeclaration => operatorDeclaration.Body != null,
            ConversionOperatorDeclarationSyntax conversionOperatorDeclaration => conversionOperatorDeclaration.Body != null,
            PropertyDeclarationSyntax propertyDeclaration => propertyDeclaration.ExpressionBody != null,
            _ => false
        };
        var hasStatements = declarationSyntax switch
        {
            MethodDeclarationSyntax methodDeclaration => methodDeclaration.Body?.Statements.Count > 0,
            ConstructorDeclarationSyntax constructorDeclaration => constructorDeclaration.Body?.Statements.Count > 0,
            AccessorDeclarationSyntax accessorDeclaration => accessorDeclaration.Body?.Statements.Count > 0,
            OperatorDeclarationSyntax operatorDeclaration => operatorDeclaration.Body?.Statements.Count > 0,
            ConversionOperatorDeclarationSyntax conversionOperatorDeclaration => conversionOperatorDeclaration.Body?.Statements.Count > 0,
            PropertyDeclarationSyntax => false,
            _ => false
        };
        var returnTypeDisplay = methodSymbol?.ReturnType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? "void";
        functionNodes[memberId.Value] = new FunctionNodeRef(
            memberId,
            memberKind,
            symbolCache.GetTypeId(symbol.ContainingType),
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
        var symbolCache = new DocumentSymbolCache();
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var symbol = model.GetDeclaredSymbol(method);
            if (symbol != null)
            {
                RegisterFunctionBodyDependencies(symbol, method.Body ?? (SyntaxNode?)method.ExpressionBody, model, symbolCache, typeEdges, functionEdges);
            }
        }

        foreach (var ctor in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
        {
            var symbol = model.GetDeclaredSymbol(ctor);
            if (symbol != null)
            {
                RegisterFunctionBodyDependencies(symbol, ctor.Body ?? (SyntaxNode?)ctor.ExpressionBody, model, symbolCache, typeEdges, functionEdges);
            }
        }

        foreach (var accessor in root.DescendantNodes().OfType<AccessorDeclarationSyntax>())
        {
            var symbol = model.GetDeclaredSymbol(accessor);
            if (symbol != null)
            {
                RegisterFunctionBodyDependencies(symbol, accessor.Body ?? (SyntaxNode?)accessor.ExpressionBody, model, symbolCache, typeEdges, functionEdges);
            }
        }

        foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Where(static property => property.ExpressionBody != null))
        {
            var propertySymbol = model.GetDeclaredSymbol(property) as IPropertySymbol;
            if (propertySymbol?.GetMethod != null)
            {
                RegisterFunctionBodyDependencies(propertySymbol.GetMethod, GetBodyOrExpression(property), model, symbolCache, typeEdges, functionEdges);
            }
        }

        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            foreach (var variable in field.Declaration.Variables.Where(variable => variable.Initializer != null))
            {
                var fieldSymbol = model.GetDeclaredSymbol(variable) as IFieldSymbol;
                if (fieldSymbol?.ContainingType != null)
                {
                    RegisterTypeBodyDependencies(fieldSymbol.ContainingType, variable.Initializer!.Value, model, symbolCache, typeEdges);
                }
            }
        }

        foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Where(property => property.Initializer != null))
        {
            var propertySymbol = model.GetDeclaredSymbol(property) as IPropertySymbol;
            if (propertySymbol?.ContainingType != null)
            {
                RegisterTypeBodyDependencies(propertySymbol.ContainingType, property.Initializer!.Value, model, symbolCache, typeEdges);
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
        DocumentSymbolCache symbolCache,
        ISet<TypeDependencyEdge> typeEdges,
        ISet<FunctionDependencyEdge> functionEdges)
    {
        if (bodyOrExpression == null)
        {
            return;
        }

        var currentMemberId = symbolCache.GetMemberId(currentMember);
        var currentTypeId = symbolCache.GetTypeId(currentMember.ContainingType);

        foreach (var node in bodyOrExpression.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case InvocationExpressionSyntax invocation:
                    if (model.GetSymbolInfo(invocation).Symbol is IMethodSymbol targetMethod)
                    {
                        functionEdges.Add(new FunctionDependencyEdge(currentMemberId, symbolCache.GetMemberId(targetMethod), FunctionDependencyKind.Calls));
                        RegisterTypeReferenceEdge(currentTypeId, targetMethod.ContainingType, TypeDependencyKind.MemberBodyReference, typeEdges, currentMemberId, symbolCache);
                    }
                    break;
                case BaseObjectCreationExpressionSyntax creation:
                    if (model.GetSymbolInfo(creation).Symbol is IMethodSymbol ctorSymbol)
                    {
                        functionEdges.Add(new FunctionDependencyEdge(currentMemberId, symbolCache.GetMemberId(ctorSymbol), FunctionDependencyKind.Creates));
                        RegisterTypeReferenceEdge(currentTypeId, ctorSymbol.ContainingType, TypeDependencyKind.ObjectCreation, typeEdges, currentMemberId, symbolCache);
                    }
                    break;
                case IdentifierNameSyntax identifier:
                    RegisterMemberReference(identifier, currentMemberId, currentTypeId, model, symbolCache, typeEdges, functionEdges);
                    break;
                case MemberAccessExpressionSyntax memberAccess:
                    var memberAccessSymbol = model.GetSymbolInfo(memberAccess).Symbol;
                    if (memberAccessSymbol?.IsStatic == true)
                    {
                        RegisterTypeReferenceEdge(currentTypeId, memberAccessSymbol.ContainingType, TypeDependencyKind.StaticMemberAccess, typeEdges, currentMemberId, symbolCache);
                    }
                    break;
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
        DocumentSymbolCache symbolCache,
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
            symbolCache.GetMemberId(symbol),
            isWrite ? FunctionDependencyKind.WritesMember : FunctionDependencyKind.ReadsMember));
        RegisterTypeReferenceEdge(currentTypeId, symbol.ContainingType, TypeDependencyKind.MemberBodyReference, typeEdges, currentMemberId, symbolCache);

        if (symbol is IPropertySymbol property)
        {
            var accessor = isWrite ? property.SetMethod : property.GetMethod;
            if (accessor != null)
            {
                functionEdges.Add(new FunctionDependencyEdge(currentMemberId, symbolCache.GetMemberId(accessor), FunctionDependencyKind.UsesPropertyAccessor));
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
        MemberId currentMemberId,
        DocumentSymbolCache symbolCache)
    {
        if (targetType == null)
        {
            return;
        }

        typeEdges.Add(new TypeDependencyEdge(currentTypeId, symbolCache.GetTypeId(targetType), kind, currentMemberId.Value));
    }

    /// <summary>
    /// 注册类型体依赖。
    /// </summary>
    private static void RegisterTypeBodyDependencies(
        INamedTypeSymbol containingType,
        SyntaxNode node,
        SemanticModel model,
        DocumentSymbolCache symbolCache,
        ISet<TypeDependencyEdge> typeEdges)
    {
        var currentTypeId = symbolCache.GetTypeId(containingType);

        foreach (var descendant in node.DescendantNodesAndSelf())
        {
            switch (descendant)
            {
                case BaseObjectCreationExpressionSyntax creation:
                    if (model.GetSymbolInfo(creation).Symbol is IMethodSymbol ctorSymbol)
                    {
                        typeEdges.Add(new TypeDependencyEdge(currentTypeId, symbolCache.GetTypeId(ctorSymbol.ContainingType), TypeDependencyKind.ObjectCreation));
                    }
                    break;
                case MemberAccessExpressionSyntax memberAccess:
                    if (model.GetSymbolInfo(memberAccess).Symbol is ISymbol memberSymbol)
                    {
                        var kind = memberSymbol.IsStatic ? TypeDependencyKind.StaticMemberAccess : TypeDependencyKind.MemberBodyReference;
                        typeEdges.Add(new TypeDependencyEdge(currentTypeId, symbolCache.GetTypeId(memberSymbol.ContainingType), kind));
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// 获取基础方法声明的函数体或表达式体节点。
    /// </summary>
    private static SyntaxNode? GetBodyOrExpression(BaseMethodDeclarationSyntax declaration)
    {
        return declaration switch
        {
            MethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody?.Expression,
            ConstructorDeclarationSyntax ctor => (SyntaxNode?)ctor.Body ?? ctor.ExpressionBody?.Expression,
            OperatorDeclarationSyntax @operator => (SyntaxNode?)@operator.Body ?? @operator.ExpressionBody?.Expression,
            ConversionOperatorDeclarationSyntax conversionOperator => (SyntaxNode?)conversionOperator.Body ?? conversionOperator.ExpressionBody?.Expression,
            _ => null
        };
    }

    /// <summary>
    /// 获取构造函数用于引用分析的作用域节点。
    /// </summary>
    private static SyntaxNode? GetReferenceScope(ConstructorDeclarationSyntax declaration) =>
        declaration.Initializer != null
            ? declaration
            : GetBodyOrExpression(declaration);

    /// <summary>
    /// 获取访问器的函数体或表达式体节点。
    /// </summary>
    private static SyntaxNode? GetBodyOrExpression(AccessorDeclarationSyntax accessor) =>
        (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody?.Expression;

    /// <summary>
    /// 获取属性表达式体节点。
    /// </summary>
    private static SyntaxNode? GetBodyOrExpression(PropertyDeclarationSyntax property) =>
        property.ExpressionBody?.Expression;

    /// <summary>
    /// 收集节点中的调用成员标识集合。
    /// </summary>
    private static IReadOnlyList<MemberId> CollectCalledMemberIds(SyntaxNode? bodyOrExpression, SemanticModel model, DocumentSymbolCache symbolCache)
    {
        if (bodyOrExpression == null)
        {
            return Array.Empty<MemberId>();
        }

        return bodyOrExpression.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Select(invocation => NormalizeReferencedMethodSymbol(model.GetSymbolInfo(invocation).Symbol))
            .OfType<IMethodSymbol>()
            .Select(symbolCache.GetMemberId)
            .DistinctBy(memberId => memberId.Value)
            .OrderBy(memberId => memberId.Value, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// 收集节点中的方法引用标识集合。
    /// </summary>
    private static IReadOnlyList<MemberId> CollectReferencedMethodIds(SyntaxNode? bodyOrExpression, SemanticModel model)
    {
        if (bodyOrExpression == null)
        {
            return Array.Empty<MemberId>();
        }

        return bodyOrExpression.DescendantNodesAndSelf()
            .Where(node => node is IdentifierNameSyntax or MemberAccessExpressionSyntax)
            .SelectMany(node =>
            {
                var info = model.GetSymbolInfo(node);
                if (NormalizeReferencedMethodSymbol(info.Symbol) is IMethodSymbol methodSymbol)
                {
                    return new[] { methodSymbol };
                }

                return info.CandidateSymbols
                    .Select(NormalizeReferencedMethodSymbol)
                    .OfType<IMethodSymbol>();
            })
            .Where(symbol => symbol.MethodKind is not MethodKind.AnonymousFunction and not MethodKind.LocalFunction)
            .Select(MetadataMemberIdBuilder.Build)
            .DistinctBy(memberId => memberId.Value)
            .OrderBy(memberId => memberId.Value, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// 规范化引用方法符号以便稳定建模。
    /// </summary>
    private static ISymbol? NormalizeReferencedMethodSymbol(ISymbol? symbol)
    {
        if (symbol is not IMethodSymbol methodSymbol)
        {
            return symbol;
        }

        if (methodSymbol.ReducedFrom is IMethodSymbol reducedFrom)
        {
            methodSymbol = reducedFrom;
        }

        return methodSymbol.MethodKind is MethodKind.Ordinary && methodSymbol.IsGenericMethod
            ? methodSymbol.OriginalDefinition
            : methodSymbol;
    }

    /// <summary>
    /// 收集持久引用语义下的类型标识集合。
    /// </summary>
    private static IReadOnlyList<string> CollectPersistentReferencedTypeIds(SyntaxNode? bodyOrExpression, SemanticModel model)
    {
        if (bodyOrExpression == null)
        {
            return Array.Empty<string>();
        }

        var referencedTypeIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var invocation in bodyOrExpression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol)
            {
                continue;
            }

            if (string.Equals(methodSymbol.Name, "Register", StringComparison.Ordinal))
            {
                foreach (var typeArgument in methodSymbol.TypeArguments.OfType<INamedTypeSymbol>())
                {
                    referencedTypeIds.Add(MetadataTypeIdBuilder.Build(typeArgument));
                }

                continue;
            }

            if (string.Equals(methodSymbol.Name, "Add", StringComparison.Ordinal) &&
                invocation.ArgumentList.Arguments.Count > 0 &&
                invocation.ArgumentList.Arguments[0].Expression is BaseObjectCreationExpressionSyntax creation &&
                methodSymbol.Parameters.Length > 0 &&
                IsKnownRuleNodeType(methodSymbol.Parameters[0].Type))
            {
                if (model.GetSymbolInfo(creation).Symbol is IMethodSymbol ctorSymbol)
                {
                    referencedTypeIds.Add(MetadataTypeIdBuilder.Build(ctorSymbol.ContainingType));
                }
            }
        }

        foreach (var assignment in bodyOrExpression.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Left is not ElementAccessExpressionSyntax elementAccess ||
                assignment.Right is not BaseObjectCreationExpressionSyntax creation)
            {
                continue;
            }

            if (!IsKnownManagerOrResolverIndexer(elementAccess, model))
            {
                continue;
            }

            if (model.GetSymbolInfo(creation).Symbol is IMethodSymbol ctorSymbol)
            {
                referencedTypeIds.Add(MetadataTypeIdBuilder.Build(ctorSymbol.ContainingType));
            }
        }

        return referencedTypeIds
            .OrderBy(typeId => typeId, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// 收集节点中新建对象的类型标识集合。
    /// </summary>
    private static IReadOnlyList<string> CollectCreatedTypeIds(SyntaxNode node, SemanticModel model)
    {
        return node.DescendantNodesAndSelf()
            .OfType<BaseObjectCreationExpressionSyntax>()
            .Select(creation => model.GetSymbolInfo(creation).Symbol as IMethodSymbol)
            .OfType<IMethodSymbol>()
            .Select(symbol => MetadataTypeIdBuilder.Build(symbol.ContainingType))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(typeId => typeId, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// 判断索引器访问是否来自管理器或解析器对象。
    /// </summary>
    private static bool IsKnownManagerOrResolverIndexer(ElementAccessExpressionSyntax elementAccess, SemanticModel model)
    {
        if (model.GetTypeInfo(elementAccess.Expression).Type is not ITypeSymbol typeSymbol)
        {
            return false;
        }

        return LooksLikeKnownPersistentOwnerType(typeSymbol.Name) ||
               (typeSymbol.ContainingType != null && LooksLikeKnownPersistentOwnerType(typeSymbol.ContainingType.Name)) ||
               IsStaticInstanceAccess(elementAccess.Expression);
    }

    /// <summary>
    /// 判断表达式是否为静态实例访问模式。
    /// </summary>
    private static bool IsStaticInstanceAccess(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Instance",
                Expression: IdentifierNameSyntax owner
            } => LooksLikeKnownPersistentOwnerType(owner.Identifier.ValueText),
            _ => false
        };
    }

    /// <summary>
    /// 判断名称是否符合已知持久拥有者类型特征。
    /// </summary>
    private static bool LooksLikeKnownPersistentOwnerType(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        KnownPersistentOwnerTypeMarkers.Any(marker => name.Contains(marker, StringComparison.Ordinal));

    /// <summary>
    /// 判断类型是否符合持久拥有者特征。
    /// </summary>
    private static bool LooksLikePersistentOwner(INamedTypeSymbol typeSymbol) =>
        LooksLikeKnownPersistentOwnerType(typeSymbol.Name);

    /// <summary>
    /// 判断字段是否符合已知持久字段特征。
    /// </summary>
    private static bool LooksLikeKnownPersistentField(IFieldSymbol fieldSymbol) =>
        fieldSymbol.IsStatic &&
        (string.Equals(fieldSymbol.Name, "Rules", StringComparison.Ordinal) ||
         string.Equals(fieldSymbol.Name, "ChainedRules", StringComparison.Ordinal)) &&
        fieldSymbol.Type is INamedTypeSymbol namedType &&
        namedType.IsGenericType;

    /// <summary>
    /// 判断类型是否为规则节点类型。
    /// </summary>
    private static bool IsKnownRuleNodeType(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is INamedTypeSymbol namedType &&
            namedType.AllInterfaces.Any(iface => iface.Name.Contains("ItemDropRule", StringComparison.Ordinal)))
        {
            return true;
        }

        return typeSymbol.Name.Contains("ItemDropRule", StringComparison.Ordinal);
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
        MemberId memberId,
        bool isHighRiskMember,
        SemanticModel model,
        MemberKind memberKind,
        DocumentSymbolCache symbolCache)
    {
        var statementInspection = AnalyzeStatementInspection(initializer, model, memberId, symbolCache, memberSymbol);
        var dataflowFacts = statementInspection.DataflowFacts;
        return new AnalysisTarget(
            new PlanTarget(
                document.RelativePath,
                memberId,
                memberKind,
                TargetKind.Statement,
                declarationNode.SpanStart,
                declarationNode.Span.Length,
                GetDisplayText(document, declarationNode.SpanStart, declarationNode.Span.Length),
                new TargetResolutionKey(declarationNode.SpanStart, declarationNode.Span.Length)),
            isHighRiskMember,
            Array.Empty<DirectiveAction>(),
            dataflowFacts.DefinesSymbols,
            dataflowFacts.UsesSymbols,
            Array.Empty<MemberId>(),
            StatementKindRef.Initializer,
            dataflowFacts.IsSanitizingAssignment,
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
        MemberId memberId,
        bool isHighRiskMember,
        SemanticModel model,
        MemberKind memberKind,
        IDictionary<BlockSyntax, (string ScopeId, string? ParentScopeId)> scopeCache,
        DocumentSymbolCache symbolCache)
    {
        var statementInspection = AnalyzeStatementInspection(statement, model, memberId, symbolCache);
        var dataflowFacts = statementInspection.DataflowFacts;
        var statementKind = ClassifyStatementKind(statement);
        var isObjectInitializerAssignment = IsObjectInitializerAssignment(statement);
        var markedExpressionKinds = statementInspection.MarkedExpressionKinds;
        var scopeInfo = GetScopeInfo(statement, scopeCache);
        return new AnalysisTarget(
            new PlanTarget(
                document.RelativePath,
                memberId,
                memberKind,
                TargetKind.Statement,
                statement.SpanStart,
                statement.Span.Length,
                GetDisplayText(document, statement.SpanStart, statement.Span.Length),
                new TargetResolutionKey(statement.SpanStart, statement.Span.Length)),
            isHighRiskMember || isObjectInitializerAssignment,
            DirectiveReader.Read(statement),
            dataflowFacts.DefinesSymbols,
            dataflowFacts.UsesSymbols,
            statementInspection.InvokedMemberIds,
            statementKind,
            dataflowFacts.IsSanitizingAssignment,
            isObjectInitializerAssignment,
            markedExpressionKinds.Count > 0,
            markedExpressionKinds,
            scopeInfo.ScopeMode,
            scopeInfo.ScopeId,
            scopeInfo.ParentScopeId);
    }

    /// <summary>
    /// 获取目标片段显示文本。
    /// </summary>
    private static string GetDisplayText(SourceDocument document, int spanStart, int spanLength)
    {
        if (spanStart < 0 || spanLength <= 0 || spanStart + spanLength > document.SourceText.Length)
        {
            return string.Empty;
        }

        return document.SourceText.AsSpan(spanStart, spanLength).ToString().Trim();
    }

    /// <summary>
    /// 计算语句的最小作用域与父块穿透信息。
    /// </summary>
    private static (StatementScopeMode ScopeMode, string ScopeId, string? ParentScopeId) GetScopeInfo(
        StatementSyntax statement,
        IDictionary<BlockSyntax, (string ScopeId, string? ParentScopeId)> scopeCache)
    {
        var currentBlock = statement.Parent as BlockSyntax;
        if (currentBlock == null)
        {
            return (StatementScopeMode.MinimalBlock, CreateFallbackScopeId(statement), null);
        }

        if (!scopeCache.TryGetValue(currentBlock, out var scopeIds))
        {
            var parentBlock = GetParentBlock(currentBlock);
            scopeIds = (
                CreateScopeId(currentBlock),
                parentBlock == null ? null : CreateScopeId(parentBlock));
            scopeCache[currentBlock] = scopeIds;
        }

        if (scopeIds.ParentScopeId == null)
        {
            return (StatementScopeMode.MinimalBlock, scopeIds.ScopeId, null);
        }

        return (StatementScopeMode.MinimalBlock, scopeIds.ScopeId, scopeIds.ParentScopeId);
    }

    /// <summary>
    /// 为无块语句创建回退作用域标识。
    /// </summary>
    private static string CreateFallbackScopeId(StatementSyntax statement) =>
        $"fallback|{statement.SyntaxTree.FilePath}|{statement.SpanStart}|{statement.Span.Length}";

    /// <summary>
    /// 创建块作用域标识。
    /// </summary>
    private static string CreateScopeId(BlockSyntax block) =>
        $"block|{block.SyntaxTree.FilePath}|{block.SpanStart}|{block.Span.Length}";

    /// <summary>
    /// 获取当前块所属的最近父块。
    /// </summary>
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
    /// 获取定义的符号。
    /// </summary>
    private static StatementInspectionResult AnalyzeStatementInspection(
        SyntaxNode node,
        SemanticModel model,
        MemberId declaringMemberId,
        DocumentSymbolCache symbolCache,
        ISymbol? memberSymbol = null)
    {
        var definesSymbols = GetDefinedSymbols(node, model, declaringMemberId, memberSymbol);
        var definedKeys = definesSymbols
            .Select(symbol => symbol.SymbolKey)
            .ToHashSet(StringComparer.Ordinal);
        var usesSymbols = new List<SymbolRef>();
        var useSymbolKeys = new HashSet<string>(StringComparer.Ordinal);
        var invokedMemberIds = new List<MemberId>();
        var invokedMemberIdValues = new HashSet<string>(StringComparer.Ordinal);
        var markedExpressionKinds = new HashSet<string>(StringComparer.Ordinal);
        var rightSideExpression = TryGetRightSideExpression(node);
        var directInvocation = TryGetDirectInvocation(node);
        var rightSideSpan = rightSideExpression?.Span;
        var hasTrackedIdentifierUse = false;

        foreach (var descendant in node.DescendantNodes())
        {
            switch (descendant)
            {
                case IdentifierNameSyntax identifier:
                    var identifierSymbol = model.GetSymbolInfo(identifier).Symbol;
                    var shouldTrackIdentifier = ShouldTrackDataflowSymbol(identifierSymbol);
                    if (!hasTrackedIdentifierUse &&
                        shouldTrackIdentifier &&
                        rightSideSpan.HasValue &&
                        identifier.SpanStart >= rightSideSpan.Value.Start &&
                        identifier.Span.End <= rightSideSpan.Value.End)
                    {
                        hasTrackedIdentifierUse = true;
                    }

                    var projectedUse = SymbolRefProjector.Project(identifierSymbol, declaringMemberId);
                    if (shouldTrackIdentifier &&
                        projectedUse != null &&
                        !definedKeys.Contains(projectedUse.SymbolKey) &&
                        useSymbolKeys.Add(projectedUse.SymbolKey))
                    {
                        usesSymbols.Add(projectedUse);
                    }

                    AddMarkedExpressionKind(markedExpressionKinds, identifier);
                    break;
                case InvocationExpressionSyntax invocation:
                    if (ReferenceEquals(invocation, directInvocation) &&
                        model.GetSymbolInfo(invocation).Symbol is IMethodSymbol methodSymbol)
                    {
                        var invokedMemberId = symbolCache.GetMemberId(methodSymbol);
                        if (invokedMemberIdValues.Add(invokedMemberId.Value))
                        {
                            invokedMemberIds.Add(invokedMemberId);
                        }
                    }

                    AddMarkedExpressionKind(markedExpressionKinds, invocation);
                    break;
                case ExpressionSyntax expression:
                    AddMarkedExpressionKind(markedExpressionKinds, expression);
                    break;
            }
        }

        var isSanitizingAssignment = rightSideExpression switch
        {
            null => false,
            LiteralExpressionSyntax => true,
            _ => !hasTrackedIdentifierUse
        };

        return new StatementInspectionResult(
            new DataflowFacts(
                definesSymbols,
                usesSymbols,
                isSanitizingAssignment),
            invokedMemberIds,
            markedExpressionKinds.OrderBy(kind => kind, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// 记录命中的表达式语法种类。
    /// </summary>
    private static void AddMarkedExpressionKind(ISet<string> markedExpressionKinds, ExpressionSyntax expression)
    {
        var kind = expression.Kind();
        if (kind is
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
        {
            markedExpressionKinds.Add(kind.ToString());
        }
    }

    /// <summary>
    /// 尝试提取赋值或初始化右侧表达式。
    /// </summary>
    private static ExpressionSyntax? TryGetRightSideExpression(SyntaxNode node)
    {
        return node switch
        {
            LocalDeclarationStatementSyntax localDeclaration => localDeclaration.Declaration.Variables.FirstOrDefault()?.Initializer?.Value,
            ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment } => assignment.Right,
            EqualsValueClauseSyntax initializer => initializer.Value,
            _ => null
        };
    }

    /// <summary>
    /// 尝试提取节点中的直接调用表达式。
    /// </summary>
    private static InvocationExpressionSyntax? TryGetDirectInvocation(SyntaxNode node)
    {
        return node switch
        {
            ExpressionStatementSyntax { Expression: InvocationExpressionSyntax expressionInvocation } => expressionInvocation,
            ReturnStatementSyntax { Expression: InvocationExpressionSyntax returnInvocation } => returnInvocation,
            _ => null
        };
    }

    /// <summary>
    /// 获取节点定义的符号集合。
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
        ISet<string>? definedKeys = null)
    {
        definedKeys ??= new HashSet<string>(StringComparer.Ordinal);

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
