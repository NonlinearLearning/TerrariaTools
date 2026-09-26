using NLCPG.Configuration;
using NLISSN.Infrastructure.Workspace;

namespace NLCPG.ProjectJson;

/// <summary>
/// 从统一 <c>nlissn.yml</c> 文档加载 <see cref="ProjectExportOptions"/>。
/// 供 <c>nlcpg-project-export</c> CLI 与 NLISSN 分析入口共用。
/// </summary>
public static class ProjectExportConfigurationLoader
{
    public const string ProjectExportToolName = "nlcpg-project-export";

    private static readonly IReadOnlySet<string> RootKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "schemaVersion", "tool", "runId", "input", "projectExport"
    };

    private static readonly IReadOnlySet<string> InputKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "path", "targetFramework", "configuration", "platform", "restore", "generatedSources"
    };

    private static readonly IReadOnlySet<string> ProjectExportKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "output", "projectWorkerCount", "resume"
    };

    public static ProjectExportOptions LoadFromWorkingDirectory(
      string expectedTool = ProjectExportToolName)
    {
        return Load(UnifiedYamlDocument.LoadFromWorkingDirectory(), expectedTool);
    }

    public static ProjectExportOptions Load(
      UnifiedYamlDocument document,
      string expectedTool = ProjectExportToolName)
    {
        RequireSchemaAndTool(document.Root, expectedTool);
        UnifiedYamlDocument.EnsureAllowed(document.Root, "configuration", RootKeys);

        var input = UnifiedYamlDocument.RequireMapping(document.Root, "input", "configuration");
        UnifiedYamlDocument.EnsureAllowed(input, "input", InputKeys);
        var projectPath = UnifiedYamlDocument.ResolvePath(
          document.Directory,
          UnifiedYamlDocument.RequireScalar(input, "path", "input"));
        if (!File.Exists(projectPath))
        {
            throw new ArgumentException($"Project file does not exist: {projectPath}");
        }

        var projectExport = UnifiedYamlDocument.RequireMapping(
          document.Root,
          "projectExport",
          "configuration");
        UnifiedYamlDocument.EnsureAllowed(projectExport, "projectExport", ProjectExportKeys);

        var targetFramework = UnifiedYamlDocument.GetScalar(input, "targetFramework", "input");
        var configuration = UnifiedYamlDocument.GetScalar(input, "configuration", "input") ?? "Debug";
        var platform = UnifiedYamlDocument.GetScalar(input, "platform", "input") ?? "AnyCPU";
        var restore = ParseRestore(UnifiedYamlDocument.GetScalar(input, "restore", "input"));
        var includeGenerated = string.Equals(
          UnifiedYamlDocument.GetScalar(input, "generatedSources", "input") ?? "exclude",
          "include",
          StringComparison.OrdinalIgnoreCase);
        var output = UnifiedYamlDocument.GetScalar(projectExport, "output", "projectExport");
        var outputPath = output is null
          ? null
          : UnifiedYamlDocument.ResolvePath(document.Directory, output);
        var workerCount = ParsePositiveInt(
          UnifiedYamlDocument.GetScalar(projectExport, "projectWorkerCount", "projectExport"),
          12,
          "projectExport.projectWorkerCount");
        var resume = ParseBoolean(
          UnifiedYamlDocument.GetScalar(projectExport, "resume", "projectExport"),
          false,
          "projectExport.resume");

        return new ProjectExportOptions(
          projectPath,
          outputPath,
          targetFramework,
          configuration,
          platform,
          restore,
          includeGenerated,
          workerCount,
          resume);
    }

    private static WorkspaceRestoreMode ParseRestore(string? value)
    {
        return (value ?? "disabled").ToLowerInvariant() switch
        {
            "disabled" => WorkspaceRestoreMode.Disabled,
            "enabled" => WorkspaceRestoreMode.Enabled,
            _ => throw new ArgumentException(
              "input.restore must be either 'disabled' or 'enabled'."),
        };
    }

    private static int ParsePositiveInt(string? value, int defaultValue, string path)
    {
        if (value is null)
        {
            return defaultValue;
        }

        var parsed = UnifiedYamlDocument.ParseInt(value, path);
        if (parsed <= 0)
        {
            throw new ArgumentException($"{path} must be a positive integer.");
        }

        return parsed;
    }

    private static bool ParseBoolean(string? value, bool defaultValue, string path)
    {
        return value is null
          ? defaultValue
          : UnifiedYamlDocument.ParseBoolean(value, path);
    }

    private static void RequireSchemaAndTool(
      YamlDotNet.RepresentationModel.YamlMappingNode root,
      string expectedTool)
    {
        var schemaVersion = UnifiedYamlDocument.RequireScalar(root, "schemaVersion", "configuration");
        if (UnifiedYamlDocument.ParseInt(schemaVersion, "schemaVersion") != 3)
        {
            throw new ArgumentException(
              "NLCPG.ProjectExport requires unified configuration schemaVersion: 3.");
        }

        var tool = UnifiedYamlDocument.RequireScalar(root, "tool", "configuration");
        if (!string.Equals(tool, expectedTool, StringComparison.Ordinal))
        {
            throw new ArgumentException(
              $"Configuration tool '{tool}' does not match NLCPG.ProjectExport; " +
              $"expected '{expectedTool}'.");
        }
    }
}
