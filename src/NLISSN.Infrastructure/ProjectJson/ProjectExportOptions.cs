using NLCPG.Contracts;
using NLISSN.Infrastructure.Workspace;

namespace NLCPG.ProjectJson;

public sealed record ProjectExportOptions
{
    public ProjectExportOptions(
      string ProjectPath,
      string? OutputPath = null,
      string? TargetFramework = null,
      string Configuration = "Debug",
      string Platform = "AnyCPU",
      WorkspaceRestoreMode RestoreMode = WorkspaceRestoreMode.Disabled,
      bool IncludeGenerated = false,
      int MaxDegreeOfParallelism = 12,
      int DocumentShardCount = 1,
      // 单文档分片的**写盘并行度**。1（默认）＝逐片串行，与引入该开关前逐字节相同。
      // >1 时各片的「投影 + 落盘」并行执行，但需要同时驻留多片 DTO，峰值随之上升。
      int DocumentShardParallelism = 1,
      long ProjectMemoryBudgetBytes = 0,
      long ProjectBaselineReservationBytes = 8L * 1024 * 1024,
      // 收集导出阶段计时（工作区加载 / 每文件构图 / 投影 / 序列化落盘）。
      // 默认 false：不收集时不读时钟、不建列表，保持既有开销。
      // ⚠ 该开关**只影响度量**，不得改变任何 payload 取值或文件布局。
      bool PerformanceDiagnostics = false,
      // 单文档构图**额外**请求的 CPG 能力位。
      //
      // null（默认）⇒ 沿用 CreateDefault() 的 RequestedCapabilities: null
      // ⇒ 解析为 NLCPGCapability.Default ⇒ 与引入本参数前**逐字节相同**。
      //
      // ⚠ 非 null 会改变 payload：能力位决定构图阶段是否运行（如 InterproceduralDataFlow
      // 会新增跨过程桥接边并重排 NodeId），故这是**契约变更**而非度量开关。
      IReadOnlyCollection<NLCPGCapability>? RequestedCapabilities = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ProjectPath);
        if (MaxDegreeOfParallelism <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDegreeOfParallelism));
        }

        // 1 = 不分片（默认，输出与分片实现引入前逐字节相同）；0 与负数无意义。
        if (DocumentShardCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DocumentShardCount));
        }

        // 1 = 逐片串行（默认）；0 与负数无意义。
        if (DocumentShardParallelism <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DocumentShardParallelism));
        }

        // 0 = 按物理内存自动取值（由导出器在运行时决定）；负数无意义。
        if (ProjectMemoryBudgetBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ProjectMemoryBudgetBytes));
        }

        if (ProjectMemoryBudgetBytes > 0 &&
            (ProjectBaselineReservationBytes < 0 ||
             ProjectBaselineReservationBytes >= ProjectMemoryBudgetBytes))
        {
            throw new ArgumentOutOfRangeException(nameof(ProjectBaselineReservationBytes));
        }

        this.ProjectPath = ProjectPath;
        this.OutputPath = OutputPath;
        this.TargetFramework = TargetFramework;
        this.Configuration = Configuration;
        this.Platform = Platform;
        this.RestoreMode = RestoreMode;
        this.IncludeGenerated = IncludeGenerated;
        this.MaxDegreeOfParallelism = MaxDegreeOfParallelism;
        this.DocumentShardCount = DocumentShardCount;
        this.DocumentShardParallelism = DocumentShardParallelism;
        this.ProjectMemoryBudgetBytes = ProjectMemoryBudgetBytes;
        this.ProjectBaselineReservationBytes = ProjectBaselineReservationBytes;
        this.PerformanceDiagnostics = PerformanceDiagnostics;
        this.RequestedCapabilities = RequestedCapabilities;
    }

    public string ProjectPath { get; }

    public string? OutputPath { get; }

    public string? TargetFramework { get; }

    public string Configuration { get; }

    public string Platform { get; }

    public WorkspaceRestoreMode RestoreMode { get; }

    public bool IncludeGenerated { get; }

    public int MaxDegreeOfParallelism { get; }

    /// <summary>
    /// 单个文档的 payload 分片数上限。1 = 不分片（默认）。
    /// </summary>
    /// <remarks>
    /// 这是「单文档内」的分片，与 <see cref="ProjectWorkerCount"/>（单文档内的 builder 并行度）
    /// 正交：前者只降低「物化 payload」那一段的驻留，后者提高单文档的构建并行度。
    /// 实际分片数还会被文档节点数收敛（见导出器），故不会产出空片。
    /// <para>
    /// ⚠ 不要把 <c>&gt; 1</c> 当作大文档 OOM 的解法：端到端实测（591023 节点文档）
    /// 8 片相对不分片峰值仅约 −4%，而 payload 字节 +40%、投影与写盘耗时上升。
    /// 端到端峰值由**构图**主导，而图在分片之前就已整篇驻留。详见 docs/cli-reference.md。
    /// </para>
    /// </remarks>
    public int DocumentShardCount { get; }

    /// <summary>
    /// 单文档各分片的并行度（投影 + 落盘）。1 = 逐片串行（默认）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="DocumentShardCount"/> 正交：后者决定切几片，本项决定几片同时在途。
    /// <para>
    /// ⚠ 与分片的内存目的直接对冲：并行度 N 意味着最多 N 片 DTO 同时驻留，
    /// 峰值高于串行。故仅当内存宽裕、且确实要换速度时开启；内存受限时应保持 1。
    /// 实测（合成图同批分片、只测投影）：wall 124 ms → 32 ms，dop=4 已近饱和（约 3.9x）。
    /// </para>
    /// <para>
    /// 不改变任何 payload 取值：各片写各自路径，manifest 仍按 sourcePath 排序，
    /// 故并行与串行的输出**逐字节相同**（由契约测试与端到端字节比对锁定）。
    /// </para>
    /// </remarks>
    public int DocumentShardParallelism { get; }

    public long ProjectMemoryBudgetBytes { get; }

    /// <summary>是否收集导出阶段计时。默认 <c>false</c>；不改变任何输出内容。</summary>
    public bool PerformanceDiagnostics { get; }

    /// <summary>
    /// 单文档构图额外请求的 CPG 能力位；<c>null</c>（默认）＝沿用 <c>NLCPGCapability.Default</c>。
    /// </summary>
    /// <remarks>
    /// ⚠ 非 <c>null</c> 会改变 payload 取值（新增边、重排 NodeId），属契约变更。
    /// 与 <see cref="PerformanceDiagnostics"/> 不同，它**不是**纯度量开关。
    /// </remarks>
    public IReadOnlyCollection<NLCPGCapability>? RequestedCapabilities { get; }

    public long ProjectBaselineReservationBytes { get; }

    public int ProjectWorkerCount => Math.Min(12, Math.Max(1, MaxDegreeOfParallelism));

    public int LocalBuilderDegreeOfParallelism => 1;

    public string FullProjectPath => Path.GetFullPath(ProjectPath);
    public string FullOutputPath => Path.GetFullPath(
      OutputPath ?? Path.Combine(
        Path.GetDirectoryName(FullProjectPath)!,
        "Build",
        "NLCPG-json"));

    public int EffectiveMaxDegreeOfParallelism => ProjectWorkerCount;
}
