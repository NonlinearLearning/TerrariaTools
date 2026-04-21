using Domain.Rules;

namespace Logic.Workspaces;

/// <summary>
/// 定义工作区默认规则构造能力。
/// </summary>
public interface IWorkspaceRuleDefaultsBuilder
{
    /// <summary>
    /// 从边界输入构造默认启用规则。
    /// </summary>
    /// <param name="inputs">规则输入集合。</param>
    /// <returns>启用规则集合。</returns>
    IReadOnlyCollection<EnabledRule> Build(IReadOnlyCollection<WorkspaceEnabledRuleInput> inputs);
}
