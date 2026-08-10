using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;

namespace NLISSN.Infrastructure.Workspace;

public sealed class MsBuildWorkspaceInputLoader
{
    private static readonly string[] GeneratedSuffixes =
    [
      ".g.cs",
    ".generated.cs",
    ".designer.cs"
    ];

    public async Task<WorkspaceLoadResult> LoadAsync(
      WorkspaceInputOptions options,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var diagnostics = new List<WorkspaceInputDiagnostic>();
        var inputPath = ValidateInput(options, diagnostics);
        if (inputPath is null)
        {
            return new WorkspaceLoadResult(null, diagnostics);
        }

        if (options.RestoreMode == WorkspaceRestoreMode.Enabled)
        {
            var restoreDiagnostic = await RestoreAsync(inputPath, options, cancellationToken)
              .ConfigureAwait(false);
            if (restoreDiagnostic is not null)
            {
                diagnostics.Add(restoreDiagnostic);
                return new WorkspaceLoadResult(null, diagnostics);
            }
        }

        using var workspace = MSBuildWorkspace.Create(CreateGlobalProperties(options));
        workspace.LoadMetadataForReferencedProjects = false;
        workspace.SkipUnrecognizedProjects = true;
        workspace.WorkspaceFailed += (_, args) =>
        {
            diagnostics.Add(MapWorkspaceDiagnostic(args.Diagnostic));
        };
        var targetFrameworkProgress = new TargetFrameworkLoadProgress();

        Solution solution;
        try
        {
            solution = Path.GetExtension(inputPath).Equals(".sln", StringComparison.OrdinalIgnoreCase)
              ? await workspace.OpenSolutionAsync(
                inputPath,
                progress: targetFrameworkProgress,
                cancellationToken: cancellationToken)
                .ConfigureAwait(false)
              : (await workspace.OpenProjectAsync(
                inputPath,
                progress: targetFrameworkProgress,
                cancellationToken: cancellationToken)
                .ConfigureAwait(false)).Solution;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            diagnostics.Add(new WorkspaceInputDiagnostic(
              "NLISSNWS001",
              WorkspaceDiagnosticSeverity.Error,
              $"MSBuild workspace could not load '{inputPath}': {exception.Message}",
              inputPath));
            return new WorkspaceLoadResult(null, diagnostics);
        }

        var isSolution = Path.GetExtension(inputPath).Equals(".sln", StringComparison.OrdinalIgnoreCase);
        var projects = SelectProjects(solution, inputPath, options, diagnostics);
        if (projects.Count == 0 && !diagnostics.Any(diagnostic => diagnostic.Severity == WorkspaceDiagnosticSeverity.Error))
        {
            diagnostics.Add(new WorkspaceInputDiagnostic(
              "NLISSNWS022",
              WorkspaceDiagnosticSeverity.Error,
              "The workspace input contains no C# projects.",
              inputPath));
            return new WorkspaceLoadResult(null, diagnostics);
        }

        var loadedTargetFrameworks = targetFrameworkProgress.Snapshot();
        var snapshots = new List<WorkspaceProjectSnapshot>();
        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await LoadProjectAsync(
              solution,
              project,
              inputPath,
              options,
              loadedTargetFrameworks,
              diagnostics,
              cancellationToken).ConfigureAwait(false);
            if (snapshot is not null)
            {
                snapshots.Add(snapshot);
            }
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == WorkspaceDiagnosticSeverity.Error))
        {
            return new WorkspaceLoadResult(null, diagnostics);
        }

        var orderedSnapshots = snapshots
          .OrderBy(snapshot => snapshot.ProjectPath, StringComparer.OrdinalIgnoreCase)
          .ToArray();
        if (!TryValidateTargetDocument(options, orderedSnapshots, diagnostics))
        {
            return new WorkspaceLoadResult(null, diagnostics);
        }

        var fingerprint = ComputeSolutionFingerprint(inputPath, options, orderedSnapshots);
        var selectedProjectPath = isSolution
          ? options.ProjectPath is null
            ? null
            : ResolveProjectSelector(inputPath, options.ProjectPath)
          : inputPath;
        var snapshotResult = new WorkspaceSolutionSnapshot(
          inputPath,
          isSolution,
          Path.GetDirectoryName(inputPath)!,
          selectedProjectPath,
          orderedSnapshots,
          diagnostics.ToArray(),
          fingerprint);
        return new WorkspaceLoadResult(snapshotResult, diagnostics);
    }

    private static bool TryValidateTargetDocument(
      WorkspaceInputOptions options,
      IReadOnlyList<WorkspaceProjectSnapshot> projects,
      ICollection<WorkspaceInputDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(options.TargetDocumentPath))
        {
            return true;
        }

        var targetDocumentPath = Path.GetFullPath(options.TargetDocumentPath);
        var matches = projects
          .SelectMany(project => project.Documents)
          .Count(document => PathsEqual(document.FilePath, targetDocumentPath));
        if (matches == 1)
        {
            return true;
        }

        diagnostics.Add(new WorkspaceInputDiagnostic(
          "NLISSNWS025",
          WorkspaceDiagnosticSeverity.Error,
          matches == 0
            ? $"The target document is not included in the selected project: {targetDocumentPath}"
            : $"The target document is included more than once: {targetDocumentPath}",
          targetDocumentPath,
          options.ProjectPath));
        return false;
    }

    private static string? ValidateInput(
      WorkspaceInputOptions options,
      ICollection<WorkspaceInputDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(options.Path))
        {
            diagnostics.Add(new WorkspaceInputDiagnostic(
              "NLISSNWS002",
              WorkspaceDiagnosticSeverity.Error,
              "Workspace input path is required.",
              "input.path"));
            return null;
        }

        var inputPath = Path.GetFullPath(options.Path);
        if (!File.Exists(inputPath))
        {
            diagnostics.Add(new WorkspaceInputDiagnostic(
              "NLISSNWS003",
              WorkspaceDiagnosticSeverity.Error,
              $"Workspace input does not exist: {inputPath}",
              inputPath));
            return null;
        }

        var extension = Path.GetExtension(inputPath);
        if (!extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(new WorkspaceInputDiagnostic(
              "NLISSNWS004",
              WorkspaceDiagnosticSeverity.Error,
              "Workspace input must be a .sln or .csproj file.",
              inputPath));
            return null;
        }

        if (extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(options.ProjectPath) &&
            string.IsNullOrWhiteSpace(options.TargetDocumentPath))
        {
            diagnostics.Add(new WorkspaceInputDiagnostic(
              "NLISSNWS005",
              WorkspaceDiagnosticSeverity.Error,
              "input.project is only valid when input.path is a .sln file.",
              inputPath));
        }

        if (string.IsNullOrWhiteSpace(options.Configuration) ||
            string.IsNullOrWhiteSpace(options.Platform))
        {
            diagnostics.Add(new WorkspaceInputDiagnostic(
              "NLISSNWS006",
              WorkspaceDiagnosticSeverity.Error,
              "Workspace configuration and platform must be non-empty.",
              inputPath));
        }

        return diagnostics.Any(diagnostic => diagnostic.Severity == WorkspaceDiagnosticSeverity.Error)
          ? null
          : inputPath;
    }

    private static Dictionary<string, string> CreateGlobalProperties(
      WorkspaceInputOptions options)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Configuration"] = options.Configuration,
            ["Platform"] = options.Platform,
            ["NuGetAudit"] = "false",
            ["RestoreIgnoreFailedSources"] = "true",
            ["NoWarn"] = "NU1801;NU1900"
        };
        if (!string.IsNullOrWhiteSpace(options.TargetFramework))
        {
            properties["TargetFramework"] = options.TargetFramework;
        }

        return properties;
    }

    private static IReadOnlyList<Project> SelectProjects(
      Solution solution,
      string inputPath,
      WorkspaceInputOptions options,
      ICollection<WorkspaceInputDiagnostic> diagnostics)
    {
        var csharpProjects = solution.Projects
          .Where(project => string.Equals(project.Language, LanguageNames.CSharp, StringComparison.Ordinal))
          .Where(project => !string.IsNullOrWhiteSpace(project.FilePath))
          .OrderBy(project => project.FilePath, StringComparer.OrdinalIgnoreCase)
          .ToArray();

        if (!Path.GetExtension(inputPath).Equals(".sln", StringComparison.OrdinalIgnoreCase))
        {
            var requestedProject = csharpProjects.FirstOrDefault(project =>
              string.Equals(project.FilePath, inputPath, StringComparison.OrdinalIgnoreCase));
            if (requestedProject is null)
            {
                diagnostics.Add(new WorkspaceInputDiagnostic(
                  "NLISSNWS021",
                  WorkspaceDiagnosticSeverity.Error,
                  $"The requested project was not loaded as a C# project: {inputPath}",
                  inputPath));
                return Array.Empty<Project>();
            }

            return new[] { requestedProject };
        }

        if (string.IsNullOrWhiteSpace(options.ProjectPath))
        {
            return csharpProjects;
        }

        var selectedPath = ResolveProjectSelector(inputPath, options.ProjectPath);
        var selected = csharpProjects.FirstOrDefault(project =>
          string.Equals(project.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
        {
            diagnostics.Add(new WorkspaceInputDiagnostic(
              "NLISSNWS007",
              WorkspaceDiagnosticSeverity.Error,
              $"The selected project is not a C# project in the solution: {selectedPath}",
              selectedPath));
            return Array.Empty<Project>();
        }

        return new[] { selected };
    }

    private static async Task<WorkspaceProjectSnapshot?> LoadProjectAsync(
      Solution solution,
      Project project,
      string inputPath,
      WorkspaceInputOptions options,
      IReadOnlyDictionary<string, IReadOnlyList<string>> loadedTargetFrameworks,
      ICollection<WorkspaceInputDiagnostic> diagnostics,
      CancellationToken cancellationToken)
    {
        var projectPath = project.FilePath!;
        var targetFramework = await ResolveTargetFrameworkAsync(
          projectPath,
          options,
          loadedTargetFrameworks,
          diagnostics,
          cancellationToken);
        if (diagnostics.Any(diagnostic =>
              diagnostic.Severity == WorkspaceDiagnosticSeverity.Error &&
              string.Equals(diagnostic.ProjectPath, projectPath, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var requiresProjectEvaluation = project.ProjectReferences.Any() ||
          project.AnalyzerReferences.Count > 0 ||
          options.GeneratorMode == WorkspaceGeneratorMode.Enabled;
        var projectEvaluation = requiresProjectEvaluation
          ? await EvaluateProjectAsync(
              projectPath,
              options with { TargetFramework = targetFramework },
              diagnostics,
              cancellationToken,
              requireSuccess: true).ConfigureAwait(false)
          : null;
        if (requiresProjectEvaluation && projectEvaluation is null)
        {
            return null;
        }

        var analyzerProjectReferences = projectEvaluation is null
          ? Array.Empty<WorkspaceReferenceSnapshot>()
          : await ReadAnalyzerProjectReferencesAsync(
              projectPath,
              options,
              targetFramework,
              projectEvaluation,
              diagnostics,
              cancellationToken).ConfigureAwait(false);
        if (diagnostics.Any(diagnostic =>
              diagnostic.Severity == WorkspaceDiagnosticSeverity.Error &&
              string.Equals(diagnostic.ProjectPath, projectPath, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        if (options.GeneratorMode == WorkspaceGeneratorMode.Enabled)
        {
            var missingAnalyzerReferences = project.AnalyzerReferences
              .Where(reference => HasSourceGenerator(reference))
              .Where(reference => string.IsNullOrWhiteSpace(reference.FullPath) || !File.Exists(reference.FullPath))
              .Select(reference => reference.FullPath ?? "<in-memory>")
              .Distinct(StringComparer.OrdinalIgnoreCase)
              .ToArray();
            foreach (var missingAnalyzerReference in missingAnalyzerReferences)
            {
                diagnostics.Add(new WorkspaceInputDiagnostic(
                  "NLISSNWS024",
                  WorkspaceDiagnosticSeverity.Error,
                  $"Source-generator analyzer reference is missing: {missingAnalyzerReference}. Build the analyzer project for the selected configuration before loading the workspace.",
                  missingAnalyzerReference,
                  projectPath,
                  targetFramework));
            }

            if (missingAnalyzerReferences.Length > 0)
            {
                return null;
            }
        }

        IReadOnlyList<Document> sourceGeneratedDocuments = Array.Empty<Document>();
        if (options.GeneratorMode == WorkspaceGeneratorMode.Enabled)
        {
            try
            {
                var generatedDocuments = await project
                  .GetSourceGeneratedDocumentsAsync(cancellationToken)
                  .ConfigureAwait(false);
                sourceGeneratedDocuments = generatedDocuments.ToArray();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                diagnostics.Add(new WorkspaceInputDiagnostic(
                  "NLISSNWS023",
                  WorkspaceDiagnosticSeverity.Error,
                  $"Source-generator execution failed: {exception.Message}",
                  projectPath,
                  projectPath,
                  targetFramework));
                return null;
            }
        }

        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        if (compilation is not CSharpCompilation csharpCompilation)
        {
            diagnostics.Add(new WorkspaceInputDiagnostic(
              "NLISSNWS008",
              WorkspaceDiagnosticSeverity.Error,
              "The loaded project did not produce a C# compilation.",
              projectPath,
              projectPath,
              targetFramework));
            return null;
        }

        var parseOptions = project.ParseOptions as CSharpParseOptions ??
          csharpCompilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions;
        var compilationOptions = project.CompilationOptions as CSharpCompilationOptions ??
          csharpCompilation.Options as CSharpCompilationOptions;
        if (parseOptions is null || compilationOptions is null)
        {
            diagnostics.Add(new WorkspaceInputDiagnostic(
              "NLISSNWS009",
              WorkspaceDiagnosticSeverity.Error,
              "The loaded project did not expose C# parse and compilation options.",
              projectPath,
              projectPath,
              targetFramework));
            return null;
        }

        var generatedTrees = await GetSourceGeneratedTreesAsync(
          sourceGeneratedDocuments,
          cancellationToken).ConfigureAwait(false);
        var missingGeneratedTrees = generatedTrees
          .Where(tree => !csharpCompilation.SyntaxTrees.Contains(tree))
          .ToArray();
        if (missingGeneratedTrees.Length > 0)
        {
            csharpCompilation = csharpCompilation.AddSyntaxTrees(missingGeneratedTrees);
        }

        var compilationDiagnostics = csharpCompilation.GetDiagnostics(cancellationToken)
          .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
          .ToArray();
        if (options.RequireCleanCompilation && compilationDiagnostics.Length > 0)
        {
            foreach (var diagnostic in compilationDiagnostics)
            {
                diagnostics.Add(new WorkspaceInputDiagnostic(
                  IsReferenceDiagnostic(diagnostic) ? "NLISSNWS010" : "NLISSNWS011",
                  WorkspaceDiagnosticSeverity.Error,
                  diagnostic.ToString(),
                  GetDiagnosticPath(diagnostic, projectPath),
                  projectPath,
                  targetFramework));
            }

            return null;
        }

        var documents = await LoadDocumentsAsync(
          project,
          csharpCompilation,
          projectPath,
          options,
          sourceGeneratedDocuments,
          cancellationToken).ConfigureAwait(false);
        var references = BuildReferences(
          solution,
          project,
          projectEvaluation?.ProjectReferences ?? Array.Empty<MsBuildProjectReference>(),
          analyzerProjectReferences,
          targetFramework);
        if (references.Any(reference => !reference.Exists && reference.Kind != WorkspaceReferenceKind.Project))
        {
            diagnostics.Add(new WorkspaceInputDiagnostic(
              "NLISSNWS012",
              WorkspaceDiagnosticSeverity.Error,
              "The project contains an unresolved metadata reference.",
              projectPath,
              projectPath,
              targetFramework));
            return null;
        }

        var generatorReferenceCount = project.AnalyzerReferences
          .Count(IsExternalSourceGeneratorReference);
        if (generatorReferenceCount > 0 &&
            options.GeneratorMode == WorkspaceGeneratorMode.Disabled)
        {
            diagnostics.Add(new WorkspaceInputDiagnostic(
              "NLISSNWS013",
              WorkspaceDiagnosticSeverity.Warning,
              $"The project has {generatorReferenceCount} source-generator reference(s); source-generator execution was not explicitly enabled.",
              projectPath,
              projectPath,
              targetFramework));
        }

        var fingerprint = ComputeProjectFingerprint(
          projectPath,
          targetFramework,
          options,
          parseOptions,
          references,
          documents);
        return new WorkspaceProjectSnapshot(
          projectPath,
          project.Name,
          project.AssemblyName ?? project.Name,
          targetFramework,
          options.Configuration,
          options.Platform,
          csharpCompilation,
          parseOptions,
          compilationOptions,
          parseOptions.PreprocessorSymbolNames.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
          documents,
          references,
          project.AnalyzerReferences.Count,
          fingerprint);
    }

    private static async Task<IReadOnlyList<WorkspaceDocumentSnapshot>> LoadDocumentsAsync(
      Project project,
      CSharpCompilation compilation,
      string projectPath,
      WorkspaceInputOptions options,
      IReadOnlyList<Document> sourceGeneratedDocuments,
      CancellationToken cancellationToken)
    {
        var documents = new List<WorkspaceDocumentSnapshot>();
        var knownTrees = new HashSet<SyntaxTree>();
        var documentIndex = 0;
        foreach (var document in project.Documents.OrderBy(document => document.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            if (tree is null)
            {
                continue;
            }

            knownTrees.Add(tree);
            var source = (await document.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
            var filePath = ResolveDocumentPath(document.FilePath ?? tree.FilePath, projectPath, documentIndex);
            var generatedKind = ClassifyGeneratedSource(filePath, document.Name, false);
            var isGenerated = generatedKind != WorkspaceGeneratedSourceKind.None;
            if (isGenerated && options.GeneratedSourceMode == WorkspaceGeneratedSourceMode.Exclude)
            {
                documentIndex++;
                continue;
            }

            documents.Add(new WorkspaceDocumentSnapshot(
              filePath,
              source,
              tree,
              isGenerated,
              generatedKind,
              !isGenerated && File.Exists(filePath)));
            documentIndex++;
        }

        foreach (var document in sourceGeneratedDocuments.OrderBy(
          document => document.FilePath ?? document.Name,
          StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            if (tree is null || !knownTrees.Add(tree))
            {
                continue;
            }

            var source = (await document.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
            var filePath = ResolveDocumentPath(document.FilePath ?? tree.FilePath, projectPath, documentIndex);
            if (options.GeneratedSourceMode == WorkspaceGeneratedSourceMode.Exclude)
            {
                documentIndex++;
                continue;
            }

            documents.Add(new WorkspaceDocumentSnapshot(
              filePath,
              source,
              tree,
              true,
              WorkspaceGeneratedSourceKind.RoslynSourceGenerator,
              false));
            documentIndex++;
        }

        foreach (var tree in compilation.SyntaxTrees)
        {
            if (knownTrees.Contains(tree))
            {
                continue;
            }

            var source = (await tree.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
            var filePath = ResolveDocumentPath(tree.FilePath, projectPath, documentIndex);
            if (options.GeneratedSourceMode == WorkspaceGeneratedSourceMode.Exclude)
            {
                documentIndex++;
                continue;
            }

            documents.Add(new WorkspaceDocumentSnapshot(
              filePath,
              source,
              tree,
              true,
              WorkspaceGeneratedSourceKind.RoslynSourceGenerator,
              false));
            documentIndex++;
        }

        return documents
          .OrderBy(document => document.FilePath, StringComparer.Ordinal)
          .ToArray();
    }

    private static async Task<IReadOnlyList<SyntaxTree>> GetSourceGeneratedTreesAsync(
      IReadOnlyList<Document> sourceGeneratedDocuments,
      CancellationToken cancellationToken)
    {
        var trees = new List<SyntaxTree>(sourceGeneratedDocuments.Count);
        foreach (var document in sourceGeneratedDocuments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            if (tree is not null)
            {
                trees.Add(tree);
            }
        }

        return trees;
    }

    private static IReadOnlyList<WorkspaceReferenceSnapshot> BuildReferences(
      Solution solution,
      Project project,
      IReadOnlyList<MsBuildProjectReference> evaluatedProjectReferences,
      IReadOnlyList<WorkspaceReferenceSnapshot> analyzerProjectReferences,
      string? targetFramework)
    {
        var references = new List<WorkspaceReferenceSnapshot>(analyzerProjectReferences);
        foreach (var projectReference in project.ProjectReferences)
        {
            var referencedProjectPath = NormalizePhysicalPath(
              solution.GetProject(projectReference.ProjectId)?.FilePath);
            var evaluatedReference = evaluatedProjectReferences.FirstOrDefault(reference =>
              PathsEqual(reference.FullPath, referencedProjectPath));
            references.Add(new WorkspaceReferenceSnapshot(
              WorkspaceReferenceKind.Project,
              referencedProjectPath ?? projectReference.ProjectId.ToString(),
              referencedProjectPath,
              referencedProjectPath is not null && File.Exists(referencedProjectPath),
              referencedProjectPath,
              ResolveReferencedTargetFramework(evaluatedReference, targetFramework),
              evaluatedReference?.Metadata));
        }

        foreach (var metadataReference in project.MetadataReferences)
        {
            var filePath = NormalizePhysicalPath(
              (metadataReference as PortableExecutableReference)?.FilePath);
            references.Add(new WorkspaceReferenceSnapshot(
              ClassifyReference(filePath),
              metadataReference.Display ?? filePath ?? "<in-memory>",
              filePath,
              filePath is null || File.Exists(filePath)));
        }

        foreach (var analyzerReference in project.AnalyzerReferences)
        {
            var filePath = NormalizePhysicalPath(analyzerReference.FullPath);
            if (references.Any(reference =>
                  reference.Kind == WorkspaceReferenceKind.Analyzer &&
                  PathsEqual(reference.FilePath, filePath)))
            {
                continue;
            }

            var projectReference = analyzerProjectReferences.FirstOrDefault(reference =>
              PathsEqual(reference.FilePath, filePath));
            references.Add(new WorkspaceReferenceSnapshot(
              WorkspaceReferenceKind.Analyzer,
              filePath ?? "<in-memory>",
              filePath,
              filePath is null || File.Exists(filePath),
              projectReference?.ReferencedProjectPath,
              projectReference?.TargetFramework,
              projectReference?.MsBuildMetadata));
        }

        return references
          .OrderBy(reference => reference.Kind)
          .ThenBy(reference => reference.FilePath, StringComparer.OrdinalIgnoreCase)
          .ThenBy(reference => reference.Display, StringComparer.Ordinal)
          .ToArray();
    }

    private static async Task<IReadOnlyList<WorkspaceReferenceSnapshot>> ReadAnalyzerProjectReferencesAsync(
      string projectPath,
      WorkspaceInputOptions options,
      string? targetFramework,
      MsBuildProjectEvaluation projectEvaluation,
      ICollection<WorkspaceInputDiagnostic> diagnostics,
      CancellationToken cancellationToken)
    {
        var references = new List<WorkspaceReferenceSnapshot>();
        foreach (var projectReference in projectEvaluation.ProjectReferences)
        {
            if (!string.Equals(
                  projectReference.OutputItemType,
                  "Analyzer",
                  StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var referencedProjectPath = projectReference.FullPath;
            if (string.IsNullOrWhiteSpace(referencedProjectPath))
            {
                if (string.IsNullOrWhiteSpace(projectReference.Identity))
                {
                    diagnostics.Add(new WorkspaceInputDiagnostic(
                      "NLISSNWS025",
                      WorkspaceDiagnosticSeverity.Error,
                      "The Analyzer project reference has no identity or full path.",
                      projectPath,
                      projectPath,
                      targetFramework));
                    continue;
                }

                referencedProjectPath = NormalizePhysicalPath(Path.Combine(
                    Path.GetDirectoryName(projectPath)!,
                    projectReference.Identity));
            }

            if (referencedProjectPath is null)
            {
                diagnostics.Add(new WorkspaceInputDiagnostic(
                  "NLISSNWS025",
                  WorkspaceDiagnosticSeverity.Error,
                  $"The Analyzer project reference path could not be resolved: {projectReference.Identity}.",
                  projectPath,
                  projectPath,
                  targetFramework));
                continue;
            }

            var referencedTargetFramework = ResolveReferencedTargetFramework(
              projectReference,
              targetFramework);
            var referencedEvaluation = await EvaluateProjectAsync(
              referencedProjectPath,
              options with { TargetFramework = referencedTargetFramework },
              diagnostics,
              cancellationToken,
              requireSuccess: true).ConfigureAwait(false);
            var analyzerOutputPath = referencedEvaluation?.TargetPath;
            var exists = analyzerOutputPath is not null && File.Exists(analyzerOutputPath);
            references.Add(new WorkspaceReferenceSnapshot(
              WorkspaceReferenceKind.Analyzer,
              analyzerOutputPath ?? referencedProjectPath,
              analyzerOutputPath,
              exists,
              referencedProjectPath,
              referencedEvaluation?.TargetFramework ?? referencedTargetFramework,
              projectReference.Metadata));

            if (!exists && options.GeneratorMode == WorkspaceGeneratorMode.Enabled)
            {
                diagnostics.Add(new WorkspaceInputDiagnostic(
                  "NLISSNWS024",
                  WorkspaceDiagnosticSeverity.Error,
                  $"Source-generator project reference output is missing: {analyzerOutputPath ?? referencedProjectPath}. Build the analyzer project for the selected configuration before loading the workspace.",
                  analyzerOutputPath ?? referencedProjectPath,
                  projectPath,
                  targetFramework));
            }
        }

        return references;
    }

    private static async Task<MsBuildProjectEvaluation?> EvaluateProjectAsync(
      string projectPath,
      WorkspaceInputOptions options,
      ICollection<WorkspaceInputDiagnostic> diagnostics,
      CancellationToken cancellationToken,
      bool requireSuccess)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("-getProperty:TargetPath");
        startInfo.ArgumentList.Add("-getProperty:TargetFramework");
        startInfo.ArgumentList.Add("-getProperty:TargetFrameworks");
        startInfo.ArgumentList.Add("-getItem:ProjectReference");
        startInfo.ArgumentList.Add("-p:Configuration=" + options.Configuration);
        startInfo.ArgumentList.Add("-p:Platform=" + options.Platform);
        startInfo.ArgumentList.Add("-p:NuGetAudit=false");
        startInfo.ArgumentList.Add("-p:RestoreIgnoreFailedSources=true");
        if (!string.IsNullOrWhiteSpace(options.TargetFramework))
        {
            startInfo.ArgumentList.Add("-p:TargetFramework=" + options.TargetFramework);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                AddMsBuildEvaluationDiagnostic(
                  diagnostics,
                  options,
                  projectPath,
                  "dotnet msbuild could not start.",
                  requireSuccess);
                return null;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                var details = string.IsNullOrWhiteSpace(error) ? output : error;
                AddMsBuildEvaluationDiagnostic(
                  diagnostics,
                  options,
                  projectPath,
                  $"dotnet msbuild evaluation failed with exit code {process.ExitCode}: {details.Trim()}",
                  requireSuccess);
                return null;
            }

            return ParseProjectEvaluation(output);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        catch (Exception exception)
        {
            TryKill(process);
            AddMsBuildEvaluationDiagnostic(
              diagnostics,
              options,
              projectPath,
              exception.Message,
              requireSuccess);
            return null;
        }
    }

    private static MsBuildProjectEvaluation ParseProjectEvaluation(string output)
    {
        using var document = ParseMsBuildJson(output);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("MSBuild evaluation JSON root must be an object.");
        }

        var properties = root.TryGetProperty("Properties", out var propertiesElement)
          ? RequireJsonObject(propertiesElement, "Properties")
          : default;
        var targetPath = properties.ValueKind == JsonValueKind.Object
          ? NormalizePhysicalPath(ReadJsonString(properties, "TargetPath"))
          : null;
        var targetFramework = properties.ValueKind == JsonValueKind.Object
          ? ReadJsonString(properties, "TargetFramework")
          : null;
        var declaredTargetFrameworks = properties.ValueKind == JsonValueKind.Object
          ? SplitTargetFrameworks(
              ReadJsonString(properties, "TargetFrameworks"),
              targetFramework)
          : Array.Empty<string>();
        var projectReferences = new List<MsBuildProjectReference>();
        if (root.TryGetProperty("Items", out var itemsElement))
        {
            var items = RequireJsonObject(itemsElement, "Items");
            if (items.TryGetProperty("ProjectReference", out var projectReferenceItems))
            {
                if (projectReferenceItems.ValueKind != JsonValueKind.Array)
                {
                    throw new FormatException("MSBuild ProjectReference items must be an array.");
                }

                foreach (var item in projectReferenceItems.EnumerateArray())
                {
                    var projectReferenceItem = RequireJsonObject(item, "ProjectReference item");
                    projectReferences.Add(new MsBuildProjectReference(
                      ReadJsonString(projectReferenceItem, "Identity") ?? string.Empty,
                      NormalizePhysicalPath(ReadJsonString(projectReferenceItem, "FullPath")),
                      ReadJsonString(projectReferenceItem, "OutputItemType"),
                      ReadMsBuildMetadata(projectReferenceItem)));
                }
            }
        }

        return new MsBuildProjectEvaluation(
          targetPath,
          targetFramework,
          declaredTargetFrameworks,
          projectReferences);
    }

    private static JsonDocument ParseMsBuildJson(string output)
    {
        try
        {
            return JsonDocument.Parse(output);
        }
        catch (JsonException firstException)
        {
            var firstObject = output.IndexOf('{');
            var lastObject = output.LastIndexOf('}');
            if (firstObject >= 0 && lastObject > firstObject)
            {
                try
                {
                    return JsonDocument.Parse(output[firstObject..(lastObject + 1)]);
                }
                catch (JsonException)
                {
                    // Preserve the stable outer diagnostic while retaining the original parse failure.
                }
            }

            throw new FormatException(
              "MSBuild returned invalid JSON evaluation output.",
              firstException);
        }
    }

    private static JsonElement RequireJsonObject(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"MSBuild evaluation property '{propertyName}' must be an object.");
        }

        return element;
    }

    private static IReadOnlyList<string> SplitTargetFrameworks(params string?[] values)
    {
        return values
          .Where(value => !string.IsNullOrWhiteSpace(value))
          .SelectMany(value => value!.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
          .Where(value => !value.Contains("$(", StringComparison.Ordinal))
          .Distinct(StringComparer.OrdinalIgnoreCase)
          .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
          .ToArray();
    }

    private static string? ReadJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
          property.ValueKind == JsonValueKind.String
          ? property.GetString()
          : null;
    }

    private static string? ReadJsonString(JsonElement property)
    {
        return property.ValueKind == JsonValueKind.String
          ? property.GetString()
          : null;
    }

    private static IReadOnlyDictionary<string, string> ReadMsBuildMetadata(JsonElement item)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in item.EnumerateObject())
        {
            if (property.NameEquals("Identity") || property.NameEquals("FullPath"))
            {
                continue;
            }

            var value = ReadJsonString(property.Value);
            if (value is not null)
            {
                metadata[property.Name] = value;
            }
        }

        return metadata;
    }

    private static void AddMsBuildEvaluationDiagnostic(
      ICollection<WorkspaceInputDiagnostic> diagnostics,
      WorkspaceInputOptions options,
      string projectPath,
      string message,
      bool requireSuccess)
    {
        diagnostics.Add(new WorkspaceInputDiagnostic(
          requireSuccess ? "NLISSNWS025" : "NLISSNWS024",
          requireSuccess || options.GeneratorMode == WorkspaceGeneratorMode.Enabled
            ? WorkspaceDiagnosticSeverity.Error
            : WorkspaceDiagnosticSeverity.Warning,
          $"MSBuild project references could not be evaluated: {message}",
          projectPath,
          projectPath,
          options.TargetFramework));
    }

    private static async Task<string?> ResolveTargetFrameworkAsync(
      string projectPath,
      WorkspaceInputOptions options,
      IReadOnlyDictionary<string, IReadOnlyList<string>> loadedTargetFrameworks,
      ICollection<WorkspaceInputDiagnostic> diagnostics,
      CancellationToken cancellationToken)
    {
        var declarationEvaluation = await EvaluateProjectAsync(
          projectPath,
          options with { TargetFramework = null },
          diagnostics,
          cancellationToken,
          requireSuccess: true).ConfigureAwait(false);
        var declaredFrameworks = declarationEvaluation?.DeclaredTargetFrameworks ??
          Array.Empty<string>();
        if (declaredFrameworks.Count == 0 &&
            loadedTargetFrameworks.TryGetValue(projectPath, out var loadedFrameworks))
        {
            declaredFrameworks = loadedFrameworks;
        }

        var selected = options.TargetFramework;
        if (selected is null && declaredFrameworks.Count > 1)
        {
            diagnostics.Add(new WorkspaceInputDiagnostic(
              "NLISSNWS014",
              WorkspaceDiagnosticSeverity.Error,
              $"The project targets multiple frameworks ({string.Join(", ", declaredFrameworks)}); input.targetFramework is required.",
              projectPath,
              projectPath));
            return null;
        }

        if (selected is not null && declaredFrameworks.Count > 0 &&
            !declaredFrameworks.Contains(selected, StringComparer.OrdinalIgnoreCase))
        {
            diagnostics.Add(new WorkspaceInputDiagnostic(
              "NLISSNWS015",
              WorkspaceDiagnosticSeverity.Error,
              $"Target framework '{selected}' is not declared by the project. Declared values: {string.Join(", ", declaredFrameworks)}.",
              projectPath,
              projectPath,
              selected));
            return null;
        }

        return selected ?? declaredFrameworks.SingleOrDefault();
    }

    private static WorkspaceInputDiagnostic MapWorkspaceDiagnostic(
      Microsoft.CodeAnalysis.WorkspaceDiagnostic diagnostic)
    {
        var kind = diagnostic.Kind.ToString();
        var severity = kind.Contains("Failure", StringComparison.OrdinalIgnoreCase) ||
          kind.Contains("Error", StringComparison.OrdinalIgnoreCase)
          ? WorkspaceDiagnosticSeverity.Error
          : WorkspaceDiagnosticSeverity.Warning;
        return new WorkspaceInputDiagnostic(
          severity == WorkspaceDiagnosticSeverity.Error ? "NLISSNWS016" : "NLISSNWS017",
          severity,
          diagnostic.Message);
    }

    private static async Task<WorkspaceInputDiagnostic?> RestoreAsync(
      string inputPath,
      WorkspaceInputOptions options,
      CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(inputPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("restore");
        startInfo.ArgumentList.Add(inputPath);
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("--property:Configuration=" + options.Configuration);
        startInfo.ArgumentList.Add("--property:Platform=" + options.Platform);
        if (!string.IsNullOrWhiteSpace(options.TargetFramework))
        {
            startInfo.ArgumentList.Add("--property:TargetFramework=" + options.TargetFramework);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return new WorkspaceInputDiagnostic(
                  "NLISSNWS018",
                  WorkspaceDiagnosticSeverity.Error,
                  "dotnet restore could not start.",
                  inputPath);
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            if (process.ExitCode == 0)
            {
                return null;
            }

            var details = string.IsNullOrWhiteSpace(error) ? output : error;
            return new WorkspaceInputDiagnostic(
              "NLISSNWS019",
              WorkspaceDiagnosticSeverity.Error,
              $"dotnet restore failed with exit code {process.ExitCode}: {details.Trim()}",
              inputPath);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        catch (Exception exception)
        {
            TryKill(process);
            return new WorkspaceInputDiagnostic(
              "NLISSNWS020",
              WorkspaceDiagnosticSeverity.Error,
              $"dotnet restore failed to execute: {exception.Message}",
              inputPath);
        }
    }

    private static void TryKill(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }

    private sealed record MsBuildProjectEvaluation(
      string? TargetPath,
      string? TargetFramework,
      IReadOnlyList<string> DeclaredTargetFrameworks,
      IReadOnlyList<MsBuildProjectReference> ProjectReferences);

    private sealed record MsBuildProjectReference(
      string Identity,
      string? FullPath,
      string? OutputItemType,
      IReadOnlyDictionary<string, string> Metadata);

    private static string? NormalizePhysicalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return path.Contains("://", StringComparison.Ordinal)
          ? path
          : Path.GetFullPath(path);
    }

    private static bool PathsEqual(string? left, string? right)
    {
        return string.Equals(
          NormalizePhysicalPath(left),
          NormalizePhysicalPath(right),
          StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveReferencedTargetFramework(
      MsBuildProjectReference? projectReference,
      string? fallback)
    {
        if (projectReference is null)
        {
            return fallback;
        }

        foreach (var metadataName in new[] { "SetTargetFramework", "TargetFramework" })
        {
            if (projectReference.Metadata.TryGetValue(metadataName, out var metadataValue))
            {
                var targetFramework = ExtractTargetFramework(metadataValue);
                if (targetFramework is not null)
                {
                    return targetFramework;
                }
            }
        }

        return fallback;
    }

    private static string? ExtractTargetFramework(string value)
    {
        foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=', StringComparison.Ordinal);
            if (separator < 0)
            {
                return part;
            }

            var propertyName = part[..separator].Trim();
            if (string.Equals(propertyName, "TargetFramework", StringComparison.OrdinalIgnoreCase))
            {
                var targetFramework = part[(separator + 1)..].Trim();
                return string.IsNullOrWhiteSpace(targetFramework) ? null : targetFramework;
            }
        }

        return null;
    }

    private static string ResolveProjectSelector(string inputPath, string projectSelector)
    {
        var solutionDirectory = Path.GetDirectoryName(inputPath)!;
        return Path.GetFullPath(Path.Combine(solutionDirectory, projectSelector));
    }

    private static string ResolveDocumentPath(string? filePath, string projectPath, int index)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            return filePath.Contains("://", StringComparison.Ordinal)
              ? filePath
              : Path.GetFullPath(filePath);
        }

        return $"generated://{Path.GetFileNameWithoutExtension(projectPath)}/{index:D4}.g.cs";
    }

    private static WorkspaceGeneratedSourceKind ClassifyGeneratedSource(
      string filePath,
      string documentName,
      bool compilerGenerated)
    {
        if (compilerGenerated)
        {
            return WorkspaceGeneratedSourceKind.RoslynSourceGenerator;
        }

        if (filePath.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase) ||
            filePath.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceGeneratedSourceKind.IntermediateOutput;
        }

        if (GeneratedSuffixes.Any(suffix =>
              filePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
              documentName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
        {
            return WorkspaceGeneratedSourceKind.MsBuildCompileItem;
        }

        return WorkspaceGeneratedSourceKind.None;
    }

    private static WorkspaceReferenceKind ClassifyReference(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return WorkspaceReferenceKind.Unknown;
        }

        if (filePath.Contains("\\.nuget\\packages\\", StringComparison.OrdinalIgnoreCase) ||
            filePath.Contains("/.nuget/packages/", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceReferenceKind.Package;
        }

        if (filePath.Contains("Reference Assemblies", StringComparison.OrdinalIgnoreCase) ||
            filePath.Contains("\\packs\\", StringComparison.OrdinalIgnoreCase) ||
            filePath.Contains("/packs/", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceReferenceKind.Framework;
        }

        return WorkspaceReferenceKind.Metadata;
    }

    private sealed class TargetFrameworkLoadProgress : IProgress<ProjectLoadProgress>
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, HashSet<string>> _frameworksByProject = new(
          StringComparer.OrdinalIgnoreCase);

        public void Report(ProjectLoadProgress value)
        {
            if (value.Operation != ProjectLoadOperation.Resolve ||
                string.IsNullOrWhiteSpace(value.FilePath) ||
                string.IsNullOrWhiteSpace(value.TargetFramework))
            {
                return;
            }

            var projectPath = Path.GetFullPath(value.FilePath);
            lock (_gate)
            {
                if (!_frameworksByProject.TryGetValue(projectPath, out var frameworks))
                {
                    frameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    _frameworksByProject.Add(projectPath, frameworks);
                }

                frameworks.Add(value.TargetFramework);
            }
        }

        public IReadOnlyDictionary<string, IReadOnlyList<string>> Snapshot()
        {
            lock (_gate)
            {
                return _frameworksByProject.ToDictionary(
                  entry => entry.Key,
                  entry => (IReadOnlyList<string>)entry.Value
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                  StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    private static bool IsExternalSourceGeneratorReference(AnalyzerReference reference)
    {
        if (string.IsNullOrWhiteSpace(reference.FullPath))
        {
            return false;
        }

        var normalizedPath = reference.FullPath.Replace('\\', '/');
        if (normalizedPath.Contains("/.nuget/packages/", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Contains("/dotnet/sdk/", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Contains("/dotnet/packs/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return reference.GetGenerators(LanguageNames.CSharp).Any();
    }

    private static bool HasSourceGenerator(AnalyzerReference reference)
    {
        try
        {
            return reference.GetGenerators(LanguageNames.CSharp).Any();
        }
        catch
        {
            return true;
        }
    }

    private static bool IsReferenceDiagnostic(Diagnostic diagnostic)
    {
        return diagnostic.Id is "CS0006" or "CS0012" or "CS0234" or "CS0246";
    }

    private static string GetDiagnosticPath(Diagnostic diagnostic, string fallback)
    {
        return diagnostic.Location.IsInSource && diagnostic.Location.SourceTree?.FilePath is { Length: > 0 } path
          ? path
          : fallback;
    }

    private static string ComputeProjectFingerprint(
      string projectPath,
      string? targetFramework,
      WorkspaceInputOptions options,
      CSharpParseOptions parseOptions,
      IReadOnlyList<WorkspaceReferenceSnapshot> references,
      IReadOnlyList<WorkspaceDocumentSnapshot> documents)
    {
        var builder = new StringBuilder();
        builder.AppendLine(projectPath);
        builder.AppendLine(targetFramework);
        builder.AppendLine(options.Configuration);
        builder.AppendLine(options.Platform);
        builder.AppendLine(options.GeneratedSourceMode.ToString());
        builder.AppendLine(options.GeneratorMode.ToString());
        builder.AppendLine(parseOptions.LanguageVersion.ToString());
        foreach (var symbol in parseOptions.PreprocessorSymbolNames.OrderBy(value => value, StringComparer.Ordinal))
        {
            builder.AppendLine(symbol);
        }

        foreach (var reference in references)
        {
            builder.AppendLine(
              $"{reference.Kind}|{reference.Display}|{reference.FilePath}|{reference.Exists}|" +
              $"{reference.ReferencedProjectPath}|{reference.TargetFramework}");
            foreach (var metadata in reference.MsBuildMetadata
              .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine($"metadata|{metadata.Key}|{metadata.Value}");
            }
        }

        foreach (var document in documents.OrderBy(document => document.FilePath, StringComparer.Ordinal))
        {
            builder.AppendLine(
              $"{document.FilePath}|{document.IsGenerated}|{document.GeneratedSourceKind}|{document.CanWrite}");
            builder.AppendLine(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(document.Source))));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static string ComputeSolutionFingerprint(
      string inputPath,
      WorkspaceInputOptions options,
      IReadOnlyList<WorkspaceProjectSnapshot> projects)
    {
        var builder = new StringBuilder();
        builder.AppendLine(inputPath);
        builder.AppendLine(options.TargetFramework);
        builder.AppendLine(options.Configuration);
        builder.AppendLine(options.Platform);
        builder.AppendLine(options.GeneratedSourceMode.ToString());
        builder.AppendLine(options.GeneratorMode.ToString());
        builder.AppendLine(options.TargetDocumentPath);
        foreach (var project in projects)
        {
            builder.AppendLine(project.ProjectPath);
            builder.AppendLine(project.Fingerprint);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
