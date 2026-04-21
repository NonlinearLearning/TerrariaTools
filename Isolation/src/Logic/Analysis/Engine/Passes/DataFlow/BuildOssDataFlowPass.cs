using Domain.Analysis.Engine.Core;
using Domain.Analysis.Engine.Semantic;
using Domain.Analysis.Engine.Semantic.Flows;

namespace Logic.Analysis.Engine.Passes.DataFlow;

/// <summary>
/// 提供最小可用的 OSS 数据流 overlay。
///
/// 当前版本只做三件事：
/// - 计算 reaching definitions；
/// - 生成 `ReachingDef` 边；
/// - 在元数据中标记 `dataflowOss` overlay。
/// </summary>
public sealed class BuildOssDataFlowPass : CpgPass
{
    private readonly ISemantics semantics;

    /// <summary>
    /// 初始化默认数据流 pass。
    /// </summary>
    public BuildOssDataFlowPass()
        : this(new NoSemantics())
    {
    }

    /// <summary>
    /// 使用外部方法语义初始化数据流 pass。
    /// </summary>
    /// <param name="semantics">外部方法语义。</param>
    public BuildOssDataFlowPass(ISemantics semantics)
    {
        this.semantics = semantics ?? throw new ArgumentNullException(nameof(semantics));
    }

    /// <summary>
    /// 当前 overlay 名称。
    /// </summary>
    public const string OverlayName = "dataflowOss";


    protected override void Execute(CpgGraphBuilder builder)
    {
        new BuildReachingDefinitionsPass().Run(builder.Graph);
        new BuildDdgPass().Run(builder.Graph);
        new BuildSemanticDataFlowPass(semantics).Run(builder.Graph);

        if (!Overlays.AppliedOverlays(builder.Graph).Contains(OverlayName, StringComparer.Ordinal))
        {
            Overlays.AppendOverlayName(builder.Graph, OverlayName);
        }
    }
}
