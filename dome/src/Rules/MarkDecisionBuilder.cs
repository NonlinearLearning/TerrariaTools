namespace TerrariaTools.Dome.Rules;

using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelRules = TerrariaTools.Dome.Model.Rules;

public interface IMarkDecisionBuilder
{
    IReadOnlyList<ModelRules.MarkDecision> BuildDecisions(ModelAnalysis.AnalysisContext context, CancellationToken cancellationToken);
}
