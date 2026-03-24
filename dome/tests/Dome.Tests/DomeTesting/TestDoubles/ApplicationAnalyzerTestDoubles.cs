using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelPlanning = TerrariaTools.Dome.Core.Planning;
using ModelRules = TerrariaTools.Dome.Core.Rules.Model;

namespace TerrariaTools.Dome.Tests.Testing.TestDoubles;

public sealed class FakeAnalysisEngine : ApplicationAbstractions.IAnalysisEngine
{
    private readonly ModelAnalysis.AnalysisOutput _result;

    public FakeAnalysisEngine(ModelAnalysis.AnalysisOutput result)
    {
        _result = result;
    }

    public Task<ModelAnalysis.AnalysisOutput> AnalyzeAsync(
        ModelAnalysis.AnalysisInput input,
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
        ModelAnalysis.AnalysisOutput analysis)
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
        ModelAnalysis.AnalysisContext context,
        IReadOnlyList<ModelRules.MarkDecision> decisions)
    {
        ObservedInitialDecisionCounts.Add(decisions.Count);
        return _predictedDecisions;
    }
}



