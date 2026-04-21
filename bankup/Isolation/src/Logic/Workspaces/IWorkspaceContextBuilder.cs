using Domain.Workspaces;

namespace Logic.Workspaces;

/// <summary>
/// 定义工作区上下文构造能力。
/// </summary>
public interface IWorkspaceContextBuilder
{
    /// <summary>
    /// 构造工作区上下文。
    /// </summary>
    /// <param name="input">构造输入。</param>
    /// <returns>工作区上下文聚合根。</returns>
    WorkspaceContext Build(WorkspaceContextBuildInput input);
}
