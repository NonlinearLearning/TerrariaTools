using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.Json;
using NLCPG.Contracts;
using NLISSN.Application;
using NLISSN.Composition;
using NLCPG.Analysis.FlowSummaries;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;
using NLISSN.Core.Rewrite;
using NLISSN.Hosting;
using NLISSN.Infrastructure.Configuration;
using NLISSN.Rules;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class WorkspaceAnalysisHostTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
      Path.GetTempPath(),
      $"nlissn-workspace-host-{Guid.NewGuid():N}");

    public WorkspaceAnalysisHostTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void Analyze_SolutionUsesWorkspaceCompilationAndNeverWritesGeneratedSource()
    {
        var generatedPath = FixturePath("App", "Generated", "Generated.g.cs");
        var originalGeneratedSource = File.ReadAllText(generatedPath);
        var configurationPath = Path.Combine(_tempDirectory, "nlissn.yml");
        File.WriteAllText(configurationPath, $"""
          schemaVersion: 2
          runId: workspace-host
          input:
            path: '{FixturePath("WorkspaceFixture.sln")}'
            targetFramework: net10.0
            configuration: Debug
            platform: AnyCPU
            restore: disabled
            generatedSources: include
          analysis:
            deleteClass: GeneratedValue
          execution:
            writeBack: true
            skipRewrite: false
            maxDegreeOfParallelism: 1
          artifacts:
            root: artifacts
            diff:
              enabled: false
            runtimeLog:
              enabled: false
            evidence:
              enabled: false
            rewritePlan:
              mode: none
            analysisLog:
              enabled: false
          """);

        var configuration = YamlConfigurationLoader.Load(configurationPath);
        YamlConfigurationLoader.PrepareRunArtifacts(configuration);
        var result = new CommandHost(RulePipelineTestFactory.Create()).Analyze(configuration);

        Assert.Contains(result.Edits, edit => edit.FilePath.EndsWith("Generated.g.cs", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
          result.RewritePlans ?? Array.Empty<PrototypeFileRewritePlan>(),
          plan => plan.FilePath.EndsWith("Generated.g.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(originalGeneratedSource, File.ReadAllText(generatedPath));
    }

    [Fact]
    public void Analyze_SourceGeneratorOutputIsSemanticOnlyAndNeverWritesBack()
    {
        var generatedPath = FixturePath("App", "Generated", "GeneratedByDriver.g.cs");
        var configurationPath = Path.Combine(_tempDirectory, "generator-nlissn.yml");
        File.WriteAllText(configurationPath, $"""
          schemaVersion: 2
          runId: workspace-generator-host
          input:
            path: '{FixturePath("App", "App.csproj")}'
            targetFramework: net10.0
            configuration: Debug
            platform: AnyCPU
            restore: disabled
            generatedSources: include
            generators: enabled
          analysis:
            deleteClass: GeneratedByDriver
          execution:
            writeBack: true
            skipRewrite: false
            maxDegreeOfParallelism: 1
          artifacts:
            root: artifacts
            diff:
              enabled: false
            runtimeLog:
              enabled: false
            evidence:
              enabled: false
            rewritePlan:
              mode: none
            analysisLog:
              enabled: false
          """);

        var configuration = YamlConfigurationLoader.Load(configurationPath);
        YamlConfigurationLoader.PrepareRunArtifacts(configuration);
        var result = new CommandHost(RulePipelineTestFactory.Create()).Analyze(configuration);

        var generatedEdit = Assert.Single(result.Edits, edit =>
          edit.FilePath.EndsWith("GeneratedByDriver.g.cs", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(generatedPath));
        Assert.False(File.Exists(generatedEdit.FilePath));
        Assert.DoesNotContain(
          result.RewritePlans ?? Array.Empty<PrototypeFileRewritePlan>(),
          plan => plan.FilePath.EndsWith("GeneratedByDriver.g.cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_SourceDocumentUsesProjectCompilationForProjectReferenceSymbols()
    {
        var sourcePath = FixturePath("App", "App.cs");
        var projectPath = FixturePath("App", "App.csproj");
        var configurationPath = Path.Combine(_tempDirectory, "document-nlissn.yml");
        File.WriteAllText(configurationPath, $"""
          schemaVersion: 2
          runId: workspace-document-host
          input:
            path: '{sourcePath}'
            project: '{projectPath}'
            targetFramework: net10.0
            configuration: Debug
            platform: AnyCPU
            restore: disabled
          analysis:
            deleteClass: LibraryEntry
          execution:
            writeBack: false
            skipRewrite: true
            maxDegreeOfParallelism: 1
          artifacts:
            root: artifacts
            diff:
              enabled: false
            runtimeLog:
              enabled: false
            evidence:
              enabled: false
            rewritePlan:
              mode: capture
            analysisLog:
              enabled: false
          """);

        var configuration = YamlConfigurationLoader.Load(configurationPath);
        YamlConfigurationLoader.PrepareRunArtifacts(configuration);
        var result = new CommandHost(RulePipelineTestFactory.Create()).Analyze(configuration);

        Assert.NotEmpty(result.SeedMarks);
        Assert.Contains(
          result.SeedMarks,
          mark => mark.SyntaxNode.ToString().Contains("LibraryEntry", StringComparison.Ordinal));
        Assert.All(
          result.SeedMarks,
          mark => Assert.Equal(Path.GetFullPath(sourcePath), mark.SyntaxNode.SyntaxTree.FilePath));
        using var manifest = JsonDocument.Parse(
          File.ReadAllText(Path.Combine(
            configuration.Artifacts.RewritePlanRoot,
            "manifest.json")));
        Assert.Equal(1, manifest.RootElement.GetProperty("sourceFileCount").GetInt32());
    }

    [Fact]
    public void Analyze_SourceDocumentOutsideSelectedProjectFailsClosed()
    {
        var sourcePath = FixturePath("Library", "Library.cs");
        var projectPath = FixturePath("App", "App.csproj");
        var configurationPath = Path.Combine(_tempDirectory, "outside-document-nlissn.yml");
        File.WriteAllText(configurationPath, $"""
          schemaVersion: 2
          runId: workspace-outside-document-host
          input:
            path: '{sourcePath}'
            project: '{projectPath}'
            targetFramework: net10.0
            configuration: Debug
            platform: AnyCPU
            restore: disabled
          analysis:
            deleteClass: LibraryEntry
          execution:
            writeBack: false
            skipRewrite: true
            maxDegreeOfParallelism: 1
          artifacts:
            root: artifacts
          """);

        var configuration = YamlConfigurationLoader.Load(configurationPath);
        YamlConfigurationLoader.PrepareRunArtifacts(configuration);

        var exception = Assert.Throws<InvalidOperationException>(
          () => new CommandHost(RulePipelineTestFactory.Create()).Analyze(configuration));

        Assert.Contains("NLISSNWS025", exception.Message, StringComparison.Ordinal);
        Assert.Contains(Path.GetFullPath(sourcePath), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_CrossFileProjectHelperDoesNotUseFrameworkSummary()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "cross-file-summary");
        Directory.CreateDirectory(projectDirectory);
        var projectPath = Path.Combine(projectDirectory, "CrossFileSummary.csproj");
        var helperPath = Path.Combine(projectDirectory, "Helper.cs");
        var consumerPath = Path.Combine(projectDirectory, "Consumer.cs");
        var configurationPath = Path.Combine(_tempDirectory, "cross-file-summary.yml");
        File.WriteAllText(projectPath, """
          <Project Sdk="Microsoft.NET.Sdk">
            <PropertyGroup>
              <TargetFramework>net10.0</TargetFramework>
              <ImplicitUsings>enable</ImplicitUsings>
              <Nullable>enable</Nullable>
            </PropertyGroup>
          </Project>
          """);
        File.WriteAllText(helperPath, """
          namespace CrossFileSummary;

          public static class Helper
          {
              public static string Echo(string value) => value;
          }
          """);
        File.WriteAllText(consumerPath, """
          using CrossFileSummary;

          namespace CrossFileSummary;

          public sealed class Consumer
          {
              public string Run(string value) => Helper.Echo(value);
          }
          """);
        File.WriteAllText(configurationPath, $"""
          schemaVersion: 2
          runId: workspace-cross-file-summary
          input:
            path: '{projectPath}'
            targetFramework: net10.0
            configuration: Debug
            platform: AnyCPU
            restore: disabled
          analysis:
            deleteUnreferencedMethods: false
          execution:
            writeBack: false
            skipRewrite: true
            maxDegreeOfParallelism: 1
          artifacts:
            root: artifacts
            diff:
              enabled: false
            runtimeLog:
              enabled: false
            evidence:
              enabled: false
            rewritePlan:
              mode: none
            analysisLog:
              enabled: false
          """);

        var configuration = YamlConfigurationLoader.Load(configurationPath);
        YamlConfigurationLoader.PrepareRunArtifacts(configuration);
        var pipeline = RulePipelineTestFactory.Create();
        Assert.Contains(pipeline.Propagators, rule => rule is ExternalSummaryFlowPropagationRule);
        var testPipeline = pipeline with
        {
            Markers = pipeline.Markers
              .Append(new CrossFileInvocationSeedRule())
              .ToArray(),
        };
        var result = new CommandHost(testPipeline).Analyze(configuration);

        var helperSummaryFacts = result.PropagatedMarks
          .Select(mark => mark.Payload)
          .OfType<ExternalSummaryFlowPayload>()
          .Where(payload => payload.InvocationSyntax?.ToString() == "Helper.Echo(value)")
          .ToArray();

        Assert.NotEmpty(helperSummaryFacts);
        Assert.All(helperSummaryFacts, payload =>
        {
            Assert.Equal(FlowSummaryResolution.Unknown, payload.Flow.Resolution);
            Assert.False(payload.Flow.IsResolved);
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
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
        return Path.Combine(current!.FullName, "tests", "NLISSN.Testing", "TestCodeSet", "Workspace", Path.Combine(parts));
    }

    private sealed class CrossFileInvocationSeedRule : RuleDefinitionMark
    {
        public override string RuleId => "test.workspace.cross-file-invocation";

        public override string Name => "Mark the cross-file helper invocation";

        public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds =>
            new[] { SyntaxKind.InvocationExpression };

        public override RuleProducesContract Produces => new(new[]
        {
            new RuleProducedSyntax(
                new[] { SyntaxKind.InvocationExpression },
                RuleFactKind.TargetExpression),
        });

        public override IEnumerable<MarkRecord> Mark(IMarkRuleContext context, SyntaxNode root)
        {
            _ = context;
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                yield return new MarkRecord(
                    RuleId,
                    invocation,
                    null,
                    null,
                    "Cross-file helper invocation seed.");
            }
        }
    }
}
