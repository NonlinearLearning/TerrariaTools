using Analysis.Core;

namespace Analysis.Passes;

/// <summary>
/// 根据文件路径列表创建文件节点。
///
/// 这个 pass 对应 Joern 基础层里的 `FileCreationPass`。
/// 它不是复杂逻辑，但它是所有结构挂载关系的起点：
/// - 后续命名空间要挂在文件下；
/// - 类型和方法也往往需要追溯到所属文件；
/// - 没有文件节点，图的结构根会很虚。
/// </summary>
public sealed class BuildFileNodesPass : CpgPass
{
    /// <summary>
    /// 初始化文件节点创建 pass。
    /// </summary>
    /// <param name="filePaths">需要进入图的源码文件路径。</param>
    public BuildFileNodesPass(IEnumerable<string> filePaths)
    {
        FilePaths = filePaths?.ToArray() ?? throw new ArgumentNullException(nameof(filePaths));
    }

    /// <summary>
    /// 获取本次要进入图的文件路径集合。
    /// </summary>
    public IReadOnlyList<string> FilePaths { get; }

    /// <inheritdoc />
    protected override void Execute(CpgGraphBuilder builder)
    {
        foreach (string filePath in FilePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            bool exists = builder.Graph
                .GetNodes(CpgNodeKind.File)
                .Any(node => node.TryGetProperty<string>("FileName", out string? current) &&
                             string.Equals(current, filePath, StringComparison.Ordinal));

            if (exists)
            {
                continue;
            }

            CpgNode fileNode = builder.CreateNode(CpgNodeKind.File);
            fileNode.SetProperty("Name", Path.GetFileName(filePath));
            fileNode.SetProperty("FileName", filePath);
            fileNode.SetProperty("FullName", filePath);
        }
    }
}
