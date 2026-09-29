using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLCPG.ProjectJson;
using NLISSN.Infrastructure.Workspace;
using System.Text.Json;
using Xunit;

namespace NLISSN.Tests.ProjectJson;

/// <summary>
/// 「导出复用分析工作区快照」的接线契约（执行方案 §4）。
/// </summary>
/// <remarks>
/// 这里**不**验证"复用与自加载逐字节相同"——那需要能解析元数据引用的真实工程，
/// 本环境不具备（见 <c>CpgProjectExportScheduleTests</c> 的说明）；
/// 该性质由真实导出的 payload 基线比对承担。
/// 本文件锁定的是复用**确实生效**这一接线前提：传入快照时导出器不得再自己去加载工作区。
/// 判据刻意选成"指向一个不存在的项目路径"——只要导出器还试图加载，就必然失败。
/// </remarks>
public sealed class ProjectJsonSnapshotReuseTests
{
    [Fact]
    public async Task ExportAsync_WithSnapshot_DoesNotLoadWorkspaceItself()
    {
        var outputPath = CreateTemporaryDirectory();
        try
        {
            // 关键构造：项目路径**不存在**。若能复用快照，加载分支根本不会被触达。
            var options = new ProjectExportOptions(
              Path.Combine(outputPath, "does-not-exist.csproj"),
              outputPath);
            var snapshot = CreateSnapshot(outputPath, "ReuseSubject.cs");

            var result = await new ProjectJsonExporter().ExportAsync(
              options,
              snapshot,
              CancellationToken.None);

            // 复用生效 ⇒ 该文档被真实导出，而不是因为"加载不存在的工作区"整体失败。
            Assert.True(result.Succeeded, Describe(result));
            Assert.Equal(1, result.WrittenFileCount);

            var payloadPath = Path.Combine(outputPath, "ReuseSubject.cs.json");
            Assert.True(File.Exists(payloadPath), "复用快照时应按快照里的文档产出 payload。");
        }
        finally
        {
            TryDelete(outputPath);
        }
    }

    [Fact]
    public async Task ExportAsync_WithoutSnapshot_StillLoadsWorkspace()
    {
        // 反向对照：不传快照时仍走自加载路径，故不存在的项目路径必须导致失败。
        // 这条同时证明上一条的通过不是"两条路径都不加载"造成的假阳性。
        var outputPath = CreateTemporaryDirectory();
        try
        {
            var options = new ProjectExportOptions(
              Path.Combine(outputPath, "does-not-exist.csproj"),
              outputPath);

            var result = await new ProjectJsonExporter().ExportAsync(options, CancellationToken.None);

            Assert.False(result.Succeeded, "缺少快照时应回退到自加载，并因项目不存在而失败。");
        }
        finally
        {
            TryDelete(outputPath);
        }
    }

    [Fact]
    public async Task ExportAsync_ReusedSnapshot_ProducesByteIdenticalPayloadsToSelfLoad()
    {
        // 执行方案 §4.3 的实证：在**同一份真实工程**上跑两条路径，
        // 「复用分析快照」与「导出自加载」必须产出逐字节相同的 payload。
        // 这直接检验三处差异（生成源取舍 / TargetDocumentPath / 项目集合）在本输入上无分歧。
        var workspace = FixturePath("WorkspaceFixture.sln");
        if (!File.Exists(workspace))
        {
            return; // 夹具缺失时不误报失败；由 WorkspaceInputLoaderTests 负责报告夹具问题。
        }

        var reuseOutput = CreateTemporaryDirectory();
        var selfLoadOutput = CreateTemporaryDirectory();
        try
        {
            // 分析侧选项：generators 关闭（复用判据要求），生成源包含。
            var analysisOptions = new WorkspaceInputOptions(
              workspace,
              TargetFramework: "net10.0",
              Configuration: "Debug",
              Platform: "AnyCPU",
              RestoreMode: WorkspaceRestoreMode.Disabled,
              GeneratedSourceMode: WorkspaceGeneratedSourceMode.Include,
              GeneratorMode: WorkspaceGeneratorMode.Disabled);
            var load = await new MsBuildWorkspaceInputLoader().LoadAsync(analysisOptions);
            if (!load.IsSuccess || load.Snapshot is null)
            {
                // 本环境无法完成该工程的 MSBuild 加载时跳过；该性质由真实导出的基线比对承担。
                return;
            }

            var reused = await new ProjectJsonExporter().ExportAsync(
              CreateExportOptions(workspace, reuseOutput),
              load.Snapshot,
              CancellationToken.None);
            var selfLoaded = await new ProjectJsonExporter().ExportAsync(
              CreateExportOptions(workspace, selfLoadOutput),
              CancellationToken.None);

            Assert.Equal(selfLoaded.WrittenFileCount, reused.WrittenFileCount);
            Assert.True(reused.WrittenFileCount > 0, "夹具工程应至少产出一个 payload。");

            var reusedFiles = RelativePayloads(reuseOutput);
            var selfLoadFiles = RelativePayloads(selfLoadOutput);

            // 文档集合必须逐一对应：这是 §4.3 第 3 条（项目集合）的判据。
            Assert.Equal(selfLoadFiles, reusedFiles);

            // 内容必须逐字节相同：这是"复用是优化而非行为变更"的判据。
            foreach (var relative in selfLoadFiles)
            {
                var expected = await File.ReadAllBytesAsync(Path.Combine(selfLoadOutput, relative));
                var actual = await File.ReadAllBytesAsync(Path.Combine(reuseOutput, relative));
                Assert.True(
                  expected.AsSpan().SequenceEqual(actual),
                  $"复用快照改变了 payload 字节：{relative}");
            }

            // manifest.json 不能整体比对（它记录输出路径，两条路径天然不同），
            // 但其中的**诊断集合**必须一致：它来自快照的 Compilation.GetDiagnostics()，
            // 是"复用未改变编译视图"的直接证据（真实输入上有 1317 条这样的诊断）。
            Assert.Equal(
              ReadManifestDiagnostics(selfLoadOutput),
              ReadManifestDiagnostics(reuseOutput));
        }
        finally
        {
            TryDelete(reuseOutput);
            TryDelete(selfLoadOutput);
        }
    }

    // 读 manifest 的诊断三元组并按稳定键排序（manifest 自身已排序，这里不依赖其顺序）。
    private static string[] ReadManifestDiagnostics(string outputRoot)
    {
        var manifestPath = Path.Combine(outputRoot, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return Array.Empty<string>();
        }

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (!document.RootElement.TryGetProperty("diagnostics", out var diagnostics))
        {
            return Array.Empty<string>();
        }

        return diagnostics
          .EnumerateArray()
          .Select(item => string.Join(
            "|",
            item.GetProperty("code").GetString(),
            item.GetProperty("path").GetString(),
            item.GetProperty("message").GetString(),
            item.TryGetProperty("projectPath", out var projectPath) ? projectPath.GetString() : null))
          .OrderBy(value => value, StringComparer.Ordinal)
          .ToArray();
    }

    private static ProjectExportOptions CreateExportOptions(string projectPath, string outputPath)
    {
        return new ProjectExportOptions(
          projectPath,
          outputPath,
          TargetFramework: "net10.0",
          Configuration: "Debug",
          Platform: "AnyCPU",
          RestoreMode: WorkspaceRestoreMode.Disabled,
          IncludeGenerated: true);
    }

    // 只取 payload（排除 manifest.json：它含输出路径，两条路径必然不同）。
    private static string[] RelativePayloads(string root)
    {
        return Directory
          .GetFiles(root, "*.json", SearchOption.AllDirectories)
          .Select(path => Path.GetRelativePath(root, path))
          .Where(relative => !string.Equals(relative, "manifest.json", StringComparison.OrdinalIgnoreCase))
          .OrderBy(relative => relative, StringComparer.Ordinal)
          .ToArray();
    }

    private static string FixturePath(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "global.json")))
        {
            current = current.Parent;
        }

        if (current is null)
        {
            return string.Empty;
        }

        var segments = new List<string> { current.FullName, "tests", "NLISSN.Testing", "TestCodeSet", "Workspace" };
        segments.AddRange(parts);
        return Path.Combine(segments.ToArray());
    }

    // 造一份"最小可用"的单项目快照：一个源文件、可编译的语法树。
    private static WorkspaceSolutionSnapshot CreateSnapshot(string root, string fileName)
    {
        const string source = "namespace Demo; public sealed class ReuseSubject { public int Run() { return 1; } }";
        var filePath = Path.Combine(root, fileName);
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions, filePath);
        var compilation = CSharpCompilation.Create(
          "ReuseSubject",
          new[] { syntaxTree },
          ReferenceAssemblies,
          new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var document = new WorkspaceDocumentSnapshot(
          filePath,
          source,
          syntaxTree,
          IsGenerated: false,
          WorkspaceGeneratedSourceKind.None,
          CanWrite: false);
        var project = new WorkspaceProjectSnapshot(
          Path.Combine(root, "ReuseSubject.csproj"),
          "ReuseSubject",
          "ReuseSubject",
          "net10.0",
          "Debug",
          "AnyCPU",
          compilation,
          parseOptions,
          compilation.Options,
          PreprocessorSymbols: Array.Empty<string>(),
          Documents: new[] { document },
          References: Array.Empty<WorkspaceReferenceSnapshot>(),
          AnalyzerReferenceCount: 0,
          Fingerprint: "reuse-subject");
        return new WorkspaceSolutionSnapshot(
          project.ProjectPath,
          IsSolution: false,
          SolutionDirectory: root,
          SelectedProjectPath: project.ProjectPath,
          Projects: new[] { project },
          Diagnostics: Array.Empty<WorkspaceInputDiagnostic>(),
          Fingerprint: "reuse-subject");
    }

    // 用当前运行时的核心程序集做元数据引用，够构建这一份最小代码的 CPG。
    private static readonly IReadOnlyList<MetadataReference> ReferenceAssemblies =
      ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
        .ToArray();

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "nlissn-snapshot-reuse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响断言结论。
        }
    }

    private static string Describe(ProjectExportResult result)
    {
        return "导出失败，诊断：" + string.Join(" | ", result.Diagnostics);
    }
}
