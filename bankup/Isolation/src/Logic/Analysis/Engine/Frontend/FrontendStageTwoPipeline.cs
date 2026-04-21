using Domain.Analysis.Engine.Core;
using Domain.Analysis.Engine.Model;
using Domain.Analysis.Engine.Semantic.Validation;
using Logic.Analysis.Engine.Passes;
using Logic.Analysis.Engine.Passes.ControlFlow;
using Logic.Analysis.Engine.Passes.ControlFlow.Dominance;
using Logic.Analysis.Engine.Passes.DataFlow;

namespace Logic.Analysis.Engine.Frontend;

/// <summary>
/// 提供前端阶段二图关系 pass 的纯编排。
/// </summary>
public static class FrontendStageTwoPipeline
{
    /// <summary>
    /// 运行前端阶段二 pass，并把校验结果计数写回元数据节点。
    /// </summary>
    /// <param name="graph">目标图。</param>
    /// <param name="imports">导入指令快照。</param>
    /// <param name="referencedTypeFullNames">引用类型全名集合。</param>
    /// <param name="externalMethodStubs">外部方法桩集合。</param>
    public static void Run(
        CpgGraph graph,
        IEnumerable<ImportDirectiveInfo> imports,
        IEnumerable<string> referencedTypeFullNames,
        IEnumerable<MethodStubDefinition> externalMethodStubs)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(imports);
        ArgumentNullException.ThrowIfNull(referencedTypeFullNames);
        ArgumentNullException.ThrowIfNull(externalMethodStubs);

        new BuildImportsPass(imports).Run(graph);
        new LinkAstPass().Run(graph);
        new BuildContainsEdgesPass().Run(graph);
        new BuildTypeNodePass().Run(graph);
        new BuildTypeStubPass(referencedTypeFullNames).Run(graph);
        new BuildInheritanceFullNamePass().Run(graph);
        new BuildMethodStubPass(externalMethodStubs).Run(graph);
        new BuildParameterIndexCompatPass().Run(graph);
        new BuildMethodDecoratorPass().Run(graph);
        new ResolveTypeRefsPass().Run(graph);
        new EvaluateNodeTypesPass().Run(graph);
        new BuildTypeHierarchyPass().Run(graph);
        new BuildAliasRelationPass().Run(graph);
        new BindIdentifierReferencePass().Run(graph);
        new BuildFieldAccessRelationPass().Run(graph);
        new BuildMethodReferencePass().Run(graph);
        new BuildImportResolverPass().Run(graph);
        new BuildTypeRecoveryPass().Run(graph);
        new BuildTypeHintCallLinkerPass().Run(graph);
        new BuildStaticCallGraphPass().Run(graph);
        new BuildDynamicCallGraphPass().Run(graph);
        new BuildDelegateCallGraphPass().Run(graph);
        new BuildCfgPass().Run(graph);
        new CfgDominatorPass().Run(graph);
        new BuildCdgPass().Run(graph);
        new BuildOssDataFlowPass().Run(graph);

        IReadOnlyList<ValidationViolation> violations = PostFrontendValidator.Validate(graph);
        CpgNode? metaDataNode = graph.GetNodes(CpgNodeKind.MetaData).FirstOrDefault();
        if (metaDataNode is not null)
        {
            metaDataNode.SetProperty("ValidationViolationCount", violations.Count);
        }
    }
}
