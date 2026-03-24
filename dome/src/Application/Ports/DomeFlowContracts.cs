using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;
using CorePlanning = TerrariaTools.Dome.Core.Planning;
using CoreRules = TerrariaTools.Dome.Core.Rules.Model;

namespace TerrariaTools.Dome.Application.Ports;

/// <summary>
/// Carries the standard load-slot input for Dome-family flows.
/// </summary>
/// <param name="Request">The application request being processed.</param>
public sealed record LoadInput(RunRequest Request);

/// <summary>
/// Carries the standard load-slot output for Dome-family flows.
/// </summary>
/// <param name="Request">The application request being processed.</param>
/// <param name="Workspace">The loaded workspace result.</param>
public sealed record LoadOutput(RunRequest Request, WorkspaceLoadResult Workspace);

/// <summary>
/// Carries the standard analyze-slot input for Dome-family flows.
/// </summary>
/// <param name="Load">The upstream load-slot output.</param>
public sealed record AnalyzeInput(LoadOutput Load);

/// <summary>
/// Carries the standard analyze-slot output for Dome-family flows.
/// </summary>
/// <param name="Load">The upstream load-slot output.</param>
/// <param name="Analysis">The analysis output produced by the analyze slot.</param>
public sealed record AnalyzeOutput(LoadOutput Load, CoreAnalysis.AnalysisOutput Analysis);

/// <summary>
/// Carries the standard rule-slot input for Dome-family flows.
/// </summary>
/// <param name="Analysis">The upstream analyze-slot output.</param>
public sealed record RuleInput(AnalyzeOutput Analysis);

/// <summary>
/// Carries the standard rule-slot output for Dome-family flows.
/// </summary>
/// <param name="Analysis">The upstream analyze-slot output.</param>
/// <param name="Decisions">The rule-engine decision set.</param>
public sealed record RuleOutput(AnalyzeOutput Analysis, CoreRules.DecisionSet Decisions);

/// <summary>
/// Carries the standard decision-slot input for Dome-family flows.
/// </summary>
/// <param name="Rule">The upstream rule-slot output.</param>
public sealed record DecisionInput(RuleOutput Rule);

/// <summary>
/// Carries the standard decision-slot output for Dome-family flows.
/// </summary>
/// <param name="Rule">The upstream rule-slot output.</param>
/// <param name="Planning">The planning output compiled from rule decisions.</param>
public sealed record DecisionOutput(RuleOutput Rule, CorePlanning.PlanningOutput Planning);

/// <summary>
/// Carries the standard result-slot input for Dome-family flows.
/// </summary>
/// <param name="Load">The upstream load-slot output.</param>
/// <param name="Analysis">The upstream analyze-slot output.</param>
/// <param name="Rule">The optional rule-slot output.</param>
/// <param name="Decision">The optional decision-slot output.</param>
public sealed record ResultInput(
    LoadOutput Load,
    AnalyzeOutput Analysis,
    RuleOutput? Rule,
    DecisionOutput? Decision);

/// <summary>
/// Defines the standard load slot contract for Dome-family flows.
/// </summary>
public interface ILoadSlot : IFlowSlot<LoadInput, LoadOutput>
{
}

/// <summary>
/// Defines the standard analyze slot contract for Dome-family flows.
/// </summary>
public interface IAnalyzeSlot : IFlowSlot<AnalyzeInput, AnalyzeOutput>
{
}

/// <summary>
/// Defines the standard rule slot contract for Dome-family flows.
/// </summary>
public interface IRuleSlot : IFlowSlot<RuleInput, RuleOutput>
{
}

/// <summary>
/// Defines the standard decision slot contract for Dome-family flows.
/// </summary>
public interface IDecisionSlot : IFlowSlot<DecisionInput, DecisionOutput>
{
}

/// <summary>
/// Defines the standard result slot contract for Dome-family flows.
/// </summary>
public interface IResultSlot : IFlowSlot<ResultInput, RunResult>
{
}
