namespace Logic.Workflow;

/// <summary>
/// 定义改写工作流产物构造能力。
/// </summary>
public interface IRewriteWorkflowArtifactAssembler
{
    /// <summary>
    /// 构造改写工作流产物。
    /// </summary>
    /// <param name="input">构造输入。</param>
    /// <returns>工作流产物。</returns>
    RewriteWorkflowArtifacts Assemble(RewriteWorkflowAssemblyInput input);
}
