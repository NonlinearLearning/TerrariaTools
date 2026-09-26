using NLCPG.Builder;
using NLCPG.Builder.Concurrency;

namespace NLISSN.Application;

/// <summary>
/// **S5-2 adapter：把应用层的分片计划接到 NLCPG 的规划端口上。**
/// </summary>
/// <remarks>
/// <para>
/// <b>依赖方向（设计 <c>2026-09-24-unified-work-scheduler-execution.md:508</c>）：</b>
/// 计划由本层拥有（<see cref="DocumentShardPlanner"/> 产出 <see cref="DocumentShardPlan"/>），
/// NLCPG 只接收只由 <see cref="int"/>/<see cref="string"/> 构成的中立契约
/// （<see cref="CpgWorkShardAssignment"/>），且<b>不得</b>新增
/// <c>NLCPG → Application</c> 引用。故转换只能发生在这一侧，
/// 而 NLCPG 侧的挂载点是一个**端口**（<see cref="INLCPGWorkShardPlanner"/>）。
/// </para>
/// <para>
/// <b>本类只做三件事：</b>
/// <list type="number">
/// <item>用 NLCPG <b>自己的</b>枚举（<see cref="NLCPGBuilder.DescribeMethodOrders"/>）
///   取得每个文件的操作根序号与成本——序号必须同源，否则分片边界会指向别的方法；</item>
/// <item>把描述符交给 <see cref="DocumentShardPlanner.PlanAll"/> 得到分片计划；</item>
/// <item>把计划压成 NLCPG 的中立契约（<see cref="ToAssignments"/>）。</item>
/// </list>
/// </para>
/// <para>
/// <b>成本口径：</b>取 <see cref="NLCPGBuilder.DescribeMethodOrders"/> 给出的成本，
/// 而**不是**本层自行数的行数。两边都是方法体行数 <c>endLine - startLine + 1</c>，
/// 故计划阈值与装箱阈值同币种，不会一紧一松。
/// </para>
/// </remarks>
public sealed class DocumentShardPlanAdapter : INLCPGWorkShardPlanner
{
  private readonly DocumentShardPlannerOptions _options;

  /// <summary>以给定阈值配置构造适配器；为 <c>null</c> 时用计划器默认值。</summary>
  public DocumentShardPlanAdapter(DocumentShardPlannerOptions? options = null)
  {
    _options = options ?? DocumentShardPlannerOptions.Default;
  }

  /// <summary>
  /// 为一批文档规划分片，返回 NLCPG 中立的分片分配。
  /// </summary>
  /// <remarks>
  /// <para>
  /// 由 NLCPG 在**多文档构建**的每次 <c>Build</c> 入口调用一次，
  /// 传入的 <paramref name="builder"/> 就是**正在用于构图的那个**实例——
  /// 故序号与成本与构图同源、同配置，且枚举结果进入该实例的操作根缓存，
  /// 构图阶段直接命中（不会重复扫语法树）。
  /// </para>
  /// <para>
  /// 单文档构建不调用本方法，故既有单文件路径与改造前逐字一致。
  /// </para>
  /// </remarks>
  /// <param name="builder">即将用于本次构图的构图器。</param>
  /// <param name="documents">本次构建的全部文档（路径唯一）。</param>
  /// <returns>各文件的分片分配；输入为空时返回空列表。</returns>
  /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
  public IReadOnlyList<CpgWorkShardAssignment> CreateShardAssignments(
    NLCPGBuilder builder,
    IReadOnlyList<NLCPGBuildDocument> documents)
  {
    ArgumentNullException.ThrowIfNull(builder);
    ArgumentNullException.ThrowIfNull(documents);
    if (documents.Count == 0)
    {
      return Array.Empty<CpgWorkShardAssignment>();
    }

    var inputFiles = new List<DocumentShardInputFile>(documents.Count);
    foreach (var document in documents)
    {
      ArgumentNullException.ThrowIfNull(document);

      // ⚠ 序号与成本都取自 NLCPG 自己的枚举——不得在此另数一遍（见类型注释）。
      var described = builder.DescribeMethodOrders(
        document.SemanticModel!,
        document.Root!);
      var methods = new List<DocumentMethodDescriptor>(described.Count);
      foreach (var (methodOrder, estimatedCost) in described)
      {
        methods.Add(new DocumentMethodDescriptor(methodOrder, estimatedCost));
      }

      inputFiles.Add(new DocumentShardInputFile(document.FilePath, methods));
    }

    return ToAssignments(DocumentShardPlanner.PlanAll(inputFiles, _options));
  }

  /// <summary>
  /// 把应用层计划压成 NLCPG 中立契约。
  /// </summary>
  /// <remarks>
  /// <para>
  /// <b>为什么按文件路径重排：</b><see cref="DocumentShardPlanner.PlanAll"/> 返回的顺序
  /// 与**输入**同序，而全局序号必须由**与输入顺序无关**的确定性全序导出，否则同一批文件
  /// 换一个输入顺序就会得到不同的调度序号。文件路径序数序正是这个与输入无关的全序
  /// （与 <see cref="DocumentShardPlanner.AssignFileOrdinals"/> 一致）。
  /// </para>
  /// <para>
  /// 片内顺序照抄计划器给出的顺序：它按父序升序，本身确定；
  /// 且片内先后只影响序号分配，不影响「哪些方法在同一片」这一语义。
  /// </para>
  /// </remarks>
  private static IReadOnlyList<CpgWorkShardAssignment> ToAssignments(
    IReadOnlyList<DocumentShardPlan> plans)
  {
    var ordered = plans
      .OrderBy(plan => plan.FilePath, StringComparer.Ordinal)
      .ToArray();

    var assignments = new List<CpgWorkShardAssignment>(ordered.Length);
    foreach (var plan in ordered)
    {
      var shards = new List<CpgWorkShard>(plan.Shards.Count);
      foreach (var shard in plan.Shards)
      {
        shards.Add(new CpgWorkShard(shard.MethodOrders));
      }

      assignments.Add(new CpgWorkShardAssignment(plan.FilePath, shards));
    }

    return assignments;
  }
}
