using TerrariaTools.Dome.Core;

namespace TerrariaTools.Testing.TestDoubles;

public sealed class FakeReferenceZeroPredictionAnalyzer : IReferenceZeroPredictionAnalyzer
{
    private readonly IReadOnlyList<MarkDecision> _predictedDecisions;

    public FakeReferenceZeroPredictionAnalyzer(params MarkDecision[] predictedDecisions)
    {
        _predictedDecisions = predictedDecisions;
    }

    public List<int> ObservedInitialDecisionCounts { get; } = [];

    public IReadOnlyList<MarkDecision> Predict(
        AnalysisExecutionSnapshot snapshot,
        AnalysisServices services,
        RuleExecutionContext executionContext,
        IReadOnlyList<MarkDecision> decisions)
    {
        ObservedInitialDecisionCounts.Add(decisions.Count);
        return _predictedDecisions;
    }

    public IReadOnlyList<MarkDecision> Predict(AnalysisContext context, IReadOnlyList<MarkDecision> decisions)
    {
        ObservedInitialDecisionCounts.Add(decisions.Count);
        return _predictedDecisions;
    }
}
