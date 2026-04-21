namespace Domain.Rules;

/// <summary>
/// 表示启用规则实例。
/// </summary>
public sealed class EnabledRule : Domain.Common.ISharedKernelType
{
    private readonly Dictionary<string, string> parameters;
    private readonly HashSet<string> tags;

    /// <summary>
    /// 初始化启用规则实例。
    /// </summary>
    public EnabledRule(
        RuleCode ruleCode,
        string displayName,
        RulePriority priority,
        RuleScope ruleScope,
        RuleExecutionPolicy ruleExecutionPolicy,
        IEnumerable<string>? tags = null,
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
        this.tags = tags?.Where(static current => !string.IsNullOrWhiteSpace(current))
            .Select(static current => current.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        this.parameters = parameters is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 获取规则编码。
    /// </summary>
    public RuleCode RuleCode { get; }

    /// <summary>
    /// 获取显示名称。
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// 获取优先级。
    /// </summary>
    public RulePriority Priority { get; }

    /// <summary>
    /// 获取规则作用边界。
    /// </summary>
    public RuleScope RuleScope { get; }

    /// <summary>
    /// 获取规则执行策略。
    /// </summary>
    public RuleExecutionPolicy RuleExecutionPolicy { get; }

    /// <summary>
    /// 获取标签集合。
    /// </summary>
    public IReadOnlyCollection<string> Tags => tags;

    /// <summary>
    /// 获取参数集合。
    /// </summary>
    public IReadOnlyDictionary<string, string> Parameters => parameters;

    /// <summary>
    /// 判断是否可在指定阶段运行。
    /// </summary>
    public bool CanRunAt(RuleStageScope stageScope)
    {
        return RuleScope.CanRunAt(stageScope);
    }

    /// <summary>
    /// 判断是否可作用于目标种类。
    /// </summary>
    public bool CanTarget(RuleTargetKind targetKind)
    {
        return RuleScope.CanTarget(targetKind);
    }

    /// <summary>
    /// 判断是否可产生候选。
    /// </summary>
    public bool CanProduceCandidate()
    {
        return RuleExecutionPolicy.CanProduceCandidate();
    }

    /// <summary>
    /// 判断是否具备指定标签。
    /// </summary>
    public bool HasTag(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        return tags.Contains(tag.Trim());
    }
}
