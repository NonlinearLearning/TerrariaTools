using Logic.Workspaces;

namespace Logic.Rules;

/// <summary>
/// 定义工作区场景的默认规则组合。
/// </summary>
public sealed class WorkspaceDefaultRulePreset : IRulePresetProvider
{
    private static readonly WorkspaceEnabledRuleInput[] WorkspaceDefaults =
    [
        new WorkspaceEnabledRuleInput
        {
            RuleCode = "workflow.rule",
        },
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<WorkspaceEnabledRuleInput> GetWorkspaceDefaults()
    {
        return WorkspaceDefaults
            .Select(static input => new WorkspaceEnabledRuleInput
            {
                RuleCode = input.RuleCode,
                DisplayName = input.DisplayName,
            })
            .ToArray();
    }
}
