namespace NLISSN.Infrastructure.Configuration;

/// <summary>
/// NLISSN 运行内联的 ProjectJson 导出设置。对应 <c>artifacts.projectJson</c>。
/// 与独立 CLI（<c>nlcpg-project-export</c>）不同，这里复用 NLISSN 已解析的
/// <c>input</c>/工作区参数，因此不需要单独的 <c>tool:</c> 配置。
/// </summary>
internal sealed record ProjectExportSettings(
    bool Enabled,
    string OutputPath,
    int ProjectWorkerCount,
    bool ResumeExistingOutput);
