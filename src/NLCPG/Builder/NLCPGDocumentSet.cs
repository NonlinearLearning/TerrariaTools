namespace NLCPG.Builder;

/// <summary>
/// **多文件构建的文档集合**：每文件一个 <see cref="NLCPGBuildContext"/>（各持一张独立图），
/// 全部图共享同一份字符串表与身份工厂。
/// </summary>
/// <remarks>
/// <para>
/// 这是 T4 的**前置层 P0 的完整形态**。仅有 <see cref="NLCPGGraphRegistry"/> 还不够：
/// 每个 pass 的 worker 在消费批次条目时，都要用**该条目所属文件**的
/// <see cref="NLCPGBuildContext.Root"/>/<c>SemanticModel</c>/<c>OperationInventory</c>
/// 去解析操作根，而不是用构造期的单一 context。
/// </para>
/// <para>
/// <b>为什么这恰好也修掉了「<c>StableOrder</c> 当数组下标」的跨文件错配：</b>
/// <c>DataFlowPass</c> 用 <c>methodPartitions[item.StableOrder]</c>、
/// <c>ControlDependencePass</c> 用 <c>_dominanceOverlays[batch.StableOrder]</c> 做下标，
/// 而 <c>StableOrder</c> 是**文件内局部**序号。跨文件批次下直接下标必然错配。
/// 但若 worker **先把批次按文件分组**、再对每个分组用**该文件自己的** context 与分区表处理，
/// 下标域就重新回到「单文件局部」——与单文件路径逐字同构。
/// ⇒ 正确路由**就是**下标修复，无需另造全局序号。
/// </para>
/// </remarks>
internal sealed class NLCPGDocumentSet
{
    private readonly Dictionary<string, NLCPGBuildContext> _contextsByFilePath = new(StringComparer.Ordinal);
    private readonly List<string> _filePaths = new();

    internal NLCPGDocumentSet(NLCPGGraphRegistry graphs)
    {
        Graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    }

    internal NLCPGGraphRegistry Graphs { get; }

    /// <summary>文件登记序（确定性）——**不是**字典枚举序。</summary>
    internal IReadOnlyList<string> FilePaths => _filePaths;

    internal int Count => _filePaths.Count;

    internal void Add(NLCPGBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_contextsByFilePath.TryGetValue(context.FilePath, out var existing))
        {
            if (!ReferenceEquals(existing, context))
            {
                throw new InvalidOperationException(
                  $"A different build context is already registered for '{context.FilePath}'.");
            }

            return;
        }

        _contextsByFilePath[context.FilePath] = context;
        _filePaths.Add(context.FilePath);
    }

    internal bool TryResolveContext(string filePath, out NLCPGBuildContext context)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            context = null!;
            return false;
        }

        return _contextsByFilePath.TryGetValue(filePath, out context!);
    }

    /// <summary>
    /// 解析某文件的构建上下文；未登记即抛出（fail-closed，理由见
    /// <see cref="NLCPGGraphRegistry"/> 的类型注释）。
    /// </summary>
    internal NLCPGBuildContext ResolveContext(string filePath)
    {
        if (TryResolveContext(filePath, out var context))
        {
            return context;
        }

        throw new InvalidOperationException(
          $"No build context is registered for source file '{filePath}'. " +
          "Cross-file routing must target a file that was explicitly added to the document set.");
    }
}
