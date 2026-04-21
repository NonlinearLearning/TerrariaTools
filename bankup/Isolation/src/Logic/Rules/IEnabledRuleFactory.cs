using Domain.Rules;
using Logic.Workspaces;

namespace Logic.Rules;

/// <summary>
/// 根据输入和目录描述构造启用规则。
/// </summary>
public interface IEnabledRuleFactory
{
    EnabledRule Create(WorkspaceEnabledRuleInput input);

    EnabledRule Create(EnabledRuleActivationInput input);
}
