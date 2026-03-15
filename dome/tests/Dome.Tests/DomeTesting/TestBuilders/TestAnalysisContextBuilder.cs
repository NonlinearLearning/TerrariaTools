using TerrariaTools.Dome.Core;

namespace TerrariaTools.Dome.Tests.Testing.TestBuilders;

internal sealed class TestAnalysisContextBuilder
{
    private readonly List<AnalysisTarget> _targets = [];
    private readonly List<AnalysisEdge> _edges = [];
    private readonly Dictionary<string, FunctionNodeRef> _functionNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<string>> _functionDocumentIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FunctionFact> _functionFacts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<string>> _functionFactDocumentIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<MemberId>> _incomingCallers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<StatementFact>> _statementFacts = new(StringComparer.Ordinal);
    private readonly Dictionary<(string TargetKey, StatementScopeMode ScopeMode), StatementGraphSnapshot> _statementSnapshots = new();
    private IInheritanceQueryService? _inheritance;
    private IReferenceQueryService? _references;
    private IStatementAnalysisService? _statements;
    private IFunctionGraphProvider? _functionGraphs;
    private ISymbolDependencyGraphProvider? _symbolDependencies;
    private IMethodCallQueryService? _methodCalls;
    private IDataFlowSummaryService? _dataFlow;
    private ISwitchFlowSummaryService? _switchFlows;
    private ICallChainAnalysisService? _callChains;
    private IAdvancedAnalysisSummaryService? _advancedAnalysis;
    private IMemberCleanupQueryService? _memberCleanup;

    public TestAnalysisContextBuilder AddTarget(AnalysisTarget target)
    {
        _targets.Add(target);
        return this;
    }

    public TestAnalysisContextBuilder AddEdge(AnalysisEdge edge)
    {
        _edges.Add(edge);
        return this;
    }

    public TestAnalysisContextBuilder AddFunctionNode(FunctionNodeRef node)
    {
        _functionNodes[node.MemberId.Value] = node;
        if (!_functionDocumentIndex.TryGetValue(node.DocumentPath, out var bucket))
        {
            bucket = [];
            _functionDocumentIndex[node.DocumentPath] = bucket;
        }

        _functionDocumentIndex[node.DocumentPath] = bucket.Concat([node.MemberId.Value]).ToArray();
        return this;
    }

    public TestAnalysisContextBuilder AddFunctionFact(FunctionFact fact)
    {
        _functionFacts[fact.Node.MemberId.Value] = fact;
        if (!_functionFactDocumentIndex.TryGetValue(fact.Node.DocumentPath, out var bucket))
        {
            bucket = [];
        }

        _functionFactDocumentIndex[fact.Node.DocumentPath] = bucket.Concat([fact.Node.MemberId.Value]).ToArray();
        return this;
    }

    public TestAnalysisContextBuilder AddIncomingCaller(MemberId callee, params MemberId[] callers)
    {
        _incomingCallers[callee.Value] = callers;
        return this;
    }

    public TestAnalysisContextBuilder AddStatementFacts(MemberId memberId, params StatementFact[] facts)
    {
        _statementFacts[memberId.Value] = facts;
        return this;
    }

    public TestAnalysisContextBuilder AddStatementSnapshot(PlanTarget seedTarget, StatementScopeMode scopeMode, params string[] nodes)
    {
        _statementSnapshots[(seedTarget.TargetKey, scopeMode)] =
            new StatementGraphSnapshot(seedTarget.TargetKey, scopeMode, seedTarget.MemberId, nodes, []);
        return this;
    }

    public TestAnalysisContextBuilder WithInheritance(IInheritanceQueryService inheritance)
    {
        _inheritance = inheritance;
        return this;
    }

    public TestAnalysisContextBuilder WithReferences(IReferenceQueryService references)
    {
        _references = references;
        return this;
    }

    public TestAnalysisContextBuilder WithStatements(IStatementAnalysisService statements)
    {
        _statements = statements;
        return this;
    }

    public TestAnalysisContextBuilder WithFunctionGraphs(IFunctionGraphProvider functionGraphs)
    {
        _functionGraphs = functionGraphs;
        return this;
    }

    public TestAnalysisContextBuilder WithSymbolDependencies(ISymbolDependencyGraphProvider symbolDependencies)
    {
        _symbolDependencies = symbolDependencies;
        return this;
    }

    public TestAnalysisContextBuilder WithMethodCalls(IMethodCallQueryService methodCalls)
    {
        _methodCalls = methodCalls;
        return this;
    }

    public TestAnalysisContextBuilder WithDataFlow(IDataFlowSummaryService dataFlow)
    {
        _dataFlow = dataFlow;
        return this;
    }

    public TestAnalysisContextBuilder WithSwitchFlows(ISwitchFlowSummaryService switchFlows)
    {
        _switchFlows = switchFlows;
        return this;
    }

    public TestAnalysisContextBuilder WithCallChains(ICallChainAnalysisService callChains)
    {
        _callChains = callChains;
        return this;
    }

    public TestAnalysisContextBuilder WithAdvancedAnalysis(IAdvancedAnalysisSummaryService advancedAnalysis)
    {
        _advancedAnalysis = advancedAnalysis;
        return this;
    }

    public TestAnalysisContextBuilder WithMemberCleanup(IMemberCleanupQueryService memberCleanup)
    {
        _memberCleanup = memberCleanup;
        return this;
    }

    public AnalysisContext BuildContext()
    {
        var snapshot = BuildSnapshot();
        return AnalysisContext.Create(snapshot, BuildServices());
    }

    public AnalysisEngineResult BuildEngineResult(params SourceDocument[] documents)
    {
        var snapshot = BuildSnapshot();
        var services = BuildServices();
        var analysisDocuments = documents.Length == 0
            ? Array.Empty<AnalysisDocumentContext>()
            : documents.Select(document => new AnalysisDocumentContext(document, null!, null!, _targets.Where(target => target.Target.DocumentPath == document.RelativePath).ToArray())).ToArray();
        return new AnalysisEngineResult(
            snapshot.View,
            analysisDocuments,
            snapshot,
            services,
            new AnalysisPerformanceSummary(analysisDocuments.Length, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero));
    }

    private AnalysisExecutionSnapshot BuildSnapshot()
    {
        var view = new AnalysisResultModel(
            _targets.ToArray(),
            _edges.ToArray(),
            new TypeDependencyGraph([], []),
            new FunctionDependencyGraph(_functionNodes.Values.ToArray(), []),
            new StatementDependencyGraph([], []),
            StatementGraphMaterialization.SnapshotOnly,
            FunctionGraphMaterialization.None);
        return new AnalysisExecutionSnapshot(
            view,
            new FunctionIndex(_functionNodes, _functionDocumentIndex),
            new FunctionFactsIndex(_functionFacts, _functionFactDocumentIndex, _incomingCallers),
            new StatementFactsIndex(_statementFacts));
    }

    private AnalysisServices BuildServices()
    {
        return new AnalysisServices(
            _inheritance ?? new StubInheritanceQueryService(),
            _references ?? new StubReferenceQueryService(),
            _statements ?? new StubStatementAnalysisService(_statementSnapshots),
            _functionGraphs ?? new StubFunctionGraphProvider(),
            _symbolDependencies ?? new StubSymbolDependencyGraphProvider(),
            _methodCalls ?? new StubMethodCallQueryService(),
            _dataFlow ?? new StubDataFlowSummaryService(),
            _switchFlows ?? new StubSwitchFlowSummaryService(),
            _callChains ?? new StubCallChainAnalysisService(),
            _advancedAnalysis ?? new StubAdvancedAnalysisSummaryService(),
            _memberCleanup ?? new StubMemberCleanupQueryService());
    }

    private sealed class StubInheritanceQueryService : IInheritanceQueryService
    {
        public bool ImplementsInterfaceMember(string memberId) => false;
        public bool IsInInheritanceChain(string typeId) => false;
        public bool IsOverrideMember(string memberId) => false;
    }

    private sealed class StubReferenceQueryService : IReferenceQueryService
    {
        public IReadOnlyList<MemberId> GetReferencingFunctions(string symbolOrMemberId) => [];
        public IReadOnlyList<string> GetReferencingTypes(string symbolOrMemberId) => [];
        public bool HasReferences(string symbolOrMemberId) => false;
    }

    private sealed class StubStatementAnalysisService(
        IReadOnlyDictionary<(string TargetKey, StatementScopeMode ScopeMode), StatementGraphSnapshot> snapshots) : IStatementAnalysisService
    {
        public StatementGraphSnapshot Analyze(PlanTarget seedTarget, StatementScopeMode scopeMode) =>
            snapshots.TryGetValue((seedTarget.TargetKey, scopeMode), out var snapshot)
                ? snapshot
                : new StatementGraphSnapshot(seedTarget.TargetKey, scopeMode, seedTarget.MemberId, [seedTarget.TargetKey], []);
    }

    private sealed class StubFunctionGraphProvider : IFunctionGraphProvider
    {
        public FunctionGraphSnapshot GetSnapshot(FunctionGraphRequest request) =>
            new(request.Scope, request.RootMemberIds, [], new FunctionDependencyGraph([], []));
    }

    private sealed class StubSymbolDependencyGraphProvider : ISymbolDependencyGraphProvider
    {
        public SymbolDependencyGraph GetBackwardSlice(string symbolId) => new([], []);
        public SymbolDependencyGraph GetBackwardSlice(string symbolId, SymbolDependencyQueryOptions options) => new([], []);
        public SymbolDependencyGraph GetForwardSlice(IReadOnlyList<string> rootSymbolIds) => new([], []);
        public SymbolDependencyGraph GetForwardSlice(IReadOnlyList<string> rootSymbolIds, SymbolDependencyQueryOptions options) => new([], []);
        public SymbolDependencyGraph GetWholeGraph() => new([], []);
    }

    private sealed class StubMethodCallQueryService : IMethodCallQueryService
    {
        public MethodReachabilityExplanation ExplainReachability(MemberId rootMemberId, MemberId targetMemberId) =>
            new(rootMemberId, targetMemberId, false, []);
        public IReadOnlyList<MemberId> GetCallees(MemberId memberId) => [];
        public IReadOnlyList<MemberId> GetCallers(MemberId memberId) => [];
        public IReadOnlyList<MemberId> GetReachableMethods(IReadOnlyList<MemberId> rootMemberIds) => [];
        public IReadOnlyList<MemberId> GetShortestPath(IReadOnlyList<MemberId> rootMemberIds, MemberId targetMemberId) => [];
        public FunctionDependencyGraph GetWholeGraph() => new([], []);
    }

    private sealed class StubDataFlowSummaryService : IDataFlowSummaryService
    {
        public DataFlowSummary Analyze(MemberId memberId) => new(memberId, [], [], []);
    }

    private sealed class StubSwitchFlowSummaryService : ISwitchFlowSummaryService
    {
        public IReadOnlyList<SwitchFlowSummary> Analyze(MemberId memberId) => [];
    }

    private sealed class StubCallChainAnalysisService : ICallChainAnalysisService
    {
        public CallChainAnalysisSummary Analyze(string logText) => new(0, [], [], []);
        public IReadOnlyList<CallChainEntry> Parse(string logText) => [];
    }

    private sealed class StubAdvancedAnalysisSummaryService : IAdvancedAnalysisSummaryService
    {
        public AdvancedAnalysisSummary BuildSummary() => new(0, 0, [], [], 0, 0, [], [], [], [], 0, 0, 0, 0);
    }

    private sealed class StubMemberCleanupQueryService : IMemberCleanupQueryService
    {
        public MemberCleanupSymbolInfo? GetSymbolInfo(string symbolOrMemberId) => null;
        public MemberCleanupTypeInfo? GetTypeInfo(string typeId) => null;
        public IReadOnlyList<MemberId> GetReorderablePublicMethods(string typeId) => [];
        public bool HasAnyReferences(string symbolOrMemberId) => false;
        public bool HasExternalMethodReferences(MemberId memberId) => false;
        public bool HasInternalMethodReferences(MemberId memberId) => false;
    }
}
