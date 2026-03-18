namespace TerrariaTools.Dome.Rules;

using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using ModelRules = TerrariaTools.Dome.Model.Rules;

public interface ISeedRule
{
    IEnumerable<ModelRules.MarkDecision> Evaluate(ModelAnalysis.AnalysisTarget target);
}

public interface IPropagationRule
{
    bool CanPropagate(ModelAnalysis.AnalysisTarget target, ModelAnalysis.SymbolRef usedSymbol, ModelRules.MarkDecision sourceDecision);
}

public interface IProtectionRule
{
    bool Blocks(ModelAnalysis.AnalysisTarget target);
}

public interface IExpressionProjectionRule
{
    IEnumerable<ModelRules.MarkDecision> Evaluate(ModelAnalysis.AnalysisTarget target);
}

public interface IMethodRule
{
    IEnumerable<ModelRules.MarkDecision> Evaluate(ModelAnalysis.AnalysisContext context, ModelAnalysis.FunctionNodeRef functionNode);
}

public interface IMemberTargetRule
{
    IEnumerable<ModelRules.MarkDecision> Evaluate(ModelAnalysis.AnalysisContext context, ModelAnalysis.AnalysisTarget target);
}

public interface IClassRule
{
    IEnumerable<ModelRules.MarkDecision> Evaluate(ModelAnalysis.AnalysisContext context, ModelAnalysis.AnalysisTarget target);
}

public interface IBoundaryPromotionRule
{
    IEnumerable<ModelRules.MarkDecision> Evaluate(ModelAnalysis.AnalysisContext context, ModelAnalysis.AnalysisTarget target, ModelRules.MarkDecision decision);
}

public interface IStatementScopeRule
{
    ModelPrimitives.StatementScopeMode SelectScopeMode(ModelAnalysis.AnalysisContext context, ModelAnalysis.AnalysisTarget seedTarget);
}
