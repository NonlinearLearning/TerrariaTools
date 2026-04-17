using Analysis.Core;
using Analysis.Layers;

namespace Analysis.X2Cpg;

/// <summary>
/// 提供 x2cpg 前端入口的核心工具函数。
///
/// 对应 Joern `X2Cpg.scala`。本项目只实现核心 CPG 能力，因此不包含 scopt
/// 命令行解析、HTTP server 和图数据库存储，只保留建图、overlay、临时代码文件
/// 和字符串处理。
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

        return new CpgGraph();
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
    /// 对前端 CPG 应用 Joern 默认 overlay。
    /// </summary>
    /// <param name="graph">目标 CPG。</param>
    public static void ApplyDefaultOverlays(CpgGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        LayerPipeline pipeline = new(DefaultOverlayCreators());
        foreach (ILayerCreator layer in DefaultOverlayCreators())
        {
            pipeline.Apply(graph, layer.OverlayName);
        }
    }

    /// <summary>
    /// 返回 Joern 默认 overlay 列表。
    ///
    /// 顺序对齐 `X2Cpg.scala`：Base、ControlFlow、TypeRelations、CallGraph。
    /// 当前不包含 Dump/DOT 类 layer，因为它们不是本项目要求的核心 CPG 能力。
    /// </summary>
    /// <returns>默认 overlay 创建器。</returns>
    public static IReadOnlyList<ILayerCreator> DefaultOverlayCreators()
    {
        return new ILayerCreator[]
        {
            new BaseLayer(),
            new ControlFlowLayer(),
            new TypeRelationsLayer(),
            new CallGraphLayer(),
        };
    }

    /// <summary>
    /// 把源码写入临时目录中的临时文件。
    /// </summary>
    /// <param name="sourceCode">源码文本。</param>
    /// <param name="tmpDirPrefix">临时目录前缀。</param>
    /// <param name="suffix">临时文件后缀。</param>
    /// <returns>临时目录路径。行为对齐 Joern：返回目录而不是文件。</returns>
    public static DirectoryInfo WriteCodeToFile(string sourceCode, string tmpDirPrefix, string suffix)
    {
        ArgumentNullException.ThrowIfNull(sourceCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(tmpDirPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(suffix);

        string tmpDirPath = Path.Combine(Path.GetTempPath(), tmpDirPrefix + Guid.NewGuid().ToString("N"));
        DirectoryInfo tmpDir = Directory.CreateDirectory(tmpDirPath);
        string codeFile = Path.Combine(tmpDir.FullName, "Test" + suffix);
        File.WriteAllText(codeFile, sourceCode);
        return tmpDir;
    }

    /// <summary>
    /// 移除字符串首尾的单引号或双引号。
    /// </summary>
    /// <param name="value">目标字符串。</param>
    /// <returns>处理后的字符串。</returns>
    public static string StripQuotes(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.Trim('"', '\'');
    }
}
