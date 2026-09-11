using System.Text.Json;
using NLISSN.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.RepresentationModel;
using NLISSN.Infrastructure.Workspace;

namespace NLISSN.Infrastructure.Configuration;

internal static class YamlConfigurationLoader
{
    internal const string ConfigurationFileName = "nlissn.yml";
    private const int SchemaVersion = 2;
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
      .WithNamingConvention(CamelCaseNamingConvention.Instance)
      .WithDuplicateKeyChecking()
      .Build();

    internal static AnalysisConfiguration LoadFromWorkingDirectory()
    {
        return Load(Path.Combine(Directory.GetCurrentDirectory(), ConfigurationFileName));
    }

    internal static AnalysisConfiguration Load(string configurationPath)
    {
        return TryLoad(configurationPath).RequireConfiguration();
    }

    internal static ConfigurationLoadResult TryLoad(string configurationPath)
    {
        try
        {
            var fullConfigurationPath = Path.GetFullPath(configurationPath);
            if (!File.Exists(fullConfigurationPath))
            {
                return Failure("NLISSN001", "configuration", $"Configuration file does not exist: {fullConfigurationPath}");
            }

            var yamlText = File.ReadAllText(fullConfigurationPath);
            var document = Deserializer.Deserialize<YamlDocument>(yamlText)
              ?? throw new ArgumentException("Configuration file must contain a YAML mapping.");
            var explicitPaths = GetExplicitPaths(yamlText);
            var diagnostics = ValidateDocument(document, explicitPaths);
            var configurationDirectory = Path.GetDirectoryName(fullConfigurationPath)!;
            diagnostics.AddRange(ValidateValues(document));
            diagnostics.AddRange(ValidateResolutionInputs(configurationDirectory, document));
            if (diagnostics.Count > 0)
            {
                return new ConfigurationLoadResult(null, OrderDiagnostics(diagnostics));
            }

            var inputPath = ResolveRequiredPath(configurationDirectory, document.Input!.Path, "input.path");
            ValidateInputPath(inputPath);
            var runId = RequireRunId(document.RunId);
            var artifacts = ResolveArtifacts(configurationDirectory, runId, document.Artifacts!);
            return new ConfigurationLoadResult(
              CreateConfiguration(configurationDirectory, inputPath, document, artifacts, explicitPaths),
              Array.Empty<ConfigurationDiagnostic>());
        }
        catch (Exception exception) when (exception is ArgumentException or YamlDotNet.Core.YamlException)
        {
            return Failure("NLISSN002", "configuration", exception.Message);
        }
    }

    private static ConfigurationLoadResult Failure(string code, string path, string message)
    {
        return new ConfigurationLoadResult(null, new[] { new ConfigurationDiagnostic(code, path, message) });
    }

    internal static void PrepareRunArtifacts(AnalysisConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var runRoot = Path.GetFullPath(configuration.Artifacts.RunRoot);
        if (Directory.Exists(runRoot) || File.Exists(runRoot))
        {
            throw new ArgumentException($"Artifact run directory already exists: {runRoot}");
        }

        var parentDirectory = Path.GetDirectoryName(runRoot)
          ?? throw new ArgumentException($"Artifact run directory has no parent: {runRoot}");
        Directory.CreateDirectory(parentDirectory);
        var stagingRoot = runRoot + ".staging-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stagingRoot);
        try
        {
            var summary = ResolvedConfigurationArtifact.Create(configuration);
            var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
            var resolvedFileName = Path.GetFileName(configuration.Artifacts.ResolvedConfigurationPath);
            if (string.IsNullOrWhiteSpace(resolvedFileName))
            {
                throw new ArgumentException("The resolved configuration artifact must have a file name.");
            }

            var stagingResolvedPath = Path.Combine(stagingRoot, resolvedFileName);
            var stagingTemporaryPath = stagingResolvedPath + ".tmp";
            File.WriteAllText(stagingTemporaryPath, json);
            File.Move(stagingTemporaryPath, stagingResolvedPath);
            try
            {
                Directory.Move(stagingRoot, runRoot);
            }
            catch (IOException) when (Directory.Exists(runRoot) || File.Exists(runRoot))
            {
                throw new ArgumentException($"Artifact run directory already exists: {runRoot}");
            }
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, recursive: true);
            }
        }
    }

    private static List<ConfigurationDiagnostic> ValidateDocument(
      YamlDocument document,
      IReadOnlySet<string> explicitPaths)
    {
        var diagnostics = new List<ConfigurationDiagnostic>();
        if (document.SchemaVersion != SchemaVersion)
        {
            diagnostics.Add(new ConfigurationDiagnostic("NLISSN100", "schemaVersion",
              $"Unsupported NLISSN configuration schemaVersion: '{document.SchemaVersion}'. Expected {SchemaVersion}."));
        }

        AddRequiredDiagnostic(diagnostics, document.Input, "input");
        AddRequiredDiagnostic(diagnostics, document.Analysis, "analysis");
        AddRequiredDiagnostic(diagnostics, document.Execution, "execution");
        AddRequiredDiagnostic(diagnostics, document.Artifacts, "artifacts");
        if (document.Logging is null && explicitPaths.Contains("logging"))
        {
            diagnostics.Add(new ConfigurationDiagnostic("NLISSN101", "logging", "must be a mapping when provided."));
        }

        if (document.Analysis is not null)
        {
            AddRequiredDiagnostic(diagnostics, document.Analysis.DisabledRuleTypes, "analysis.disabledRuleTypes");
        }

        if (document.Artifacts is not null)
        {
            AddRequiredDiagnostic(diagnostics, document.Artifacts.Diff, "artifacts.diff");
            AddRequiredDiagnostic(diagnostics, document.Artifacts.RuntimeLog, "artifacts.runtimeLog");
            AddRequiredDiagnostic(diagnostics, document.Artifacts.Performance, "artifacts.performance");
            AddRequiredDiagnostic(diagnostics, document.Artifacts.Evidence, "artifacts.evidence");
            AddRequiredDiagnostic(diagnostics, document.Artifacts.RewritePlan, "artifacts.rewritePlan");
            AddRequiredDiagnostic(diagnostics, document.Artifacts.AnalysisLog, "artifacts.analysisLog");
        }

        if (document.Logging is not null)
        {
            AddRequiredDiagnostic(diagnostics, document.Logging.Profile, "logging.profile");
            AddRequiredDiagnostic(diagnostics, document.Logging.Level, "logging.level");
            AddRequiredDiagnostic(diagnostics, document.Logging.Categories, "logging.categories");
            AddRequiredDiagnostic(diagnostics, document.Logging.Events, "logging.events");
            AddRequiredDiagnostic(diagnostics, document.Logging.View, "logging.view");
        }

        return diagnostics;
    }

    private static AnalysisConfiguration CreateConfiguration(
      string configurationDirectory,
      string inputPath,
      YamlDocument document,
      ArtifactSettings artifacts,
      IReadOnlySet<string> explicitPaths)
    {
        var analysis = document.Analysis!;
        var execution = document.Execution!;
        var logging = document.Logging ?? new YamlLogging();
        var origins = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["input.path"] = GetOrigin(explicitPaths, "input.path"),
            ["input.project"] = GetOrigin(explicitPaths, "input.project"),
            ["input.targetFramework"] = GetOrigin(explicitPaths, "input.targetFramework"),
            ["input.configuration"] = GetOrigin(explicitPaths, "input.configuration"),
            ["input.platform"] = GetOrigin(explicitPaths, "input.platform"),
            ["input.restore"] = GetOrigin(explicitPaths, "input.restore"),
            ["input.generatedSources"] = GetOrigin(explicitPaths, "input.generatedSources"),
            ["input.generators"] = GetOrigin(explicitPaths, "input.generators"),
            ["analysis.validateBindings"] = GetOrigin(explicitPaths, "analysis.validateBindings"),
            ["analysis.deleteUnreachableMethods"] = GetOrigin(explicitPaths, "analysis.deleteUnreachableMethods"),
            ["analysis.deleteUnreferencedMethods"] = GetOrigin(explicitPaths, "analysis.deleteUnreferencedMethods"),
            ["analysis.clearUnusedInterfaceImplementations"] = GetOrigin(explicitPaths, "analysis.clearUnusedInterfaceImplementations"),
            ["analysis.privatizeInternalOnlyPublicMethods"] = GetOrigin(explicitPaths, "analysis.privatizeInternalOnlyPublicMethods")
        };
        var workspace = CreateWorkspaceOptions(configurationDirectory, inputPath, document.Input!);
        return new AnalysisConfiguration(
          inputPath,
          new RulePolicySettings(
            analysis.TargetName,
            analysis.DeleteClass,
            new HashSet<string>(analysis.DisabledRuleTypes!, StringComparer.OrdinalIgnoreCase),
            analysis.ValidateBindings,
            analysis.DeleteUnreachableMethods,
            analysis.DeleteUnreferencedMethods,
            analysis.ClearUnusedInterfaceImplementations,
            analysis.PrivatizeInternalOnlyPublicMethods),
          new ExecutionSettings(
            execution.WriteBack,
            execution.SkipRewrite,
            execution.MaxDegreeOfParallelism,
            execution.CpgMaxDegreeOfParallelism,
            execution.DirectoryParallelism,
            execution.GroupParallelism,
            execution.HelperParallelism,
            execution.FastDeleteClassDirectory,
            execution.FilterDeleteClassFilesByTargetName),
          artifacts,
          new LoggingSettings(
            logging.Profile!,
            logging.Level!,
            logging.Categories!.ToArray(),
            logging.Events!.ToArray(),
            logging.View!),
          new ConfigurationProvenance(SchemaVersion, SchemaVersion, Array.Empty<string>(), origins),
          workspace);
    }

    private static ArtifactSettings ResolveArtifacts(
      string configurationDirectory,
      string runId,
      YamlArtifacts artifacts)
    {
        var diff = artifacts.Diff!;
        var runtimeLog = artifacts.RuntimeLog!;
        var performance = artifacts.Performance!;
        var evidence = artifacts.Evidence!;
        var rewritePlan = artifacts.RewritePlan!;
        var analysisLog = artifacts.AnalysisLog!;
        var root = string.IsNullOrWhiteSpace(artifacts.Root)
          ? Path.Combine(FindRepositoryRoot(configurationDirectory), "Build", "Result")
          : ResolveArtifactRoot(configurationDirectory, artifacts.Root);
        var runRoot = Path.Combine(root, runId);
        var rewritePlanMode = ParseRewritePlanMode(rewritePlan.Mode);
        var replayPlanPath = rewritePlanMode == RewritePlanMode.Replay
          ? Path.Combine(root, RequireRunId(rewritePlan.SourceRunId), "RewritePlan")
          : null;
        if (analysisLog.Enabled)
        {
            throw new ArgumentException("artifacts.analysisLog.enabled requires an analysis-log writer, which is not implemented.");
        }

        return new ArtifactSettings(
          runRoot,
          Path.Combine(runRoot, "Diff"),
          Path.Combine(runRoot, "RuntimeLog", "runtime.log"),
          Path.Combine(runRoot, "Evidence", "evidence.json"),
          Path.Combine(runRoot, "RewritePlan"),
          replayPlanPath,
          Path.Combine(runRoot, "resolved-configuration.json"),
          diff.Enabled,
          runtimeLog.Enabled,
          evidence.Enabled,
          rewritePlanMode,
          diff.View,
          runId,
          performance.Enabled,
          ParsePerformanceMode(performance.Mode),
          Path.Combine(runRoot, "Performance", "summary.json"));
    }

    private static List<ConfigurationDiagnostic> ValidateValues(YamlDocument document)
    {
        var diagnostics = new List<ConfigurationDiagnostic>();
        if (document.Input is null ||
            document.Analysis is null ||
            document.Execution is null ||
            document.Artifacts is null ||
            document.Analysis.DisabledRuleTypes is null ||
            document.Artifacts.Diff is null ||
            document.Artifacts.RuntimeLog is null ||
            document.Artifacts.Performance is null ||
            document.Artifacts.Evidence is null ||
            document.Artifacts.RewritePlan is null ||
            document.Artifacts.AnalysisLog is null)
        {
            return diagnostics;
        }

        var execution = document.Execution!;
        if (execution.MaxDegreeOfParallelism <= 0)
        {
            diagnostics.Add(new ConfigurationDiagnostic("NLISSN110", "execution.maxDegreeOfParallelism", "must be a positive integer."));
        }

        if (execution.CpgMaxDegreeOfParallelism is <= 0)
        {
            diagnostics.Add(new ConfigurationDiagnostic("NLISSN111", "execution.cpgMaxDegreeOfParallelism", "must be a positive integer."));
        }

        if (execution.FilterDeleteClassFilesByTargetName &&
            (!execution.FastDeleteClassDirectory || string.IsNullOrWhiteSpace(document.Analysis!.DeleteClass)))
        {
            diagnostics.Add(new ConfigurationDiagnostic("NLISSN112", "execution.filterDeleteClassFilesByTargetName",
              "requires fastDeleteClassDirectory and analysis.deleteClass."));
        }

        if (string.Equals(document.Artifacts.RewritePlan.Mode, "replay", StringComparison.OrdinalIgnoreCase) &&
            (!string.IsNullOrWhiteSpace(document.Analysis!.DeleteClass) ||
             !string.IsNullOrWhiteSpace(document.Analysis.TargetName) ||
             execution.SkipRewrite))
        {
            diagnostics.Add(new ConfigurationDiagnostic("NLISSN113", "artifacts.rewritePlan.mode",
              "replay cannot be combined with analysis targets or execution.skipRewrite."));
        }

        if (document.Logging is not null &&
            document.Logging.Profile is not null &&
            document.Logging.Level is not null &&
            document.Logging.Categories is not null &&
            document.Logging.Events is not null &&
            document.Logging.View is not null &&
            !document.Artifacts.RuntimeLog.Enabled &&
            !document.Artifacts.AnalysisLog.Enabled)
        {
            diagnostics.Add(new ConfigurationDiagnostic("NLISSN114", "logging",
              "requires artifacts.runtimeLog.enabled or artifacts.analysisLog.enabled."));
        }

        AddEnumDiagnostic(diagnostics, document.Artifacts.Diff.View, "artifacts.diff.view", "legacy", "readable");
        AddEnumDiagnostic(diagnostics, document.Artifacts.RewritePlan.Mode, "artifacts.rewritePlan.mode", "none", "capture", "replay");
        AddEnumDiagnostic(diagnostics, document.Artifacts.Performance.Mode, "artifacts.performance.mode", "normal", "diagnostic", "profile", "benchmark");
        AddEnumDiagnostic(diagnostics, document.Logging?.View ?? "normal", "logging.view", "compact", "normal", "diagnostic", "benchmark");
        AddEnumDiagnostic(diagnostics, document.Logging?.Level ?? "debug", "logging.level", "error", "warn", "info", "debug", "trace");
        AddEnumDiagnostic(diagnostics, document.Logging?.Profile ?? "normal", "logging.profile", "minimal", "normal", "diagnostic", "benchmark");
        AddEnumValueDiagnostics(diagnostics, document.Logging?.Categories, "logging.categories", Enum.GetNames<TextLogCategory>());
        AddEnumValueDiagnostics(diagnostics, document.Logging?.Events, "logging.events", Enum.GetNames<TextLogEventType>());
        return diagnostics;
    }

    private static List<ConfigurationDiagnostic> ValidateResolutionInputs(
      string configurationDirectory,
      YamlDocument document)
    {
        var diagnostics = new List<ConfigurationDiagnostic>();
        if (document.Input is null ||
            document.Artifacts is null ||
            document.Artifacts.RewritePlan is null ||
            document.Artifacts.AnalysisLog is null)
        {
            return diagnostics;
        }

        if (string.IsNullOrWhiteSpace(document.Input.Path))
        {
            diagnostics.Add(new ConfigurationDiagnostic("NLISSN130", "input.path", "requires a non-empty path."));
        }
        else
        {
            var inputPath = ResolvePath(configurationDirectory, document.Input.Path);
            if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
            {
                diagnostics.Add(new ConfigurationDiagnostic("NLISSN130", "input.path", $"does not exist: {inputPath}"));
            }
            else if (File.Exists(inputPath))
            {
                var extension = Path.GetExtension(inputPath);
                if (!IsSupportedInputExtension(extension))
                {
                    diagnostics.Add(new ConfigurationDiagnostic("NLISSN130", "input.path", "must identify a .cs file, directory, .sln, or .csproj."));
                }
                else if (IsWorkspaceInputExtension(extension) ||
                         string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase) &&
                         !string.IsNullOrWhiteSpace(document.Input.Project))
                {
                    ValidateWorkspaceInput(
                      configurationDirectory,
                      inputPath,
                      document.Input,
                      diagnostics);
                }
                else if (HasWorkspaceOptions(document.Input))
                {
                    diagnostics.Add(new ConfigurationDiagnostic(
                      "NLISSN133",
                      "input",
                      "Workspace options require input.path to be a .sln, .csproj, or a .cs file with input.project."));
                }
            }
            else if (HasWorkspaceOptions(document.Input))
            {
                diagnostics.Add(new ConfigurationDiagnostic(
                  "NLISSN133",
                  "input",
                  "Workspace options require input.path to be a .sln, .csproj, or a .cs file with input.project."));
            }
        }

        if (!IsValidRunId(document.RunId))
        {
            diagnostics.Add(new ConfigurationDiagnostic("NLISSN131", "runId", "must contain only letters, digits, '-', '_', or '.'."));
        }

        if (!string.IsNullOrWhiteSpace(document.Artifacts.Root) &&
            !IsValidArtifactRoot(document.Artifacts.Root))
        {
            diagnostics.Add(new ConfigurationDiagnostic("NLISSN132", "artifacts.root", "must be a non-empty relative path without '..' segments."));
        }

        if (string.Equals(document.Artifacts.RewritePlan.Mode, "replay", StringComparison.OrdinalIgnoreCase) &&
            !IsValidRunId(document.Artifacts.RewritePlan.SourceRunId))
        {
            diagnostics.Add(new ConfigurationDiagnostic("NLISSN131", "artifacts.rewritePlan.sourceRunId", "must contain only letters, digits, '-', '_', or '.'."));
        }

        if (document.Artifacts.AnalysisLog.Enabled)
        {
            diagnostics.Add(new ConfigurationDiagnostic("NLISSN115", "artifacts.analysisLog.enabled", "requires an analysis-log writer, which is not implemented."));
        }

        return diagnostics;
    }

    private static void ValidateInputPath(string inputPath)
    {
        if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
        {
            throw new ArgumentException($"input.path does not exist: {inputPath}");
        }

        if (File.Exists(inputPath) && !IsSupportedInputExtension(Path.GetExtension(inputPath)))
        {
            throw new ArgumentException("input.path must identify a .cs file, directory, .sln, or .csproj.");
        }
    }

    private static WorkspaceInputOptions? CreateWorkspaceOptions(
      string configurationDirectory,
      string inputPath,
      YamlInput input)
    {
        var extension = Path.GetExtension(inputPath);
        var isSourceDocument = string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase);
        if (!IsWorkspaceInputExtension(extension) &&
            (!isSourceDocument || string.IsNullOrWhiteSpace(input.Project)))
        {
            return null;
        }

        var projectPath = string.IsNullOrWhiteSpace(input.Project)
          ? null
          : ResolveProjectPath(configurationDirectory, inputPath, input.Project);
        var workspacePath = isSourceDocument ? projectPath! : inputPath;
        return new WorkspaceInputOptions(
          workspacePath,
          projectPath,
          input.TargetFramework,
          input.Configuration ?? "Debug",
          input.Platform ?? "AnyCPU",
          ParseWorkspaceRestoreMode(input.Restore ?? "disabled"),
          ParseWorkspaceGeneratedSourceMode(input.GeneratedSources ?? "include"),
          ParseWorkspaceGeneratorMode(input.Generators ?? "disabled"),
          TargetDocumentPath: isSourceDocument ? inputPath : null);
    }

    private static void ValidateWorkspaceInput(
      string configurationDirectory,
      string inputPath,
      YamlInput input,
      ICollection<ConfigurationDiagnostic> diagnostics)
    {
        var extension = Path.GetExtension(inputPath);
        if (string.IsNullOrWhiteSpace(input.Configuration))
        {
            diagnostics.Add(new ConfigurationDiagnostic("NLISSN134", "input.configuration", "must be non-empty."));
        }

        if (string.IsNullOrWhiteSpace(input.Platform))
        {
            diagnostics.Add(new ConfigurationDiagnostic("NLISSN134", "input.platform", "must be non-empty."));
        }

        ValidateEnumValue(diagnostics, input.Restore, "input.restore", "disabled", "enabled");
        ValidateEnumValue(diagnostics, input.GeneratedSources, "input.generatedSources", "include", "exclude");
        ValidateEnumValue(diagnostics, input.Generators, "input.generators", "disabled", "enabled");

        if (string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(input.Project))
        {
            diagnostics.Add(new ConfigurationDiagnostic("NLISSN135", "input.project", "is only valid when input.path is a .sln file."));
        }

        if (string.IsNullOrWhiteSpace(input.Project))
        {
            return;
        }

        var selectedProjectPath = ResolveProjectPath(
          configurationDirectory,
          inputPath,
          input.Project);
        if (!File.Exists(selectedProjectPath))
        {
            diagnostics.Add(new ConfigurationDiagnostic("NLISSN136", "input.project", $"does not exist: {selectedProjectPath}"));
            return;
        }

        if (!string.Equals(Path.GetExtension(selectedProjectPath), ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(new ConfigurationDiagnostic("NLISSN137", "input.project", "must identify a .csproj file."));
        }

        if (string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase) &&
            !IsWithinDirectory(Path.GetDirectoryName(inputPath)!, selectedProjectPath))
        {
            diagnostics.Add(new ConfigurationDiagnostic("NLISSN138", "input.project", "must be inside the solution directory."));
        }
    }

    private static bool HasWorkspaceOptions(YamlInput input)
    {
        return input.Project is not null ||
          input.TargetFramework is not null ||
          input.Configuration is not null ||
          input.Platform is not null ||
          input.Restore is not null ||
          input.GeneratedSources is not null ||
          input.Generators is not null;
    }

    private static bool IsSupportedInputExtension(string extension)
    {
        return string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase) ||
          IsWorkspaceInputExtension(extension);
    }

    private static bool IsWorkspaceInputExtension(string extension)
    {
        return string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWithinDirectory(string directoryPath, string candidatePath)
    {
        var root = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(candidatePath);
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateEnumValue(
      ICollection<ConfigurationDiagnostic> diagnostics,
      string? value,
      string path,
      params string[] allowedValues)
    {
        if (value is null || allowedValues.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        diagnostics.Add(new ConfigurationDiagnostic("NLISSN120", path,
          $"must be one of: {string.Join(", ", allowedValues)}."));
    }

    private static WorkspaceRestoreMode ParseWorkspaceRestoreMode(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "disabled" => WorkspaceRestoreMode.Disabled,
            "enabled" => WorkspaceRestoreMode.Enabled,
            _ => throw new ArgumentException("input.restore must be disabled or enabled.")
        };
    }

    private static WorkspaceGeneratedSourceMode ParseWorkspaceGeneratedSourceMode(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "include" => WorkspaceGeneratedSourceMode.Include,
            "exclude" => WorkspaceGeneratedSourceMode.Exclude,
            _ => throw new ArgumentException("input.generatedSources must be include or exclude.")
        };
    }

    private static WorkspaceGeneratorMode ParseWorkspaceGeneratorMode(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "disabled" => WorkspaceGeneratorMode.Disabled,
            "enabled" => WorkspaceGeneratorMode.Enabled,
            _ => throw new ArgumentException("input.generators must be disabled or enabled.")
        };
    }

    private static string RequireRunId(string? runId)
    {
        if (!IsValidRunId(runId))
        {
            throw new ArgumentException("runId must contain only letters, digits, '-', '_', or '.'.");
        }

        return runId!;
    }

    private static RewritePlanMode ParseRewritePlanMode(string mode)
    {
        return mode.ToLowerInvariant() switch
        {
            "none" => RewritePlanMode.None,
            "capture" => RewritePlanMode.Capture,
            "replay" => RewritePlanMode.Replay,
            _ => throw new ArgumentException("artifacts.rewritePlan.mode must be none, capture, or replay.")
        };
    }

    private static string ParsePerformanceMode(string mode)
    {
        return mode.ToLowerInvariant() switch
        {
            "normal" or "diagnostic" or "profile" or "benchmark" => mode.ToLowerInvariant(),
            _ => throw new ArgumentException("artifacts.performance.mode must be normal, diagnostic, profile, or benchmark.")
        };
    }

    private static string FindRepositoryRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new ArgumentException("Could not locate the repository root from the configuration file.");
    }

    private static string ResolveRequiredPath(string baseDirectory, string? value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{propertyName} requires a non-empty path.");
        }

        return ResolvePath(baseDirectory, value);
    }

    private static string ResolveArtifactRoot(string baseDirectory, string? value)
    {
        if (!IsValidArtifactRoot(value))
        {
            throw new ArgumentException("artifacts.root must be a non-empty relative path without '..' segments.");
        }

        return Path.GetFullPath(Path.Combine(baseDirectory, value!));
    }

    private static bool IsValidRunId(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
          value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.');
    }

    private static bool IsValidArtifactRoot(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
          !Path.IsPathRooted(value) &&
          !value.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => string.Equals(segment, "..", StringComparison.Ordinal));
    }

    private static string ResolvePath(string baseDirectory, string value)
    {
        return Path.IsPathRooted(value) ? Path.GetFullPath(value) : Path.GetFullPath(Path.Combine(baseDirectory, value));
    }

    private static string ResolveProjectPath(
      string configurationDirectory,
      string inputPath,
      string projectPath)
    {
        if (Path.IsPathRooted(projectPath))
        {
            return Path.GetFullPath(projectPath);
        }

        var baseDirectory = string.Equals(
          Path.GetExtension(inputPath),
          ".sln",
          StringComparison.OrdinalIgnoreCase)
          ? Path.GetDirectoryName(inputPath)!
          : configurationDirectory;
        return Path.GetFullPath(Path.Combine(baseDirectory, projectPath));
    }

    private static void AddRequiredDiagnostic<T>(
      ICollection<ConfigurationDiagnostic> diagnostics,
      T? value,
      string path)
      where T : class
    {
        if (value is null)
        {
            diagnostics.Add(new ConfigurationDiagnostic("NLISSN101", path, "is required."));
        }
    }

    private static void AddEnumDiagnostic(
      ICollection<ConfigurationDiagnostic> diagnostics,
      string value,
      string path,
      params string[] values)
    {
        if (!values.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            diagnostics.Add(new ConfigurationDiagnostic("NLISSN120", path,
              $"must be one of: {string.Join(", ", values)}."));
        }
    }

    private static void AddEnumValueDiagnostics(
      ICollection<ConfigurationDiagnostic> diagnostics,
      IReadOnlyList<string>? values,
      string path,
      IReadOnlyList<string> allowedValues)
    {
        if (values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            AddEnumDiagnostic(diagnostics, value, path, allowedValues.ToArray());
        }
    }

    private static IReadOnlySet<string> GetExplicitPaths(string yamlText)
    {
        using var reader = new StringReader(yamlText);
        var stream = new YamlStream();
        stream.Load(reader);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        if (stream.Documents.Count == 1 && stream.Documents[0].RootNode is YamlMappingNode root)
        {
            CollectExplicitPaths(root, string.Empty, paths);
        }

        return paths;
    }

    private static void CollectExplicitPaths(
      YamlMappingNode mapping,
      string prefix,
      ISet<string> paths)
    {
        foreach (var (keyNode, valueNode) in mapping.Children)
        {
            if (keyNode is not YamlScalarNode { Value: { } key })
            {
                continue;
            }

            var path = string.IsNullOrEmpty(prefix) ? key : $"{prefix}.{key}";
            paths.Add(path);
            if (valueNode is YamlMappingNode child)
            {
                CollectExplicitPaths(child, path, paths);
            }
        }
    }

    private static string GetOrigin(IReadOnlySet<string> explicitPaths, string path)
    {
        return explicitPaths.Contains(path) ? "explicit" : "schema-default";
    }

    private static IReadOnlyList<ConfigurationDiagnostic> OrderDiagnostics(
      IEnumerable<ConfigurationDiagnostic> diagnostics)
    {
        return diagnostics
          .OrderBy(diagnostic => diagnostic.Path, StringComparer.Ordinal)
          .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
          .ToArray();
    }
}

internal sealed class YamlDocument
{
    public int SchemaVersion { get; set; }

    public string? RunId { get; set; }

    public YamlInput? Input { get; set; }

    public YamlAnalysis? Analysis { get; set; }

    public YamlExecution? Execution { get; set; }

    public YamlArtifacts? Artifacts { get; set; }

    public YamlLogging? Logging { get; set; }
}

internal sealed class YamlInput
{
    public string? Path { get; set; }

    public string? Project { get; set; }

    public string? TargetFramework { get; set; }

    public string? Configuration { get; set; }

    public string? Platform { get; set; }

    public string? Restore { get; set; }

    public string? GeneratedSources { get; set; }

    public string? Generators { get; set; }
}

internal sealed class YamlAnalysis
{
    public string? TargetName { get; set; }

    public string? DeleteClass { get; set; }

    public List<string>? DisabledRuleTypes { get; set; } = [];

    public bool ValidateBindings { get; set; }

    public bool DeleteUnreachableMethods { get; set; }

    public bool DeleteUnreferencedMethods { get; set; }

    public bool ClearUnusedInterfaceImplementations { get; set; }

    public bool PrivatizeInternalOnlyPublicMethods { get; set; }
}

internal sealed class YamlExecution
{
    public bool WriteBack { get; set; }

    public bool SkipRewrite { get; set; }

    public int MaxDegreeOfParallelism { get; set; }

    public int? CpgMaxDegreeOfParallelism { get; set; }

    public bool DirectoryParallelism { get; set; } = true;

    public bool GroupParallelism { get; set; }

    public bool HelperParallelism { get; set; } = true;

    public bool FastDeleteClassDirectory { get; set; }

    public bool FilterDeleteClassFilesByTargetName { get; set; }
}

internal sealed class YamlArtifacts
{
    public string? Root { get; set; }

    public YamlDiff? Diff { get; set; } = new();

    public YamlToggle? RuntimeLog { get; set; } = new();

    public YamlPerformance? Performance { get; set; } = new();

    public YamlToggle? Evidence { get; set; } = new();

    public YamlRewritePlan? RewritePlan { get; set; } = new();

    public YamlToggle? AnalysisLog { get; set; } = new();
}

internal sealed class YamlPerformance : YamlToggle
{
    public string Mode { get; set; } = "normal";
}

internal sealed class YamlDiff : YamlToggle
{
    public string View { get; set; } = "legacy";
}

internal class YamlToggle
{
    public bool Enabled { get; set; }
}

internal sealed class YamlRewritePlan
{
    public string Mode { get; set; } = "none";

    public string? SourceRunId { get; set; }
}

internal sealed class YamlLogging
{
    public string? Profile { get; set; } = "normal";

    public string? Level { get; set; } = "debug";

    public List<string>? Categories { get; set; } = [];

    public List<string>? Events { get; set; } = [];

    public string? View { get; set; } = "normal";
}
