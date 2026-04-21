using Logic.Workspaces;

namespace Logic.Rules;

/// <summary>
/// 提供场景规则预设。
/// </summary>
public interface IRulePresetProvider
{
    /// <summary>
    /// 获取工作区默认规则输入。
    /// </summary>
    IReadOnlyCollection<WorkspaceEnabledRuleInput> GetWorkspaceDefaults();
}
