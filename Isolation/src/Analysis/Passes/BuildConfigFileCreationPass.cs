using Analysis.Core;

namespace Analysis.Passes;

/// <summary>
/// 扫描源码根目录中的常见配置文件，并将其加入图中。
///
/// 这个 pass 参考 Joern `XConfigFileCreationPass.scala` 的职责，
/// 但当前只保留最小可用能力：
/// - 从 `MetaData.InputPath` 读取扫描根路径；
/// - 按扩展名与文件名过滤配置文件；
/// - 生成 `ConfigFile` 节点。
/// </summary>
public sealed class BuildConfigFileCreationPass : CpgPass
{
    private static readonly string[] SupportedExtensions =
    [
        ".json",
        ".yaml",
        ".yml",
        ".xml",
        ".config",
        ".props",
        ".targets",
    ];

    private static readonly string[] SupportedFileNames =
    [
        "appsettings.json",
        "nuget.config",
        "Directory.Build.props",
        "Directory.Build.targets",
    ];

    /// <inheritdoc />
    protected override void Execute(CpgGraphBuilder builder)
    {
        CpgNode? metaDataNode = builder.Graph.GetNodes(CpgNodeKind.MetaData).FirstOrDefault();
        if (metaDataNode is null ||
            !metaDataNode.TryGetProperty<string>("InputPath", out string? inputPath) ||
            string.IsNullOrWhiteSpace(inputPath))
        {
            return;
        }

        string rootPath = Directory.Exists(inputPath)
            ? inputPath
            : Path.GetDirectoryName(inputPath) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return;
        }

        foreach (string filePath in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            if (!IsConfigFile(filePath))
            {
                continue;
            }

            bool exists = builder.Graph
                .GetNodes(CpgNodeKind.ConfigFile)
                .Any(node => node.TryGetProperty<string>("FileName", out string? current) &&
                             string.Equals(current, filePath, StringComparison.Ordinal));
            if (exists)
            {
                continue;
            }

            CpgNode configNode = builder.CreateNode(CpgNodeKind.ConfigFile);
            configNode.SetProperty("Name", Path.GetFileName(filePath));
            configNode.SetProperty("FileName", filePath);
            configNode.SetProperty("FullName", filePath);
            configNode.SetProperty("Content", File.ReadAllText(filePath));
            configNode.SetProperty("AstParentId", metaDataNode.Id);
        }
    }

    private static bool IsConfigFile(string filePath)
    {
        string fileName = Path.GetFileName(filePath);
        if (SupportedFileNames.Any(supported =>
                string.Equals(fileName, supported, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        string extension = Path.GetExtension(filePath);
        return SupportedExtensions.Any(supported =>
            string.Equals(extension, supported, StringComparison.OrdinalIgnoreCase));
    }
}
