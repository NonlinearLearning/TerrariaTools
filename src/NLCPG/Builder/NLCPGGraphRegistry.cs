using NLCPG.Model;

namespace NLCPG.Builder;

/// <summary>
/// 多文件构建的**图注册表**：**每文件一张独立图**。
/// </summary>
/// <remarks>
/// <para>
/// 这是 T4（D1「按项路由」）的**前置层 P0**——此前仓库里不存在多文件图容器：
/// 全 <c>src/</c> 检索 <c>BuildFromSources</c>/<c>BuildMany</c>/<c>GraphByFile</c>/<c>GraphsByFile</c>
/// 均为 0 命中，且 <see cref="NLCPGBuildContext"/> 的图字段是**单数**。
/// </para>
/// <para>
/// <b>为什么是「每文件一张」而不是「一张全局主图」：</b>
/// 权威设计 <c>docs/plans/2026-09-24-unified-work-scheduler-design.md</c> §13b.7（<c>:923</c>）
/// 明确「每文件一张独立图」，并在 <c>:955</c> 特意警告易混淆点：
/// §4.2 的「合并为同一张图」指的是 **<c>F</c> 的图（每文件一张）**，**不是**一张全局主图。
/// </para>
/// <para>
/// <b>⚠️ 共享字符串表与身份工厂是本类型存在的全部理由。</b>
/// <see cref="NLCPGGraph"/> 默认各自 <c>new StringInterner()</c>（<c>NLCPGGraph.cs:154</c>），
/// 而 <see cref="StableNodeAnchor"/> 以 <c>FilePathId</c>（字符串表内的整数标识）参与相等性。
/// 若各图各持一份字符串表，同名文件在不同图里会得到**不同的 <c>FilePathId</c>**，
/// 锚点即**不可跨图比较** ⇒ fragment 无法被安全地路由与物化。
/// 故注册表强制所有兄弟图共享**同一个** <see cref="StringInterner"/>（与同一个
/// <see cref="StableNodeIdentityFactory"/>），使锚点在全局成为一个一致的命名空间。
/// </para>
/// <para>
/// <b>失败模式（fail-closed）：</b> <see cref="Resolve"/> 对未注册文件**抛异常**，
/// 而不是隐式新建图。理由见设计 <c>:699</c> 的 <c>R10</c>：
/// 路由遗漏的后果是「把 B 文件的事实写进 A 文件的图」——**静默错误而非崩溃**。
/// 静默新建一张永远不被 freeze、也永远不被任何人引用的图，属于同一类静默失效，
/// 故此处以显式异常把不变量钉死。
/// </para>
/// </remarks>
internal sealed class NLCPGGraphRegistry
{
    private readonly Dictionary<string, NLCPGGraph> _graphsByFilePath = new(StringComparer.Ordinal);
    private readonly List<string> _registeredFilePaths = new();

    internal NLCPGGraphRegistry(
      StableNodeIdentityFactory identityFactory,
      StringInterner stringInterner)
    {
        IdentityFactory = identityFactory ?? throw new ArgumentNullException(nameof(identityFactory));
        StringTable = stringInterner ?? throw new ArgumentNullException(nameof(stringInterner));
    }

    /// <summary>所有兄弟图共享的身份工厂（决定稳定锚点）。</summary>
    internal StableNodeIdentityFactory IdentityFactory { get; }

    /// <summary>所有兄弟图共享的字符串表（决定 <c>FilePathId</c> 等整数标识）。</summary>
    internal StringInterner StringTable { get; }

    /// <summary>注册序（确定性）——**不是**字典枚举序。</summary>
    internal IReadOnlyList<string> RegisteredFilePaths => _registeredFilePaths;

    internal int Count => _registeredFilePaths.Count;

    /// <summary>
    /// 为某文件登记其图。重复登记**同一实例**是幂等的；
    /// 为同一路径登记**不同实例**则抛出——那会让「按路径解析」失去唯一答案。
    /// </summary>
    internal NLCPGGraph Register(string filePath, NLCPGGraph graph)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(graph);

        if (_graphsByFilePath.TryGetValue(filePath, out var existing))
        {
            if (!ReferenceEquals(existing, graph))
            {
                throw new InvalidOperationException(
                  $"A different graph instance is already registered for '{filePath}'.");
            }

            return existing;
        }

        _graphsByFilePath[filePath] = graph;
        _registeredFilePaths.Add(filePath);
        return graph;
    }

    internal bool TryResolve(string filePath, out NLCPGGraph graph)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            graph = null!;
            return false;
        }

        return _graphsByFilePath.TryGetValue(filePath, out graph!);
    }

    /// <summary>
    /// 解析某文件的目标图；未注册即抛出（见类型注释的 fail-closed 理由）。
    /// </summary>
    internal NLCPGGraph Resolve(string filePath)
    {
        if (TryResolve(filePath, out var graph))
        {
            return graph;
        }

        throw new InvalidOperationException(
          $"No graph is registered for source file '{filePath}'. " +
          "Cross-file routing must target a file that was explicitly registered " +
          "in the multi-file graph registry.");
    }

    /// <summary>
    /// 取某文件的图，不存在则新建并登记。
    /// </summary>
    /// <remarks>
    /// 供**显式**多文件入口使用。新建的图与兄弟图共享字符串表/身份工厂（见类型注释），
    /// 并在 <see cref="FreezeAll"/> 时一并冻结。
    /// </remarks>
    internal NLCPGGraph GetOrAdd(string filePath, string? source = null)
    {
        if (TryResolve(filePath, out var existing))
        {
            if (source is not null)
            {
                existing.RegisterSource(filePath, source);
            }

            return existing;
        }

        var graph = new NLCPGGraph(
          identityFactory: IdentityFactory,
          stringInterner: StringTable);
        if (source is not null)
        {
            graph.RegisterSource(filePath, source);
        }

        return Register(filePath, graph);
    }

    /// <summary>
    /// 冻结**全部**已注册图。单文件时等价于对那张图调用 <c>FreezeQueryIndex</c>。
    /// </summary>
    /// <remarks>
    /// 必须按 <see cref="RegisteredFilePaths"/> 的注册序遍历，使多次构建的产物次序确定。
    /// </remarks>
    internal void FreezeAll()
    {
        foreach (var filePath in _registeredFilePaths)
        {
            _graphsByFilePath[filePath].FreezeQueryIndex();
        }
    }
}
