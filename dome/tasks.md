# Dome Project Documentation Tasks

This file tracks the progress of adding or translating documentation comments for functions in the project.

## Completed Tasks

- [x] `src\Analysis\Roslyn\ReferenceZeroPredictionAnalyzer.cs`
    - `Predict` (Overload 1)
    - `Predict` (Overload 2)
- [x] `src\Analysis\Roslyn\FunctionImpactAnalyzer.cs`
    - `Analyze` (Overload 1)
- [x] `src\Analysis\Roslyn\WorkspaceLoaders.cs`
    - `IWorkspaceLoader.LoadAsync`
    - `CodeAnalysisWorkspaceLoader` (Class)
    - `CodeAnalysisWorkspaceLoader.LoadAsync`
    - `CodeAnalysisWorkspaceLoader.EnsureMsBuildRegistered`
    - `CodeAnalysisWorkspaceLoader.BuildResultFromSolutionAsync`
    - `CodeAnalysisWorkspaceLoader.BuildResultFromProjectAsync`
    - `CodeAnalysisWorkspaceLoader.BuildDocumentContextsAsync`
    - `WorkspaceLoadCoordinator` (Class)
    - `WorkspaceLoadCoordinator.LoadAsync`
    - `WorkspaceLoadCoordinator.GetSourceOnlyRoot`
- [x] `src\Analysis\Roslyn\AnalysisContext.cs`
    - `AnalysisContext` (Class - Translate to Chinese)
    - `Snapshot`
    - `Services`
    - `View`
    - `Inheritance`
    - `References`
    - `FunctionIndex`
    - `FunctionFacts`
    - `StatementFacts`
    - `Statements`
    - `FunctionGraphs`
    - `Create`
- [x] `src\Analysis\Roslyn\SourceWorkspaceLoader.cs`
    - `SourceWorkspaceLoader` (Class)
    - `LoadAsync`
    - `LoadFromRootAsync`
    - `LoadInternalAsync`
- [x] `src\Analysis\Roslyn\FunctionGraphProvider.cs`
- [x] `src\Analysis\Roslyn\StatementAnalysisService.cs`
- [x] `src\Analysis\Roslyn\RoslynAnalysisEngine.cs`
- [x] `src\Core\Models.cs`
- [x] `src\Application\DomeApplication.cs`
- [x] `src\Rules\MarkingRuleEngine.cs`
- [x] `src\Application\DomeApplicationFactory.cs`
- [x] `src\Cli\DomeCliParser.cs`
- [x] `src\Rewrite\Roslyn\RoslynRewriteExecutor.cs`
- [x] `src\Reporting\JsonArtifactWriter.cs`
- [x] `src\Plan\AuditPlanCompiler.cs`
- [x] `src\Cli\Program.cs`
- [x] `src\Analysis\Roslyn\SymbolRefProjector.cs`
- [x] `src\Analysis\Roslyn\QueryServices.cs`
- [x] `src\Analysis\Roslyn\MetadataTypeIdBuilder.cs`
- [x] `src\Analysis\Roslyn\MetadataMemberIdBuilder.cs`
- [x] `src\Analysis\Roslyn\DirectiveReader.cs`

## Pending Tasks

- [x] `src\Analysis\Roslyn\RoslynAnalysisEngine.cs`
- [x] `src\Analysis\Roslyn\WorkspaceLoaders.cs`
- [x] `src\Application\ArtifactPlanBuilder.cs`
- [x] `src\Application\DomeApplication.cs`
- [x] `src\Application\DomeApplicationFactory.cs`
- [x] `src\Application\RunReportBuilder.cs`
- [x] `src\Application\TerrariaRuntimeApplication.cs`
- [x] `src\Application\TerrariaRuntimeBuildExecutor.cs`
- [x] `src\Application\TerrariaRuntimeEnvironmentBuilder.cs`
- [x] `src\Core\Models.cs`
- [x] `src\Rewrite\Roslyn\RoslynRewriteExecutor.cs`
- [x] `src\Rules\BoundaryPromotionEngine.cs`
- [x] `src\Rules\CompatibilityExecutionContextFactory.cs`
- [ ] `src\Rules\MarkingRuleEngine.cs`
- [x] `src\Rules\StatementPropagationEngine.cs`
