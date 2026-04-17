using Analysis.Core;

namespace Analysis.Passes;

/// <summary>
/// 根据命名空间定义创建命名空间和命名空间块节点。
///
/// 这个 pass 对应 Joern 基础层里 `NamespaceCreator` 的职责方向。
/// 当前实现保持最小化：
/// - 创建 `Namespace` 节点表示逻辑命名空间；
/// - 创建 `NamespaceBlock` 节点表示文件中的具体承载块；
/// - 在两者之间用 `Ref` 建立最小关联。
/// </summary>
public sealed class BuildNamespaceNodesPass : CpgPass
{
    /// <summary>
    /// 初始化命名空间创建 pass。
    /// </summary>
    /// <param name="definitions">命名空间定义集合。</param>
    public BuildNamespaceNodesPass(IEnumerable<NamespaceDefinition> definitions)
    {
        Definitions = definitions?.ToArray() ?? throw new ArgumentNullException(nameof(definitions));
    }

    /// <summary>
    /// 获取命名空间定义集合。
    /// </summary>
    public IReadOnlyList<NamespaceDefinition> Definitions { get; }

    /// <inheritdoc />
    protected override void Execute(CpgGraphBuilder builder)
    {
        foreach (NamespaceDefinition definition in Definitions)
        {
            CpgNode namespaceNode = builder.Graph
                .GetNodes(CpgNodeKind.Namespace)
                .FirstOrDefault(node => node.TryGetProperty<string>("FullName", out string? fullName) &&
                                        string.Equals(fullName, definition.FullName, StringComparison.Ordinal))
                ?? CreateNamespaceNode(builder, definition);

            CpgNode namespaceBlockNode = builder.CreateNode(CpgNodeKind.NamespaceBlock);
            namespaceBlockNode.SetProperty("Name", definition.Name);
            namespaceBlockNode.SetProperty("FullName", definition.FullName);
            namespaceBlockNode.SetProperty("FileName", definition.FileName);

            builder.AddEdge(namespaceBlockNode.Id, namespaceNode.Id, CpgEdgeKind.Ref);
        }
    }

    private static CpgNode CreateNamespaceNode(CpgGraphBuilder builder, NamespaceDefinition definition)
    {
        CpgNode namespaceNode = builder.CreateNode(CpgNodeKind.Namespace);
        namespaceNode.SetProperty("Name", definition.Name);
        namespaceNode.SetProperty("FullName", definition.FullName);
        return namespaceNode;
    }
}

/// <summary>
/// 表示一个待创建的命名空间定义。
/// </summary>
/// <param name="Name">命名空间短名。</param>
/// <param name="FullName">命名空间全名。</param>
/// <param name="FileName">命名空间所在文件。</param>
public sealed record NamespaceDefinition(string Name, string FullName, string FileName);
