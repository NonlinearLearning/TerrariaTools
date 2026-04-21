using Domain.Analysis.Engine.Core;
using Logic.Analysis.Engine.X2Cpg;

namespace Infrastructure.Analysis.Engine.X2Cpg;

/// <summary>
/// 提供 x2cpg 前端入口的核心工具函数。
///
/// 对应 Joern `X2Cpg.scala`。本项目只实现核心 CPG 能力，因此不包含 scopt
/// 命令行解析、HTTP server 和图数据库存储，只保留建图生命周期和临时代码文件适配。
/// </summary>
public static class X2Cpg
{
    /// <summary>
    /// 创建空的内存态 CPG。
    /// </summary>
    /// <param name="optionalOutputPath">保留 Joern 参数语义；当前内存实现不使用。</param>
    /// <returns>新的空图。</returns>
    public static CpgGraph NewEmptyCpg(string? optionalOutputPath = null)
    {
        if (!string.IsNullOrWhiteSpace(optionalOutputPath) && File.Exists(optionalOutputPath))
        {
            File.Delete(optionalOutputPath);
        }

        return X2CpgGraphFactory.CreateEmptyGraph();
    }

    /// <summary>
    /// 创建空 CPG 并运行调用方提供的建图逻辑。
    /// </summary>
    /// <param name="outputPath">输出路径。空字符串表示内存态。</param>
    /// <param name="config">前端配置。</param>
    /// <param name="applyPasses">在空图上运行的建图逻辑。</param>
    /// <returns>建图后的 CPG。</returns>
    public static CpgGraph WithNewEmptyCpg(
        string outputPath,
        X2CpgConfig config,
        Action<CpgGraph, X2CpgConfig> applyPasses)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(applyPasses);

        CpgGraph graph = NewEmptyCpg(string.IsNullOrWhiteSpace(outputPath) ? null : outputPath);
        applyPasses(graph, config);
        return graph;
    }

    /// <summary>
    /// 把源码写入临时目录中的临时文件。
    /// </summary>
    /// <param name="sourceCode">源码文本。</param>
    /// <param name="temporaryDirectoryPrefix">临时目录前缀。</param>
    /// <param name="suffix">临时文件后缀。</param>
    /// <returns>临时目录路径。行为对齐 Joern：返回目录而不是文件。</returns>
    public static DirectoryInfo WriteCodeToFile(string sourceCode, string temporaryDirectoryPrefix, string suffix)
    {
        ArgumentNullException.ThrowIfNull(sourceCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryDirectoryPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(suffix);

        string temporaryDirectoryPath = Path.Combine(
            Path.GetTempPath(),
            temporaryDirectoryPrefix + Guid.NewGuid().ToString("N"));
        DirectoryInfo temporaryDirectory = Directory.CreateDirectory(temporaryDirectoryPath);
        string codeFile = Path.Combine(temporaryDirectory.FullName, "Test" + suffix);
        File.WriteAllText(codeFile, sourceCode);
        return temporaryDirectory;
    }
}
