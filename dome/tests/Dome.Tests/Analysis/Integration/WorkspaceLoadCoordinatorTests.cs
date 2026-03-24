using TerrariaTools.Dome.Adapters.Analysis.Roslyn;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using PortsCommon = TerrariaTools.Dome.Application.Ports;
using Xunit;

namespace TerrariaTools.Dome.Tests.Analysis;

public class WorkspaceLoadCoordinatorLegacyTests
{
    [Fact]
    public async Task LoadAsync_UsesSourceOnlyForSingleCsFile()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-loader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var inputFile = Path.Combine(tempRoot, "Sample.cs");
            await File.WriteAllTextAsync(inputFile, "class Sample { }");

            var sourceLoader = new FakeWorkspaceLoader(_ => Task.FromResult(ApplicationAbstractions.WorkspaceLoadResult.Success(
                new ModelAnalysis.AnalysisInput(
                    new ModelAnalysis.SourceDocumentSet(inputFile, tempRoot, [new ModelAnalysis.SourceDocument(inputFile, "Sample.cs", "class Sample { }")]),
                    ModelAnalysis.AnalysisInputMode.SourceOnly),
                PortsCommon.WorkspaceLoadMode.SourceOnly,
                "SourceOnly")));
            var codeLoader = new FakeWorkspaceLoader(_ => Task.FromResult(ApplicationAbstractions.WorkspaceLoadResult.Failure(
                PortsCommon.WorkspaceLoadMode.CodeAnalysis,
                "CodeAnalysis",
                [new ApplicationAbstractions.WorkspaceLoadDiagnostic("CodeAnalysisLoad", PortsCommon.WorkspaceLoadDiagnosticSeverity.Error, "Should not be called.")])));

            var coordinator = new WorkspaceLoadCoordinator(codeLoader, sourceLoader);
            var result = await coordinator.LoadAsync(inputFile, ApplicationAbstractions.WorkspaceLoadOptions.Default, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(PortsCommon.WorkspaceLoadMode.SourceOnly, result.LoadMode);
            Assert.False(result.FallbackUsed);
            Assert.NotNull(result.Input?.SourceSet);
            Assert.Single(result.Documents);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

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

            var codeLoader = new FakeWorkspaceLoader(_ => Task.FromResult(ApplicationAbstractions.WorkspaceLoadResult.Failure(
                PortsCommon.WorkspaceLoadMode.CodeAnalysis,
                "CodeAnalysis",
                [new ApplicationAbstractions.WorkspaceLoadDiagnostic("CodeAnalysisLoad", PortsCommon.WorkspaceLoadDiagnosticSeverity.Error, "MSBuild load failed.")])));
            var sourceLoader = new FakeWorkspaceLoader(path => Task.FromResult(ApplicationAbstractions.WorkspaceLoadResult.Success(
                new ModelAnalysis.AnalysisInput(
                    new ModelAnalysis.SourceDocumentSet(path, tempRoot, [new ModelAnalysis.SourceDocument(sourceFile, "Sample.cs", "class Sample { }")]),
                    ModelAnalysis.AnalysisInputMode.SourceOnly),
                PortsCommon.WorkspaceLoadMode.SourceOnly,
                "SourceOnly")));

            var coordinator = new WorkspaceLoadCoordinator(codeLoader, sourceLoader);
            var result = await coordinator.LoadAsync(projectPath, ApplicationAbstractions.WorkspaceLoadOptions.Default, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(PortsCommon.WorkspaceLoadMode.CodeAnalysisFallbackToSourceOnly, result.LoadMode);
            Assert.True(result.FallbackUsed);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Stage == "CodeAnalysisLoad");
            Assert.NotNull(result.Input?.SourceSet);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LoadAsync_FailsWhenFallbackIsDisabled()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-loader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var projectPath = Path.Combine(tempRoot, "Sample.csproj");
            await File.WriteAllTextAsync(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

            var codeLoader = new FakeWorkspaceLoader(_ => Task.FromResult(ApplicationAbstractions.WorkspaceLoadResult.Failure(
                PortsCommon.WorkspaceLoadMode.CodeAnalysis,
                "CodeAnalysis",
                [new ApplicationAbstractions.WorkspaceLoadDiagnostic("CodeAnalysisLoad", PortsCommon.WorkspaceLoadDiagnosticSeverity.Error, "MSBuild load failed.")])));
            var sourceLoader = new FakeWorkspaceLoader(_ => throw new InvalidOperationException("Source fallback should not run."));

            var coordinator = new WorkspaceLoadCoordinator(codeLoader, sourceLoader);
            var result = await coordinator.LoadAsync(
                projectPath,
                new ApplicationAbstractions.WorkspaceLoadOptions(ApplicationAbstractions.WorkspaceLoaderPreference.CodeAnalysisFirst, false),
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(PortsCommon.WorkspaceLoadMode.CodeAnalysis, result.LoadMode);
            Assert.False(result.FallbackUsed);
            Assert.Null(result.Input?.SourceSet);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private sealed class FakeWorkspaceLoader(Func<string, Task<ApplicationAbstractions.WorkspaceLoadResult>> handler) : ApplicationAbstractions.IWorkspaceLoader
    {
        public Task<ApplicationAbstractions.WorkspaceLoadResult> LoadAsync(string inputPath, ApplicationAbstractions.WorkspaceLoadOptions options, CancellationToken cancellationToken) =>
            handler(inputPath);
    }
}
