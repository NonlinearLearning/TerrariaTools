using System.Text.Json;
using System.Reflection;
using NLCPG.ProjectExport;
using NLCPG.ProjectJson;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class NLCPGProjectExportTests
{
    [Fact]
    public void ProjectExportCli_ExposesUnifiedYamlEntryPoint()
    {
        var cliType = typeof(ProjectExportCli).Assembly.GetType(
          "NLCPG.ProjectExport.ProjectExportCli");

        Assert.NotNull(cliType);
        Assert.NotNull(cliType!.GetMethod(
          "RunFromWorkingDirectory",
          BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public async Task ProjectExportCli_LoadsUnifiedYamlConfiguration()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDirectory);
        var outputPath = Path.Combine(tempDirectory, "Build", "NLCPG-json");
        var projectPath = FixturePath("App", "App.csproj").Replace('\\', '/');
        File.WriteAllText(
          Path.Combine(tempDirectory, "nlissn.yml"),
          $"""
          schemaVersion: 3
          tool: nlcpg-project-export
          input:
            path: {projectPath}
            targetFramework: net10.0
            configuration: Debug
            platform: AnyCPU
            restore: disabled
            generatedSources: exclude
          projectExport:
            output: ./Build/NLCPG-json
            projectWorkerCount: 1
            resume: false
          """);

        var originalDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(tempDirectory);
            var exitCode = await new ProjectExportCli().RunFromWorkingDirectory();

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(Path.Combine(outputPath, "manifest.json")));
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            await DeleteDirectoryWithRetryAsync(tempDirectory);
        }
    }

    [Fact]
    public async Task ProjectExportCli_RejectsCommandLineArguments()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
          new ProjectExportCli().RunAsync(new[] { "--project", "App.csproj" }));
    }

    [Fact]
    public async Task ProjectExportCli_RejectsConfigurationForAnotherTool()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDirectory);
        await File.WriteAllTextAsync(
          Path.Combine(tempDirectory, "nlissn.yml"),
          """
          schemaVersion: 3
          tool: nlissn
          """);

        var originalDirectory = Directory.GetCurrentDirectory();
        var originalError = Console.Error;
        try
        {
            Directory.SetCurrentDirectory(tempDirectory);
            Console.SetError(TextWriter.Null);
            var exitCode = await new ProjectExportCli().RunFromWorkingDirectory();

            Assert.Equal(2, exitCode);
        }
        finally
        {
            Console.SetError(originalError);
            Directory.SetCurrentDirectory(originalDirectory);
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ExportOptions_DefaultToProjectWorkerPoolWithLocalBuilderDopOne()
    {
        var options = new ProjectExportOptions("sample.csproj");

        Assert.Equal(12, options.MaxDegreeOfParallelism);
        Assert.Equal(12, options.ProjectWorkerCount);
        Assert.Equal(1, options.LocalBuilderDegreeOfParallelism);
        Assert.Equal(3, new ProjectExportOptions("sample.csproj", MaxDegreeOfParallelism: 3).MaxDegreeOfParallelism);
        Assert.Equal(3, new ProjectExportOptions("sample.csproj", MaxDegreeOfParallelism: 3).ProjectWorkerCount);
    }

    [Fact]
    public async Task ExportProject_WritesMirroredFileAndGlobalManifestCatalog()
    {
        var projectPath = FixturePath("App", "App.csproj");
        var outputPath = Path.Combine(
          Path.GetTempPath(),
          "nlcpg-project-export-tests",
          Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputPath);

        try
        {
            var result = await new ProjectJsonExporter().ExportAsync(
              new ProjectExportOptions(
                projectPath,
                outputPath,
                TargetFramework: "net10.0"));

            Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));

            var sourcePayloadPath = Path.Combine(outputPath, "App.cs.json");
            var manifestPath = Path.Combine(outputPath, "manifest.json");
            Assert.True(File.Exists(sourcePayloadPath));
            Assert.True(File.Exists(manifestPath));

            using var sourcePayload = JsonDocument.Parse(await File.ReadAllTextAsync(sourcePayloadPath));
            Assert.Equal(1, sourcePayload.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("App.cs", sourcePayload.RootElement.GetProperty("sourcePath").GetString());
            Assert.NotEmpty(sourcePayload.RootElement.GetProperty("nodes").EnumerateArray());
            Assert.NotEmpty(sourcePayload.RootElement.GetProperty("edges").EnumerateArray());
            Assert.All(
              sourcePayload.RootElement.GetProperty("nodes").EnumerateArray(),
              node => Assert.False(node.TryGetProperty("text", out _)));

            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
            Assert.Equal(1, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Contains(
              manifest.RootElement.GetProperty("files").EnumerateArray(),
              file => file.GetProperty("outputPath").GetString() == "App.cs.json");

            var nodeIds = manifest.RootElement.GetProperty("nodes")
              .EnumerateArray()
              .Select(node => node.GetProperty("id").GetString())
              .Where(id => id is not null)
              .ToHashSet(StringComparer.Ordinal);
            Assert.NotEmpty(nodeIds);
            foreach (var edge in manifest.RootElement.GetProperty("edges").EnumerateArray())
            {
                Assert.Contains(edge.GetProperty("source").GetString(), nodeIds);
                Assert.Contains(edge.GetProperty("target").GetString(), nodeIds);
            }
        }
        finally
        {
            Directory.Delete(outputPath, recursive: true);
        }
    }

    [Fact]
    public async Task ExportProject_ResumeReusesExistingPayloadAndWritesManifest()
    {
        var projectPath = FixturePath("App", "App.csproj");
        var outputPath = Path.Combine(
          Path.GetTempPath(),
          "nlcpg-project-export-resume-tests",
          Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputPath);

        try
        {
            var existingPayload = """
                {
                  "schemaVersion": 1,
                  "sourcePath": "App.cs",
                  "nodes": [
                    {
                      "id": "node-a",
                      "localNodeId": 1,
                      "kind": "Method",
                      "displayKind": "Method",
                      "name": "A",
                      "fullName": "App.A",
                      "signature": "void A()",
                      "dispatchKind": null,
                      "typeFullName": "App",
                      "filePath": "App.cs",
                      "spanStart": 0,
                      "spanEnd": 1,
                      "isImplicit": false
                    },
                    {
                      "id": "node-b",
                      "localNodeId": 2,
                      "kind": "Method",
                      "displayKind": "Method",
                      "name": "B",
                      "fullName": "App.B",
                      "signature": "void B()",
                      "dispatchKind": null,
                      "typeFullName": "App",
                      "filePath": "App.cs",
                      "spanStart": 2,
                      "spanEnd": 3,
                      "isImplicit": false
                    }
                  ],
                  "edges": [
                    {
                      "source": "node-a",
                      "target": "node-b",
                      "kind": "Call",
                      "label": null,
                      "contextId": null,
                      "callSite": null,
                      "isCrossFile": false
                    }
                  ]
                }
                """;
            await File.WriteAllTextAsync(
              Path.Combine(outputPath, "App.cs.json"),
              existingPayload);

            var result = await new ProjectJsonExporter().ExportAsync(
              new ProjectExportOptions(
                projectPath,
                outputPath,
                TargetFramework: "net10.0",
                ResumeExistingOutput: true));

            Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));

            var manifestPath = Path.Combine(outputPath, "manifest.json");
            Assert.True(File.Exists(manifestPath));
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
            var appFile = Assert.Single(
              manifest.RootElement.GetProperty("files").EnumerateArray(),
              file => file.GetProperty("sourcePath").GetString() == "App.cs");
            Assert.Equal("written", appFile.GetProperty("status").GetString());
            Assert.Equal(2, appFile.GetProperty("nodeCount").GetInt32());
            Assert.Equal(1, appFile.GetProperty("edgeCount").GetInt32());

            var nodeIds = manifest.RootElement.GetProperty("nodes")
              .EnumerateArray()
              .Select(node => node.GetProperty("id").GetString())
              .Where(id => id is not null)
              .ToHashSet(StringComparer.Ordinal);
            Assert.Contains("node-a", nodeIds);
            Assert.Contains("node-b", nodeIds);
            var edge = Assert.Single(
              manifest.RootElement.GetProperty("edges").EnumerateArray(),
              candidate => candidate.GetProperty("source").GetString() == "node-a" &&
                candidate.GetProperty("target").GetString() == "node-b");
            Assert.Equal("App.cs", Assert.Single(edge.GetProperty("files").EnumerateArray()).GetString());
        }
        finally
        {
            Directory.Delete(outputPath, recursive: true);
        }
    }

    // .sln 输入：每个项目一个输出子目录，manifest 汇总全部项目且标注 complete。
    [Fact]
    public async Task ExportProject_SolutionInput_MirrorsPerProjectDirectoriesAndWritesProjectEntries()
    {
        var solutionPath = FixturePath("WorkspaceFixture.sln");
        var outputPath = Path.Combine(
          Path.GetTempPath(),
          "nlcpg-project-export-solution-tests",
          Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputPath);

        try
        {
            var result = await new ProjectJsonExporter().ExportAsync(
              new ProjectExportOptions(
                solutionPath,
                outputPath,
                Configuration: "Debug"));

            Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
            Assert.Equal(ExportStatus.Complete, result.Status);
            Assert.Equal(0, result.FailedFileCount);

            // 子目录名即项目名，其下镜像该项目源目录。
            Assert.True(File.Exists(Path.Combine(outputPath, "App", "App.cs.json")));
            Assert.True(File.Exists(Path.Combine(outputPath, "Library", "Library.cs.json")));

            using var manifest = JsonDocument.Parse(
              await File.ReadAllTextAsync(Path.Combine(outputPath, "manifest.json")));
            var root = manifest.RootElement;
            Assert.Equal("complete", root.GetProperty("status").GetString());

            var projects = root.GetProperty("projects").EnumerateArray().ToArray();
            Assert.Equal(3, projects.Length);
            foreach (var project in projects)
            {
                var name = project.GetProperty("name").GetString();
                Assert.False(string.IsNullOrEmpty(name));
                Assert.Equal(name, project.GetProperty("outputDirectory").GetString());
                Assert.Equal("complete", project.GetProperty("status").GetString());
                Assert.True(project.GetProperty("totalDocumentCount").GetInt32() > 0);
            }

            // 顶层计数与项目维度一致，且每个 payload 路径都落在项目子目录下。
            var totalDocuments = projects.Sum(p => p.GetProperty("totalDocumentCount").GetInt32());
            Assert.Equal(totalDocuments, root.GetProperty("totalDocuments").GetInt32());
            Assert.Equal(totalDocuments, root.GetProperty("writtenFiles").GetInt32());
            Assert.All(
              root.GetProperty("files").EnumerateArray(),
              file =>
              {
                  var outputRelativePath = file.GetProperty("outputPath").GetString()!;
                  Assert.Contains("/", outputRelativePath, StringComparison.Ordinal);
                  Assert.True(
                    File.Exists(Path.Combine(outputPath, outputRelativePath.Replace('/', Path.DirectorySeparatorChar))),
                    outputRelativePath);
              });

            // 顶层节点/边是跨项目并集，边端点必须都出现在节点集合中。
            var nodeIds = root.GetProperty("nodes")
              .EnumerateArray()
              .Select(node => node.GetProperty("id").GetString())
              .Where(id => id is not null)
              .ToHashSet(StringComparer.Ordinal);
            Assert.NotEmpty(nodeIds);
            Assert.NotEmpty(root.GetProperty("edges").EnumerateArray());
            foreach (var edge in root.GetProperty("edges").EnumerateArray())
            {
                Assert.Contains(edge.GetProperty("source").GetString(), nodeIds);
                Assert.Contains(edge.GetProperty("target").GetString(), nodeIds);
            }
        }
        finally
        {
            await DeleteDirectoryWithRetryAsync(outputPath);
        }
    }

    // 个别文档失败只降级为 incomplete，manifest 仍写出并保留失败项，导出不中断。
    [Fact]
    public async Task ExportProject_DocumentFailure_ReportsIncompleteStatusAndKeepsManifest()
    {
        var projectPath = FixturePath("App", "App.csproj");
        var outputPath = Path.Combine(
          Path.GetTempPath(),
          "nlcpg-project-export-incomplete-tests",
          Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputPath);

        try
        {
            // 打开 generatedSources 以纳入项目根之外的生成文件，制造可继续的失败项。
            var result = await new ProjectJsonExporter().ExportAsync(
              new ProjectExportOptions(
                projectPath,
                outputPath,
                TargetFramework: "net10.0",
                IncludeGenerated: true));

            Assert.True(result.Status == ExportStatus.Incomplete);
            Assert.True(result.FailedFileCount > 0);
            Assert.True(result.WrittenFileCount > 0);

            var manifestPath = Path.Combine(outputPath, "manifest.json");
            Assert.True(File.Exists(manifestPath));
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
            var root = manifest.RootElement;
            Assert.Equal("incomplete", root.GetProperty("status").GetString());
            Assert.Equal(result.FailedFileCount, root.GetProperty("failedFiles").GetInt32());
            Assert.Equal("incomplete", root.GetProperty("project").GetProperty("status").GetString());
            Assert.Contains(
              root.GetProperty("files").EnumerateArray(),
              file => file.GetProperty("status").GetString() == "failed");
        }
        finally
        {
            await DeleteDirectoryWithRetryAsync(outputPath);
        }
    }

    private static string FixturePath(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "global.json")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return Path.Combine(
          current!.FullName,
          "tests",
          "NLISSN.Testing",
          "TestCodeSet",
          "Workspace",
          Path.Combine(parts));
    }

    private static async Task DeleteDirectoryWithRetryAsync(string path)
    {
        for (var attempt = 0; attempt < 20; attempt += 1)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < 19)
            {
                await Task.Delay(100);
            }
            catch (IOException)
            {
                return;
            }
        }
    }
}
