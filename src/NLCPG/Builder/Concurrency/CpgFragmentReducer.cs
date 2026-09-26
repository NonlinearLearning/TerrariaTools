using NLCPG.Builder.Streaming;
using NLCPG.Model;

namespace NLCPG.Builder.Concurrency;

/// <summary>
/// 将 worker 产出的局部 fragment 按 stable anchor 归并到单一可变图。
/// </summary>
public sealed class CpgFragmentReducer
{
    /// <summary>
    /// **D1 按项路由**：把每个 fragment 归并到**它自己所属文件**的图。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是 T4 最集中的接缝。单图重载 <see cref="ReduceInto(NLCPGGraph, IEnumerable{LocalCpgFragment})"/>
    /// 把所有 fragment 一律写入传入的那张图——单文件时正确，**跨文件时会把 B 文件的事实
    /// 写进 A 文件的图**（设计 R10 定性为「静默错误，非崩溃」）。
    /// </para>
    /// <para>
    /// ⚠ 关键：<see cref="LocalCpgFragment.SourceFilePath"/> **早已存在**，但单图重载
    /// **从不读它**。本重载正是那个缺失的读取点——钩子是现成的。
    /// </para>
    /// <para>
    /// 归并**顺序**必须与单图路径逐字一致，否则同一输入会产生不同的节点/边次序，
    /// 破坏「跨文件批次的每文件图签名 == 单文件路径的图签名」这一等价性断言。
    /// 故实现为：按 <b>注册序</b>遍历图，对每张图取「属于它的 fragment（保持传入次序）」，
    /// 再走**同一个**单图归并核心。这样每张图内部看到的分片序列与单文件时完全相同。
    /// </para>
    /// <para>
    /// <b>fail-closed：</b>fragment 指向未注册文件时由
    /// <see cref="NLCPGGraphRegistry.Resolve"/> 抛出，而非回退到「第一张图」。
    /// </para>
    /// </remarks>
    /// <param name="registry">多文件图注册表。</param>
    /// <param name="fragments">worker 产出的局部 fragment（可跨多个文件）。</param>
    /// <returns>**所有**图合并后的归并计数（各图之和）。</returns>
    internal IReadOnlyDictionary<string, CpgFragmentReductionMetrics> ReduceInto(
      NLCPGGraphRegistry registry,
      IEnumerable<LocalCpgFragment> fragments)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(fragments);

        // 先物化：fragments 可能是惰性序列，且下面要按图分组多次遍历。
        var materialized = fragments as IReadOnlyList<LocalCpgFragment> ?? fragments.ToArray();

        // 按注册序分组（**不是**字典枚举序），且组内保持传入次序。
        var fragmentsByFilePath = new Dictionary<string, List<LocalCpgFragment>>(StringComparer.Ordinal);
        foreach (var fragment in materialized)
        {
            if (!fragmentsByFilePath.TryGetValue(fragment.SourceFilePath, out var bucket))
            {
                bucket = new List<LocalCpgFragment>();
                fragmentsByFilePath[fragment.SourceFilePath] = bucket;
            }

            bucket.Add(fragment);
        }

        // 出现在 fragment 里、却未在图注册表中登记的文件 = 路由缺口，先统一报错。
        foreach (var filePath in fragmentsByFilePath.Keys)
        {
            _ = registry.Resolve(filePath);
        }

        var metricsByFilePath = new Dictionary<string, CpgFragmentReductionMetrics>(StringComparer.Ordinal);
        foreach (var filePath in registry.RegisteredFilePaths)
        {
            if (!fragmentsByFilePath.TryGetValue(filePath, out var bucket) || bucket.Count == 0)
            {
                continue;
            }

            metricsByFilePath[filePath] = ReduceInto(registry.Resolve(filePath), bucket);
        }

        return metricsByFilePath;
    }

    /// <summary>
    /// 按稳定节点身份提交 fragment，并跳过无法解析的 boundary edge。
    /// </summary>
    public CpgFragmentReductionMetrics ReduceInto(
      NLCPGGraph graph,
      IEnumerable<LocalCpgFragment> fragments)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(fragments);

        var materializedDescriptors = new Dictionary<StableNodeAnchor, CpgNodeDescriptor>();
        var deduplicatedNodeCount = 0;
        var allDescriptors = fragments
          .SelectMany(fragment => fragment.Nodes)
          .OrderBy(descriptor => descriptor.Anchor.Kind)
          .ThenBy(descriptor => descriptor.Anchor.FilePathId)
          .ThenBy(descriptor => descriptor.Anchor.SpanStart)
          .ThenBy(descriptor => descriptor.Anchor.SpanEnd)
          .ThenBy(descriptor => descriptor.Anchor.Role)
          .ThenBy(descriptor => descriptor.Anchor.Ordinal)
          .ThenBy(descriptor => descriptor.Anchor.ExtraKeyId)
          .ThenBy(descriptor => descriptor.Kind)
          .ThenBy(descriptor => descriptor.NameId)
          .ThenBy(descriptor => descriptor.FullNameId)
          .ThenBy(descriptor => descriptor.SignatureId)
          .ThenBy(descriptor => descriptor.TypeFullNameId)
          .ThenBy(descriptor => descriptor.FilePathId)
          .ThenBy(descriptor => descriptor.SpanStart)
          .ThenBy(descriptor => descriptor.SpanEnd)
          .ThenBy(descriptor => descriptor.IsImplicit)
          .ToArray();

        foreach (var descriptor in allDescriptors)
        {
            if (!materializedDescriptors.TryAdd(descriptor.Anchor, descriptor))
            {
                deduplicatedNodeCount += 1;
            }
        }

        var allocation = graph.HasPreallocatedNodeIds
          ? graph.RequirePreallocatedNodeIds()
          : DeterministicNodeIdTable.Create(materializedDescriptors.Keys);
        var nodesByAnchor = new Dictionary<StableNodeAnchor, NLCPGNode>();
        foreach (var descriptor in materializedDescriptors
          .OrderBy(entry => entry.Key.Kind)
          .ThenBy(entry => entry.Key.FilePathId)
          .ThenBy(entry => entry.Key.SpanStart)
          .ThenBy(entry => entry.Key.SpanEnd)
          .ThenBy(entry => entry.Key.Role)
          .ThenBy(entry => entry.Key.Ordinal)
          .ThenBy(entry => entry.Key.ExtraKeyId)
          .Select(entry => entry.Value))
        {
            nodesByAnchor[descriptor.Anchor] = graph.AddNode(descriptor.Materialize(allocation));
        }

        var deduplicatedEdgeCount = 0;
        var skippedUnavailableBoundaryEdgeCount = 0;
        var seenEdges = new HashSet<CpgEdgeCandidate>();
        foreach (var candidate in fragments
          .SelectMany(fragment => fragment.Edges)
          .OrderBy(edge => edge.SourceAnchor.Kind)
          .ThenBy(edge => edge.SourceAnchor.FilePathId)
          .ThenBy(edge => edge.SourceAnchor.SpanStart)
          .ThenBy(edge => edge.SourceAnchor.SpanEnd)
          .ThenBy(edge => edge.SourceAnchor.Role)
          .ThenBy(edge => edge.SourceAnchor.Ordinal)
          .ThenBy(edge => edge.SourceAnchor.ExtraKeyId)
          .ThenBy(edge => edge.TargetAnchor.Kind)
          .ThenBy(edge => edge.TargetAnchor.FilePathId)
          .ThenBy(edge => edge.TargetAnchor.SpanStart)
          .ThenBy(edge => edge.TargetAnchor.SpanEnd)
          .ThenBy(edge => edge.TargetAnchor.Role)
          .ThenBy(edge => edge.TargetAnchor.Ordinal)
          .ThenBy(edge => edge.TargetAnchor.ExtraKeyId)
          .ThenBy(edge => edge.Kind))
        {
            if (!nodesByAnchor.TryGetValue(candidate.SourceAnchor, out var source) ||
                !nodesByAnchor.TryGetValue(candidate.TargetAnchor, out var target))
            {
                skippedUnavailableBoundaryEdgeCount += 1;
                continue;
            }

            if (!seenEdges.Add(candidate))
            {
                deduplicatedEdgeCount += 1;
                continue;
            }

            graph.AddEdge(
              source,
              target,
              candidate.Kind,
              candidate.StructuredLabel,
              candidate.ContextId,
              candidate.CallSiteContext);
        }

        return new CpgFragmentReductionMetrics(
          materializedDescriptors.Count + deduplicatedNodeCount,
          materializedDescriptors.Count,
          deduplicatedNodeCount,
          seenEdges.Count + deduplicatedEdgeCount,
          seenEdges.Count,
          deduplicatedEdgeCount,
          skippedUnavailableBoundaryEdgeCount);
    }
}

/// <summary>
/// 描述一次 fragment 归并的去重和 boundary 处理结果。
/// </summary>
public sealed record CpgFragmentReductionMetrics(
  int InputNodeDescriptorCount,
  int ReducedNodeCount,
  int DeduplicatedNodeCount,
  int InputEdgeCandidateCount,
  int ReducedEdgeCount,
  int DeduplicatedEdgeCount,
  int SkippedUnavailableBoundaryEdgeCount);
