using Domain.Analysis.Engine.Core;
using Infrastructure.Analysis.Engine.Frontend;
using Domain.Analysis;
using Logic.Analysis;

namespace Infrastructure.Analysis;

/// <summary>
/// 通过既有 Analysis 模块构建领域 CPG 快照。
/// </summary>
public sealed class AnalysisBackedCpgGateway : IAnalysisCpgGateway
{
    private readonly IAnalysisCpgSnapshotAssembler analysisCpgSnapshotAssembler;

    /// <summary>
    /// 初始化基于 Analysis 的 CPG 网关。
    /// </summary>
    public AnalysisBackedCpgGateway(IAnalysisCpgSnapshotAssembler analysisCpgSnapshotAssembler)
    {
        this.analysisCpgSnapshotAssembler = analysisCpgSnapshotAssembler;
    }


    public async Task<AnalysisCpgSnapshot> BuildCpgSnapshotAsync(
        AnalysisInputDescriptor inputDescriptor,
        MinimumAnalysisTarget minimumTarget,
        string entrySymbol,
        int depth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputDescriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(entrySymbol);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
        ValidateSourceKind(inputDescriptor);

        RoslynCompilationContext context = await new RoslynProjectLoader()
            .LoadAsync(inputDescriptor.SourcePath, cancellationToken);
        CpgGraph graph = new();
        new DefaultRoslynCpgBuilder().Build(graph, context);

        return analysisCpgSnapshotAssembler.Assemble(
            graph,
            inputDescriptor,
            minimumTarget,
            entrySymbol,
            depth);
    }

    private static void ValidateSourceKind(AnalysisInputDescriptor inputDescriptor)
    {
        if (!AnalysisInputValidationRules.IsSourceKindMatched(inputDescriptor.SourceKind, inputDescriptor.SourcePath))
        {
            throw new InvalidOperationException(AnalysisInputValidationRules.BuildSourceKindMismatchMessage(
                inputDescriptor.SourceKind,
                inputDescriptor.SourcePath));
        }
    }

}
