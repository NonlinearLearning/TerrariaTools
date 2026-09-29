using NLISSN.Infrastructure.Configuration;
using NLISSN.Core.Rewrite;
using NLISSN.Hosting;
using System.Text.Json;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class YamlConfigurationLoaderTests : IDisposable
{
  private const string DuplicateKeyConfiguration = """
    schemaVersion: 2
    runId: duplicate-key
    input: { path: Input.cs }
    analysis: {}
    execution: { directoryMaxDegreeOfParallelism: 1, cpgMaxDegreeOfParallelism: 1, groupMaxDegreeOfParallelism: 1, helperMaxDegreeOfParallelism: 1, replayMaxDegreeOfParallelism: 1, maxConcurrentOperations: 1 }
    artifacts: {}
    runId: duplicate-key-again
    """;

  private readonly string _tempDirectory = Path.Combine(
    Path.GetTempPath(),
    $"nlissn-configuration-tests-{Guid.NewGuid():N}");

  public YamlConfigurationLoaderTests()
  {
    Directory.CreateDirectory(_tempDirectory);
  }

  [Fact]
  public void Load_CompleteConfiguration_MapsOptionsAndArtifactDirectories()
  {
    var sourcePath = Path.Combine(_tempDirectory, "Input.cs");
    var configurationPath = Path.Combine(_tempDirectory, "nlissn.yml");
    File.WriteAllText(sourcePath, "public sealed class Input { }");
    File.WriteAllText(
      configurationPath,
      """
      schemaVersion: 2
      runId: fixture-1
      input:
        path: Input.cs
      analysis:
        targetName: target
        deleteClass: Target
        disabledRuleTypes:
          - RuleOne
        validateBindings: true
        deleteUnreachableMethods: true
        deleteUnreferencedMethods: true
        clearUnusedInterfaceImplementations: true
        privatizeInternalOnlyPublicMethods: true
      execution:
        writeBack: false
        skipRewrite: false
        directoryMaxDegreeOfParallelism: 2
        cpgMaxDegreeOfParallelism: 1
        groupMaxDegreeOfParallelism: 2
        helperMaxDegreeOfParallelism: 2
        replayMaxDegreeOfParallelism: 2
        maxConcurrentOperations: 2
        directoryParallelism: false
        groupParallelism: true
        helperParallelism: false
        fastDeleteClassDirectory: true
        filterDeleteClassFilesByTargetName: true
      artifacts:
        root: artifacts
        diff:
          enabled: true
          view: readable
        runtimeLog:
          enabled: true
        evidence:
          enabled: true
        rewritePlan:
          mode: capture
        analysisLog:
          enabled: false
      logging:
        profile: benchmark
        level: debug
        categories:
          - run
        events:
          - completed
        view: normal
      """);

    var configuration = YamlConfigurationLoader.Load(configurationPath);

    Assert.Equal(Path.GetFullPath(sourcePath), configuration.InputPath);
    Assert.Equal("target", configuration.RulePolicy.TargetName);
    Assert.Equal("Target", configuration.RulePolicy.DeleteClass);
    Assert.Contains("RuleOne", configuration.RulePolicy.DisabledRuleTypes);
    Assert.True(configuration.RulePolicy.ValidateBindings);
    Assert.True(configuration.RulePolicy.DeleteUnreachableMethods);
    Assert.True(configuration.RulePolicy.DeleteUnreferencedMethods);
    Assert.True(configuration.RulePolicy.ClearUnusedInterfaceImplementations);
    Assert.True(configuration.RulePolicy.PrivatizeInternalOnlyPublicMethods);
    Assert.False(configuration.Execution.DirectoryParallelism);
    Assert.True(configuration.Execution.GroupParallelism);
    Assert.False(configuration.Execution.HelperParallelism);
    Assert.Equal("readable", configuration.Artifacts.DiffView);
    Assert.Equal(new[] { "run" }, configuration.Logging.Categories);
    Assert.Equal(new[] { "completed" }, configuration.Logging.Events);
    Assert.Equal(
      Path.Combine(_tempDirectory, "artifacts", "fixture-1", "Diff"),
      configuration.Artifacts.DiffRoot);
    Assert.Equal(
      Path.Combine(_tempDirectory, "artifacts", "fixture-1", "RuntimeLog", "runtime.log"),
      configuration.Artifacts.RuntimeLogPath);
    Assert.Equal(
      Path.Combine(_tempDirectory, "artifacts", "fixture-1", "Evidence", "evidence.json"),
      configuration.Artifacts.EvidencePath);
    Assert.Equal(RewritePlanMode.Capture, configuration.Artifacts.RewritePlanMode);
    Assert.False(configuration.Artifacts.WritePerformanceSummary);
    Assert.Equal("normal", configuration.Artifacts.PerformanceMode);
    Assert.Equal(
      Path.Combine(_tempDirectory, "artifacts", "fixture-1", "Performance", "summary.json"),
      configuration.Artifacts.PerformanceSummaryPath);
  }

  [Fact]
  public void Load_Schema3NlissnConfiguration_UsesUnifiedToolAndPreservesProvenance()
  {
    var sourcePath = Path.Combine(_tempDirectory, "Schema3Input.cs");
    var configurationPath = Path.Combine(_tempDirectory, "nlissn.yml");
    File.WriteAllText(sourcePath, "public sealed class Input { }");
    File.WriteAllText(
      configurationPath,
      """
      schemaVersion: 3
      tool: nlissn
      runId: schema-3
      input: { path: Schema3Input.cs }
      analysis: {}
      execution:
        directoryMaxDegreeOfParallelism: 1
        cpgMaxDegreeOfParallelism: 2
        groupMaxDegreeOfParallelism: 3
        helperMaxDegreeOfParallelism: 4
        replayMaxDegreeOfParallelism: 5
        maxConcurrentOperations: 6
      artifacts:
        root: artifacts
      """);

    var configuration = YamlConfigurationLoader.Load(configurationPath);

    Assert.Equal(Path.GetFullPath(sourcePath), configuration.InputPath);
    Assert.Equal(1, configuration.Execution.DirectoryMaxDegreeOfParallelism);
    Assert.Equal(2, configuration.Execution.CpgMaxDegreeOfParallelism);
    Assert.Equal(3, configuration.Execution.GroupMaxDegreeOfParallelism);
    Assert.Equal(4, configuration.Execution.HelperMaxDegreeOfParallelism);
    Assert.Equal(5, configuration.Execution.ReplayMaxDegreeOfParallelism);
    Assert.Equal(6, configuration.Execution.MaxConcurrentOperations);
    Assert.Equal(3, configuration.Provenance.SourceSchemaVersion);
    Assert.Equal(3, configuration.Provenance.TargetSchemaVersion);
  }

  [Fact]
  public void TryLoad_Schema3ForAnotherTool_RejectsConfiguration()
  {
    var sourcePath = Path.Combine(_tempDirectory, "MismatchedToolInput.cs");
    var configurationPath = Path.Combine(_tempDirectory, "nlissn.yml");
    File.WriteAllText(sourcePath, "public sealed class Input { }");
    File.WriteAllText(
      configurationPath,
      """
      schemaVersion: 3
      tool: nlcpg
      runId: mismatched-tool
      input: { path: MismatchedToolInput.cs }
      analysis: {}
      execution:
        directoryMaxDegreeOfParallelism: 1
        cpgMaxDegreeOfParallelism: 1
        groupMaxDegreeOfParallelism: 1
        helperMaxDegreeOfParallelism: 1
        replayMaxDegreeOfParallelism: 1
        maxConcurrentOperations: 1
      artifacts: {}
      """);

    var result = YamlConfigurationLoader.TryLoad(configurationPath);

    Assert.False(result.IsSuccess);
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "NLISSN103" && diagnostic.Path == "tool");
  }

  [Fact]
  public void Load_IndependentConcurrencySettings_MapsEachConfiguredLimit()
  {
    var sourcePath = Path.Combine(_tempDirectory, "Input-independent-concurrency.cs");
    var configurationPath = Path.Combine(_tempDirectory, "independent-concurrency.yml");
    File.WriteAllText(sourcePath, "public sealed class Input { }");
    File.WriteAllText(
      configurationPath,
      """
      schemaVersion: 2
      runId: independent-concurrency
      input:
        path: Input-independent-concurrency.cs
      analysis: {}
      execution:
        directoryMaxDegreeOfParallelism: 2
        cpgMaxDegreeOfParallelism: 3
        groupMaxDegreeOfParallelism: 4
        helperMaxDegreeOfParallelism: 5
        replayMaxDegreeOfParallelism: 6
        maxConcurrentOperations: 7
      artifacts: { root: artifacts }
      """);

    var configuration = YamlConfigurationLoader.Load(configurationPath);

    Assert.Equal(2, configuration.Execution.DirectoryMaxDegreeOfParallelism);
    Assert.Equal(3, configuration.Execution.CpgMaxDegreeOfParallelism);
    Assert.Equal(4, configuration.Execution.GroupMaxDegreeOfParallelism);
    Assert.Equal(5, configuration.Execution.HelperMaxDegreeOfParallelism);
    Assert.Equal(6, configuration.Execution.ReplayMaxDegreeOfParallelism);
    Assert.Equal(7, configuration.Execution.MaxConcurrentOperations);

    YamlConfigurationLoader.PrepareRunArtifacts(configuration);
    using var resolvedDocument = JsonDocument.Parse(
      File.ReadAllText(configuration.Artifacts.ResolvedConfigurationPath));
    var resolvedExecution = resolvedDocument.RootElement.GetProperty("execution");
    Assert.Equal(2, resolvedExecution.GetProperty("directoryMaxDegreeOfParallelism").GetInt32());
    Assert.Equal(3, resolvedExecution.GetProperty("cpgMaxDegreeOfParallelism").GetInt32());
    Assert.Equal(4, resolvedExecution.GetProperty("groupMaxDegreeOfParallelism").GetInt32());
    Assert.Equal(5, resolvedExecution.GetProperty("helperMaxDegreeOfParallelism").GetInt32());
    Assert.Equal(6, resolvedExecution.GetProperty("replayMaxDegreeOfParallelism").GetInt32());
    Assert.Equal(7, resolvedExecution.GetProperty("maxConcurrentOperations").GetInt32());
    Assert.False(resolvedExecution.TryGetProperty("maxDegreeOfParallelism", out _));
  }

  [Fact]
  public void TryLoad_LegacyGlobalConcurrencyField_IsRejected()
  {
    var sourcePath = Path.Combine(_tempDirectory, "Input.cs");
    var configurationPath = Path.Combine(_tempDirectory, "legacy-global-concurrency.yml");
    File.WriteAllText(sourcePath, "public sealed class Input { }");
    File.WriteAllText(
      configurationPath,
      """
      schemaVersion: 2
      runId: legacy-global-concurrency
      input: { path: Input.cs }
      analysis: {}
      execution:
        directoryMaxDegreeOfParallelism: 1
        cpgMaxDegreeOfParallelism: 1
        groupMaxDegreeOfParallelism: 1
        helperMaxDegreeOfParallelism: 1
        replayMaxDegreeOfParallelism: 1
        maxConcurrentOperations: 1
        maxDegreeOfParallelism: 1
      artifacts: { root: artifacts }
      """);

    var result = YamlConfigurationLoader.TryLoad(configurationPath);

    Assert.False(result.IsSuccess);
    var diagnostic = Assert.Single(result.Diagnostics);
    Assert.Equal("NLISSN002", diagnostic.Code);
    Assert.Contains("maxDegreeOfParallelism", diagnostic.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Load_PerformanceArtifact_IsIndependentAndChangesResolvedFingerprint()
  {
    var sourcePath = Path.Combine(_tempDirectory, "Input-performance.cs");
    var disabledPath = Path.Combine(_tempDirectory, "disabled.yml");
    var enabledPath = Path.Combine(_tempDirectory, "enabled.yml");
    File.WriteAllText(sourcePath, "public sealed class Input { }");
    var sourceFileName = Path.GetFileName(sourcePath);
    File.WriteAllText(disabledPath, string.Join(Environment.NewLine, new[]
    {
      "schemaVersion: 2",
      "runId: performance-disabled",
      $"input: {{ path: {sourceFileName} }}",
      "analysis: {}",
      "execution: { directoryMaxDegreeOfParallelism: 1, cpgMaxDegreeOfParallelism: 1, groupMaxDegreeOfParallelism: 1, helperMaxDegreeOfParallelism: 1, replayMaxDegreeOfParallelism: 1, maxConcurrentOperations: 1 }",
      "artifacts:",
      "  root: artifacts",
      "  runtimeLog:",
      "    enabled: true",
      string.Empty
    }));
    File.WriteAllText(enabledPath, string.Join(Environment.NewLine, new[]
    {
      "schemaVersion: 2",
      "runId: performance-enabled",
      $"input: {{ path: {sourceFileName} }}",
      "analysis: {}",
      "execution: { directoryMaxDegreeOfParallelism: 1, cpgMaxDegreeOfParallelism: 1, groupMaxDegreeOfParallelism: 1, helperMaxDegreeOfParallelism: 1, replayMaxDegreeOfParallelism: 1, maxConcurrentOperations: 1 }",
      "artifacts:",
      "  root: artifacts",
      "  performance:",
      "    enabled: true",
      "    mode: benchmark",
      string.Empty
    }));

    var disabled = YamlConfigurationLoader.Load(disabledPath);
    var enabled = YamlConfigurationLoader.Load(enabledPath);

    Assert.False(disabled.Artifacts.WritePerformanceSummary);
    Assert.True(disabled.Artifacts.WriteRuntimeLog);
    Assert.True(enabled.Artifacts.WritePerformanceSummary);
    Assert.Equal("benchmark", enabled.Artifacts.PerformanceMode);
    Assert.NotEqual(
      ResolvedConfigurationArtifact.Create(disabled).Fingerprint,
      ResolvedConfigurationArtifact.Create(enabled).Fingerprint);
  }

  [Fact]
  public void Load_AnalysisLogEnabled_RejectsUnsupportedWriter()
  {
    var sourcePath = Path.Combine(_tempDirectory, "Input.cs");
    var configurationPath = Path.Combine(_tempDirectory, "nlissn.yml");
    File.WriteAllText(sourcePath, "public sealed class Input { }");
    File.WriteAllText(
      configurationPath,
      """
      schemaVersion: 2
      runId: fixture-2
      input:
        path: Input.cs
      analysis: {}
      execution:
        directoryMaxDegreeOfParallelism: 1
        cpgMaxDegreeOfParallelism: 1
        groupMaxDegreeOfParallelism: 1
        helperMaxDegreeOfParallelism: 1
        replayMaxDegreeOfParallelism: 1
        maxConcurrentOperations: 1
      artifacts:
        analysisLog:
          enabled: true
      """);

    var exception = Assert.Throws<ConfigurationLoadException>(() => YamlConfigurationLoader.Load(configurationPath));

    Assert.Contains("analysisLog", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void PrepareRunArtifacts_NewRun_WritesResolvedConfigurationAndRejectsReuse()
  {
    var sourcePath = Path.Combine(_tempDirectory, "Input.cs");
    var configurationPath = Path.Combine(_tempDirectory, "nlissn.yml");
    File.WriteAllText(sourcePath, "public sealed class Input { }");
    File.WriteAllText(
      configurationPath,
      """
      schemaVersion: 2
      runId: fixture-3
      input:
        path: Input.cs
      analysis: {}
      execution:
        directoryMaxDegreeOfParallelism: 1
        cpgMaxDegreeOfParallelism: 1
        groupMaxDegreeOfParallelism: 1
        helperMaxDegreeOfParallelism: 1
        replayMaxDegreeOfParallelism: 1
        maxConcurrentOperations: 1
      artifacts:
        root: artifacts
      """);
    var configuration = YamlConfigurationLoader.Load(configurationPath);

    Assert.False(configuration.RulePolicy.ValidateBindings);
    Assert.Equal("schema-default", configuration.Provenance.FieldOrigins["analysis.validateBindings"]);

    YamlConfigurationLoader.PrepareRunArtifacts(configuration);

    Assert.True(File.Exists(configuration.Artifacts.ResolvedConfigurationPath));
    using var resolvedDocument = JsonDocument.Parse(File.ReadAllText(configuration.Artifacts.ResolvedConfigurationPath));
    var provenance = resolvedDocument.RootElement.GetProperty("provenance");
    Assert.Equal(2, provenance.GetProperty("sourceSchemaVersion").GetInt32());
    Assert.Equal("schema-default", provenance.GetProperty("fieldOrigins")
      .GetProperty("analysis.deleteUnreachableMethods").GetString());
    Assert.Matches("^[0-9A-F]{64}$", resolvedDocument.RootElement.GetProperty("fingerprint").GetString()!);
    Assert.Throws<ArgumentException>(() => YamlConfigurationLoader.PrepareRunArtifacts(configuration));
  }

  [Fact]
  public void TryLoad_IndependentInvalidFields_ReturnsOrderedDiagnostics()
  {
    var configurationPath = Path.Combine(_tempDirectory, "nlissn.yml");
    File.WriteAllText(
      configurationPath,
      """
      schemaVersion: 2
      runId: invalid run id
      input: { path: "" }
      analysis: { targetName: target }
      execution:
        directoryMaxDegreeOfParallelism: 0
        cpgMaxDegreeOfParallelism: 0
        groupMaxDegreeOfParallelism: 0
        helperMaxDegreeOfParallelism: 0
        replayMaxDegreeOfParallelism: 0
        maxConcurrentOperations: 0
        skipRewrite: true
      artifacts:
        root: ../outside
        diff: { view: unsupported }
        rewritePlan: { mode: replay }
      """);

    var result = YamlConfigurationLoader.TryLoad(configurationPath);

    Assert.False(result.IsSuccess);
    Assert.Equal(
      result.Diagnostics.OrderBy(diagnostic => diagnostic.Path, StringComparer.Ordinal)
        .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
        .Select(diagnostic => (diagnostic.Path, diagnostic.Code)),
      result.Diagnostics.Select(diagnostic => (diagnostic.Path, diagnostic.Code)));
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Path == "input.path" && diagnostic.Code == "NLISSN130");
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Path == "execution.directoryMaxDegreeOfParallelism" && diagnostic.Code == "NLISSN110");
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Path == "execution.cpgMaxDegreeOfParallelism" && diagnostic.Code == "NLISSN111");
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Path == "execution.groupMaxDegreeOfParallelism" && diagnostic.Code == "NLISSN114");
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Path == "execution.helperMaxDegreeOfParallelism" && diagnostic.Code == "NLISSN115");
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Path == "execution.replayMaxDegreeOfParallelism" && diagnostic.Code == "NLISSN116");
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Path == "execution.maxConcurrentOperations" && diagnostic.Code == "NLISSN117");
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Path == "artifacts.diff.view" && diagnostic.Code == "NLISSN120");
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Path == "artifacts.root" && diagnostic.Code == "NLISSN132");
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Path == "artifacts.rewritePlan.mode" && diagnostic.Code == "NLISSN113");
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Path == "runId" && diagnostic.Code == "NLISSN131");
    var exception = Assert.Throws<ConfigurationLoadException>(() => result.RequireConfiguration());
    Assert.Equal(result.Diagnostics, exception.Diagnostics);
  }

  [Theory]
  [InlineData("directoryMaxDegreeOfParallelism", "NLISSN110")]
  [InlineData("cpgMaxDegreeOfParallelism", "NLISSN111")]
  [InlineData("groupMaxDegreeOfParallelism", "NLISSN114")]
  [InlineData("helperMaxDegreeOfParallelism", "NLISSN115")]
  [InlineData("replayMaxDegreeOfParallelism", "NLISSN116")]
  [InlineData("maxConcurrentOperations", "NLISSN117")]
  public void TryLoad_MissingConcurrencyField_ReturnsIndependentDiagnostic(
    string omittedField,
    string expectedCode)
  {
    var sourcePath = Path.Combine(_tempDirectory, "Input.cs");
    var configurationPath = Path.Combine(_tempDirectory, $"missing-{omittedField}.yml");
    File.WriteAllText(sourcePath, "public sealed class Input { }");
    var fields = new[]
    {
      (Name: "directoryMaxDegreeOfParallelism", Value: 1),
      (Name: "cpgMaxDegreeOfParallelism", Value: 1),
      (Name: "groupMaxDegreeOfParallelism", Value: 1),
      (Name: "helperMaxDegreeOfParallelism", Value: 1),
      (Name: "replayMaxDegreeOfParallelism", Value: 1),
      (Name: "maxConcurrentOperations", Value: 1)
    };
    var executionFields = string.Join(
      Environment.NewLine,
      fields
        .Where(field => !string.Equals(field.Name, omittedField, StringComparison.Ordinal))
        .Select(field => $"  {field.Name}: {field.Value}"));
    File.WriteAllText(
      configurationPath,
      $"""
      schemaVersion: 2
      runId: missing-{omittedField}
      input:
        path: Input.cs
      analysis:
        disabledRuleTypes: []
      execution:
      {executionFields}
      artifacts:
        root: artifacts
      """);

    var result = YamlConfigurationLoader.TryLoad(configurationPath);

    Assert.Contains(
      result.Diagnostics,
      diagnostic => diagnostic.Path == $"execution.{omittedField}" && diagnostic.Code == expectedCode);
  }

  [Fact]
  public void TryLoad_LoggingWithoutWriter_UsesDistinctDiagnosticCode()
  {
    var sourcePath = Path.Combine(_tempDirectory, "Input.cs");
    var configurationPath = Path.Combine(_tempDirectory, "logging-without-writer.yml");
    File.WriteAllText(sourcePath, "public sealed class Input { }");
    File.WriteAllText(
      configurationPath,
      """
      schemaVersion: 2
      runId: logging-without-writer
      input: { path: Input.cs }
      analysis: {}
      execution:
        directoryMaxDegreeOfParallelism: 1
        cpgMaxDegreeOfParallelism: 1
        groupMaxDegreeOfParallelism: 1
        helperMaxDegreeOfParallelism: 1
        replayMaxDegreeOfParallelism: 1
        maxConcurrentOperations: 1
      artifacts:
        root: artifacts
        diff: { enabled: false }
        runtimeLog: { enabled: false }
        performance: { enabled: false, mode: normal }
        evidence: { enabled: false }
        rewritePlan: { mode: none }
        analysisLog: { enabled: false }
      logging:
        profile: normal
        level: info
        categories: []
        events: []
        view: normal
      """);

    var result = YamlConfigurationLoader.TryLoad(configurationPath);

    var diagnostic = Assert.Single(
      result.Diagnostics,
      item => item.Path == "logging");
    Assert.Equal("NLISSN118", diagnostic.Code);
  }

  [Fact]
  public void Load_ExplicitFalseRulePolicy_RecordsExplicitOriginAndStableFingerprint()
  {
    var sourcePath = Path.Combine(_tempDirectory, "Input.cs");
    File.WriteAllText(sourcePath, "public sealed class Input { }");
    var first = LoadConfiguration(
      "first-run",
      "Input.cs",
      "deleteUnreachableMethods: false",
      "writeBack: false",
      "mode: none");
    var second = LoadConfiguration(
      "second-run",
      "Input.cs",
      "deleteUnreachableMethods: false",
      "writeBack: false",
      "mode: none");

    Assert.False(first.RulePolicy.DeleteUnreachableMethods);
    Assert.Equal("explicit", first.Provenance.FieldOrigins["analysis.deleteUnreachableMethods"]);
    Assert.Equal(ResolvedConfigurationArtifact.Create(first).Fingerprint, ResolvedConfigurationArtifact.Create(second).Fingerprint);
  }

  [Fact]
  public void Analyze_DirectoryConfiguration_WritesEnabledArtifactsBelowOneRunRoot()
  {
    var sourceDirectory = Path.Combine(_tempDirectory, "source");
    var configurationPath = Path.Combine(_tempDirectory, "nlissn.yml");
    Directory.CreateDirectory(sourceDirectory);
    File.WriteAllText(
      Path.Combine(sourceDirectory, "PlayerInput.cs"),
      "namespace Demo; public sealed class PlayerInput { }");
    File.WriteAllText(
      configurationPath,
      """
      schemaVersion: 2
      runId: artifact-run
      input:
        path: source
      analysis:
        deleteClass: PlayerInput
      execution:
        directoryMaxDegreeOfParallelism: 1
        cpgMaxDegreeOfParallelism: 1
        groupMaxDegreeOfParallelism: 1
        helperMaxDegreeOfParallelism: 1
        replayMaxDegreeOfParallelism: 1
        maxConcurrentOperations: 1
      artifacts:
        root: artifacts
        diff:
          enabled: true
        runtimeLog:
          enabled: true
        evidence:
          enabled: true
        rewritePlan:
          mode: capture
      """);
    var configuration = YamlConfigurationLoader.Load(configurationPath);

    YamlConfigurationLoader.PrepareRunArtifacts(configuration);
    var result = new CommandHost(RulePipelineTestFactory.Create()).Analyze(configuration);

    Assert.NotEmpty(result.Edits);
    Assert.True(File.Exists(configuration.Artifacts.ResolvedConfigurationPath));
    Assert.NotEmpty(Directory.EnumerateFiles(configuration.Artifacts.DiffRoot, "*.diff", SearchOption.AllDirectories));
    Assert.True(File.Exists(configuration.Artifacts.RuntimeLogPath));
    Assert.All(
      File.ReadAllLines(configuration.Artifacts.RuntimeLogPath),
      line => Assert.Contains("run=artifact-run", line, StringComparison.Ordinal));
    Assert.True(File.Exists(configuration.Artifacts.EvidencePath));
    using var evidenceDocument = JsonDocument.Parse(File.ReadAllText(configuration.Artifacts.EvidencePath));
    var evidenceConfiguration = evidenceDocument.RootElement.GetProperty("configuration");
    Assert.Equal(ResolvedConfigurationArtifact.Create(configuration).Fingerprint,
      evidenceConfiguration.GetProperty("fingerprint").GetString());
    Assert.False(evidenceConfiguration.GetProperty("rulePolicy")
      .GetProperty("deleteUnreachableMethods").GetBoolean());
    Assert.True(File.Exists(Path.Combine(configuration.Artifacts.RewritePlanRoot, "manifest.json")));
    Assert.All(
      new[]
      {
        configuration.Artifacts.DiffRoot,
        configuration.Artifacts.RuntimeLogPath,
        configuration.Artifacts.EvidencePath,
        configuration.Artifacts.RewritePlanRoot
      },
      path => Assert.StartsWith(configuration.Artifacts.RunRoot, path, StringComparison.Ordinal));
  }

  [Fact]
  public void Load_InvalidArtifactRootOrUnknownProperty_RejectsConfiguration()
  {
    var sourcePath = Path.Combine(_tempDirectory, "Input.cs");
    var configurationPath = Path.Combine(_tempDirectory, "nlissn.yml");
    File.WriteAllText(sourcePath, "public sealed class Input { }");
    File.WriteAllText(
      configurationPath,
      """
      schemaVersion: 2
      runId: invalid-root
      input: { path: Input.cs }
      analysis: {}
      execution: { directoryMaxDegreeOfParallelism: 1, cpgMaxDegreeOfParallelism: 1, groupMaxDegreeOfParallelism: 1, helperMaxDegreeOfParallelism: 1, replayMaxDegreeOfParallelism: 1, maxConcurrentOperations: 1 }
      artifacts: { root: ../outside }
      """);

    var rootException = Assert.Throws<ConfigurationLoadException>(() => YamlConfigurationLoader.Load(configurationPath));
    Assert.Contains("artifacts.root", rootException.Message, StringComparison.Ordinal);

    File.WriteAllText(
      configurationPath,
      """
      schemaVersion: 2
      runId: unknown-property
      input: { path: Input.cs }
      analysis: {}
      execution: { directoryMaxDegreeOfParallelism: 1, cpgMaxDegreeOfParallelism: 1, groupMaxDegreeOfParallelism: 1, helperMaxDegreeOfParallelism: 1, replayMaxDegreeOfParallelism: 1, maxConcurrentOperations: 1 }
      artifacts: {}
      unexpected: true
      """);

    Assert.ThrowsAny<Exception>(() => YamlConfigurationLoader.Load(configurationPath));

    File.WriteAllText(configurationPath, DuplicateKeyConfiguration);

    var duplicateResult = YamlConfigurationLoader.TryLoad(configurationPath);

    Assert.False(duplicateResult.IsSuccess);
    Assert.Equal("NLISSN002", Assert.Single(duplicateResult.Diagnostics).Code);
  }

  [Theory]
  [InlineData("diff", "artifacts.diff")]
  [InlineData("runtimeLog", "artifacts.runtimeLog")]
  [InlineData("rewritePlan", "artifacts.rewritePlan")]
  public void TryLoad_ExplicitNullArtifactSection_ReturnsDiagnostic(
    string property,
    string expectedPath)
  {
    var sourcePath = Path.Combine(_tempDirectory, "Input.cs");
    var configurationPath = Path.Combine(_tempDirectory, "nlissn.yml");
    File.WriteAllText(sourcePath, "public sealed class Input { }");
    File.WriteAllText(
      configurationPath,
      $$"""
      schemaVersion: 2
      runId: null-artifact-section
      input: { path: Input.cs }
      analysis: {}
      execution: { directoryMaxDegreeOfParallelism: 1, cpgMaxDegreeOfParallelism: 1, groupMaxDegreeOfParallelism: 1, helperMaxDegreeOfParallelism: 1, replayMaxDegreeOfParallelism: 1, maxConcurrentOperations: 1 }
      artifacts:
        {{property}}: null
      """);

    var result = YamlConfigurationLoader.TryLoad(configurationPath);

    Assert.False(result.IsSuccess);
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Path == expectedPath);
  }

  [Fact]
  public void TryLoad_ExplicitNullLoggingCategories_ReturnsDiagnostic()
  {
    var sourcePath = Path.Combine(_tempDirectory, "Input.cs");
    var configurationPath = Path.Combine(_tempDirectory, "nlissn.yml");
    File.WriteAllText(sourcePath, "public sealed class Input { }");
    File.WriteAllText(
      configurationPath,
      """
      schemaVersion: 2
      runId: null-logging-categories
      input: { path: Input.cs }
      analysis: {}
      execution: { directoryMaxDegreeOfParallelism: 1, cpgMaxDegreeOfParallelism: 1, groupMaxDegreeOfParallelism: 1, helperMaxDegreeOfParallelism: 1, replayMaxDegreeOfParallelism: 1, maxConcurrentOperations: 1 }
      artifacts: {}
      logging:
        categories: null
      """);

    var result = YamlConfigurationLoader.TryLoad(configurationPath);

    Assert.False(result.IsSuccess);
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Path == "logging.categories");
  }

  [Fact]
  public void TryLoad_ExplicitNullLoggingSection_ReturnsDiagnostic()
  {
    var sourcePath = Path.Combine(_tempDirectory, "Input.cs");
    var configurationPath = Path.Combine(_tempDirectory, "nlissn.yml");
    File.WriteAllText(sourcePath, "public sealed class Input { }");
    File.WriteAllText(
      configurationPath,
      """
      schemaVersion: 2
      runId: null-logging-section
      input: { path: Input.cs }
      analysis: {}
      execution: { directoryMaxDegreeOfParallelism: 1, cpgMaxDegreeOfParallelism: 1, groupMaxDegreeOfParallelism: 1, helperMaxDegreeOfParallelism: 1, replayMaxDegreeOfParallelism: 1, maxConcurrentOperations: 1 }
      artifacts: {}
      logging: null
      """);

    var result = YamlConfigurationLoader.TryLoad(configurationPath);

    Assert.False(result.IsSuccess);
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Path == "logging");
  }

  [Fact]
  public async Task PrepareRunArtifacts_SameRunId_AllowsOnlyOneReservation()
  {
    var sourcePath = Path.Combine(_tempDirectory, "Input.cs");
    var configurationPath = Path.Combine(_tempDirectory, "nlissn.yml");
    File.WriteAllText(sourcePath, "public sealed class Input { }");
    File.WriteAllText(
      configurationPath,
      """
      schemaVersion: 2
      runId: concurrent-reservation
      input: { path: Input.cs }
      analysis: {}
      execution: { directoryMaxDegreeOfParallelism: 1, cpgMaxDegreeOfParallelism: 1, groupMaxDegreeOfParallelism: 1, helperMaxDegreeOfParallelism: 1, replayMaxDegreeOfParallelism: 1, maxConcurrentOperations: 1 }
      artifacts: { root: artifacts }
      """);
    var configuration = YamlConfigurationLoader.Load(configurationPath);
    using var start = new ManualResetEventSlim(false);
    var tasks = Enumerable.Range(0, 16)
      .Select(_ => Task.Run(() =>
      {
        start.Wait();
        try
        {
          YamlConfigurationLoader.PrepareRunArtifacts(configuration);
          return true;
        }
        catch (ArgumentException)
        {
          return false;
        }
      }))
      .ToArray();

    start.Set();
    var results = await Task.WhenAll(tasks);

    Assert.Equal(1, results.Count(result => result));
    Assert.Equal(15, results.Count(result => !result));
    Assert.True(File.Exists(configuration.Artifacts.ResolvedConfigurationPath));
    var artifactParent = Directory.GetParent(configuration.Artifacts.RunRoot)!.FullName;
    Assert.Empty(Directory.EnumerateDirectories(artifactParent, "concurrent-reservation.staging-*"));
  }

  [Fact]
  public void Analyze_FileConfiguration_WritesDiffAndEvidenceInsideRunRoot()
  {
    var sourcePath = Path.Combine(_tempDirectory, "PlayerInput.cs");
    File.WriteAllText(sourcePath, "namespace Demo; public sealed class PlayerInput { }");
    var configuration = LoadConfiguration(
      "file-run",
      "PlayerInput.cs",
      "deleteClass: PlayerInput",
      "writeBack: false",
      "mode: none");

    YamlConfigurationLoader.PrepareRunArtifacts(configuration);
    var result = new CommandHost(RulePipelineTestFactory.Create()).Analyze(configuration);

    Assert.NotEmpty(result.Edits);
    Assert.Equal(configuration.Artifacts.DiffRoot, result.DiffFilePath);
    Assert.NotEmpty(Directory.GetFiles(configuration.Artifacts.DiffRoot, "*.rewrite.diff", SearchOption.AllDirectories));
    Assert.True(File.Exists(configuration.Artifacts.EvidencePath));
  }

  [Fact]
  public void Analyze_DirectoryCaptureThenReplay_UsesSeparateRunRootsAndEquivalentDiffs()
  {
    var sourceDirectory = CreatePlayerInputDirectory("replay-source");
    var capture = LoadConfiguration(
      "capture-run",
      "replay-source",
      "deleteClass: PlayerInput",
      "writeBack: false",
      "mode: capture");
    YamlConfigurationLoader.PrepareRunArtifacts(capture);
    var host = new CommandHost(RulePipelineTestFactory.Create());
    var captured = host.Analyze(capture);

    var replay = LoadConfiguration(
      "replay-run",
      "replay-source",
      string.Empty,
      "writeBack: false",
      "mode: replay\nsourceRunId: capture-run");
    YamlConfigurationLoader.PrepareRunArtifacts(replay);
    var replayed = host.Analyze(replay);

    Assert.NotEmpty(captured.Edits);
    Assert.Equal(captured.Diff.ToString(), replayed.Diff.ToString());
    Assert.NotEqual(capture.Artifacts.RunRoot, replay.Artifacts.RunRoot);
    Assert.True(File.Exists(Path.Combine(capture.Artifacts.RewritePlanRoot, "manifest.json")));
    Assert.True(File.Exists(replay.Artifacts.EvidencePath));
    Assert.True(Directory.Exists(sourceDirectory));
  }

  [Fact]
  public void Analyze_DirectoryConfigurations_AreEquivalentAcrossDopOneTwoAndSixteen()
  {
    var inputDirectory = CreatePlayerInputDirectory("dop-source");
    var originalSources = Directory.EnumerateFiles(inputDirectory, "*.cs", SearchOption.AllDirectories)
      .ToDictionary(path => path, File.ReadAllText, StringComparer.Ordinal);
    var outcomes = new List<(
      PrototypeAnalysisResult Result,
      AnalysisConfiguration Configuration,
      string ResolvedConfiguration,
      string Evidence,
      string[] Sources,
      string[] Diffs)>();
    foreach (var degreeOfParallelism in new[] { 1, 2, 16 })
    {
      var configuration = LoadConfiguration(
        $"dop-{degreeOfParallelism}",
        Path.GetFileName(inputDirectory),
        "deleteClass: PlayerInput",
        "writeBack: true",
        "mode: none",
        degreeOfParallelism);
      YamlConfigurationLoader.PrepareRunArtifacts(configuration);
      var result = new CommandHost(RulePipelineTestFactory.Create()).Analyze(configuration);
      outcomes.Add((
        result,
        configuration,
        File.ReadAllText(configuration.Artifacts.ResolvedConfigurationPath),
        File.ReadAllText(configuration.Artifacts.EvidencePath),
        Directory.EnumerateFiles(inputDirectory, "*.cs", SearchOption.AllDirectories)
          .OrderBy(path => path, StringComparer.Ordinal)
          .Select(File.ReadAllText)
          .ToArray(),
        Directory.EnumerateFiles(configuration.Artifacts.DiffRoot, "*.diff", SearchOption.AllDirectories)
          .OrderBy(path => path, StringComparer.Ordinal)
          .Select(File.ReadAllText)
          .ToArray()));
      foreach (var (path, source) in originalSources)
      {
        File.WriteAllText(path, source);
      }
    }

    var baseline = outcomes[0];
    var fingerprints = outcomes
      .Select(outcome => GetFingerprint(outcome.ResolvedConfiguration))
      .ToArray();
    Assert.Equal(fingerprints.Length, fingerprints.Distinct(StringComparer.Ordinal).Count());
    Assert.All(outcomes.Skip(1), outcome =>
    {
      Assert.Equal(
        baseline.Result.Decisions.Select(CreateDecisionKey),
        outcome.Result.Decisions.Select(CreateDecisionKey));
      Assert.Equal(
        System.Text.Json.JsonSerializer.Serialize(baseline.Result.Evidence),
        System.Text.Json.JsonSerializer.Serialize(outcome.Result.Evidence));
      Assert.Equal(
        RemoveConfigurationProjection(baseline.Evidence),
        RemoveConfigurationProjection(outcome.Evidence));
      Assert.Equal(baseline.Sources, outcome.Sources);
      Assert.Equal(baseline.Diffs, outcome.Diffs);
    });
    Assert.All(outcomes, outcome => Assert.Equal(
      GetFingerprint(outcome.ResolvedConfiguration),
      GetEvidenceFingerprint(outcome.Evidence)));
  }

  public void Dispose()
  {
    if (Directory.Exists(_tempDirectory))
    {
      Directory.Delete(_tempDirectory, recursive: true);
    }
  }

  private AnalysisConfiguration LoadConfiguration(
    string runId,
    string inputPath,
    string analysisBody,
    string writeBack,
    string rewritePlanBody,
    int degreeOfParallelism = 1)
  {
    var configurationPath = Path.Combine(_tempDirectory, "nlissn.yml");
    var lines = new List<string>
    {
      "schemaVersion: 2",
      $"runId: {runId}",
      "input:",
      $"  path: {inputPath}",
      "analysis:",
      "execution:",
      $"  {writeBack}",
      $"  directoryMaxDegreeOfParallelism: {degreeOfParallelism}",
      $"  cpgMaxDegreeOfParallelism: {degreeOfParallelism}",
      $"  groupMaxDegreeOfParallelism: {degreeOfParallelism}",
      $"  helperMaxDegreeOfParallelism: {degreeOfParallelism}",
      $"  replayMaxDegreeOfParallelism: {degreeOfParallelism}",
      $"  maxConcurrentOperations: {degreeOfParallelism}",
      "artifacts:",
      "  root: artifacts",
      "  diff:",
      "    enabled: true",
      "  runtimeLog:",
      "    enabled: true",
      "  evidence:",
      "    enabled: true",
      "  rewritePlan:"
    };
    if (string.IsNullOrWhiteSpace(analysisBody))
    {
      lines[4] = "analysis: {}";
    }
    else
    {
      lines.InsertRange(5, analysisBody.Split('\n').Select(line => $"  {line}"));
    }

    lines.AddRange(rewritePlanBody.Split('\n').Select(line => $"    {line}"));
    File.WriteAllLines(configurationPath, lines);
    return YamlConfigurationLoader.Load(configurationPath);
  }

  [Fact]
  public void TryLoad_ProjectJsonEnabledWithDirectoryInput_FailsBeforeAnalysis()
  {
    var inputDirectory = CreatePlayerInputDirectory("projectjson-directory");
    var configurationPath = Path.Combine(_tempDirectory, "nlissn.yml");
    File.WriteAllText(
      configurationPath,
      $$"""
      schemaVersion: 3
      tool: nlissn
      runId: projectjson-directory
      input: { path: {{Path.GetFileName(inputDirectory)}} }
      analysis: {}
      execution: { directoryMaxDegreeOfParallelism: 1, cpgMaxDegreeOfParallelism: 1, groupMaxDegreeOfParallelism: 1, helperMaxDegreeOfParallelism: 1, replayMaxDegreeOfParallelism: 1, maxConcurrentOperations: 1 }
      artifacts:
        root: artifacts
        projectJson:
          enabled: true
      """);

    var result = YamlConfigurationLoader.TryLoad(configurationPath);

    Assert.False(result.IsSuccess);
    var diagnostic = Assert.Single(
      result.Diagnostics,
      item => item.Path == "artifacts.projectJson.enabled");
    Assert.Equal("NLISSN139", diagnostic.Code);
  }

  [Fact]
  public void TryLoad_ProjectJsonEnabledWithStandaloneSourceFile_FailsBeforeAnalysis()
  {
    var sourcePath = Path.Combine(_tempDirectory, "Standalone.cs");
    var configurationPath = Path.Combine(_tempDirectory, "nlissn.yml");
    File.WriteAllText(sourcePath, "public sealed class Standalone { }");
    File.WriteAllText(
      configurationPath,
      """
      schemaVersion: 3
      tool: nlissn
      runId: projectjson-standalone
      input: { path: Standalone.cs }
      analysis: {}
      execution: { directoryMaxDegreeOfParallelism: 1, cpgMaxDegreeOfParallelism: 1, groupMaxDegreeOfParallelism: 1, helperMaxDegreeOfParallelism: 1, replayMaxDegreeOfParallelism: 1, maxConcurrentOperations: 1 }
      artifacts:
        root: artifacts
        projectJson:
          enabled: true
      """);

    var result = YamlConfigurationLoader.TryLoad(configurationPath);

    Assert.False(result.IsSuccess);
    Assert.Contains(
      result.Diagnostics,
      item => item.Code == "NLISSN139" && item.Path == "artifacts.projectJson.enabled");
  }

  [Fact]
  public void TryLoad_ProjectJsonDisabledWithDirectoryInput_IsAccepted()
  {
    // 反向护栏：开关关闭时，目录输入必须照旧可跑，不能被上面的 fail-fast 误伤。
    var inputDirectory = CreatePlayerInputDirectory("projectjson-disabled");
    var configurationPath = Path.Combine(_tempDirectory, "nlissn.yml");
    File.WriteAllText(
      configurationPath,
      $$"""
      schemaVersion: 3
      tool: nlissn
      runId: projectjson-disabled
      input: { path: {{Path.GetFileName(inputDirectory)}} }
      analysis: {}
      execution: { directoryMaxDegreeOfParallelism: 1, cpgMaxDegreeOfParallelism: 1, groupMaxDegreeOfParallelism: 1, helperMaxDegreeOfParallelism: 1, replayMaxDegreeOfParallelism: 1, maxConcurrentOperations: 1 }
      artifacts:
        root: artifacts
        projectJson:
          enabled: false
      """);

    var configuration = YamlConfigurationLoader.Load(configurationPath);

    Assert.Null(configuration.ProjectExport);
    Assert.Null(configuration.Workspace);
  }

  [Fact]
  public void TryLoad_RemovedProjectJsonResumeKey_IsRejectedNotIgnored()
  {
    // resume 已删除；旧配置里的 resume: false 必须硬报错，不能被静默忽略——
    // 否则用户会以为"每次全量重算"这句话被遵守了，而实际上旧键根本没被解析。
    var fixtureProject = FixturePath("App", "App.csproj");
    var configurationPath = Path.Combine(_tempDirectory, "nlissn.yml");
    File.WriteAllText(
      configurationPath,
      $$"""
      schemaVersion: 3
      tool: nlissn
      runId: projectjson-stale-resume
      input:
        path: '{{fixtureProject}}'
        targetFramework: net10.0
        configuration: Debug
        platform: AnyCPU
      analysis: {}
      execution: { directoryMaxDegreeOfParallelism: 1, cpgMaxDegreeOfParallelism: 1, groupMaxDegreeOfParallelism: 1, helperMaxDegreeOfParallelism: 1, replayMaxDegreeOfParallelism: 1, maxConcurrentOperations: 1 }
      artifacts:
        root: artifacts
        projectJson:
          enabled: true
          resume: false
      """);

    var result = YamlConfigurationLoader.TryLoad(configurationPath);

    Assert.False(result.IsSuccess);
  }

  [Fact]
  public void Load_ProjectJsonEnabledWithProjectInput_MapsWorkerCountAndOutput()
  {
    var fixtureProject = FixturePath("App", "App.csproj");
    var configurationPath = Path.Combine(_tempDirectory, "nlissn.yml");
    File.WriteAllText(
      configurationPath,
      $$"""
      schemaVersion: 3
      tool: nlissn
      runId: projectjson-project
      input:
        path: '{{fixtureProject}}'
        targetFramework: net10.0
        configuration: Debug
        platform: AnyCPU
      analysis: {}
      execution: { directoryMaxDegreeOfParallelism: 1, cpgMaxDegreeOfParallelism: 1, groupMaxDegreeOfParallelism: 1, helperMaxDegreeOfParallelism: 1, replayMaxDegreeOfParallelism: 1, maxConcurrentOperations: 1 }
      artifacts:
        root: artifacts
        projectJson:
          enabled: true
          output: custom-out
          projectWorkerCount: 3
      """);

    var configuration = YamlConfigurationLoader.Load(configurationPath);

    var settings = Assert.IsType<ProjectExportSettings>(configuration.ProjectExport);
    Assert.True(settings.Enabled);
    Assert.Equal(3, settings.ProjectWorkerCount);
    Assert.Equal(
      Path.GetFullPath(Path.Combine(_tempDirectory, "custom-out")),
      settings.OutputPath);
  }

  [Fact]
  public void TryLoad_ProjectJsonExplicitNonPositiveWorkerCount_IsRejected()
  {
    // 显式写 0 不能被静默改写成默认 12（schema 声明 minimum: 1）。
    var fixtureProject = FixturePath("App", "App.csproj");
    var configurationPath = Path.Combine(_tempDirectory, "nlissn.yml");
    File.WriteAllText(
      configurationPath,
      $$"""
      schemaVersion: 3
      tool: nlissn
      runId: projectjson-zero-workers
      input:
        path: '{{fixtureProject}}'
        targetFramework: net10.0
        configuration: Debug
        platform: AnyCPU
      analysis: {}
      execution: { directoryMaxDegreeOfParallelism: 1, cpgMaxDegreeOfParallelism: 1, groupMaxDegreeOfParallelism: 1, helperMaxDegreeOfParallelism: 1, replayMaxDegreeOfParallelism: 1, maxConcurrentOperations: 1 }
      artifacts:
        root: artifacts
        projectJson:
          enabled: true
          projectWorkerCount: 0
      """);

    var result = YamlConfigurationLoader.TryLoad(configurationPath);

    Assert.False(result.IsSuccess);
    var diagnostic = Assert.Single(
      result.Diagnostics,
      item => item.Path == "artifacts.projectJson.projectWorkerCount");
    Assert.Equal("NLISSN119", diagnostic.Code);
  }

  [Fact]
  public void Load_ProjectJsonOmittedWorkerCount_DefaultsWithoutDiagnostic()
  {
    // 未写该字段时必须仍走默认值，不能被 NLISSN119 误伤。
    var fixtureProject = FixturePath("App", "App.csproj");
    var configurationPath = Path.Combine(_tempDirectory, "nlissn.yml");
    File.WriteAllText(
      configurationPath,
      $$"""
      schemaVersion: 3
      tool: nlissn
      runId: projectjson-default-workers
      input:
        path: '{{fixtureProject}}'
        targetFramework: net10.0
        configuration: Debug
        platform: AnyCPU
      analysis: {}
      execution: { directoryMaxDegreeOfParallelism: 1, cpgMaxDegreeOfParallelism: 1, groupMaxDegreeOfParallelism: 1, helperMaxDegreeOfParallelism: 1, replayMaxDegreeOfParallelism: 1, maxConcurrentOperations: 1 }
      artifacts:
        root: artifacts
        projectJson:
          enabled: true
      """);

    var configuration = YamlConfigurationLoader.Load(configurationPath);

    var settings = Assert.IsType<ProjectExportSettings>(configuration.ProjectExport);
    Assert.Equal(12, settings.ProjectWorkerCount);
  }

  [Fact]
  public void Load_ProjectJsonRequestedCapabilities_ArePreservedInOrder()
  {
    var fixtureProject = FixturePath("App", "App.csproj");
    var configurationPath = Path.Combine(_tempDirectory, "nlissn.yml");
    File.WriteAllText(
      configurationPath,
      $$"""
      schemaVersion: 3
      tool: nlissn
      runId: projectjson-capabilities
      input:
        path: '{{fixtureProject}}'
        targetFramework: net10.0
        configuration: Debug
        platform: AnyCPU
      analysis: {}
      execution: { directoryMaxDegreeOfParallelism: 1, cpgMaxDegreeOfParallelism: 1, groupMaxDegreeOfParallelism: 1, helperMaxDegreeOfParallelism: 1, replayMaxDegreeOfParallelism: 1, maxConcurrentOperations: 1 }
      artifacts:
        root: artifacts
        projectJson:
          enabled: true
          requestedCapabilities: ["InterproceduralDataFlow"]
      """);

    var configuration = YamlConfigurationLoader.Load(configurationPath);

    var settings = Assert.IsType<ProjectExportSettings>(configuration.ProjectExport);
    Assert.Equal(new[] { "InterproceduralDataFlow" }, settings.EffectiveRequestedCapabilities);
  }

  [Fact]
  public void Load_ProjectJsonWithoutRequestedCapabilities_YieldsEmptySet()
  {
    // 未配置时必须为空集合（而不是 null 元素或默认能力名）：
    // 空集合才会让导出沿用 NLCPGCapability.Default，既有配置的产物因此不变。
    var fixtureProject = FixturePath("App", "App.csproj");
    var configurationPath = Path.Combine(_tempDirectory, "nlissn.yml");
    File.WriteAllText(
      configurationPath,
      $$"""
      schemaVersion: 3
      tool: nlissn
      runId: projectjson-no-capabilities
      input:
        path: '{{fixtureProject}}'
        targetFramework: net10.0
        configuration: Debug
        platform: AnyCPU
      analysis: {}
      execution: { directoryMaxDegreeOfParallelism: 1, cpgMaxDegreeOfParallelism: 1, groupMaxDegreeOfParallelism: 1, helperMaxDegreeOfParallelism: 1, replayMaxDegreeOfParallelism: 1, maxConcurrentOperations: 1 }
      artifacts:
        root: artifacts
        projectJson:
          enabled: true
      """);

    var configuration = YamlConfigurationLoader.Load(configurationPath);

    var settings = Assert.IsType<ProjectExportSettings>(configuration.ProjectExport);
    Assert.Empty(settings.EffectiveRequestedCapabilities);
  }

  [Fact]
  public void Load_ProjectJsonEmptyRequestedCapabilities_DiffersFromUnconfiguredFingerprint()
  {
    // 显式写空数组与完全不写，在 ProjectExportSettings 上**都是**空集合（行为等价）；
    // 但 resolved-configuration 的 fingerprint 把能力名列表纳入投影，
    // 故这里只锁定行为等价这一条，不锁定指纹相同。
    var fixtureProject = FixturePath("App", "App.csproj");
    var configurationPath = Path.Combine(_tempDirectory, "nlissn.yml");
    File.WriteAllText(
      configurationPath,
      $$"""
      schemaVersion: 3
      tool: nlissn
      runId: projectjson-empty-capabilities
      input:
        path: '{{fixtureProject}}'
        targetFramework: net10.0
        configuration: Debug
        platform: AnyCPU
      analysis: {}
      execution: { directoryMaxDegreeOfParallelism: 1, cpgMaxDegreeOfParallelism: 1, groupMaxDegreeOfParallelism: 1, helperMaxDegreeOfParallelism: 1, replayMaxDegreeOfParallelism: 1, maxConcurrentOperations: 1 }
      artifacts:
        root: artifacts
        projectJson:
          enabled: true
          requestedCapabilities: []
      """);

    var configuration = YamlConfigurationLoader.Load(configurationPath);

    var settings = Assert.IsType<ProjectExportSettings>(configuration.ProjectExport);
    Assert.Empty(settings.EffectiveRequestedCapabilities);
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

  private static string CreateDecisionKey(RuleDecision decision)
  {
    return string.Join(
      "|",
      decision.Action,
      decision.FinalNode.RawKind,
      decision.FinalNode.SpanStart,
      decision.FinalNode.Span.Length,
      decision.Reason,
      decision.EvidenceRootId);
  }

  private static string GetFingerprint(string resolvedConfiguration)
  {
    using var document = JsonDocument.Parse(resolvedConfiguration);
    return document.RootElement.GetProperty("fingerprint").GetString()!;
  }

  private static string GetEvidenceFingerprint(string evidence)
  {
    using var document = JsonDocument.Parse(evidence);
    return document.RootElement
      .GetProperty("configuration")
      .GetProperty("fingerprint")
      .GetString()!;
  }

  private static string RemoveConfigurationProjection(string evidence)
  {
    using var document = JsonDocument.Parse(evidence);
    var root = document.RootElement;
    var graph = new
    {
      nodes = root.GetProperty("nodes"),
      edges = root.GetProperty("edges"),
      budget = root.GetProperty("budget")
    };
    return JsonSerializer.Serialize(graph);
  }

  private string CreatePlayerInputDirectory(string name)
  {
    var directory = Path.Combine(_tempDirectory, name);
    Directory.CreateDirectory(directory);
    File.WriteAllText(
      Path.Combine(directory, "PlayerInput.cs"),
      "namespace Demo; public sealed class PlayerInput { }");
    File.WriteAllText(
      Path.Combine(directory, "Consumer.cs"),
      "namespace Demo; public static class Consumer { public static int Value => 1; }");
    return directory;
  }
}
