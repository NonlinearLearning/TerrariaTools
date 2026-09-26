namespace NLCPG.Builder;

/// <summary>
/// G0-P R-1：**阶段依赖的声明式唯一权威**（取代此前只靠 <c>NLCPGBuilder.cs:411-417</c> 书写顺序的隐式约定）。
/// <para>
/// <b>为什么需要它（附录 N–Q 的实测依据）：</b>后置 pass 之间的依赖是**隐式**的——驱动它们的是
/// <c>RunOptionalPass(buildPlan.Requires*, ...)</c>，而 <c>Requires*</c> 全部来自 capability 位，
/// <b>只回答「能力是否被请求」，不表达阶段依赖</b>。真实依赖仅由那 7 行的**书写顺序**保证。
/// </para>
/// <para>
/// 已实测的 6 条依赖（见本类 <see cref="RequiredPredecessors"/> 的逐条注释）中，
/// <b>只有 1 条有守卫且实测不可达，其余 5 条零守卫</b>；被破坏时的表现是
/// <b>静默少边</b>或<b>整阶段静默跳过</b>，<b>不抛异常、不打日志</b>。
/// </para>
/// <para>
/// <b>实证价值：</b>轮次 19 实测把 <c>Dominance</c> 与 <c>ControlDependence</c> 两行对调，
/// 全量 <c>~Cpg</c> 由 548/548 全绿变为 <b>5 失败</b>（附录 Q.4）。
/// 那一次是**测试**抓到的；本类的作用是让同类改动**在任何 worker 启动之前就被拒绝**。
/// </para>
/// <para>
/// ⚠ <b>本类是纯校验设施：不改变任何执行顺序、不改变任何产物。</b>
/// 它只把「顺序正确」这一既存前提**显式化并强制化**（fail-closed）。
/// </para>
/// </summary>
internal static class StageDependencyTable
{
    /// <summary>稳定阶段标识。顺序为 <b>声明序</b>，同时用作拓扑的确定性输出顺序。</summary>
    internal enum Stage
    {
        Syntax,
        Operation,
        CallGraph,
        MemberAccess,
        ControlFlow,
        DataFlow,
        InterproceduralDataFlow,
        Dominance,
        ControlDependence,
    }

    /// <summary>
    /// 每个阶段的**必需前置集合**。这是依赖的唯一权威来源；新增阶段必须在此登记。
    /// <para>
    /// 各条目的证据出处（均为源码确证，非推断）：
    /// </para>
    /// </summary>
    private static readonly IReadOnlyDictionary<Stage, Stage[]> RequiredPredecessors =
      new Dictionary<Stage, Stage[]>
      {
          // 语法/操作是建图起点，无 pass 前置。
          [Stage.Syntax] = Array.Empty<Stage>(),
          [Stage.Operation] = new[] { Stage.Syntax },

          // 依赖①：DataFlow 需要 CallGraph 已缓存解析后的调用目标。
          //   证据：DataFlowPass.cs:1934-1937 有 throw，但实测【不可达】——被破坏时表现为桥接边静默变空。
          [Stage.CallGraph] = new[] { Stage.Operation },

          // MemberAccess 读 CallGraph 登记的属性访问调用点（CallGraphPass.cs:238 → DataFlowPass.cs:2103 同一字段）。
          [Stage.MemberAccess] = new[] { Stage.CallGraph },

          // ControlFlow 写入 _cfgPredecessors/SuccessorsByNode（NLCPGBuilder.cs:2062-2063）。
          [Stage.ControlFlow] = new[] { Stage.MemberAccess },

          // 依赖④：DataFlow 的 CFG 邻接规划读 ControlFlow 写入的缓存（DataFlowPass.cs:1227-1229）。
          [Stage.DataFlow] = new[] { Stage.CallGraph, Stage.ControlFlow },

          // 依赖⑥：DataFlow 经 _propertyAccessorCallSiteNodesByKey 查调用点（CallGraphPass.cs:238 写）。
          // 依赖②：Interprocedural 的快照包含【当时已加入的边】（NLCPGBuilder.cs:1062-1063）。
          //   ⚠ 附录 Q.3 已【推翻】旧表述「要求 DataFlow reducer 完成」——_interproceduralBarrierCompleted
          //     全仓仅 2 处（仅声明+赋值），从不参与门控。故此处登记的是“快照内容依赖”，非硬前置；
          //     保守起见仍要求 DataFlow 在前（与该阶段实参一致），但【不声称】它是运行期强制的。
          [Stage.InterproceduralDataFlow] = new[] { Stage.DataFlow },

          // ❗ 依赖③（本表最重要的一条）：ControlDependencePass.cs:67
          //    `if (_dominanceOverlays.Count == 0) return;` —— 该列表由 DominancePass 填充。
          //    顺序反了 ⇒ 该阶段【整体静默跳过】，不抛异常、不打日志。
          //    实证：附录 Q.4（对调两行 ⇒ 全量 ~Cpg 由 548/548 变为 5 失败）。
          [Stage.Dominance] = new[] { Stage.InterproceduralDataFlow },
          [Stage.ControlDependence] = new[] { Stage.Dominance },
      };

    /// <summary>该阶段的必需前置（只读视图）。</summary>
    internal static IReadOnlyList<Stage> PredecessorsOf(Stage stage)
    {
        return RequiredPredecessors.TryGetValue(stage, out var predecessors)
          ? predecessors
          : Array.Empty<Stage>();
    }

    /// <summary>全部已登记阶段，按声明序（确定性）。</summary>
    internal static IReadOnlyList<Stage> AllStages { get; } =
      RequiredPredecessors.Keys.OrderBy(stage => (int)stage).ToArray();

    /// <summary>
    /// 校验给定执行序列是否满足全部依赖。**违反即返回失败，由调用方拒绝执行（fail-closed）。**
    /// </summary>
    /// <param name="executionOrder">实际执行序列（按先后）。允许省略未请求的阶段。</param>
    /// <param name="reason">失败原因；成功时为 <c>null</c>。</param>
    /// <returns>满足全部已声明依赖时为 <c>true</c>。</returns>
    internal static bool TryValidateOrder(
      IReadOnlyList<Stage> executionOrder,
      out string? reason)
    {
        ArgumentNullException.ThrowIfNull(executionOrder);

        var position = new Dictionary<Stage, int>(executionOrder.Count);
        for (var index = 0; index < executionOrder.Count; index += 1)
        {
            // 同一阶段不得重复执行：重复即意味着状态被覆盖或产物重复发布。
            if (!position.TryAdd(executionOrder[index], index))
            {
                reason = $"阶段 {executionOrder[index]} 在执行序列中出现多次（下标 {position[executionOrder[index]]} 与 {index}）；"
                  + "阶段依赖表要求每个阶段至多执行一次。";
                return false;
            }
        }

        foreach (var stage in executionOrder)
        {
            if (!RequiredPredecessors.TryGetValue(stage, out var predecessors))
            {
                reason = $"阶段 {stage} 未在 StageDependencyTable 中登记；"
                  + "请先登记其必需前置，否则其依赖不会被校验。";
                return false;
            }

            foreach (var predecessor in predecessors)
            {
                // 未被请求的前置视为已满足（能力未开启时该阶段整体不运行）。
                if (!position.TryGetValue(predecessor, out var predecessorIndex))
                {
                    continue;
                }

                if (predecessorIndex > position[stage])
                {
                    reason = $"阶段依赖被违反：{stage} 需要 {predecessor} 先完成，"
                      + $"但执行序列中 {stage} 位于下标 {position[stage]}、{predecessor} 位于下标 {predecessorIndex}。"
                      + DescribeFailureMode(stage);
                    return false;
                }
            }
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// 该依赖被破坏时的**实测失效形态**——写进异常消息，避免使用者只看到"顺序错了"却不知后果。
    /// </summary>
    private static string DescribeFailureMode(Stage stage)
    {
        return stage switch
        {
            Stage.ControlDependence =>
              " 实测后果：ControlDependencePass.cs:67 读到空的 _dominanceOverlays 后【整体静默 return】，"
              + "不抛异常、不打日志，控制依赖边全部缺失（附录 Q.4 实证：全量 ~Cpg 由 548/548 变为 5 失败）。",
            Stage.DataFlow =>
              " 实测后果：CFG 邻接缓存（_cfgPredecessors/SuccessorsByNode）为空，"
              + "数据流邻接规划读到空缓存，数据流边集静默变化（附录 Q.1 实证：互换后 19 条失败）。",
            Stage.InterproceduralDataFlow =>
              " 注意：该依赖为【快照内容依赖】而非硬前置（附录 Q.3 已推翻旧的“reducer 必须完成”表述）；"
              + "被破坏时表现为跨过程桥接边缺失。",
            Stage.MemberAccess =>
              " 实测后果：DataFlow 读不到属性访问调用点（DataFlowPass.cs:2103 返回 null 静默降级）。",
            _ =>
              " 该依赖被破坏时为静默少边或整阶段静默跳过，不抛异常、不打日志。",
        };
    }
}
