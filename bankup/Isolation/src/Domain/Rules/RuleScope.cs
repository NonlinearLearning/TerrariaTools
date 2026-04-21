namespace Domain.Rules;

/// <summary>
/// 表示规则作用边界。
/// </summary>
public sealed class RuleScope : Domain.Common.ISharedKernelType
{
    private readonly HashSet<RuleTargetKind> targetKinds = new();
    private readonly HashSet<RuleStageScope> stageScopes = new();

    /// <summary>
    /// 初始化规则作用边界。
    /// </summary>
    /// <param name="targetKinds">目标类型集合。</param>
    /// <param name="stageScopes">阶段范围集合。</param>
    /// <param name="boundary">边界范围。</param>
    /// <param name="propagationAllowance">传播许可。</param>
    public RuleScope(
        IEnumerable<RuleTargetKind> targetKinds,
        IEnumerable<RuleStageScope> stageScopes,
        RuleBoundary boundary,
        RulePropagationAllowance propagationAllowance)
    {
        ArgumentNullException.ThrowIfNull(targetKinds);
        ArgumentNullException.ThrowIfNull(stageScopes);

        this.targetKinds = targetKinds.ToHashSet();
        this.stageScopes = stageScopes.ToHashSet();

        if (this.targetKinds.Count == 0)
        {
            throw new ArgumentException("规则作用目标不能为空。", nameof(targetKinds));
        }

        if (this.stageScopes.Count == 0)
        {
            throw new ArgumentException("规则阶段范围不能为空。", nameof(stageScopes));
        }

        Boundary = boundary;
        PropagationAllowance = propagationAllowance;
    }

    /// <summary>
    /// 获取目标类型集合。
    /// </summary>
    public IReadOnlyCollection<RuleTargetKind> TargetKinds => targetKinds;

    /// <summary>
    /// 获取阶段范围集合。
    /// </summary>
    public IReadOnlyCollection<RuleStageScope> StageScopes => stageScopes;

    /// <summary>
    /// 获取边界范围。
    /// </summary>
    public RuleBoundary Boundary { get; }

    /// <summary>
    /// 获取传播许可。
    /// </summary>
    public RulePropagationAllowance PropagationAllowance { get; }

    /// <summary>
    /// 判断是否可作用于目标种类。
    /// </summary>
    public bool CanTarget(RuleTargetKind targetKind)
    {
        return targetKinds.Contains(targetKind);
    }

    /// <summary>
    /// 判断是否可在指定阶段执行。
    /// </summary>
    public bool CanRunAt(RuleStageScope stageScope)
    {
        return stageScopes.Contains(stageScope);
    }
}

/// <summary>
/// 表示规则目标种类。
/// </summary>
public enum RuleTargetKind
{
    Unknown = 0,
    Class = 1,
    Method = 2,
    Member = 3,
    Statement = 4,
    Closure = 5,
    ShadowType = 6,
}

/// <summary>
/// 表示规则阶段范围。
/// </summary>
public enum RuleStageScope
{
    Unknown = 0,
    Marking = 1,
    Propagation = 2,
    Decision = 3,
    Planning = 4,
    Evidence = 5,
}

/// <summary>
/// 表示规则边界。
/// </summary>
public enum RuleBoundary
{
    Unknown = 0,
    CurrentMember = 1,
    CurrentType = 2,
    CurrentDocument = 3,
    CurrentProject = 4,
    CurrentWorkspace = 5,
}

/// <summary>
/// 表示规则传播许可。
/// </summary>
public enum RulePropagationAllowance
{
    None = 0,
    SameTypeOnly = 1,
    CallPropagation = 2,
    DependencyPropagation = 3,
    ClosureExpansion = 4,
}
