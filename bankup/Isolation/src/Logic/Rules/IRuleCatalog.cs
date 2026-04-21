using Domain.Rules;

namespace Logic.Rules;

/// <summary>
/// 提供规则目录查询能力。
/// </summary>
public interface IRuleCatalog
{
    RuleDescriptor Get(RuleCode ruleCode);

    bool TryGet(RuleCode ruleCode, out RuleDescriptor? descriptor);

    bool Contains(RuleCode ruleCode);
}
