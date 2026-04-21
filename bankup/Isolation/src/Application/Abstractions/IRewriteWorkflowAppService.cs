using Application.Contracts.Workflow;

namespace Application.Abstractions;

/// <summary>
/// 改写工作流应用服务。
/// </summary>
public interface IRewriteWorkflowAppService
{
    Task<RewriteWorkflowRunDto> RunAsync(
        RunRewriteWorkflowRequest request,
        CancellationToken cancellationToken = default);
}
