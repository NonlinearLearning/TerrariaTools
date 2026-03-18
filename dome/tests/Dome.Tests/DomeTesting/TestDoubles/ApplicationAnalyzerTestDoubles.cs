using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;
using ModelRules = TerrariaTools.Dome.Model.Rules;

namespace TerrariaTools.Dome.Tests.Testing.TestDoubles;

public sealed class FakeAnalysisEngine : ApplicationAbstractions.IAnalysisEngine
{
    private readonly ApplicationAbstractions.AnalysisEngineResult _result;

    public FakeAnalysisEngine(ApplicationAbstractions.AnalysisEngineResult result)
    {
        _result = result;
    }

    public Task<ApplicationAbstractions.AnalysisEngineResult> AnalyzeAsync(
        ApplicationAbstractions.SourceDocumentSet sourceSet,
        CancellationToken cancellationToken) =>
        Task.FromResult(_result);
}

public sealed class FakeFunctionImpactAnalyzer : ApplicationAbstractions.IFunctionImpactAnalyzer
{
    private readonly ModelPlanning.FunctionImpactSet _result;

    public FakeFunctionImpactAnalyzer(ModelPlanning.FunctionImpactSet? result = null)
    {
        _result = result ?? new ModelPlanning.FunctionImpactSet([], [], [], 0, []);
    }

    public List<ModelPlanning.AuditPlan> ObservedPlans { get; } = [];

    ModelPlanning.FunctionImpactSet ApplicationAbstractions.IFunctionImpactAnalyzer.Analyze(
        ModelPlanning.AuditPlan plan,
        ModelAnalysis.AnalysisServices services,
        ModelAnalysis.FunctionGraphRequest request) =>
        ((ApplicationAbstractions.IFunctionImpactAnalyzer)this).Analyze(plan, services.FunctionGraphs.GetSnapshot(request));

    ModelPlanning.FunctionImpactSet ApplicationAbstractions.IFunctionImpactAnalyzer.Analyze(
        ModelPlanning.AuditPlan plan,
        ModelAnalysis.FunctionGraphSnapshot snapshot)
    {
        ObservedPlans.Add(plan);
        return _result;
    }
}

public sealed class FakeReferenceZeroPredictionAnalyzer : ApplicationAbstractions.IReferenceZeroPredictionAnalyzer
{
    private readonly IReadOnlyList<ModelRules.MarkDecision> _predictedDecisions;

    public FakeReferenceZeroPredictionAnalyzer(params ModelRules.MarkDecision[] predictedDecisions)
    {
        _predictedDecisions = predictedDecisions;
    }

    public List<int> ObservedInitialDecisionCounts { get; } = [];

    IReadOnlyList<ModelRules.MarkDecision> ApplicationAbstractions.IReferenceZeroPredictionAnalyzer.Predict(
        ModelAnalysis.AnalysisExecutionSnapshot snapshot,
        ModelAnalysis.AnalysisServices services,
        ModelRules.RuleExecutionContext executionContext,
        IReadOnlyList<ModelRules.MarkDecision> decisions)
    {
        ObservedInitialDecisionCounts.Add(decisions.Count);
        return _predictedDecisions;
    }

    IReadOnlyList<ModelRules.MarkDecision> ApplicationAbstractions.IReferenceZeroPredictionAnalyzer.Predict(
        ModelAnalysis.AnalysisContext context,
        IReadOnlyList<ModelRules.MarkDecision> decisions)
    {
        ObservedInitialDecisionCounts.Add(decisions.Count);
        return _predictedDecisions;
    }
}
