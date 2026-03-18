using TerrariaTools.Dome.Model.Planning;
using TerrariaTools.Dome.Model.Primitives;
using TerrariaTools.Dome.Model.Rules;

namespace TerrariaTools.Dome.Model.Analysis;

public enum AnalysisEdgeKind
{
    Defines,
    Uses,
    Precedes
}

public enum SymbolKindRef
{
    Unknown,
    Local,
    Parameter,
    Field,
    Property
}

public enum TypeDependencyKind
{
    Inherits,
    Implements,
    FieldType,
    PropertyType,
    ParameterType,
    ReturnType,
    ObjectCreation,
    StaticMemberAccess,
    MemberBodyReference
}

public enum StatementDependencyKind
{
    Defines,
    Uses,
    Precedes
}

public sealed record SymbolRef(
    string SymbolKey,
    string DisplayName,
    SymbolKindRef SymbolKind,
    MemberId DeclaringMemberId,
    int DeclarationSpanStart,
    int DeclarationSpanLength);

public sealed record AnalysisEdge(
    string SourceTargetKey,
    string TargetTargetKey,
    AnalysisEdgeKind Kind,
    string? SymbolKey = null);

public sealed record AnalysisTarget(
    TargetIdentity Target,
    TargetLocator Locator,
    bool IsHighRisk,
    IReadOnlyList<DirectiveAction> Directives,
    IReadOnlyList<SymbolRef> DefinesSymbols,
    IReadOnlyList<SymbolRef> UsesSymbols,
    IReadOnlyList<MemberId> InvokedMemberIds,
    StatementKindRef StatementKind,
    bool IsSanitizingAssignment,
    bool IsObjectInitializerAssignment,
    bool HasMarkedExpressionSeed,
    IReadOnlyList<string> MarkedExpressionKinds,
    StatementScopeMode ScopeMode,
    string? ScopeId,
    string? ParentScopeId);

public sealed record TypeNodeRef(string TypeId, string DisplayName, string DocumentPath);
public sealed record TypeDependencyEdge(string SourceTypeId, string TargetTypeId, TypeDependencyKind Kind, string? MemberId = null, string? SymbolKey = null);
public sealed record TypeDependencyGraph(IReadOnlyList<TypeNodeRef> Nodes, IReadOnlyList<TypeDependencyEdge> Edges);
public sealed record FunctionNodeRef(MemberId MemberId, MemberKind MemberKind, string DeclaringTypeId, string DisplayName, string DocumentPath, int SpanStart, int SpanLength, bool IsPrivate, bool ReturnsVoid, bool HasBody, bool HasStatements, string ReturnTypeDisplay);
public sealed record FunctionDependencyEdge(MemberId SourceMemberId, MemberId TargetMemberId, FunctionDependencyKind Kind, string? SymbolKey = null);
public sealed record FunctionDependencyGraph(IReadOnlyList<FunctionNodeRef> Nodes, IReadOnlyList<FunctionDependencyEdge> Edges);
public sealed record StatementDependencyEdge(string SourceTargetKey, string TargetTargetKey, StatementDependencyKind Kind, string? SymbolKey = null);
public sealed record StatementDependencyGraph(IReadOnlyList<string> Nodes, IReadOnlyList<StatementDependencyEdge> Edges);
public sealed record FunctionFact(FunctionNodeRef Node, IReadOnlyList<MemberId> CalledMemberIds);
public sealed record FunctionIndex(IReadOnlyDictionary<string, FunctionNodeRef> NodesByMemberId, IReadOnlyDictionary<string, IReadOnlyList<string>> MemberIdsByDocumentPath)
{
    public static FunctionIndex Empty { get; } = new(new Dictionary<string, FunctionNodeRef>(StringComparer.Ordinal), new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));
}
public sealed record FunctionFactsIndex(IReadOnlyDictionary<string, FunctionFact> FactsByMemberId, IReadOnlyDictionary<string, IReadOnlyList<string>> MemberIdsByDocumentPath, IReadOnlyDictionary<string, IReadOnlyList<MemberId>> IncomingCallersByMemberId)
{
    public static FunctionFactsIndex Empty { get; } = new(new Dictionary<string, FunctionFact>(StringComparer.Ordinal), new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal), new Dictionary<string, IReadOnlyList<MemberId>>(StringComparer.Ordinal));
}
public sealed record StatementFact(
    string TargetKey,
    MemberId MemberId,
    StatementKindRef StatementKind,
    IReadOnlyList<SymbolRef> DefinedSymbols,
    IReadOnlyList<SymbolRef> UsedSymbols,
    IReadOnlyList<MemberId> InvokedMemberIds,
    StatementScopeMode ScopeMode,
    string? ScopeId,
    string? ParentScopeId,
    int SpanStart,
    int SpanLength)
{
    public StatementFact(string targetKey, IReadOnlyList<SymbolRef> definedSymbols, IReadOnlyList<SymbolRef> usedSymbols)
        : this(
            targetKey,
            new MemberId(string.Empty),
            StatementKindRef.Unknown,
            definedSymbols,
            usedSymbols,
            Array.Empty<MemberId>(),
            StatementScopeMode.MinimalBlock,
            null,
            null,
            0,
            0)
    {
    }
}

public sealed record StatementFactsIndex(
    IReadOnlyDictionary<string, StatementFact> FactsByTargetKey,
    IReadOnlyDictionary<string, IReadOnlyList<StatementFact>> FactsByMemberId)
{
    public StatementFactsIndex(IReadOnlyDictionary<string, StatementFact> factsByTargetKey)
        : this(factsByTargetKey, BuildFactsByMemberId(factsByTargetKey))
    {
    }

    public static StatementFactsIndex Empty { get; } = new(
        new Dictionary<string, StatementFact>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlyList<StatementFact>>(StringComparer.Ordinal));

    private static IReadOnlyDictionary<string, IReadOnlyList<StatementFact>> BuildFactsByMemberId(
        IReadOnlyDictionary<string, StatementFact> factsByTargetKey)
    {
        return factsByTargetKey.Values
            .Where(static fact => !string.IsNullOrEmpty(fact.MemberId.Value))
            .GroupBy(static fact => fact.MemberId.Value, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<StatementFact>)group
                    .OrderBy(static fact => fact.SpanStart)
                    .ThenBy(static fact => fact.TargetKey, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
    }
}
public sealed record AnalysisResultModel(IReadOnlyList<AnalysisTarget> Targets, IReadOnlyList<AnalysisEdge> Edges, TypeDependencyGraph TypeGraph, FunctionDependencyGraph FunctionGraph, StatementDependencyGraph StatementGraph, StatementGraphMaterialization StatementGraphMaterialization, FunctionGraphMaterialization FunctionGraphMaterialization);
public sealed record AnalysisExecutionSnapshot(AnalysisResultModel View, FunctionIndex FunctionIndex, FunctionFactsIndex FunctionFacts, StatementFactsIndex StatementFacts);
public sealed record AnalysisPerformanceSummary(int DocumentCount, TimeSpan SyntaxIndexTime, TimeSpan TypeGraphTime, TimeSpan FunctionNodeTime, TimeSpan TypeBodyGraphTime, TimeSpan TargetAnalysisTime, TimeSpan FunctionFactsTime, TimeSpan MergeTime);
public sealed record AnalysisServices(IInheritanceQueryService Inheritance, IReferenceQueryService References, IStatementAnalysisService Statements, IFunctionGraphProvider FunctionGraphs, ISymbolDependencyGraphProvider SymbolDependencies, IMethodCallQueryService MethodCalls, IDataFlowSummaryService DataFlow, ISwitchFlowSummaryService SwitchFlows, ICallChainAnalysisService CallChains, IAdvancedAnalysisSummaryService AdvancedAnalysis, IMemberCleanupQueryService MemberCleanup);
public sealed class AnalysisContext
{
    private AnalysisContext(AnalysisExecutionSnapshot snapshot, AnalysisServices services)
    {
        Snapshot = snapshot;
        Services = services;
    }

    public AnalysisExecutionSnapshot Snapshot { get; }
    public AnalysisServices Services { get; }
    public AnalysisResultModel View => Snapshot.View;
    public FunctionIndex FunctionIndex => Snapshot.FunctionIndex;
    public FunctionFactsIndex FunctionFacts => Snapshot.FunctionFacts;
    public StatementFactsIndex StatementFacts => Snapshot.StatementFacts;
    public IInheritanceQueryService Inheritance => Services.Inheritance;
    public IReferenceQueryService References => Services.References;
    public IStatementAnalysisService Statements => Services.Statements;
    public IFunctionGraphProvider FunctionGraphs => Services.FunctionGraphs;
    public ISymbolDependencyGraphProvider SymbolDependencies => Services.SymbolDependencies;
    public IMethodCallQueryService MethodCalls => Services.MethodCalls;
    public IDataFlowSummaryService DataFlow => Services.DataFlow;
    public ISwitchFlowSummaryService SwitchFlows => Services.SwitchFlows;
    public ICallChainAnalysisService CallChains => Services.CallChains;
    public IAdvancedAnalysisSummaryService AdvancedAnalysis => Services.AdvancedAnalysis;
    public IMemberCleanupQueryService MemberCleanup => Services.MemberCleanup;

    public static AnalysisContext Create(AnalysisExecutionSnapshot snapshot, AnalysisServices services) => new(snapshot, services);
}

public interface IInheritanceQueryService { bool IsOverrideMember(string memberId); bool ImplementsInterfaceMember(string memberId); bool IsInInheritanceChain(string typeId); }
public interface IReferenceQueryService { bool HasReferences(string symbolOrMemberId); IReadOnlyList<MemberId> GetReferencingFunctions(string symbolOrMemberId); IReadOnlyList<string> GetReferencingTypes(string symbolOrMemberId); }
public interface IFunctionGraphProvider { FunctionGraphSnapshot GetSnapshot(FunctionGraphRequest request); }
public interface ISymbolDependencyGraphProvider { SymbolDependencySlice GetForwardSlice(IReadOnlyList<string> rootSymbolIds, SymbolDependencyQueryOptions options); }
public interface IMethodCallQueryService { IReadOnlyList<MemberId> GetReachableMethods(IReadOnlyList<MemberId> rootMemberIds); }
public interface IDataFlowSummaryService { DataFlowSummary Analyze(string targetKey); }
public interface ISwitchFlowSummaryService { SwitchFlowSummary Analyze(string targetKey); }
public interface ICallChainAnalysisService { CallChainAnalysisSummary Analyze(string memberId); }
public interface IAdvancedAnalysisSummaryService { AdvancedAnalysisSummary BuildSummary(); }
public interface IMemberCleanupQueryService
{
    MemberCleanupSymbolInfo? GetSymbolInfo(string symbolId);
    MemberCleanupTypeInfo? GetTypeInfo(string typeId);
    bool HasAnyReferences(string symbolId);
    bool HasInternalMethodReferences(MemberId memberId);
    bool HasExternalMethodReferences(MemberId memberId);
    IReadOnlyList<MemberId> GetReorderablePublicMethods(string typeId);
}
public interface IStatementAnalysisService
{
    StatementGraphSnapshot Analyze(string targetKey);
    StatementGraphSnapshot Analyze(string targetKey, StatementScopeMode scopeMode);
}

public sealed record FunctionGraphSnapshot(FunctionGraphScope Scope, IReadOnlyList<MemberId> RootMemberIds, IReadOnlyList<string> IncludedDocumentPaths, FunctionDependencyGraph Graph);
public enum FunctionGraphScope { WholeProject, ExpandedMembers }
public sealed record FunctionGraphRequest(FunctionGraphScope Scope, IReadOnlyList<MemberId> RootMemberIds, int Depth, IReadOnlyList<FunctionDependencyKind> EdgeKinds, string Requester, string Reason);
public sealed record SymbolDependencyNode(string SymbolId, string DisplayName, SymbolDependencyNodeKind Kind, string? DocumentPath);
public sealed record SymbolDependencyEdge(string SourceSymbolId, string TargetSymbolId, SymbolDependencyEdgeKind Kind);
public sealed record SymbolDependencyGraph(IReadOnlyList<SymbolDependencyNode> Nodes, IReadOnlyList<SymbolDependencyEdge> Edges);
public sealed record SymbolDependencyQueryOptions(IReadOnlyList<SymbolDependencyEdgeKind> AllowedEdgeKinds, IReadOnlyList<SymbolDependencyNodeKind> AllowedNodeKinds);
public sealed record SymbolDependencyPath(IReadOnlyList<string> SymbolIds, IReadOnlyList<SymbolDependencyEdgeKind> EdgeKinds);
public sealed record SymbolDependencySlice(IReadOnlyList<SymbolDependencyNode> Nodes, IReadOnlyList<SymbolDependencyEdge> Edges, IReadOnlyList<SymbolDependencyPath> Paths);
public enum SymbolDependencyNodeKind { Type, Method, Property, Field, Event }
public enum SymbolDependencyEdgeKind { ContainsType, BaseType, InterfaceImplementation, ExplicitInterfaceImplementation, Override, ReturnType, ParameterType, FieldType, PropertyType, EventType, ObjectCreation, Conversion }
public sealed record MethodReachabilityExplanation(IReadOnlyList<MemberId> Path);
public sealed record DataFlowSummary(IReadOnlyList<string> Reads, IReadOnlyList<string> Writes);
public sealed record SwitchCaseSummary(string Label, bool FallsThrough);
public sealed record SwitchFlowSummary(IReadOnlyList<SwitchCaseSummary> Cases);
public sealed record CallChainEntry(string MemberId, string DisplayName);
public sealed record CallChainAnalysisSummary(IReadOnlyList<CallChainEntry> Entries);
public sealed record AdvancedAnalysisSummary(int PersistentTypeCount = 0, int RiskyTypeCount = 0, IReadOnlyList<string>? Notes = null);
public sealed record MemberCleanupSymbolInfo(
    string SymbolId,
    MemberKind MemberKind,
    string DeclaringTypeId,
    string DocumentPath,
    string Name,
    bool IsPublic,
    bool IsPrivate,
    bool IsStatic,
    bool IsAbstract,
    bool IsVirtual,
    bool IsOverride,
    bool IsExtern,
    bool IsOrdinaryMethod,
    bool IsPartialType,
    bool IsNestedType,
    bool IsInInterfaceType,
    bool IsEntryPointLike);

public sealed record MemberCleanupTypeInfo(
    string TypeId,
    string DocumentPath,
    string Name,
    bool IsPublic,
    bool IsAbstract,
    bool IsStatic,
    bool IsPartial,
    bool IsNested,
    bool IsInterface,
    bool IsInInheritanceChain);
public sealed record StatementGraphSnapshot(
    string SeedTargetKey,
    StatementScopeMode ScopeMode,
    MemberId BoundaryMemberId,
    IReadOnlyList<string> Nodes,
    IReadOnlyList<StatementDependencyEdge> Edges)
{
    public StatementGraphSnapshot(IReadOnlyList<string> Nodes, IReadOnlyList<StatementDependencyEdge> Edges)
        : this(string.Empty, StatementScopeMode.MinimalBlock, new MemberId(string.Empty), Nodes, Edges)
    {
    }
}
