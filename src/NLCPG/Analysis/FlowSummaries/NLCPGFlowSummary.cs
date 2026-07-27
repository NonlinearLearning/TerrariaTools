namespace NLCPG.Analysis.FlowSummaries;

/// <summary>
/// 表示方法流摘要中参与数据流的端点类别。
/// </summary>
public enum NLCPGFlowSummaryEndpointKind
{
    /// <summary>实例方法的接收者。</summary>
    Receiver,

    /// <summary>由参数序号标识的形式参数。</summary>
    Parameter,

    /// <summary>方法返回值。</summary>
    Return,

    /// <summary>输入直接传递到输出，未指定具体的参数或返回端点。</summary>
    PassThrough,

    /// <summary>数据流在此方法边界被阻断。</summary>
    Block,
}

/// <summary>
/// 描述流摘要的一个源端点或目标端点。仅 <see cref="NLCPGFlowSummaryEndpointKind.Parameter"/>
/// 使用 <paramref name="ParameterOrdinal"/>；返回端点固定使用 <c>-1</c>。
/// </summary>
/// <param name="Kind">端点类别。</param>
/// <param name="ParameterOrdinal">参数端点的零基序号。</param>
public sealed record NLCPGFlowSummaryEndpoint(NLCPGFlowSummaryEndpointKind Kind, int ParameterOrdinal = 0)
{
    /// <summary>实例方法的接收者端点。</summary>
    public static NLCPGFlowSummaryEndpoint Receiver { get; } = new(NLCPGFlowSummaryEndpointKind.Receiver);

    /// <summary>方法返回值端点。</summary>
    public static NLCPGFlowSummaryEndpoint Return { get; } = new(NLCPGFlowSummaryEndpointKind.Return, -1);

    /// <summary>创建指定零基序号的参数端点。</summary>
    public static NLCPGFlowSummaryEndpoint Parameter(int ordinal) => new(NLCPGFlowSummaryEndpointKind.Parameter, ordinal);
}

/// <summary>
/// 描述一个方法内已知的数据流：<see cref="Sources"/> 中的任一端点可流向 <see cref="Target"/>。
/// </summary>
/// <param name="AssemblyIdentity">声明程序集的标识。</param>
/// <param name="ContainingMetadataName">包含该方法的元数据类型名。</param>
/// <param name="MethodName">方法名。</param>
/// <param name="GenericArity">方法泛型参数数量。</param>
/// <param name="Sources">数据流源端点。</param>
/// <param name="Target">数据流目标端点。</param>
public sealed record NLCPGFlowSummary(
  string AssemblyIdentity,
  string ContainingMetadataName,
  string MethodName,
  int GenericArity,
  IReadOnlyList<NLCPGFlowSummaryEndpoint> Sources,
  NLCPGFlowSummaryEndpoint Target)
{
    /// <summary>
    /// 由程序集、类型、方法名和泛型参数数量组成的确定性查找键。
    /// </summary>
    public string StableKey => string.Join(
      "|",
      AssemblyIdentity,
      ContainingMetadataName,
      MethodName,
      GenericArity.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>
/// 表示流摘要的解析来源。
/// </summary>
public enum NLCPGFlowSummaryResolution
{
    /// <summary>来自项目提供的覆盖摘要。</summary>
    Project,

    /// <summary>来自框架内置摘要。</summary>
    Framework,

    /// <summary>未找到匹配摘要。</summary>
    Unknown,
}

/// <summary>
/// 一次流摘要查询的结果及其来源。
/// </summary>
/// <param name="Resolution">解析来源。</param>
/// <param name="Summary">解析到的摘要；来源为 <see cref="NLCPGFlowSummaryResolution.Unknown"/> 时为 <see langword="null"/>。</param>
public sealed record NLCPGFlowSummaryLookupResult(NLCPGFlowSummaryResolution Resolution, NLCPGFlowSummary? Summary)
{
    /// <summary>表示未解析到摘要的共享结果。</summary>
    public static NLCPGFlowSummaryLookupResult Unknown { get; } = new(NLCPGFlowSummaryResolution.Unknown, null);
}

/// <summary>
/// 按稳定键查询流摘要。项目摘要优先于同键的框架摘要。
/// </summary>
public sealed class NLCPGFlowSummaryRegistry
{
    private readonly IReadOnlyDictionary<string, NLCPGFlowSummary> _projectOverrides;
    private readonly IReadOnlyDictionary<string, NLCPGFlowSummary> _frameworkSummaries;

    /// <summary>
    /// 初始化流摘要注册表，并分别建立项目覆盖与框架摘要索引。
    /// </summary>
    /// <param name="projectOverrides">项目提供的覆盖摘要，优先级最高。</param>
    /// <param name="frameworkSummaries">框架提供的默认摘要。</param>
    public NLCPGFlowSummaryRegistry(IEnumerable<NLCPGFlowSummary>? projectOverrides = null, IEnumerable<NLCPGFlowSummary>? frameworkSummaries = null)
    {
        _projectOverrides = ToStableKeyIndex(projectOverrides);
        _frameworkSummaries = ToStableKeyIndex(frameworkSummaries);
    }

    /// <summary>
    /// 使用稳定键解析流摘要，依次查询项目覆盖和框架摘要。
    /// </summary>
    /// <param name="stableKey">由 <see cref="NLCPGFlowSummary.StableKey"/> 生成的查找键。</param>
    /// <returns>摘要及其解析来源；未命中时返回 <see cref="NLCPGFlowSummaryLookupResult.Unknown"/>。</returns>
    public NLCPGFlowSummaryLookupResult Resolve(string stableKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(stableKey);
        if (_projectOverrides.TryGetValue(stableKey, out var project))
        {
            return new NLCPGFlowSummaryLookupResult(NLCPGFlowSummaryResolution.Project, project);
        }

        if (_frameworkSummaries.TryGetValue(stableKey, out var framework))
        {
            return new NLCPGFlowSummaryLookupResult(NLCPGFlowSummaryResolution.Framework, framework);
        }

        return NLCPGFlowSummaryLookupResult.Unknown;
    }

    private static IReadOnlyDictionary<string, NLCPGFlowSummary> ToStableKeyIndex(IEnumerable<NLCPGFlowSummary>? summaries)
    {
        return (summaries ?? Array.Empty<NLCPGFlowSummary>())
          .OrderBy(summary => summary.StableKey, StringComparer.Ordinal)
          .ToDictionary(summary => summary.StableKey, StringComparer.Ordinal);
    }
}
