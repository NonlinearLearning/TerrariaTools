using TerrariaTools.Dome.Core;

namespace TerrariaTools.Testing.TestDoubles;

public sealed class FakeFunctionImpactAnalyzer : IFunctionImpactAnalyzer
{
    private readonly FunctionImpactSet _result;

    public FakeFunctionImpactAnalyzer(FunctionImpactSet? result = null)
    {
        _result = result ?? new FunctionImpactSet([], [], [], 0, []);
    }

    public List<AuditPlan> ObservedPlans { get; } = [];

    public FunctionImpactSet Analyze(AuditPlan plan, AnalysisServices services, FunctionGraphRequest request)
    {
        ObservedPlans.Add(plan);
        return _result;
    }

    public FunctionImpactSet Analyze(AuditPlan plan, FunctionGraphSnapshot snapshot)
    {
        ObservedPlans.Add(plan);
        return _result;
    }
}
