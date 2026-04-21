using Domain.Common;

namespace Domain.Rules;

/// <summary>
/// 表示规则集合聚合根。
/// </summary>
public sealed class RuleSet : AggregateRoot<Guid>, Domain.Common.ISharedKernelType
{
    private readonly List<EnabledRule> enabledRules = new();
    private readonly HashSet<string> tags = new(StringComparer.OrdinalIgnoreCase);

    private RuleSet(
        Guid id,
        string name,
        string? description,
        RuleExecutionPolicy defaultExecutionPolicy,
        string version,
        IEnumerable<string>? tags)
        : base(id)
    {
        Name = name;
        Description = description;
        DefaultExecutionPolicy = defaultExecutionPolicy;
        Version = version;

        if (tags is not null)
        {
            foreach (string current in tags.Where(static current => !string.IsNullOrWhiteSpace(current)))
            {
                this.tags.Add(current.Trim());
            }
        }
    }

    /// <summary>
    /// 获取规则集名称。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 获取规则集描述。
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// 获取默认执行策略。
    /// </summary>
    public RuleExecutionPolicy DefaultExecutionPolicy { get; }

    /// <summary>
    /// 获取规则集版本。
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// 获取规则标签集合。
    /// </summary>
    public IReadOnlyCollection<string> Tags => tags;

    /// <summary>
    /// 获取启用规则集合。
    /// </summary>
    public IReadOnlyCollection<EnabledRule> EnabledRules => enabledRules.AsReadOnly();

    /// <summary>
    /// 创建规则集。
    /// </summary>
    public static RuleSet Create(
        string name,
        RuleExecutionPolicy defaultExecutionPolicy,
        string version = "1.0.0",
        string? description = null,
        IEnumerable<string>? tags = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(defaultExecutionPolicy);

        return new RuleSet(
            Guid.NewGuid(),
            name.Trim(),
            description?.Trim(),
            defaultExecutionPolicy,
            version.Trim(),
            tags);
    }

    /// <summary>
    /// 增加启用规则。
    /// </summary>
    public void AddRule(EnabledRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ValidateRuleCompatibility(rule);

        if (ContainsRule(rule.RuleCode))
        {
            throw new InvalidOperationException($"规则 {rule.RuleCode} 已存在于规则集中。");
        }

        enabledRules.Add(rule);
    }

    /// <summary>
    /// 移除启用规则。
    /// </summary>
    public bool RemoveRule(RuleCode ruleCode)
    {
        EnabledRule? existing = enabledRules.SingleOrDefault(current => current.RuleCode.Equals(ruleCode));
        if (existing is null)
        {
            return false;
        }

        if (enabledRules.Count == 1)
        {
            throw new InvalidOperationException("规则集至少需要保留一个启用规则。");
        }

        return enabledRules.Remove(existing);
    }

    /// <summary>
    /// 判断是否包含规则。
    /// </summary>
    public bool ContainsRule(RuleCode ruleCode)
    {
        return enabledRules.Any(current => current.RuleCode.Equals(ruleCode));
    }

    /// <summary>
    /// 获取排序后的规则集合。
    /// </summary>
    public IReadOnlyCollection<EnabledRule> GetOrderedRules()
    {
        return enabledRules
            .OrderByDescending(static current => current.Priority.Value)
            .ThenBy(static current => current.RuleCode.Value, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// 校验规则集。
    /// </summary>
    public void Validate()
    {
        ValidateUniqueRuleCodes();
        ValidatePolicyCompatibility();
    }

    private void ValidateUniqueRuleCodes()
    {
        HashSet<RuleCode> seenCodes = new();
        foreach (EnabledRule current in enabledRules)
        {
            if (!seenCodes.Add(current.RuleCode))
            {
                throw new InvalidOperationException($"规则 {current.RuleCode} 在规则集中重复定义。");
            }
        }
    }

    private void ValidatePolicyCompatibility()
    {
        foreach (EnabledRule current in enabledRules)
        {
            ValidateRuleCompatibility(current);
        }
    }

    private static void ValidateRuleCompatibility(EnabledRule rule)
    {
        if (rule.RuleExecutionPolicy.IsEvidenceOnly() &&
            rule.RuleExecutionPolicy.CanBlockWorkflow())
        {
            throw new InvalidOperationException($"规则 {rule.RuleCode} 不能同时是证据型规则且阻断工作流。");
        }

        if (rule.RuleExecutionPolicy.IsEvidenceOnly() &&
            rule.RuleExecutionPolicy.CanProduceCandidate())
        {
            throw new InvalidOperationException($"规则 {rule.RuleCode} 的执行策略存在矛盾。");
        }
    }
}
