namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;

/// <summary>
/// Hybrid 入口输入状态键定义。
/// </summary>
public static class HybridInputStateKeys
{
    public const string MarkedNodes = "hybrid.input.marked_nodes";
    public const string GlobalMethodActions = "hybrid.input.global_method_actions";
    public const string NamePattern = "hybrid.input.name_pattern";
    public const string DeleteMatched = "hybrid.input.delete_matched";
    public const string ClearBodyMatched = "hybrid.input.clear_body_matched";
    public const string Solution = "hybrid.input.solution";
}
