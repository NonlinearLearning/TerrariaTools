namespace NLISSN.Infrastructure.Configuration;

/// <summary>
/// NLISSN 运行内联的 ProjectJson 导出设置。对应 <c>artifacts.projectJson</c>。
/// 复用 NLISSN 已解析的 <c>input</c>/工作区参数，因此不需要单独的 <c>tool:</c> 配置。
/// </summary>
internal sealed record ProjectExportSettings(
    bool Enabled,
    string OutputPath,
    int ProjectWorkerCount,
    int DocumentShardCount = 1,
    // 收集并输出导出阶段计时（工作区加载 / 每文件构图 / 投影 / 序列化落盘）。
    // 默认 false：不收集时导出不付任何计时开销，且输出与引入该开关前逐字节相同。
    bool PerformanceDiagnostics = false,
    // 单文档各分片的并行度（投影 + 落盘）。默认 1 = 逐片串行，输出与引入前逐字节相同。
    // >1 时多片同时在途，峰值随之上升；只应在内存宽裕时开启。
    int DocumentShardParallelism = 1,
    // 导出侧 CPG 构图**额外**请求的能力位（NLCPGCapability 名称，如 "InterproceduralDataFlow"）。
    //
    // ⚠ 刻意保持为**字符串**而非 NLCPGCapability：本工程（NLISSN.Configuration）的
    // ProjectReference 列表被 LayoutArchitectureTests 冻结，不得新增对 NLCPG 的引用。
    // 名字 → 枚举的解析与 fail-fast 落在 NLISSN.Hosting.ProjectJsonExportService。
    //
    // 空集合 = 沿用 NLCPGBuilderOptions.CreateDefault() 的 RequestedCapabilities: null，
    // 即 NLCPGCapability.Default（不含 InterproceduralDataFlow / Dominance / ControlDependence）。
    // 故既有配置的导出产物不变。
    IReadOnlyCollection<string>? RequestedCapabilities = null)
{
    public IReadOnlyCollection<string> EffectiveRequestedCapabilities =>
      RequestedCapabilities ?? Array.Empty<string>();
}
