namespace Domain.Workspaces;

/// <summary>
/// 表示工作流运行模式。
/// </summary>
public enum RunMode
{
    Unknown = 0,
    AnalysisOnly = 1,
    DecisionOnly = 2,
    FullWorkflow = 3,
}
