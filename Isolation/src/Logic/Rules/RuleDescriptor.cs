using Domain.Rules;

namespace Logic.Rules;

/// <summary>
/// 表示规则目录中的稳定描述对象。
/// </summary>
public sealed class RuleDescriptor
{
    /// <summary>
    /// 初始化规则描述对象。
    /// </summary>
    public RuleDescriptor(
        RuleCode ruleCode,
        string displayName,
        RulePriority priority,
        RuleScope ruleScope,
        RuleExecutionPolicy ruleExecutionPolicy,
        IReadOnlyCollection<string>? tags = null,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(ruleScope);
        ArgumentNullException.ThrowIfNull(ruleExecutionPolicy);

        RuleCode = ruleCode;
        DisplayName = displayName.Trim();
        Priority = priority;
        RuleScope = ruleScope;
        RuleExecutionPolicy = ruleExecutionPolicy;
        Tags = tags ?? Array.Empty<string>();
        Parameters = parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public RuleCode RuleCode { get; }

    public string DisplayName { get; }

    public RulePriority Priority { get; }

    public RuleScope RuleScope { get; }

    public RuleExecutionPolicy RuleExecutionPolicy { get; }

    public IReadOnlyCollection<string> Tags { get; }

    public IReadOnlyDictionary<string, string> Parameters { get; }
}
