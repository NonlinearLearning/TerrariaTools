using Domain.Analysis;
using Domain.Marking;
using Domain.Rewrite;
using Domain.Workspaces;
using Infrastructure.Analysis;
using Infrastructure.Persistence;
using Infrastructure.Roslyn;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.DependencyInjection;

/// <summary>
/// 注册四层实现所需服务。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Isolation 分析模块。
    /// </summary>
    public static IServiceCollection AddIsolationAnalysis(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IWorkspaceContextRepository, InMemoryWorkspaceContextRepository>();
        services.AddSingleton<IAnalysisSnapshotRepository, InMemoryAnalysisSnapshotRepository>();
        services.AddSingleton<IRuleTargetRepository, InMemoryRuleTargetRepository>();
        services.AddSingleton<IAnalysisSnapshotFactory, DefaultAnalysisSnapshotBuilder>();
        services.AddSingleton<ICodeIsolationGateway, RoslynCodeIsolationGateway>();

        return services;
    }
}
