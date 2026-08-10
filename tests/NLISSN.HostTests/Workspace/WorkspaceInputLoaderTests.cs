using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Infrastructure.Workspace;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class WorkspaceInputLoaderTests
{
    [Fact]
    public async Task LoadSolution_UsesRealCompilationProjectReferencesSymbolsGeneratedSourcesAndFrameworkReferences()
    {
        var solutionPath = FixturePath("WorkspaceFixture.sln");
        var result = await new MsBuildWorkspaceInputLoader().LoadAsync(
          new WorkspaceInputOptions(
            solutionPath,
            TargetFramework: "net10.0",
            Configuration: "Debug",
            Platform: "AnyCPU",
            RestoreMode: WorkspaceRestoreMode.Disabled));

        Assert.True(result.IsSuccess, FormatDiagnostics(result.Diagnostics));
        var snapshot = Assert.IsType<WorkspaceSolutionSnapshot>(result.Snapshot);
        Assert.True(snapshot.IsSolution);
        Assert.Equal(3, snapshot.Projects.Count);

        var app = Assert.Single(snapshot.Projects, project => project.ProjectName == "App");
        Assert.Equal("net10.0", app.TargetFramework);
        Assert.Contains("WORKSPACE_DEBUG", app.PreprocessorSymbols);
        Assert.DoesNotContain("WORKSPACE_RELEASE", app.PreprocessorSymbols);
        Assert.Contains(app.Documents, document => document.IsGenerated && document.FilePath.EndsWith("Generated.g.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(app.References, reference =>
          reference.Kind == WorkspaceReferenceKind.Project &&
          reference.FilePath?.EndsWith("Library.csproj", StringComparison.OrdinalIgnoreCase) == true);
        var libraryProjectReference = Assert.Single(app.References, reference =>
          reference.Kind == WorkspaceReferenceKind.Project &&
          reference.FilePath?.EndsWith("Library.csproj", StringComparison.OrdinalIgnoreCase) == true);
        AssertReferenceMetadata(
          libraryProjectReference,
          "SetTargetFramework",
          "TargetFramework=net10.0");
        var portableReferencePaths = app.Compilation.References
          .OfType<PortableExecutableReference>()
          .Select(reference => reference.FilePath)
          .OfType<string>()
          .Select(Path.GetFullPath)
          .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var frameworkReferences = app.References
          .Where(reference => reference.Kind == WorkspaceReferenceKind.Framework)
          .ToArray();
        Assert.NotEmpty(frameworkReferences);
        Assert.Contains(
          frameworkReferences,
          reference => reference.FilePath is not null &&
            portableReferencePaths.Contains(Path.GetFullPath(reference.FilePath)));
        Assert.Contains(app.Compilation.References, reference => reference is CompilationReference);

        var generatorProject = Assert.Single(snapshot.Projects, project =>
          project.ProjectPath.EndsWith("Workspace.Generator.csproj", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(generatorProject.References, reference => reference.Kind == WorkspaceReferenceKind.Package);

        var appDocument = Assert.Single(app.Documents, document => document.FilePath.EndsWith("App.cs", StringComparison.OrdinalIgnoreCase));
        var semanticModel = app.Compilation.GetSemanticModel(appDocument.SyntaxTree);
        var root = appDocument.SyntaxTree.GetRoot();
        var libraryReference = root.DescendantNodes()
          .OfType<IdentifierNameSyntax>()
          .Single(identifier => identifier.Identifier.ValueText == "LibraryEntry");
        var generatedDocument = Assert.Single(app.Documents, document =>
          document.FilePath.EndsWith("Generated.g.cs", StringComparison.OrdinalIgnoreCase));
        var generatedSemanticModel = app.Compilation.GetSemanticModel(generatedDocument.SyntaxTree);
        var generatedType = generatedDocument.SyntaxTree.GetRoot()
          .DescendantNodes()
          .OfType<ClassDeclarationSyntax>()
          .Single(declaration => declaration.Identifier.ValueText == "GeneratedValue");
        Assert.Equal("Workspace.Library.LibraryEntry", semanticModel.GetSymbolInfo(libraryReference).Symbol?.ToDisplayString());
        Assert.Equal("Workspace.App.GeneratedValue", generatedSemanticModel.GetDeclaredSymbol(generatedType)?.ToDisplayString());
    }

    [Fact]
    public async Task LoadSolution_ProjectSelectorLimitsAnalysisToOneProject()
    {
        var solutionPath = FixturePath("WorkspaceFixture.sln");
        var appPath = FixturePath("App", "App.csproj");
        var result = await new MsBuildWorkspaceInputLoader().LoadAsync(
          new WorkspaceInputOptions(solutionPath, ProjectPath: appPath));

        Assert.True(result.IsSuccess, FormatDiagnostics(result.Diagnostics));
        Assert.Single(result.Snapshot!.Projects);
        Assert.Equal(Path.GetFullPath(appPath), result.Snapshot.Projects[0].ProjectPath);
        Assert.Equal(Path.GetFullPath(appPath), result.Snapshot.SelectedProjectPath);
    }

    [Fact]
    public async Task LoadProject_AnalyzesOnlyTheRequestedProjectWhileKeepingProjectReferenceMetadata()
    {
      var appPath = FixturePath("App", "App.csproj");
      var result = await new MsBuildWorkspaceInputLoader().LoadAsync(
          new WorkspaceInputOptions(appPath, TargetFramework: "net10.0"));

        Assert.True(result.IsSuccess, FormatDiagnostics(result.Diagnostics));
        var app = Assert.Single(result.Snapshot!.Projects);
        Assert.Equal(Path.GetFullPath(appPath), app.ProjectPath);
        Assert.Contains(app.References, reference =>
          reference.Kind == WorkspaceReferenceKind.Project &&
          reference.FilePath?.EndsWith("Library.csproj", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task LoadProject_RestoreEnabledUsesTheWorkspacePackageCache()
    {
        var appPath = FixturePath("App", "App.csproj");
        var result = await new MsBuildWorkspaceInputLoader().LoadAsync(
          new WorkspaceInputOptions(
            appPath,
            TargetFramework: "net10.0",
            RestoreMode: WorkspaceRestoreMode.Enabled));

        Assert.True(result.IsSuccess, FormatDiagnostics(result.Diagnostics));
        Assert.Single(result.Snapshot!.Projects);
    }

    [Fact]
    public async Task LoadProject_MultipleTargetFrameworksRequiresExplicitTargetFramework()
    {
      var projectPath = FixturePath("MultiTarget", "MultiTarget.csproj");
      var result = await new MsBuildWorkspaceInputLoader().LoadAsync(
          new WorkspaceInputOptions(projectPath));

        Assert.False(result.IsSuccess);
      Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "NLISSNWS014");
    }

    [Fact]
    public async Task LoadProject_RejectsAnUndeclaredTargetFramework()
    {
        var projectPath = FixturePath("Conditional", "Conditional.csproj");
        var result = await new MsBuildWorkspaceInputLoader().LoadAsync(
          new WorkspaceInputOptions(projectPath, TargetFramework: "netstandard2.0"));

        Assert.False(result.IsSuccess);
        var diagnostic = Assert.Single(
          result.Diagnostics,
          diagnostic => diagnostic.Code == "NLISSNWS015");
        Assert.Equal(Path.GetFullPath(projectPath), diagnostic.ProjectPath);
        Assert.Equal("netstandard2.0", diagnostic.TargetFramework);
        Assert.Contains("net10.0", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadProject_ImportedMultipleTargetFrameworksRequiresExplicitTargetFramework()
    {
        var projectPath = FixturePath("ImportedMultiTarget", "ImportedMultiTarget.csproj");
        var result = await new MsBuildWorkspaceInputLoader().LoadAsync(
          new WorkspaceInputOptions(projectPath));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "NLISSNWS014");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("net10.0", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("netstandard2.0", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LoadProject_ReleaseConfigurationUsesReleaseConditionalSymbols()
    {
      var appPath = FixturePath("Conditional", "Conditional.csproj");
      var result = await new MsBuildWorkspaceInputLoader().LoadAsync(
          new WorkspaceInputOptions(
            appPath,
            TargetFramework: "net10.0",
            Configuration: "Release"));

        Assert.True(result.IsSuccess, FormatDiagnostics(result.Diagnostics));
        var app = Assert.Single(result.Snapshot!.Projects);
      Assert.Contains("WORKSPACE_RELEASE", app.PreprocessorSymbols);
      Assert.DoesNotContain("WORKSPACE_DEBUG", app.PreprocessorSymbols);
    }

    [Fact]
    public async Task LoadProject_PlatformConditionUsesPlatformSymbol()
    {
      var projectPath = FixturePath("Conditional", "Conditional.csproj");
      var result = await new MsBuildWorkspaceInputLoader().LoadAsync(
          new WorkspaceInputOptions(
            projectPath,
            TargetFramework: "net10.0",
            Configuration: "Debug",
            Platform: "x64"));

      Assert.True(result.IsSuccess, FormatDiagnostics(result.Diagnostics));
      var project = Assert.Single(result.Snapshot!.Projects);
      Assert.Contains("WORKSPACE_X64", project.PreprocessorSymbols);
    }

    [Fact]
    public async Task LoadSolution_InvalidProjectSelectorReturnsStableDiagnostic()
    {
      var solutionPath = FixturePath("WorkspaceFixture.sln");
      var selector = Path.Combine("Missing", "Missing.csproj");
      var result = await new MsBuildWorkspaceInputLoader().LoadAsync(
        new WorkspaceInputOptions(solutionPath, ProjectPath: selector));

      Assert.False(result.IsSuccess);
      var diagnostic = Assert.Single(
        result.Diagnostics,
        diagnostic => diagnostic.Code == "NLISSNWS007");
      Assert.Equal(
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(solutionPath)!, selector)),
        diagnostic.Path);
    }

    [Fact]
    public async Task LoadSolution_RepeatedLoadsProduceDeterministicSnapshot()
    {
      var solutionPath = FixturePath("WorkspaceFixture.sln");
      var options = new WorkspaceInputOptions(
        solutionPath,
        TargetFramework: "net10.0",
        Configuration: "Debug",
        Platform: "AnyCPU");

      var first = await new MsBuildWorkspaceInputLoader().LoadAsync(options);
      var second = await new MsBuildWorkspaceInputLoader().LoadAsync(options);

      Assert.True(first.IsSuccess, FormatDiagnostics(first.Diagnostics));
      Assert.True(second.IsSuccess, FormatDiagnostics(second.Diagnostics));
      Assert.Equal(first.Snapshot!.Fingerprint, second.Snapshot!.Fingerprint);
      Assert.Equal(
        first.Snapshot.Projects.Select(project => project.ProjectPath),
        second.Snapshot.Projects.Select(project => project.ProjectPath));

      foreach (var (firstProject, secondProject) in first.Snapshot.Projects.Zip(second.Snapshot.Projects))
      {
        Assert.Equal(firstProject.Fingerprint, secondProject.Fingerprint);
        Assert.Equal(
          firstProject.Documents.Select(document =>
            $"{document.FilePath}|{document.IsGenerated}|{document.GeneratedSourceKind}|{document.CanWrite}"),
          secondProject.Documents.Select(document =>
            $"{document.FilePath}|{document.IsGenerated}|{document.GeneratedSourceKind}|{document.CanWrite}"));
        Assert.Equal(
          firstProject.References.Select(ReferenceSignature),
          secondProject.References.Select(ReferenceSignature));
      }

      static string ReferenceSignature(WorkspaceReferenceSnapshot reference)
      {
        var metadata = string.Join(
          ";",
          reference.MsBuildMetadata
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => $"{entry.Key}={entry.Value}"));
        return $"{reference.Kind}|{reference.Display}|{reference.FilePath}|{reference.Exists}|" +
          $"{reference.ReferencedProjectPath}|{reference.TargetFramework}|{metadata}";
      }
    }

    [Fact]
    public async Task LoadProject_CompilationErrorsFailClosedWithProjectAndTargetFramework()
    {
      var root = CreateTemporaryProject(
        "CompileError",
        """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
          </PropertyGroup>
        </Project>
        """,
        """
        namespace Workspace.CompileError;
        public sealed class BrokenType
        {
          public int Value => "not an integer";
        }
        """);
      try
      {
        var projectPath = Path.Combine(root, "CompileError.csproj");
        var result = await new MsBuildWorkspaceInputLoader().LoadAsync(
          new WorkspaceInputOptions(
            projectPath,
            TargetFramework: "net10.0",
            RestoreMode: WorkspaceRestoreMode.Enabled));

        Assert.False(result.IsSuccess);
        var diagnostic = Assert.Single(
          result.Diagnostics,
          diagnostic => diagnostic.Code == "NLISSNWS011");
        Assert.Equal(Path.GetFullPath(projectPath), diagnostic.ProjectPath);
        Assert.Equal("net10.0", diagnostic.TargetFramework);
        Assert.Contains("CS0029", diagnostic.Message, StringComparison.Ordinal);
      }
      finally
      {
        DeleteTemporaryDirectory(root);
      }
    }

    [Fact]
    public async Task LoadProject_UnresolvedReferenceFailsClosedWithReferenceDiagnostic()
    {
      var root = CreateTemporaryProject(
        "MissingReference",
        """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
          </PropertyGroup>
          <ItemGroup>
            <Reference Include="Missing.Reference">
              <HintPath>missing\Missing.Reference.dll</HintPath>
            </Reference>
          </ItemGroup>
        </Project>
        """,
        """
        namespace Workspace.MissingReference;
        public sealed class MissingConsumer
        {
          public Missing.Reference.MissingType Value { get; }
        }
        """);
      try
      {
        var projectPath = Path.Combine(root, "MissingReference.csproj");
        var result = await new MsBuildWorkspaceInputLoader().LoadAsync(
          new WorkspaceInputOptions(
            projectPath,
            TargetFramework: "net10.0",
            RestoreMode: WorkspaceRestoreMode.Enabled));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, diagnostic =>
          diagnostic.Code == "NLISSNWS010" || diagnostic.Code == "NLISSNWS012");
        Assert.Contains(result.Diagnostics, diagnostic =>
          string.Equals(diagnostic.ProjectPath, Path.GetFullPath(projectPath), StringComparison.OrdinalIgnoreCase) &&
          string.Equals(diagnostic.TargetFramework, "net10.0", StringComparison.OrdinalIgnoreCase));
      }
      finally
      {
        DeleteTemporaryDirectory(root);
      }
    }

    [Fact]
    public async Task LoadProject_CanceledTokenPropagatesCancellation()
    {
      using var cancellation = new CancellationTokenSource();
      cancellation.Cancel();

      await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
        new MsBuildWorkspaceInputLoader().LoadAsync(
          new WorkspaceInputOptions(
            FixturePath("App", "App.csproj"),
            TargetFramework: "net10.0"),
          cancellation.Token));
    }

    [Fact]
    public async Task LoadProject_FingerprintIncludesProjectReferenceMetadata()
    {
        var root = Path.Combine(
          Path.GetTempPath(),
          $"nlissn-workspace-fingerprint-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var libraryDirectory = Path.Combine(root, "Library");
            Directory.CreateDirectory(libraryDirectory);
            File.WriteAllText(
              Path.Combine(libraryDirectory, "Library.csproj"),
              """
              <Project Sdk="Microsoft.NET.Sdk">
                <PropertyGroup>
                  <TargetFramework>net10.0</TargetFramework>
                  <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
                  <GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute>
                </PropertyGroup>
              </Project>
              """);
            File.WriteAllText(
              Path.Combine(libraryDirectory, "Library.cs"),
              "namespace Fingerprint.Library; public sealed class Entry { }");

            var appPath = Path.Combine(root, "App.csproj");
            File.WriteAllText(appPath, ProjectReferenceProject(includeGlobalPropertiesToRemove: false));
            var first = await new MsBuildWorkspaceInputLoader().LoadAsync(
              new WorkspaceInputOptions(appPath, TargetFramework: "net10.0"));

            Assert.True(first.IsSuccess, FormatDiagnostics(first.Diagnostics));
            var firstProject = Assert.Single(first.Snapshot!.Projects);
            var firstReference = Assert.Single(
              firstProject.References,
              reference => reference.Kind == WorkspaceReferenceKind.Project);
            Assert.Empty(firstReference.MsBuildMetadata.GetValueOrDefault(
              "GlobalPropertiesToRemove",
              string.Empty));

            File.WriteAllText(appPath, ProjectReferenceProject(includeGlobalPropertiesToRemove: true));
            var second = await new MsBuildWorkspaceInputLoader().LoadAsync(
              new WorkspaceInputOptions(appPath, TargetFramework: "net10.0"));

            Assert.True(second.IsSuccess, FormatDiagnostics(second.Diagnostics));
            var secondProject = Assert.Single(second.Snapshot!.Projects);
            var secondReference = Assert.Single(
              secondProject.References,
              reference => reference.Kind == WorkspaceReferenceKind.Project);
            AssertReferenceMetadata(
              secondReference,
              "GlobalPropertiesToRemove",
              "Platform");
            Assert.NotEqual(firstProject.Fingerprint, secondProject.Fingerprint);

            string ProjectReferenceProject(bool includeGlobalPropertiesToRemove)
            {
                var metadataText = includeGlobalPropertiesToRemove
                  ? " GlobalPropertiesToRemove=\"Platform\""
                  : string.Empty;
                return $"""
                  <Project Sdk="Microsoft.NET.Sdk">
                    <PropertyGroup>
                      <TargetFramework>net10.0</TargetFramework>
                      <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
                      <GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute>
                    </PropertyGroup>
                    <ItemGroup>
                      <ProjectReference Include="Library\Library.csproj"{metadataText} />
                    </ItemGroup>
                  </Project>
                  """;
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LoadProject_DisabledSourceGeneratorsWarnsOnlyForExternalGeneratorReferences()
    {
        var appPath = FixturePath("App", "App.csproj");
        var result = await new MsBuildWorkspaceInputLoader().LoadAsync(
          new WorkspaceInputOptions(appPath, TargetFramework: "net10.0"));

        Assert.True(result.IsSuccess, FormatDiagnostics(result.Diagnostics));
        Assert.Contains(result.Diagnostics, diagnostic =>
          diagnostic.Code == "NLISSNWS013" &&
          diagnostic.ProjectPath?.EndsWith("App.csproj", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
          diagnostic.Code == "NLISSNWS013" &&
          diagnostic.ProjectPath?.EndsWith("Generator.csproj", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task LoadProject_EnabledSourceGeneratorUsesAdditionalTextAndAnalyzerConfigWithoutWriteback()
    {
        var appPath = FixturePath("App", "App.csproj");
        var result = await new MsBuildWorkspaceInputLoader().LoadAsync(
          new WorkspaceInputOptions(
            appPath,
            TargetFramework: "net10.0",
            Configuration: "Debug",
            GeneratorMode: WorkspaceGeneratorMode.Enabled));

        Assert.True(result.IsSuccess, FormatDiagnostics(result.Diagnostics));
        var app = Assert.Single(result.Snapshot!.Projects);
        var generated = Assert.Single(app.Documents, document =>
          document.FilePath.EndsWith("GeneratedByDriver.g.cs", StringComparison.OrdinalIgnoreCase));
        Assert.True(generated.IsGenerated);
        Assert.Equal(WorkspaceGeneratedSourceKind.RoslynSourceGenerator, generated.GeneratedSourceKind);
        Assert.False(generated.CanWrite);
        var analyzerReference = Assert.Single(app.References, reference =>
          reference.Kind == WorkspaceReferenceKind.Analyzer &&
          reference.ReferencedProjectPath?.EndsWith("Workspace.Generator.csproj", StringComparison.OrdinalIgnoreCase) == true &&
          reference.Exists);
        AssertReferenceMetadata(analyzerReference, "OutputItemType", "Analyzer");
        AssertReferenceMetadata(analyzerReference, "ReferenceOutputAssembly", "false");
        AssertReferenceMetadata(
          analyzerReference,
          "SetTargetFramework",
          "TargetFramework=net10.0");
        Assert.Contains("source-generator", generated.Source, StringComparison.Ordinal);
        Assert.Contains("additional-from-msbuild", generated.Source, StringComparison.Ordinal);
        Assert.Contains("configured-from-analyzer-config", generated.Source, StringComparison.Ordinal);

        var generatedSemanticModel = app.Compilation.GetSemanticModel(generated.SyntaxTree);
        var generatedType = generated.SyntaxTree.GetRoot()
          .DescendantNodes()
          .OfType<ClassDeclarationSyntax>()
          .Single(declaration => declaration.Identifier.ValueText == "GeneratedByDriver");
        Assert.Equal(
          "Workspace.App.GeneratedByDriver",
          generatedSemanticModel.GetDeclaredSymbol(generatedType)?.ToDisplayString());
    }

    [Fact]
    public async Task LoadProject_EnabledSourceGeneratorsFailClosedWhenProjectAnalyzerOutputIsMissing()
    {
        var appPath = FixturePath("App", "App.csproj");
        var result = await new MsBuildWorkspaceInputLoader().LoadAsync(
          new WorkspaceInputOptions(
            appPath,
            TargetFramework: "net10.0",
            Configuration: "NLISSNWorkspaceMissingGenerator",
            GeneratorMode: WorkspaceGeneratorMode.Enabled));

        Assert.False(result.IsSuccess, FormatDiagnostics(result.Diagnostics));
        Assert.Contains(result.Diagnostics, diagnostic =>
          diagnostic.Code == "NLISSNWS024" &&
          diagnostic.ProjectPath?.EndsWith("App.csproj", StringComparison.OrdinalIgnoreCase) == true &&
          diagnostic.TargetFramework == "net10.0" &&
          diagnostic.Message.Contains("analyzer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LoadSolution_ExcludeGeneratedSourcesKeepsCompilationButOmitsWritableDocument()
    {
        var solutionPath = FixturePath("WorkspaceFixture.sln");
        var result = await new MsBuildWorkspaceInputLoader().LoadAsync(
          new WorkspaceInputOptions(
            solutionPath,
            TargetFramework: "net10.0",
            GeneratedSourceMode: WorkspaceGeneratedSourceMode.Exclude));

        Assert.True(result.IsSuccess, FormatDiagnostics(result.Diagnostics));
        var app = Assert.Single(result.Snapshot!.Projects, project => project.ProjectName == "App");
        Assert.DoesNotContain(app.Documents, document => document.FilePath.EndsWith("Generated.g.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(app.Compilation.SyntaxTrees, tree => tree.FilePath.EndsWith("Generated.g.cs", StringComparison.OrdinalIgnoreCase));
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

    private static string CreateTemporaryProject(
      string projectName,
      string projectText,
      string sourceText)
    {
      var root = Path.Combine(
        Path.GetTempPath(),
        $"nlissn-workspace-{projectName.ToLowerInvariant()}-{Guid.NewGuid():N}");
      Directory.CreateDirectory(root);
      File.WriteAllText(Path.Combine(root, $"{projectName}.csproj"), projectText);
      File.WriteAllText(Path.Combine(root, $"{projectName}.cs"), sourceText);
      return root;
    }

    private static void DeleteTemporaryDirectory(string root)
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }

    private static string FormatDiagnostics(IReadOnlyList<WorkspaceInputDiagnostic> diagnostics)
    {
        return string.Join(Environment.NewLine, diagnostics.Select(diagnostic =>
          $"{diagnostic.Code}: {diagnostic.Message}"));
    }

    private static void AssertReferenceMetadata(
      WorkspaceReferenceSnapshot reference,
      string metadataName,
      string expectedValue)
    {
        var metadataProperty = typeof(WorkspaceReferenceSnapshot).GetProperty("MsBuildMetadata");
        Assert.NotNull(metadataProperty);
        var metadata = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(
          metadataProperty!.GetValue(reference));
        Assert.True(metadata.TryGetValue(metadataName, out var actualValue));
        Assert.Equal(expectedValue, actualValue);
    }
}
