using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;

namespace TerrariaTools.Dome.Tests.Testing.TestBuilders;

/// <summary>
/// Compatibility-only builder for native analysis contexts.
/// </summary>
internal sealed partial class LegacyAnalysisContextBuilder
{
    private readonly List<ModelAnalysis.AnalysisTarget> _targets = [];
    private readonly List<ModelAnalysis.AnalysisEdge> _edges = [];
    private readonly Dictionary<string, ModelAnalysis.FunctionNodeRef> _functionNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<string>> _functionDocumentIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ModelAnalysis.FunctionFact> _functionFacts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<string>> _functionFactDocumentIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<ModelPrimitives.MemberId>> _incomingCallers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<ModelAnalysis.StatementFact>> _statementFacts = new(StringComparer.Ordinal);
    private readonly Dictionary<(string TargetKey, ModelPrimitives.StatementScopeMode ScopeMode), ModelAnalysis.StatementGraphSnapshot> _statementSnapshots = new();
    private ModelAnalysis.IInheritanceQueryService? _inheritance;
    private ModelAnalysis.IReferenceQueryService? _references;
    private ModelAnalysis.IStatementAnalysisService? _statements;
    private ModelAnalysis.IFunctionGraphProvider? _functionGraphs;
    private ModelAnalysis.ISymbolDependencyGraphProvider? _symbolDependencies;
    private ModelAnalysis.IMethodCallQueryService? _methodCalls;
    private ModelAnalysis.IDataFlowSummaryService? _dataFlow;
    private ModelAnalysis.ISwitchFlowSummaryService? _switchFlows;
    private ModelAnalysis.ICallChainAnalysisService? _callChains;
    private ModelAnalysis.IAdvancedAnalysisSummaryService? _advancedAnalysis;
    private ModelAnalysis.IMemberCleanupQueryService? _memberCleanup;

    public LegacyAnalysisContextBuilder AddTarget(ModelAnalysis.AnalysisTarget target)
    {
        _targets.Add(target);
        return this;
    }

    public LegacyAnalysisContextBuilder AddEdge(ModelAnalysis.AnalysisEdge edge)
    {
        _edges.Add(edge);
        return this;
    }

    public LegacyAnalysisContextBuilder AddFunctionNode(ModelAnalysis.FunctionNodeRef node)
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

    public LegacyAnalysisContextBuilder AddFunctionFact(ModelAnalysis.FunctionFact fact)
    {
        _functionFacts[fact.Node.MemberId.Value] = fact;
        if (!_functionFactDocumentIndex.TryGetValue(fact.Node.DocumentPath, out var bucket))
        {
            bucket = [];
        }

        _functionFactDocumentIndex[fact.Node.DocumentPath] = bucket.Concat([fact.Node.MemberId.Value]).ToArray();
        return this;
    }

    public LegacyAnalysisContextBuilder AddIncomingCaller(ModelPrimitives.MemberId callee, params ModelPrimitives.MemberId[] callers)
    {
        _incomingCallers[callee.Value] = callers;
        return this;
    }

    public LegacyAnalysisContextBuilder AddStatementFacts(ModelPrimitives.MemberId memberId, params ModelAnalysis.StatementFact[] facts)
    {
        _statementFacts[memberId.Value] = facts;
        return this;
    }

    public LegacyAnalysisContextBuilder AddStatementSnapshot(ModelAnalysis.AnalysisTarget seedTarget, ModelPrimitives.StatementScopeMode scopeMode, params string[] nodes)
    {
        _statementSnapshots[(seedTarget.Locator.TargetKey, scopeMode)] =
            new ModelAnalysis.StatementGraphSnapshot(seedTarget.Locator.TargetKey, scopeMode, seedTarget.Target.MemberId, nodes, []);
        return this;
    }

    public LegacyAnalysisContextBuilder WithInheritance(ModelAnalysis.IInheritanceQueryService inheritance)
    {
        _inheritance = inheritance;
        return this;
    }

    public LegacyAnalysisContextBuilder WithReferences(ModelAnalysis.IReferenceQueryService references)
    {
        _references = references;
        return this;
    }

    public LegacyAnalysisContextBuilder WithStatements(ModelAnalysis.IStatementAnalysisService statements)
    {
        _statements = statements;
        return this;
    }

    public LegacyAnalysisContextBuilder WithFunctionGraphs(ModelAnalysis.IFunctionGraphProvider functionGraphs)
    {
        _functionGraphs = functionGraphs;
        return this;
    }

    public LegacyAnalysisContextBuilder WithSymbolDependencies(ModelAnalysis.ISymbolDependencyGraphProvider symbolDependencies)
    {
        _symbolDependencies = symbolDependencies;
        return this;
    }

    public LegacyAnalysisContextBuilder WithMethodCalls(ModelAnalysis.IMethodCallQueryService methodCalls)
    {
        _methodCalls = methodCalls;
        return this;
    }

    public LegacyAnalysisContextBuilder WithDataFlow(ModelAnalysis.IDataFlowSummaryService dataFlow)
    {
        _dataFlow = dataFlow;
        return this;
    }

    public LegacyAnalysisContextBuilder WithSwitchFlows(ModelAnalysis.ISwitchFlowSummaryService switchFlows)
    {
        _switchFlows = switchFlows;
        return this;
    }

    public LegacyAnalysisContextBuilder WithCallChains(ModelAnalysis.ICallChainAnalysisService callChains)
    {
        _callChains = callChains;
        return this;
    }

    public LegacyAnalysisContextBuilder WithAdvancedAnalysis(ModelAnalysis.IAdvancedAnalysisSummaryService advancedAnalysis)
    {
        _advancedAnalysis = advancedAnalysis;
        return this;
    }

    public LegacyAnalysisContextBuilder WithMemberCleanup(ModelAnalysis.IMemberCleanupQueryService memberCleanup)
    {
        _memberCleanup = memberCleanup;
        return this;
    }

    public ModelAnalysis.AnalysisContext BuildContext()
    {
        var snapshot = BuildSnapshot();
        return ModelAnalysis.AnalysisContext.Create(snapshot, BuildServices());
    }

    public ApplicationAbstractions.AnalysisEngineResult BuildEngineResult(params ApplicationAbstractions.SourceDocument[] documents)
    {
        var snapshot = BuildSnapshot();
        var services = BuildServices();
        return new ApplicationAbstractions.AnalysisEngineResult(
            snapshot.View,
            snapshot,
            services,
            new ModelAnalysis.AnalysisPerformanceSummary(
                documents.Length,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero));
    }

    private ModelAnalysis.AnalysisExecutionSnapshot BuildSnapshot()
    {
        var view = new ModelAnalysis.AnalysisResultModel(
            _targets.ToArray(),
            _edges.ToArray(),
            new ModelAnalysis.TypeDependencyGraph([], []),
            new ModelAnalysis.FunctionDependencyGraph(_functionNodes.Values.ToArray(), []),
            new ModelAnalysis.StatementDependencyGraph([], []),
            ModelPrimitives.StatementGraphMaterialization.SnapshotOnly,
            ModelPrimitives.FunctionGraphMaterialization.None);

        var statementFacts = _statementFacts
            .SelectMany(static pair => pair.Value)
            .ToDictionary(static fact => fact.TargetKey, StringComparer.Ordinal);

        return new ModelAnalysis.AnalysisExecutionSnapshot(
            view,
            new ModelAnalysis.FunctionIndex(_functionNodes, _functionDocumentIndex),
            new ModelAnalysis.FunctionFactsIndex(_functionFacts, _functionFactDocumentIndex, _incomingCallers),
            new ModelAnalysis.StatementFactsIndex(statementFacts));
    }
}
