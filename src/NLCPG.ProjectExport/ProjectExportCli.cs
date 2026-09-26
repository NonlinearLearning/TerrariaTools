using NLCPG.ProjectJson;

namespace NLCPG.ProjectExport;

/// <summary>
/// <c>nlcpg-project-export</c> 的薄入口：解析 <c>nlissn.yml</c>、调用导出组件、打印结果摘要。
/// 导出实现位于 <c>NLCPG.ProjectJson</c> 组件（<c>src/NLISSN.Infrastructure/ProjectJson</c>）。
/// </summary>
public sealed class ProjectExportCli
{
    public Task<int> RunFromWorkingDirectory()
    {
        return RunAsync(Array.Empty<string>());
    }

    public async Task<int> RunAsync(IReadOnlyList<string> args)
    {
        if (args.Count != 0)
        {
            throw new ArgumentException(
              "NLCPG.ProjectExport reads configuration from nlissn.yml and accepts no command-line parameters.");
        }

        try
        {
            var options = ProjectExportConfigurationLoader.LoadFromWorkingDirectory();
            var result = await new ProjectJsonExporter().ExportAsync(options);
            Console.WriteLine($"Manifest: {result.ManifestPath}");
            Console.WriteLine($"Status: {(result.Status == ExportStatus.Complete ? "complete" : "incomplete")}");
            Console.WriteLine($"Written files: {result.WrittenFileCount}");
            Console.WriteLine($"Failed files: {result.FailedFileCount}");
            foreach (var diagnostic in result.Diagnostics)
            {
                Console.Error.WriteLine(diagnostic);
            }

            return result.Succeeded ? 0 : 1;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (Exception exception)
        {
            // 致命失败（例如工作区无法加载、磁盘写满导致 manifest 写不出）以非零退出并给出原因，
            // 而不是让 CLR 以未处理异常终止、只留下转储。
            Console.Error.WriteLine($"NLCPG.ProjectExport failed: {exception.Message}");
            return 1;
        }
    }
}
