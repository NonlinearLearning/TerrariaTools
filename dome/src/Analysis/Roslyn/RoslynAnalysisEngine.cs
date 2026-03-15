using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome.Analysis.Roslyn;

using TerrariaTools.Dome.Core;

/// <summary>
/// 鍑芥暟浜嬪疄绉嶅瓙锛岃褰曞嚱鏁颁笌鍏惰皟鐢ㄦ垚鍛橀泦鍚堛€?
/// </summary>
/// <param name="MemberId">鍑芥暟鎴愬憳鏍囪瘑銆?/param>
/// <param name="CalledMemberIds">琚皟鐢ㄦ垚鍛樻爣璇嗛泦鍚堛€?/param>
internal sealed record FunctionFactSeed(
    MemberId MemberId,
    IReadOnlyList<MemberId> CalledMemberIds);

/// <summary>
/// 鏁版嵁娴佷簨瀹烇紝鍖呭惈瀹氫箟闆嗗悎銆佷娇鐢ㄩ泦鍚堜笌鍑€鍖栬祴鍊兼爣璁般€?
/// </summary>
/// <param name="DefinesSymbols">瀹氫箟鐨勭鍙烽泦鍚堛€?/param>
/// <param name="UsesSymbols">浣跨敤鐨勭鍙烽泦鍚堛€?/param>
/// <param name="IsSanitizingAssignment">鏄惁涓哄噣鍖栬祴鍊笺€?/param>
internal sealed record DataflowFacts(
    IReadOnlyList<SymbolRef> DefinesSymbols,
    IReadOnlyList<SymbolRef> UsesSymbols,
    bool IsSanitizingAssignment);

/// <summary>
/// 璇彞妫€鏌ョ粨鏋滐紝鍖呭惈鏁版嵁娴併€佽皟鐢ㄦ垚鍛樹笌琛ㄨ揪寮忔爣璁颁俊鎭€?
/// </summary>
/// <param name="DataflowFacts">鏁版嵁娴佷簨瀹炪€?/param>
/// <param name="InvokedMemberIds">鐩存帴璋冪敤鐨勬垚鍛樻爣璇嗛泦鍚堛€?/param>
/// <param name="MarkedExpressionKinds">鍛戒腑鐨勮〃杈惧紡璇硶绉嶇被闆嗗悎銆?/param>
internal sealed record StatementInspectionResult(
    DataflowFacts DataflowFacts,
    IReadOnlyList<MemberId> InvokedMemberIds,
    IReadOnlyList<string> MarkedExpressionKinds);

/// <summary>
/// 鍗曟枃妗ｅ垎鏋愯€楁椂鏄庣粏锛圱icks锛夈€?
/// </summary>
/// <param name="SyntaxIndexTicks">璇硶绱㈠紩闃舵鑰楁椂銆?/param>
/// <param name="TypeGraphTicks">绫诲瀷鍥炬瀯寤洪樁娈佃€楁椂銆?/param>
/// <param name="FunctionNodeTicks">鍑芥暟鑺傜偣娉ㄥ唽闃舵鑰楁椂銆?/param>
/// <param name="TypeBodyGraphTicks">绫诲瀷浣撲緷璧栧垎鏋愰樁娈佃€楁椂銆?/param>
/// <param name="TargetAnalysisTicks">鐩爣鍒嗘瀽闃舵鑰楁椂銆?/param>
/// <param name="FunctionFactsTicks">鍑芥暟浜嬪疄鐢熸垚闃舵鑰楁椂銆?/param>
internal sealed record DocumentAnalysisTimings(
    long SyntaxIndexTicks,
    long TypeGraphTicks,
    long FunctionNodeTicks,
    long TypeBodyGraphTicks,
    long TargetAnalysisTicks,
    long FunctionFactsTicks);

/// <summary>
/// 鏂囨。璇硶绱㈠紩锛岀紦瀛樺父鐢ㄨ娉曡妭鐐归泦鍚堜互闄嶄綆閲嶅閬嶅巻鎴愭湰銆?
/// </summary>
/// <param name="BaseTypes">鍩虹绫诲瀷澹版槑闆嗗悎銆?/param>
/// <param name="Fields">瀛楁澹版槑闆嗗悎銆?/param>
/// <param name="PropertiesWithInitializer">甯﹀垵濮嬪寲鍣ㄧ殑灞炴€у０鏄庨泦鍚堛€?/param>
/// <param name="PropertiesWithExpressionBody">琛ㄨ揪寮忎綋灞炴€у０鏄庨泦鍚堛€?/param>
/// <param name="Classes">绫诲０鏄庨泦鍚堛€?/param>
/// <param name="Methods">鏂规硶澹版槑闆嗗悎銆?/param>
/// <param name="Constructors">鏋勯€犲嚱鏁板０鏄庨泦鍚堛€?/param>
/// <param name="Accessors">璁块棶鍣ㄥ０鏄庨泦鍚堛€?/param>
/// <param name="Operators">杩愮畻绗﹀０鏄庨泦鍚堛€?/param>
/// <param name="ConversionOperators">杞崲杩愮畻绗﹀０鏄庨泦鍚堛€?/param>
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
/// 鍗曟枃妗ｅ垎鏋愭墦鍖呯粨鏋滐紝鍖呭惈浜х墿涓庢€ц兘鏁版嵁銆?
/// </summary>
/// <param name="Document">婧愭枃妗ｃ€?/param>
/// <param name="Root">缂栬瘧鍗曞厓璇硶鏍广€?/param>
/// <param name="SemanticModel">璇箟妯″瀷銆?/param>
/// <param name="Targets">鍒嗘瀽鐩爣闆嗗悎銆?/param>
/// <param name="AnalysisEdges">鍒嗘瀽杈归泦鍚堛€?/param>
/// <param name="TypeNodes">绫诲瀷鑺傜偣闆嗗悎銆?/param>
/// <param name="TypeEdges">绫诲瀷渚濊禆杈归泦鍚堛€?/param>
/// <param name="FunctionNodes">鍑芥暟鑺傜偣闆嗗悎銆?/param>
/// <param name="FunctionFacts">鍑芥暟浜嬪疄闆嗗悎銆?/param>
/// <param name="Timings">鏂囨。鍒嗘瀽鑰楁椂鏄庣粏銆?/param>
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
/// 鏂囨。绾х鍙风紦瀛橈紝澶嶇敤鎴愬憳鏍囪瘑涓庣被鍨嬫爣璇嗚绠楃粨鏋溿€?
/// </summary>
internal sealed class DocumentSymbolCache
{
    private readonly Dictionary<ISymbol, MemberId> _memberIds = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<ITypeSymbol, string> _typeIds = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// 鑾峰彇鎴愬憳鏍囪瘑锛屽苟鍦ㄩ娆¤闂椂鍐欏叆缂撳瓨銆?
    /// </summary>
    /// <param name="symbol">鎴愬憳绗﹀彿銆?/param>
    /// <returns>鎴愬憳鏍囪瘑銆?/returns>
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
    /// 鑾峰彇绫诲瀷鏍囪瘑锛屽苟鍦ㄩ娆¤闂椂鍐欏叆缂撳瓨銆?
    /// </summary>
    /// <param name="symbol">绫诲瀷绗﹀彿銆?/param>
    /// <returns>绫诲瀷鏍囪瘑銆?/returns>
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
/// Roslyn鍒嗘瀽寮曟搸锛岃礋璐ｅ崗璋冩枃妗ｇ殑瑙ｆ瀽銆佺紪璇戝拰渚濊禆鍒嗘瀽銆?
/// 绫讳緷璧栧浘鏄鍙杝ln椤圭洰灏辫繘琛屽叏閮ㄥ垎鏋愶紝鍑芥暟渚濊禆鍒嗘瀽闇€瑕佹敮鎸佹€ц兘鏇村ソ鐨勫姩鎬佹枃浠惰寖鍥村垎鏋愬拰鍏╯ln椤圭洰鍒嗘瀽
/// 鑰岃鍙ュ垎鏋愬垵鐗堥渶瑕佹敮鎸佹渶灏忎綔鐢ㄥ煙鍒嗘瀽,鐒跺悗鏀寔寮曠敤鍏崇郴绌块€?鍗宠法瓒婁綔鐢ㄥ煙浣嗘闃舵涓嶄細璺ㄨ秺鍑芥暟浣滅敤鍩熷拰绫讳綔鐢ㄥ煙
/// 涓嬩竴闃舵鏄敮鎸佸煙鍒嗘瀽绌块€忔敮鎸佸埌鍑芥暟鍜岀被鎴愬憳灞炴€?涓嬮樁娈垫洿涓哄鏉傞渶瑕佹敮鎸佸浣滅敤璺ㄨ秺鍜屼笉鍚屼綔鐢ㄥ煙鐨勬贩鍚堝垎鏋?
/// </summary>
public sealed class RoslynAnalysisEngine : IAnalysisEngine
{
    private static readonly string[] KnownPersistentOwnerTypeMarkers =
    [
        "Manager",
        "Registry",
        "Resolver"
    ];

    /// <summary>
    /// 寮傛鎵ц鍒嗘瀽銆?
    /// </summary>
    /// <param name="documents">婧愭枃妗ｅ垪琛ㄣ€?/param>
    /// <param name="cancellationToken">鍙栨秷浠ょ墝銆?/param>
    /// <returns>Roslyn鍒嗘瀽缁撴灉銆?/returns>
    public Task<AnalysisEngineResult> AnalyzeAsync(
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
    /// 鎸夎緭鍏ョ被鍨嬮€夋嫨鍒嗘瀽鍏ュ彛銆?
    /// </summary>
    /// <param name="input">鍒嗘瀽杈撳叆銆?/param>
    /// <param name="cancellationToken">鍙栨秷浠ょ墝銆?/param>
    /// <returns>Roslyn鍒嗘瀽缁撴灉銆?/returns>
    public async Task<AnalysisEngineResult> AnalyzeAsync(
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
    /// 浠呭垎鏋愭簮鐮佹ā寮忋€?
    /// </summary>
    private async Task<AnalysisEngineResult> AnalyzeSourceOnlyAsync(
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
        var analyzedDocuments = new List<AnalysisDocumentContext>(bundles.Length);
        var allTargets = new List<AnalysisTarget>();
        var allEdges = new List<AnalysisEdge>();
        var typeNodes = new Dictionary<string, TypeNodeRef>(StringComparer.Ordinal);
        var typeEdges = new HashSet<TypeDependencyEdge>();
        var functionNodes = new Dictionary<string, FunctionNodeRef>(StringComparer.Ordinal);

        foreach (var bundle in bundles)
        {
            analyzedDocuments.Add(new AnalysisDocumentContext(bundle.Document, bundle.Root, bundle.SemanticModel, bundle.Targets));
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

        //鎶借薄鍐欐硶
        //璇彞绾у垎鏋愪笉鑳戒娇鐢╯ln椤圭洰绾х殑鍏ㄩ噺鍒嗘瀽
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

        var snapshot = new AnalysisExecutionSnapshot(
            view,
            functionIndex,
            functionFacts,
            BuildStatementFactsIndex(view.Targets));
        var services = BuildAnalysisServices(analyzedDocuments, snapshot);
        return new AnalysisEngineResult(view, analyzedDocuments, snapshot, services, SummarizePerformance(bundles, mergeElapsed));
    }

    /// <summary>
    /// 鍒嗘瀽宸ヤ綔鍖烘ā寮忋€?
    /// </summary>
    private async Task<AnalysisEngineResult> AnalyzeWorkspaceAsync(
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
        var analyzedDocuments = new List<AnalysisDocumentContext>(bundles.Length);
        var allTargets = new List<AnalysisTarget>();
        var allEdges = new List<AnalysisEdge>();
        var typeNodes = new Dictionary<string, TypeNodeRef>(StringComparer.Ordinal);
        var typeEdges = new HashSet<TypeDependencyEdge>();
        var functionNodes = new Dictionary<string, FunctionNodeRef>(StringComparer.Ordinal);

        foreach (var bundle in bundles)
        {
            analyzedDocuments.Add(new AnalysisDocumentContext(bundle.Document, bundle.Root, bundle.SemanticModel, bundle.Targets));
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

        var snapshot = new AnalysisExecutionSnapshot(
            view,
            functionIndex,
            functionFacts,
            BuildStatementFactsIndex(view.Targets));
        var services = BuildAnalysisServices(analyzedDocuments, snapshot);
        return new AnalysisEngineResult(view, analyzedDocuments, snapshot, services, SummarizePerformance(bundles, mergeElapsed));
    }

    /// <summary>
    /// 鍒嗘瀽鍗曚釜鏂囨。骞舵眹鎬讳腑闂翠骇鐗┿€?
    /// </summary>
    /// <param name="sourceDocument">婧愭枃妗ｃ€?/param>
    /// <param name="root">缂栬瘧鍗曞厓璇硶鏍广€?/param>
    /// <param name="semanticModel">璇箟妯″瀷銆?/param>
    /// <param name="cancellationToken">鍙栨秷浠ょ墝銆?/param>
    /// <returns>鏂囨。鍒嗘瀽鎵撳寘缁撴灉銆?/returns>
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
    /// 姹囨€绘枃妗ｅ垎鏋愯€楁椂骞剁敓鎴愭€ц兘鎽樿銆?
    /// </summary>
    /// <param name="bundles">鏂囨。鍒嗘瀽鎵撳寘缁撴灉闆嗗悎銆?/param>
    /// <param name="mergeTime">鍚堝苟闃舵鑰楁椂銆?/param>
    /// <returns>鎬ц兘鎽樿銆?/returns>
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
    /// 鏍规嵁鍒嗘瀽缁撴灉鍒涘缓鍒嗘瀽涓婁笅鏂囥€?
    /// 涓婁笅鏂囬渶瑕佹牴鎹枃浠舵暟閲忎互鍙婁綔鐢ㄨ寖鍥存潵缁存姢涓婁笅鏂?瑕佷箞灏辨槸涓嶅彉涓婁笅鏀惧純缁存姢
    /// 闇€瑕佺‘瀹氫笂涓嬫枃闇€瑕佷紶閫掍粈涔堟暟鎹拰澶у皬
    /// </summary>
    /// <param name="result">Roslyn鍒嗘瀽缁撴灉銆?/param>
    /// <returns>鍒嗘瀽涓婁笅鏂囥€?/returns>
    /// <summary>
    /// 鏍稿績鍒涘缓鍒嗘瀽鏈嶅姟閫昏緫銆?
    /// </summary>
    private static AnalysisServices BuildAnalysisServices(
        IReadOnlyList<AnalysisDocumentContext> documents,
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
                (CompilationUnitSyntax)document.Root,
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
            RegisterPersistentInitializerTypeReferences((CompilationUnitSyntax)document.Root, document.SemanticModel, typeToFunctions, typeToTypes);
        }

        var symbolDependencies = new SymbolDependencyGraphProvider(documents);
        var methodCalls = new MethodCallQueryService(snapshot.FunctionFacts);
        var advancedAnalysis = new AdvancedAnalysisSummaryService(methodCalls, symbolDependencies);
        var inheritance = new InheritanceQueryService(overrideMembers, interfaceMembers, inheritanceTypes);
        var references = new ReferenceQueryService(memberToFunctions, memberToTypes, typeToFunctions, typeToTypes);
        var memberCleanup = new MemberCleanupQueryService(
            documents,
            snapshot.FunctionIndex,
            references,
            inheritance,
            symbolDependencies);

        return new AnalysisServices(
            inheritance,
            references,
            new StatementAnalysisService(snapshot.StatementFacts),
            new FunctionGraphProvider(snapshot.FunctionIndex, snapshot.FunctionFacts),
            symbolDependencies,
            methodCalls,
            new DataFlowSummaryService(documents),
            new SwitchFlowSummaryService(documents),
            new CallChainAnalysisService(snapshot.FunctionIndex, methodCalls),
            advancedAnalysis,
            memberCleanup);
    }

    public AnalysisExecutionSnapshot CreateSnapshot(AnalysisEngineResult result) => result.Snapshot;

    public AnalysisServices CreateServices(AnalysisEngineResult result) => result.Services;

    public AnalysisContext CreateContext(AnalysisEngineResult result) => result.CreateContext();

    /// <summary>
    /// 鍒ゆ柇绫诲瀷渚濊禆杈规槸鍚﹀睘浜庢寔涔呭紩鐢ㄣ€?
    /// </summary>
    /// <param name="kind">绫诲瀷渚濊禆绉嶇被銆?/param>
    /// <returns>鑻ュ睘浜庢寔涔呭紩鐢ㄥ垯涓?<see langword="true"/>銆?/returns>
    private static bool IsPersistentTypeReference(TypeDependencyKind kind) =>
        kind is TypeDependencyKind.Inherits
            or TypeDependencyKind.Implements
            or TypeDependencyKind.FieldType
            or TypeDependencyKind.PropertyType
            or TypeDependencyKind.ParameterType
            or TypeDependencyKind.ReturnType
            or TypeDependencyKind.StaticMemberAccess;

    /// <summary>
    /// 鍒ゆ柇鏄惁涓哄祵濂楃被鍨嬩綋寮曠敤銆?
    /// </summary>
    /// <param name="edge">绫诲瀷渚濊禆杈广€?/param>
    /// <returns>鑻ヤ负宓屽绫诲瀷浣撳紩鐢ㄥ垯涓?<see langword="true"/>銆?/returns>
    private static bool IsNestedTypeBodyReference(TypeDependencyEdge edge) =>
        edge.Kind is TypeDependencyKind.ObjectCreation or TypeDependencyKind.MemberBodyReference &&
        IsNestedTypeReference(edge.SourceTypeId, edge.TargetTypeId);

    /// <summary>
    /// 鍒ゆ柇鐩爣绫诲瀷鏄惁涓烘簮绫诲瀷鐨勫祵濂楃被鍨嬨€?
    /// </summary>
    /// <param name="sourceTypeId">婧愮被鍨嬫爣璇嗐€?/param>
    /// <param name="targetTypeId">鐩爣绫诲瀷鏍囪瘑銆?/param>
    /// <returns>鑻ヤ负宓屽鍏崇郴鍒欎负 <see langword="true"/>銆?/returns>
    private static bool IsNestedTypeReference(string sourceTypeId, string targetTypeId) =>
        targetTypeId.StartsWith(sourceTypeId + ".", StringComparison.Ordinal);

    /// <summary>
    /// 娉ㄥ唽鏂规硶缁勫紩鐢ㄥ埌鎴愬憳鍜岀被鍨嬪弽鍚戠储寮曘€?
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
    /// 娉ㄥ唽琛ㄨ揪寮忎綋灞炴€т腑鐨勬柟娉曠粍寮曠敤銆?
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
    /// 娉ㄥ唽瀛楁涓庡睘鎬у垵濮嬪寲鍣ㄤ腑鐨勬柟娉曞紩鐢ㄣ€?
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
    /// 鏀堕泦鍒濆鍖栧櫒涓殑琚紩鐢ㄦ柟娉曟爣璇嗛泦鍚堛€?
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
    /// 娉ㄥ唽鎸佷箙绫诲瀷寮曠敤鍒扮被鍨嬪弽鍚戠储寮曘€?
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
    /// 娉ㄥ唽鎸佷箙鍖栧瓧娈靛垵濮嬪寲鍣ㄤ腑鐨勭被鍨嬪紩鐢ㄣ€?
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
    /// 鏋勫缓鍑芥暟绱㈠紩銆?
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
    /// 鏋勫缓鍑芥暟浜嬪疄绱㈠紩銆?
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
    /// 涓烘枃妗ｅ垱寤哄嚱鏁颁簨瀹炪€?
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
    /// 娉ㄥ唽寮曠敤鍏崇郴銆?
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
    /// 鏋勫缓璇彞渚濊禆鍥俱€?
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
    /// 鍒嗘瀽鍗曚釜鏂囨。锛屾彁鍙栧垎鏋愮洰鏍囥€?
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

        foreach (var field in syntaxIndex.Fields)
        {
            foreach (var variable in field.Declaration.Variables)
            {
                var memberSymbol = model.GetDeclaredSymbol(variable);
                if (memberSymbol == null)
                {
                    continue;
                }

                targets.Add(CreateFieldTarget(document, field, variable, memberSymbol, symbolCache));
            }
        }

        foreach (var property in syntaxIndex.PropertiesWithInitializer)
        {
            var memberSymbol = model.GetDeclaredSymbol(property);
            if (memberSymbol == null)
            {
                continue;
            }

            targets.Add(CreatePropertyTarget(document, property, memberSymbol, symbolCache));
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
    /// 鍒涘缓绫荤洰鏍囥€?
    /// </summary>
    private static AnalysisTarget CreateFieldTarget(
        SourceDocument document,
        FieldDeclarationSyntax declaration,
        VariableDeclaratorSyntax variable,
        ISymbol fieldSymbol,
        DocumentSymbolCache symbolCache)
    {
        var memberId = symbolCache.GetMemberId(fieldSymbol);
        return new AnalysisTarget(
            new PlanTarget(
                document.RelativePath,
                memberId,
                MemberKind.Field,
                TargetKind.Field,
                variable.SpanStart,
                variable.Span.Length,
                GetDisplayText(document, variable.SpanStart, variable.Span.Length),
                new TargetResolutionKey(variable.SpanStart, variable.Span.Length)),
            fieldSymbol.DeclaredAccessibility != Accessibility.Private || fieldSymbol.IsStatic,
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

    private static AnalysisTarget CreatePropertyTarget(
        SourceDocument document,
        PropertyDeclarationSyntax property,
        ISymbol propertySymbol,
        DocumentSymbolCache symbolCache)
    {
        var memberId = symbolCache.GetMemberId(propertySymbol);
        return new AnalysisTarget(
            new PlanTarget(
                document.RelativePath,
                memberId,
                MemberKind.Property,
                TargetKind.Property,
                property.SpanStart,
                property.Span.Length,
                GetDisplayText(document, property.SpanStart, property.Span.Length),
                new TargetResolutionKey(property.SpanStart, property.Span.Length)),
            propertySymbol.DeclaredAccessibility != Accessibility.Private || IsHighRiskMember(propertySymbol),
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
    /// 瑙ｆ瀽鍓嶄竴涓洰鏍囷紝鐢ㄤ簬寤虹珛鎵ц椤哄簭銆?
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
    /// 娉ㄥ唽绫诲瀷鍥炬枃妗ｃ€?
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
    /// 娉ㄥ唽澹版槑绫诲瀷渚濊禆杈广€?
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
    /// 鏋氫妇寮曠敤绫诲瀷鍙婂叾宓屽绫诲瀷鍙傛暟銆?
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
    /// 娉ㄥ唽鍑芥暟鑺傜偣銆?
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
    /// 娉ㄥ唽绫诲瀷浣撲腑鐨勪緷璧栬竟銆?
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
    /// 鍒涘缓鏂囨。璇硶绱㈠紩銆?
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
    /// 娉ㄥ唽鍗曚釜鍑芥暟鑺傜偣銆?
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
    /// 娉ㄥ唽鍑芥暟浣撳拰绫诲瀷浣撶殑渚濊禆鍥俱€?
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
    /// 娉ㄥ唽鍑芥暟浣撲緷璧栥€?
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
    /// 娉ㄥ唽鎴愬憳寮曠敤銆?
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
    /// 娉ㄥ唽绫诲瀷寮曠敤杈广€?
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
    /// 娉ㄥ唽绫诲瀷浣撲緷璧栥€?
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
    /// 鑾峰彇鍩虹鏂规硶澹版槑鐨勫嚱鏁颁綋鎴栬〃杈惧紡浣撹妭鐐广€?
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
    /// 鑾峰彇鏋勯€犲嚱鏁扮敤浜庡紩鐢ㄥ垎鏋愮殑浣滅敤鍩熻妭鐐广€?
    /// </summary>
    private static SyntaxNode? GetReferenceScope(ConstructorDeclarationSyntax declaration) =>
        declaration.Initializer != null
            ? declaration
            : GetBodyOrExpression(declaration);

    /// <summary>
    /// 鑾峰彇璁块棶鍣ㄧ殑鍑芥暟浣撴垨琛ㄨ揪寮忎綋鑺傜偣銆?
    /// </summary>
    private static SyntaxNode? GetBodyOrExpression(AccessorDeclarationSyntax accessor) =>
        (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody?.Expression;

    /// <summary>
    /// 鑾峰彇灞炴€ц〃杈惧紡浣撹妭鐐广€?
    /// </summary>
    private static SyntaxNode? GetBodyOrExpression(PropertyDeclarationSyntax property) =>
        property.ExpressionBody?.Expression;

    /// <summary>
    /// 鏀堕泦鑺傜偣涓殑璋冪敤鎴愬憳鏍囪瘑闆嗗悎銆?
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
    /// 鏀堕泦鑺傜偣涓殑鏂规硶寮曠敤鏍囪瘑闆嗗悎銆?
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
    /// 瑙勮寖鍖栧紩鐢ㄦ柟娉曠鍙蜂互渚跨ǔ瀹氬缓妯°€?
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
    /// 鏀堕泦鎸佷箙寮曠敤璇箟涓嬬殑绫诲瀷鏍囪瘑闆嗗悎銆?
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
    /// 鏀堕泦鑺傜偣涓柊寤哄璞＄殑绫诲瀷鏍囪瘑闆嗗悎銆?
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
    /// 鍒ゆ柇绱㈠紩鍣ㄨ闂槸鍚︽潵鑷鐞嗗櫒鎴栬В鏋愬櫒瀵硅薄銆?
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
    /// 鍒ゆ柇琛ㄨ揪寮忔槸鍚︿负闈欐€佸疄渚嬭闂ā寮忋€?
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
    /// 鍒ゆ柇鍚嶇О鏄惁绗﹀悎宸茬煡鎸佷箙鎷ユ湁鑰呯被鍨嬬壒寰併€?
    /// </summary>
    private static bool LooksLikeKnownPersistentOwnerType(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        KnownPersistentOwnerTypeMarkers.Any(marker => name.Contains(marker, StringComparison.Ordinal));

    /// <summary>
    /// 鍒ゆ柇绫诲瀷鏄惁绗﹀悎鎸佷箙鎷ユ湁鑰呯壒寰併€?
    /// </summary>
    private static bool LooksLikePersistentOwner(INamedTypeSymbol typeSymbol) =>
        LooksLikeKnownPersistentOwnerType(typeSymbol.Name);

    /// <summary>
    /// 鍒ゆ柇瀛楁鏄惁绗﹀悎宸茬煡鎸佷箙瀛楁鐗瑰緛銆?
    /// </summary>
    private static bool LooksLikeKnownPersistentField(IFieldSymbol fieldSymbol) =>
        fieldSymbol.IsStatic &&
        (string.Equals(fieldSymbol.Name, "Rules", StringComparison.Ordinal) ||
         string.Equals(fieldSymbol.Name, "ChainedRules", StringComparison.Ordinal)) &&
        fieldSymbol.Type is INamedTypeSymbol namedType &&
        namedType.IsGenericType;

    /// <summary>
    /// 鍒ゆ柇绫诲瀷鏄惁涓鸿鍒欒妭鐐圭被鍨嬨€?
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
    /// 鏋氫妇璇彞鍧椾腑鐨勮鍙ャ€?
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
    /// 鍒涘缓鍒濆鍖栧櫒鐩爣銆?
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
    /// 鍒涘缓璇彞鐩爣銆?
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
    /// 鑾峰彇鐩爣鐗囨鏄剧ず鏂囨湰銆?
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
    /// 璁＄畻璇彞鐨勬渶灏忎綔鐢ㄥ煙涓庣埗鍧楃┛閫忎俊鎭€?
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
    /// 涓烘棤鍧楄鍙ュ垱寤哄洖閫€浣滅敤鍩熸爣璇嗐€?
    /// </summary>
    private static string CreateFallbackScopeId(StatementSyntax statement) =>
        $"fallback|{statement.SyntaxTree.FilePath}|{statement.SpanStart}|{statement.Span.Length}";

    /// <summary>
    /// 鍒涘缓鍧椾綔鐢ㄥ煙鏍囪瘑銆?
    /// </summary>
    private static string CreateScopeId(BlockSyntax block) =>
        $"block|{block.SyntaxTree.FilePath}|{block.SpanStart}|{block.Span.Length}";

    /// <summary>
    /// 鑾峰彇褰撳墠鍧楁墍灞炵殑鏈€杩戠埗鍧椼€?
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
    /// 鑾峰彇瀹氫箟鐨勭鍙枫€?
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
    /// 璁板綍鍛戒腑鐨勮〃杈惧紡璇硶绉嶇被銆?
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
    /// 灏濊瘯鎻愬彇璧嬪€兼垨鍒濆鍖栧彸渚ц〃杈惧紡銆?
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
    /// 灏濊瘯鎻愬彇鑺傜偣涓殑鐩存帴璋冪敤琛ㄨ揪寮忋€?
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
    /// 鑾峰彇鑺傜偣瀹氫箟鐨勭鍙烽泦鍚堛€?
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
    /// 鑾峰彇浣跨敤鐨勭鍙枫€?
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
    /// 鍒嗙被璇彞绫诲瀷銆?
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
    /// 妫€鏌ユ槸鍚︿负瀵硅薄鍒濆鍖栬祴鍊笺€?
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
    /// 妫€鏌ユ槸鍚﹀簲璺熻釜鏁版嵁娴佺鍙枫€?
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
    /// 娣诲姞鍒嗘瀽鐩爣鐨勮竟銆?
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
    /// 妫€鏌ユ爣璇嗙鏄惁涓哄啓鍏ョ洰鏍囥€?
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
    /// 妫€鏌ユ垚鍛樻槸鍚︿负楂橀闄╂垚鍛橈紙濡傝櫄鏂规硶銆佹娊璞℃柟娉曟垨鎺ュ彛瀹炵幇锛夈€?
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
    /// 妫€鏌ユ柟娉曟槸鍚﹀尮閰嶅疄鐜般€?
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


