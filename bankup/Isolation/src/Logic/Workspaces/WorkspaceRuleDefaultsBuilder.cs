using Domain.Rules;
using Logic.Rules;

namespace Logic.Workspaces;

/// <summary>
/// 工作区默认规则构造器。
/// </summary>
public sealed class WorkspaceRuleDefaultsBuilder : IWorkspaceRuleDefaultsBuilder
{
    private readonly IEnabledRuleFactory enabledRuleFactory;

    public WorkspaceRuleDefaultsBuilder(IEnabledRuleFactory enabledRuleFactory)
    {
        this.enabledRuleFactory = enabledRuleFactory;
    }

    public IReadOnlyCollection<EnabledRule> Build(IReadOnlyCollection<WorkspaceEnabledRuleInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        return inputs.Select(enabledRuleFactory.Create).ToArray();
    }
}
