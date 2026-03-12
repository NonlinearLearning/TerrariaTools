namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;

/// <summary>
/// Analysis 阶段状态键定义，避免魔术字符串。
/// </summary>
public static class AnalysisStateKeys
{
    public const string ScopeRoot = "analysis.scope.root";
    public const string ScopeMap = "analysis.scope.map";
    public const string DefUseGraph = "analysis.defuse.graph";
    public const string DefinitionMap = "analysis.defuse.definition_map";
    public const string UnusedDefinitions = "analysis.defuse.unused_definitions";
    public const string MruPlanItems = "analysis.mru.plan_items";
}
