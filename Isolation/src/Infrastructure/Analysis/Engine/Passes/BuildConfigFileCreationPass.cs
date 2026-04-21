using Domain.Analysis.Engine.Core;
using Logic.Analysis.Engine.Passes;

namespace Infrastructure.Analysis.Engine.Passes;

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

    protected override void Execute(CpgGraphBuilder builder)
    {
        CpgNode? metaDataNode = builder.Graph.GetNodes(CpgNodeKind.MetaData).FirstOrDefault();
        if (metaDataNode is null ||
            !metaDataNode.TryGetProperty<string>("InputPath", out string? inputPath) ||
            string.IsNullOrWhiteSpace(inputPath))
        {
            return;
        }

        string rootPath = ConfigFileConventions.ResolveRootPath(inputPath);
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return;
        }

        foreach (string filePath in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            if (!ConfigFileConventions.IsConfigFile(filePath))
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
            ConfigFileNodeConventions.ApplyConfigFileProperties(
                configNode,
                filePath,
                File.ReadAllText(filePath),
                metaDataNode.Id);
        }
    }
}
