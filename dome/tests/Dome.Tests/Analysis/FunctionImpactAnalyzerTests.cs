using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;
using Xunit;

namespace TerrariaTools.Dome.Tests.Analysis;

/// <summary>
/// 函数影响分析器测试。
/// </summary>
public class FunctionImpactAnalyzerTests
{
    /// <summary>
    /// 测试 Analyze 方法收集已删除方法的一跳调用邻居。
    /// </summary>
    [Fact]
    public void Analyze_CollectsOneHopCallsNeighborsForDeletedMethod()
    {
        var analyzer = new FunctionImpactAnalyzer();
        var deleted = new PlanTarget("Sample.cs", new MemberId("Sample.Player.Middle()"), MemberKind.Method, TargetKind.Method, 0, 10, "Middle");
        var plan = new AuditPlan(
            new PlanMetadata("dome", "1", "in", "out", RunMode.PlanOnly),
            new[]
            {
                new PlannedChange(0, deleted, new PlanAction(PlanActionKind.Delete), new PlanReason("function-mark", "delete"))
            },
            Array.Empty<PlanConflict>());
        var graph = new FunctionDependencyGraph(
            new[]
            {
                new FunctionNodeRef(new MemberId("Sample.Player.Caller()"), MemberKind.Method, "Sample.Player", "Caller", "Sample.cs", 0, 1, true, true, true, true, "void"),
                new FunctionNodeRef(new MemberId("Sample.Player.Middle()"), MemberKind.Method, "Sample.Player", "Middle", "Sample.cs", 0, 1, true, true, true, true, "void"),
                new FunctionNodeRef(new MemberId("Sample.Player.Callee()"), MemberKind.Method, "Sample.Player", "Callee", "Sample.cs", 0, 1, true, true, true, true, "void")
            },
            new[]
            {
                new FunctionDependencyEdge(new MemberId("Sample.Player.Caller()"), new MemberId("Sample.Player.Middle()"), FunctionDependencyKind.Calls),
                new FunctionDependencyEdge(new MemberId("Sample.Player.Middle()"), new MemberId("Sample.Player.Callee()"), FunctionDependencyKind.Calls)
            });
        var snapshot = new FunctionGraphSnapshot(
            FunctionGraphScope.ExpandedMembers,
            new[] { new MemberId("Sample.Player.Middle()") },
            new[] { "Sample.cs" },
            graph);

        var impact = analyzer.Analyze(plan, snapshot);

        Assert.Equal(new[] { "Sample.Player.Middle()" }, impact.DeletedFunctionIds);
        Assert.Equal(2, impact.AffectedFunctionIds.Count);
        Assert.Contains("Sample.Player.Caller()", impact.AffectedFunctionIds);
        Assert.Contains("Sample.Player.Callee()", impact.AffectedFunctionIds);
        Assert.Equal(new[] { "Sample.cs" }, impact.AffectedDocumentPaths);
        Assert.Equal(new[] { FunctionDependencyKind.Calls }, impact.EdgeKinds);
    }

    /// <summary>
    /// 测试 Analyze 方法在第一阶段忽略非调用边。
    /// </summary>
    [Fact]
    public void Analyze_IgnoresNonCallEdgesInFirstStage()
    {
        var analyzer = new FunctionImpactAnalyzer();
        var deleted = new PlanTarget("Sample.cs", new MemberId("Sample.Player.Touch()"), MemberKind.Method, TargetKind.Method, 0, 10, "Touch");
        var plan = new AuditPlan(
            new PlanMetadata("dome", "1", "in", "out", RunMode.PlanOnly),
            new[]
            {
                new PlannedChange(0, deleted, new PlanAction(PlanActionKind.Delete), new PlanReason("function-mark", "delete"))
            },
            Array.Empty<PlanConflict>());
        var graph = new FunctionDependencyGraph(
            new[]
            {
                new FunctionNodeRef(new MemberId("Sample.Player.Update()"), MemberKind.Method, "Sample.Player", "Update", "Sample.cs", 0, 1, true, true, true, true, "void"),
                new FunctionNodeRef(new MemberId("Sample.Player.Touch()"), MemberKind.Method, "Sample.Player", "Touch", "Sample.cs", 0, 1, true, true, true, true, "void"),
                new FunctionNodeRef(new MemberId("Sample.Player.Value.get"), MemberKind.Accessor, "Sample.Player", "get_Value", "Sample.cs", 0, 1, false, false, true, true, "int")
            },
            new[]
            {
                new FunctionDependencyEdge(new MemberId("Sample.Player.Update()"), new MemberId("Sample.Player.Touch()"), FunctionDependencyKind.ReadsMember),
                new FunctionDependencyEdge(new MemberId("Sample.Player.Touch()"), new MemberId("Sample.Player.Value.get"), FunctionDependencyKind.UsesPropertyAccessor)
            });
        var snapshot = new FunctionGraphSnapshot(
            FunctionGraphScope.ExpandedMembers,
            new[] { new MemberId("Sample.Player.Touch()") },
            new[] { "Sample.cs" },
            graph);

        var impact = analyzer.Analyze(plan, snapshot);

        Assert.Empty(impact.AffectedFunctionIds);
        Assert.Empty(impact.AffectedDocumentPaths);
    }

    [Fact]
    public void Analyze_AcceptsServicesAndExplicitRequest()
    {
        var analyzer = new FunctionImpactAnalyzer();
        var deleted = new PlanTarget("Sample.cs", new MemberId("Sample.Player.Middle()"), MemberKind.Method, TargetKind.Method, 0, 10, "Middle");
        var plan = new AuditPlan(
            new PlanMetadata("dome", "1", "in", "out", RunMode.PlanOnly),
            new[]
            {
                new PlannedChange(0, deleted, new PlanAction(PlanActionKind.Delete), new PlanReason("function-mark", "delete"))
            },
            Array.Empty<PlanConflict>());
        var services = new AnalysisServices(
            new StubInheritanceQueryService(),
            new StubReferenceQueryService(),
            new StubStatementAnalysisService(),
            new StubFunctionGraphProvider(
                new FunctionGraphSnapshot(
                    FunctionGraphScope.ExpandedMembers,
                    new[] { new MemberId("Sample.Player.Middle()") },
                    new[] { "Sample.cs" },
                    new FunctionDependencyGraph(
                        new[]
                        {
                            new FunctionNodeRef(new MemberId("Sample.Player.Caller()"), MemberKind.Method, "Sample.Player", "Caller", "Sample.cs", 0, 1, true, true, true, true, "void"),
                            new FunctionNodeRef(new MemberId("Sample.Player.Middle()"), MemberKind.Method, "Sample.Player", "Middle", "Sample.cs", 0, 1, true, true, true, true, "void"),
                            new FunctionNodeRef(new MemberId("Sample.Player.Callee()"), MemberKind.Method, "Sample.Player", "Callee", "Sample.cs", 0, 1, true, true, true, true, "void")
                        },
                        new[]
                        {
                            new FunctionDependencyEdge(new MemberId("Sample.Player.Caller()"), new MemberId("Sample.Player.Middle()"), FunctionDependencyKind.Calls),
                            new FunctionDependencyEdge(new MemberId("Sample.Player.Middle()"), new MemberId("Sample.Player.Callee()"), FunctionDependencyKind.Calls)
                        }))));
        var impact = analyzer.Analyze(
            plan,
            services,
            FunctionGraphRequests.ExpandedMembersCalls(
                new[] { new MemberId("Sample.Player.Middle()") },
                "FunctionImpactAnalyzerTests",
                "verify explicit request"));

        Assert.Equal(new[] { "Sample.Player.Middle()" }, impact.DeletedFunctionIds);
        Assert.Contains("Sample.Player.Caller()", impact.AffectedFunctionIds);
        Assert.Contains("Sample.Player.Callee()", impact.AffectedFunctionIds);
    }

    private sealed class StubInheritanceQueryService : IInheritanceQueryService
    {
        public bool ImplementsInterfaceMember(string memberId) => false;
        public bool IsInInheritanceChain(string typeId) => false;
        public bool IsOverrideMember(string memberId) => false;
    }

    private sealed class StubReferenceQueryService : IReferenceQueryService
    {
        public IReadOnlyList<MemberId> GetReferencingFunctions(string symbolOrMemberId) => Array.Empty<MemberId>();
        public IReadOnlyList<string> GetReferencingTypes(string symbolOrMemberId) => Array.Empty<string>();
        public bool HasReferences(string symbolOrMemberId) => false;
    }

    private sealed class StubStatementAnalysisService : IStatementAnalysisService
    {
        public StatementGraphSnapshot Analyze(PlanTarget seedTarget, StatementScopeMode scopeMode) =>
            new(seedTarget.TargetKey, scopeMode, seedTarget.MemberId, Array.Empty<string>(), Array.Empty<StatementDependencyEdge>());
    }

    private sealed class StubFunctionGraphProvider(FunctionGraphSnapshot snapshot) : IFunctionGraphProvider
    {
        public FunctionGraphSnapshot GetSnapshot(FunctionGraphRequest request) => snapshot;
    }
}
