using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;
using Xunit;

namespace TerrariaTools.Dome.Tests.Analysis;

/// <summary>
/// 测试 WorkspaceLoadCoordinator 的工作区加载协调功能。
/// </summary>
public class WorkspaceLoadCoordinatorTests
{
    /// <summary>
    /// 测试当输入为单个 .cs 文件时，使用 SourceOnly 加载器。
    /// </summary>
    [Fact]
    public async Task LoadAsync_UsesSourceOnlyForSingleCsFile()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-loader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var inputFile = Path.Combine(tempRoot, "Sample.cs");
            await File.WriteAllTextAsync(inputFile, "class Sample { }");

            var sourceLoader = new FakeWorkspaceLoader(_ => Task.FromResult(WorkspaceLoadResult.Success(
                new[] { new SourceDocument(inputFile, "Sample.cs", "class Sample { }") },
                WorkspaceLoadMode.SourceOnly,
                "SourceOnly")));
            var codeLoader = new FakeWorkspaceLoader(_ => Task.FromResult(WorkspaceLoadResult.Failure(
                WorkspaceLoadMode.CodeAnalysis,
                "CodeAnalysis",
                new[]
                {
                    new WorkspaceLoadDiagnostic("CodeAnalysisLoad", WorkspaceLoadDiagnosticSeverity.Error, "Should not be called.")
                })));

            var coordinator = new WorkspaceLoadCoordinator(codeLoader, sourceLoader);
            var result = await coordinator.LoadAsync(inputFile, WorkspaceLoadOptions.Default, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(WorkspaceLoadMode.SourceOnly, result.LoadMode);
            Assert.False(result.FallbackUsed);
            Assert.IsType<SourceOnlyAnalysisInput>(result.AnalysisInput);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// 测试当 CodeAnalysis 加载失败时，回退到 SourceOnly 加载器。
    /// </summary>
    [Fact]
    public async Task LoadAsync_FallsBackToSourceOnlyWhenCodeAnalysisFails()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-loader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var projectPath = Path.Combine(tempRoot, "Sample.csproj");
            var sourceFile = Path.Combine(tempRoot, "Sample.cs");
            await File.WriteAllTextAsync(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            await File.WriteAllTextAsync(sourceFile, "class Sample { }");

            var codeLoader = new FakeWorkspaceLoader(_ => Task.FromResult(WorkspaceLoadResult.Failure(
                WorkspaceLoadMode.CodeAnalysis,
                "CodeAnalysis",
                new[]
                {
                    new WorkspaceLoadDiagnostic("CodeAnalysisLoad", WorkspaceLoadDiagnosticSeverity.Error, "MSBuild load failed.")
                })));
            var sourceLoader = new FakeWorkspaceLoader(path => Task.FromResult(WorkspaceLoadResult.Success(
                new[] { new SourceDocument(sourceFile, "Sample.cs", "class Sample { }") },
                WorkspaceLoadMode.SourceOnly,
                "SourceOnly")));

            var coordinator = new WorkspaceLoadCoordinator(codeLoader, sourceLoader);
            var result = await coordinator.LoadAsync(projectPath, WorkspaceLoadOptions.Default, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(WorkspaceLoadMode.CodeAnalysisFallbackToSourceOnly, result.LoadMode);
            Assert.True(result.FallbackUsed);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Stage == "CodeAnalysisLoad");
            Assert.IsType<SourceOnlyAnalysisInput>(result.AnalysisInput);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// 测试当禁用回退时，CodeAnalysis 加载失败会导致整体失败。
    /// </summary>
    [Fact]
    public async Task LoadAsync_FailsWhenFallbackIsDisabled()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-loader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var projectPath = Path.Combine(tempRoot, "Sample.csproj");
            await File.WriteAllTextAsync(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

            var codeLoader = new FakeWorkspaceLoader(_ => Task.FromResult(WorkspaceLoadResult.Failure(
                WorkspaceLoadMode.CodeAnalysis,
                "CodeAnalysis",
                new[]
                {
                    new WorkspaceLoadDiagnostic("CodeAnalysisLoad", WorkspaceLoadDiagnosticSeverity.Error, "MSBuild load failed.")
                })));
            var sourceLoader = new FakeWorkspaceLoader(_ => throw new InvalidOperationException("Source fallback should not run."));

            var coordinator = new WorkspaceLoadCoordinator(codeLoader, sourceLoader);
            var result = await coordinator.LoadAsync(
                projectPath,
                new WorkspaceLoadOptions(WorkspaceLoaderPreference.CodeAnalysisFirst, false),
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(WorkspaceLoadMode.CodeAnalysis, result.LoadMode);
            Assert.False(result.FallbackUsed);
            Assert.Null(result.AnalysisInput);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// 用于测试的伪造工作区加载器。
    /// </summary>
    /// <param name="handler">处理加载请求的委托。</param>
    private sealed class FakeWorkspaceLoader(Func<string, Task<WorkspaceLoadResult>> handler) : IWorkspaceLoader
    {
        /// <inheritdoc />
        public Task<WorkspaceLoadResult> LoadAsync(string inputPath, WorkspaceLoadOptions options, CancellationToken cancellationToken)
        {
            return handler(inputPath);
        }
    }
}
