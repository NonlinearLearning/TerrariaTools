using Application.Abstractions;
using Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Application.DependencyInjection;

/// <summary>
/// 注册 Application 编排层服务。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Isolation 应用服务。
    /// </summary>
    public static IServiceCollection AddIsolationApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IWorkspaceContextAppService, WorkspaceContextAppService>();
        services.AddSingleton<IAnalysisAppService, AnalysisAppService>();
        services.AddSingleton<IAnalysisCpgAppService, AnalysisCpgAppService>();
        services.AddSingleton<IRuleTargetAppService, RuleTargetAppService>();
        services.AddSingleton<IPropagationAppService, PropagationAppService>();
        services.AddSingleton<IDecisionAppService, DecisionAppService>();
        services.AddSingleton<IRewriteWorkflowAppService, RewriteWorkflowAppService>();
        services.AddSingleton<ICodeIsolationAppService, CodeIsolationAppService>();
        return services;
    }
}
