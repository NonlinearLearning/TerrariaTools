namespace NLCPG.Builder.Concurrency;

/// <summary>
/// **分片规划端口**（S5-2 的注入点）：由**应用层**实现，NLCPG 只调用。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么是端口而不是「把计划当配置传进来」：</b>分片序号必须与构图**同源同配置**——
/// 计划里的方法序号要匹配构图分配的 <c>order</c>，成本口径要匹配
/// <see cref="CpgWorkBatchCostModel.Estimate"/>。若让调用方先自建一个 builder 算出计划、
/// 再把计划塞进配置，就成立了「两个 builder 必须同配置」这一**只能靠约定维持**的约束，
/// 且每个语法树要枚举两遍（<c>GetOperationRootPlans</c> 会逐方法调 <c>GetDeclaredSymbol</c>）。
/// </para>
/// <para>
/// 改成端口后，实现方拿到的是<b>正在用于构图的那个 builder</b>，两个问题同时消失：
/// 配置不可能不一致；枚举结果进入该 builder 自己的缓存，构图阶段直接命中，不重复枚举。
/// </para>
/// <para>
/// <b>依赖方向</b>（<c>2026-09-24-unified-work-scheduler-execution.md:508</c>）：
/// 本接口与 <see cref="CpgWorkShardAssignment"/> 都只由 <see cref="int"/>/<see cref="string"/>
/// 构成，故 <c>NLCPG → Application</c> 的引用**不必要也不允许**——
/// 应用层的计划器实现本接口即可，方向仍是 <c>Application → NLCPG</c>。
/// </para>
/// </remarks>
public interface INLCPGWorkShardPlanner
{
    /// <summary>
    /// 为一批文档规划分片，返回**各文件的分片分配**（顺序即文件序）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 实现方应当用 <see cref="NLCPGBuilder.DescribeMethodOrders"/> 取得方法序号与成本，
    /// <b>不得</b>自行扫语法树重数一遍——序号一旦与构图不同源，分片边界会静默指向别的方法。
    /// </para>
    /// <para>
    /// 只在多文档构建（<c>BuildManyDocuments</c>）时被调用；单文档不调用，
    /// 故单文件路径与改造前逐字一致。
    /// </para>
    /// </remarks>
    /// <param name="builder">**即将用于本次构图**的构图器实例。</param>
    /// <param name="documents">本次构建的全部文档（路径唯一、顺序与调用方一致）。</param>
    /// <returns>各文件的分片分配；允许返回空（等价于不规划）。</returns>
    IReadOnlyList<CpgWorkShardAssignment> CreateShardAssignments(
      NLCPGBuilder builder,
      IReadOnlyList<NLCPGBuildDocument> documents);
}
