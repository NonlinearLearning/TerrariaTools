namespace NL.Concurrency;

/// <summary>
/// 标识工作项在竞争同一内核预算时的服务类别。
/// </summary>
public enum WorkPriority
{
    /// <summary>
    /// 优先保证较低延迟，用于规则 DAG 等有依赖的短任务。
    /// </summary>
    LatencySensitive,

    /// <summary>
    /// 优先保证整体吞吐，用于 CPG 分片等无依赖的 CPU 批。
    /// </summary>
    Throughput,
}

/// <summary>
/// 内核识别的提交类别；与 YAML execution 字段一一对应。
/// </summary>
public static class WorkCategories
{
    public const string Default = "default";

    /// <summary>
    /// 不是提交类别，而是由分片计划器查询的「窗口内同时存活的文件数」上限。
    /// 它不出现在任何 <see cref="WorkSubmission{TResult}.Category"/> 上。
    /// </summary>
    public const string Directory = "directory";

    public const string Cpg = "cpg";

    public const string RuleGroup = "rule-group";

    public const string Helper = "helper";

    public const string Replay = "replay";
}
