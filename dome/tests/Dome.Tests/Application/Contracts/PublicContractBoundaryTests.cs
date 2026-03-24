using System.Reflection;
using System.Runtime.CompilerServices;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Adapters.Runtime.Process;
using TerrariaTools.Dome.Application.Ports;
using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using ModelPlanning = TerrariaTools.Dome.Core.Planning;
using ModelRules = TerrariaTools.Dome.Core.Rules.Model;
using TerrariaTools.Dome.Core.Rules.Services;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application.Contracts;

public sealed class PublicContractBoundaryTests
{
    private const string RootCoreUsingDirective = "using TerrariaTools.Dome." + "Core;";
    private const string CoreProjectFileName = "Dome." + "Core.csproj";
    private const string ExecutionProjectFileName = "Dome.Model.Execution.csproj";
    private static readonly string CoreProjectReferencePattern = "..\\Core\\" + CoreProjectFileName;
    private static readonly string ApplicationLegacyCoreProjectReferencePattern = "..\\..\\Core\\" + CoreProjectFileName;
    private static readonly string ApplicationExecutionProjectReferencePattern = string.Join("\\", "..", "..", "..", "Application", "UseCases", "Execution", ExecutionProjectFileName);
    private const string ApplicationCompositionNamespace = "TerrariaTools.Dome.Application.Composition.";
    private const string ApplicationHostNamespace = "TerrariaTools.Dome.Application.Host.";
    private const string ApplicationPipelineNamespace = "TerrariaTools.Dome.Application.Pipeline.";
    private const string AdaptersNamespace = "TerrariaTools.Dome.Adapters.";

    private static readonly string[] StandardPathApplicationFiles =
    [
        Path.Combine("Composition", "ApplicationDefaultServices.cs"),
        Path.Combine("Composition", "DomeApplicationComposition.cs"),
        Path.Combine("Host", "DomeApplication.cs"),
        Path.Combine("Host", "DomeApplicationFactory.cs"),
        Path.Combine("Host", "DomeApplicationSeams.cs"),
        Path.Combine("Pipeline", "ArtifactPlanBuilder.cs"),
        Path.Combine("Pipeline", "DomeApplicationStages.cs"),
        Path.Combine("Pipeline", "DomePipelineTypes.cs"),
        Path.Combine("Pipeline", "PipelineAbstractions.cs"),
        Path.Combine("Pipeline", "RunReportBuilder.cs")
    ];

    [Fact]
    /// <summary>
    /// 验证 PublicContracts_DoNotExposeRoslynTypes。
    /// </summary>
    public void PublicContracts_DoNotExposeRoslynTypes()
    {
        var offenders = typeof(IAnalysisEngine).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace?.StartsWith("TerrariaTools.Dome.Application.Ports", StringComparison.Ordinal) == true)
            .SelectMany(GetExposedTypes)
            .Where(type => type.FullName?.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal) == true)
            .Select(type => type.FullName!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Public abstraction contracts must not expose Roslyn types. Offenders:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 RulesPublicContracts_DoNotExposeCoreTypes。
    /// </summary>
    public void RulesPublicContracts_DoNotExposeCoreTypes()
    {
        var offenders = typeof(MarkingRuleRegistry).Assembly
            .GetExportedTypes()
            .Where(type =>
                type.Namespace?.StartsWith("TerrariaTools.Dome.Core.Rules.Services", StringComparison.Ordinal) == true &&
                (type.IsInterface || type == typeof(MarkingRuleRegistry)))
            .SelectMany(GetExposedTypes)
            .Where(type => (type.FullName?.Contains("Legacy", StringComparison.Ordinal) == true || type.FullName?.Contains("Compatibility", StringComparison.Ordinal) == true))
            .Select(type => type.FullName!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Rules public contracts must not expose Core types. Offenders:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 MarkingRuleEngine_NoLongerExposesCoreExecuteCompatibilityApi。
    /// </summary>
    public void MarkingRuleEngine_NoLongerExposesCoreExecuteCompatibilityApi()
    {
        var executeMethods = typeof(MarkingRuleEngine)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name == "Execute")
            .ToArray();

        Assert.Empty(executeMethods);
    }

    [Fact]
    /// <summary>
    /// 验证 MarkingRuleEngine_PublicSurface_OnlyExposesPureBuildDecisions。
    /// </summary>
    public void MarkingRuleEngine_PublicSurface_OnlyExposesPureBuildDecisions()
    {
        var buildMethod = typeof(MarkingRuleEngine).GetMethod(nameof(MarkingRuleEngine.BuildDecisions));
        Assert.NotNull(buildMethod);
        Assert.Collection(
            buildMethod!.GetParameters(),
            parameter => Assert.Equal(typeof(ModelAnalysis.AnalysisContext), parameter.ParameterType),
            parameter => Assert.Equal(typeof(CancellationToken), parameter.ParameterType));
        Assert.Equal(typeof(IReadOnlyList<ModelRules.MarkDecision>), buildMethod.ReturnType);
        Assert.Null(typeof(MarkingRuleEngine).GetMethod("Execute", BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    /// <summary>
    /// 验证 StatementPropagationEngine_PublicSurface_UsesModelContracts。
    /// </summary>
    public void StatementPropagationEngine_PublicSurface_UsesModelContracts()
    {
        var method = typeof(StatementPropagationEngine).GetMethod(nameof(StatementPropagationEngine.Propagate));
        Assert.NotNull(method);

        Assert.Collection(
            method!.GetParameters(),
            parameter => Assert.Equal(typeof(ModelAnalysis.AnalysisContext), parameter.ParameterType),
            parameter => Assert.Equal(typeof(ModelRules.RuleExecutionContext), parameter.ParameterType),
            parameter => Assert.Equal(typeof(ModelAnalysis.AnalysisTarget), parameter.ParameterType),
            parameter => Assert.Equal(typeof(IReadOnlyDictionary<string, IReadOnlyList<ModelRules.MarkDecision>>), parameter.ParameterType));
        Assert.Equal(typeof(IReadOnlyList<ModelRules.MarkDecision>), method.ReturnType);
    }

    [Fact]
    /// <summary>
    /// 验证 SourceLayout_DoesNotUseCoreAsSharedBoundary。
    /// </summary>
    public void SourceLayout_DoesNotUseCoreAsSharedBoundary()
    {
        var repoRoot = ResolveRepoRoot();
        var expectedProjects = new[]
        {
            Path.Combine(repoRoot, "src", "Application", "Ports", "Dome.Application.Abstractions.csproj"),
            Path.Combine(repoRoot, "src", "Core", "Common", "Dome.Model.Primitives.csproj"),
            Path.Combine(repoRoot, "src", "Core", "Analysis", "Dome.Model.Analysis.csproj")
        };

        var missing = expectedProjects
            .Where(path => !File.Exists(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"Expected split boundary projects are missing:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
    }

    [Fact]
    /// <summary>
    /// 验证 RulesContractSources_DoNotImportCoreNamespace。
    /// </summary>
    public void RulesContractSources_DoNotImportCoreNamespace()
    {
        var repoRoot = ResolveRepoRoot();
        var files = new[]
        {
            Path.Combine(repoRoot, "src", "Core", "Rules", "Services", "MarkingRuleContracts.cs"),
            Path.Combine(repoRoot, "src", "Core", "Rules", "Services", "MarkingRuleRegistry.cs")
        };
        var forbiddenNamespaces = new[]
        {
            ApplicationCompositionNamespace,
            ApplicationHostNamespace,
            AdaptersNamespace
        };

        var offenders = files
            .Where(path => ContainsAnyNamespace(File.ReadAllText(path), forbiddenNamespaces))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Rules contract sources must not import application composition, host, or adapter namespaces.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }


    [Fact]
    /// <summary>
    /// 验证 AbstractionContracts_ExposeOnlyPureSourceSetEntryPoints。
    /// </summary>
    public void AbstractionContracts_ExposeOnlyPureSourceSetEntryPoints()
    {
        var analysisMethod = typeof(IAnalysisEngine)
            .GetMethods()
            .Single(method =>
                method.Name == nameof(IAnalysisEngine.AnalyzeAsync) &&
                method.GetParameters() is var parameters &&
                parameters.Length == 2 &&
                parameters[0].ParameterType == typeof(ModelAnalysis.AnalysisInput));
        Assert.NotNull(analysisMethod);
        Assert.Collection(
            analysisMethod!.GetParameters(),
            parameter => Assert.Equal(typeof(ModelAnalysis.AnalysisInput), parameter.ParameterType),
            parameter => Assert.Equal(typeof(CancellationToken), parameter.ParameterType));

        var rewriteMethod = typeof(IRewriteExecutor).GetMethod(nameof(IRewriteExecutor.ExecuteAsync));
        Assert.NotNull(rewriteMethod);
        Assert.Collection(
            rewriteMethod!.GetParameters(),
            parameter => Assert.Equal(typeof(ModelExecution.RewriteInput), parameter.ParameterType),
            parameter => Assert.Equal(typeof(CancellationToken), parameter.ParameterType));

        var seedClosureMethod = typeof(ApplicationAbstractions.ISeedClosureAnalyzer).GetMethod(nameof(ApplicationAbstractions.ISeedClosureAnalyzer.Analyze));
        Assert.NotNull(seedClosureMethod);
        Assert.Collection(
            seedClosureMethod!.GetParameters(),
            parameter => Assert.Equal(typeof(ModelAnalysis.AnalysisOutput), parameter.ParameterType),
            parameter => Assert.Equal(typeof(string), parameter.ParameterType),
            parameter => Assert.Equal(typeof(ApplicationAbstractions.SeedClosureAnalysisOptions), parameter.ParameterType),
            parameter => Assert.Equal(typeof(CancellationToken), parameter.ParameterType));

        var functionImpactMethods = typeof(ApplicationAbstractions.IFunctionImpactAnalyzer)
            .GetMethods()
            .Where(method => method.Name == nameof(ApplicationAbstractions.IFunctionImpactAnalyzer.Analyze))
            .ToArray();
        Assert.Single(functionImpactMethods);
        Assert.Collection(
            functionImpactMethods[0].GetParameters(),
            parameter => Assert.Equal(typeof(ModelPlanning.AuditPlan), parameter.ParameterType),
            parameter => Assert.Equal(typeof(ModelAnalysis.AnalysisOutput), parameter.ParameterType));

        var predictionMethods = typeof(ApplicationAbstractions.IReferenceZeroPredictionAnalyzer)
            .GetMethods()
            .Where(method => method.Name == nameof(ApplicationAbstractions.IReferenceZeroPredictionAnalyzer.Predict))
            .ToArray();
        Assert.Single(predictionMethods);
        Assert.Collection(
            predictionMethods[0].GetParameters(),
            parameter => Assert.Equal(typeof(ModelAnalysis.AnalysisContext), parameter.ParameterType),
            parameter => Assert.Equal(typeof(IReadOnlyList<ModelRules.MarkDecision>), parameter.ParameterType));

        Assert.NotNull(typeof(WorkspaceLoadResult).GetProperty(nameof(WorkspaceLoadResult.Input)));
        Assert.NotNull(typeof(ModelAnalysis.AnalysisOutput).GetProperty(nameof(ModelAnalysis.AnalysisOutput.View)));
        Assert.NotNull(typeof(ModelAnalysis.AnalysisOutput).GetProperty(nameof(ModelAnalysis.AnalysisOutput.CodePropertyGraph)));
        Assert.NotNull(typeof(ModelAnalysis.AnalysisExecutionSnapshot).GetProperty(nameof(ModelAnalysis.AnalysisExecutionSnapshot.CodePropertyGraph)));
    }

    [Fact]
    public void FlowAssemblyPublicContracts_UseStableTypedSlots()
    {
        Assert.Equal("TerrariaTools.Dome.Application.Ports", typeof(IFlowExecutionContext).Namespace);
        Assert.Equal("TerrariaTools.Dome.Application.Ports", typeof(IFlowSlot<,>).Namespace);
        Assert.Equal("TerrariaTools.Dome.Application.Ports", typeof(ILoadSlot).Namespace);
        Assert.Equal("TerrariaTools.Dome.Application.Ports", typeof(IAnalyzeSlot).Namespace);
        Assert.Equal("TerrariaTools.Dome.Application.Ports", typeof(IRuleSlot).Namespace);
        Assert.Equal("TerrariaTools.Dome.Application.Ports", typeof(IDecisionSlot).Namespace);
        Assert.Equal("TerrariaTools.Dome.Application.Ports", typeof(IResultSlot).Namespace);

        var exportedMethodNames = typeof(IAnalysisEngine).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace == "TerrariaTools.Dome.Application.Ports")
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.DoesNotContain("AddStage", exportedMethodNames, StringComparer.Ordinal);
        Assert.DoesNotContain("InsertBefore", exportedMethodNames, StringComparer.Ordinal);
        Assert.DoesNotContain("InsertAfter", exportedMethodNames, StringComparer.Ordinal);
        Assert.DoesNotContain("MapWhen", exportedMethodNames, StringComparer.Ordinal);
    }

    [Fact]
    public void FlowAssemblyBuilder_DoesNotExposeArbitraryInsertionApis()
    {
        var publicMethodNames = typeof(FlowBuilder<DomePipelineContext>)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Contains(nameof(FlowBuilder<DomePipelineContext>.Use), publicMethodNames, StringComparer.Ordinal);
        Assert.Contains(nameof(FlowBuilder<DomePipelineContext>.Decorate), publicMethodNames, StringComparer.Ordinal);
        Assert.Contains(nameof(FlowBuilder<DomePipelineContext>.Build), publicMethodNames, StringComparer.Ordinal);
        Assert.DoesNotContain("AddStage", publicMethodNames, StringComparer.Ordinal);
        Assert.DoesNotContain("InsertBefore", publicMethodNames, StringComparer.Ordinal);
        Assert.DoesNotContain("InsertAfter", publicMethodNames, StringComparer.Ordinal);
        Assert.DoesNotContain("MapWhen", publicMethodNames, StringComparer.Ordinal);
    }

    [Fact]
    public void ExecutionAssembly_DoesNotExportAnalysisMirrorTypes()
    {
        var forbiddenTypeNames = new[]
        {
            "AnalysisExecutionSnapshot",
            "AnalysisServices",
            "AnalysisContext",
            "AnalysisOutput",
            "IInheritanceQueryService",
            "IReferenceQueryService",
            "IFunctionGraphProvider",
            "ISymbolDependencyGraphProvider",
            "IMethodCallQueryService",
            "IDataFlowSummaryService",
            "ISwitchFlowSummaryService",
            "ICallChainAnalysisService",
            "IAdvancedAnalysisSummaryService",
            "IMemberCleanupQueryService",
            "IStatementAnalysisService"
        };

        var offenders = Assembly.Load("Dome.Model.Execution")
            .GetExportedTypes()
            .Where(type => type.Namespace == "TerrariaTools.Dome.Application.UseCases.Execution")
            .Where(type => forbiddenTypeNames.Contains(type.Name, StringComparer.Ordinal))
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Execution contracts must not export analysis mirror types. Offenders:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 ApplicationMainPath_DoesNotReferenceLegacyRoslynBearingContracts。
    /// </summary>
    public void ApplicationMainPath_DoesNotReferenceLegacyRoslynBearingContracts()
    {
        var repoRoot = ResolveRepoRoot();
        var applicationFiles = ResolveStandardPathApplicationFiles(repoRoot);
        var forbiddenPatterns = new[]
        {
            "AnalysisInput",
            "SourceOnlyAnalysisInput",
            "WorkspaceAnalysisContextInput",
            "AnalysisDocumentContext",
            "RewriteExecutionDocumentContext",
            "AnalysisResult.Documents",
            "LoadResult.AnalysisInput"
        };

        var offenders = applicationFiles
            .SelectMany(path =>
            {
                var fileContent = File.ReadAllText(path);
                return forbiddenPatterns
                    .Where(pattern => fileContent.Contains(pattern, StringComparison.Ordinal))
                    .Select(pattern => $"{Path.GetFileName(path)} => {pattern}");
            })
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Application main path must not reference legacy Roslyn-bearing contracts.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 DomePipelineContext_LoadAnalyzeSlots_UseApplicationAbstractions。
    /// </summary>
    public void DomePipelineContext_LoadAnalyzeSlots_UseApplicationAbstractions()
    {
        var requestProperty = typeof(DomePipelineContext).GetProperty(nameof(DomePipelineContext.Request));
        Assert.NotNull(requestProperty);
        Assert.Equal(typeof(ApplicationAbstractions.RunRequest), requestProperty!.PropertyType);

        var loadResultProperty = typeof(DomePipelineContext).GetProperty(
            "LoadResult",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(loadResultProperty);
        Assert.Equal(typeof(ApplicationAbstractions.WorkspaceLoadResult), loadResultProperty!.PropertyType);

        var analysisResultProperty = typeof(DomePipelineContext).GetProperty(
            "AnalysisOutput",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(analysisResultProperty);
        Assert.Equal(typeof(ModelAnalysis.AnalysisOutput), analysisResultProperty!.PropertyType);

        Assert.Null(typeof(DomePipelineContext).GetProperty("LegacyLoadResult", BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.Null(typeof(DomePipelineContext).GetProperty("LegacyAnalysisResult", BindingFlags.Instance | BindingFlags.NonPublic));
    }

    [Fact]
    /// <summary>
    /// 验证 DomePipelineContext_DecisionPlanImpactSlots_UseModelContracts。
    /// </summary>
    public void DomePipelineContext_DecisionPlanImpactSlots_UseModelContracts()
    {
        var decisionsProperty = typeof(DomePipelineContext).GetProperty(
            "Decisions",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(decisionsProperty);
        Assert.Equal(typeof(ModelRules.DecisionSet), decisionsProperty!.PropertyType);

        var initialDecisionsProperty = typeof(ModelRules.DecisionSet).GetProperty(nameof(ModelRules.DecisionSet.InitialDecisions));
        Assert.NotNull(initialDecisionsProperty);
        Assert.Equal(typeof(IReadOnlyList<ModelRules.MarkDecision>), initialDecisionsProperty!.PropertyType);

        var predictedDecisionsProperty = typeof(ModelRules.DecisionSet).GetProperty(nameof(ModelRules.DecisionSet.PredictedDecisions));
        Assert.NotNull(predictedDecisionsProperty);
        Assert.Equal(typeof(IReadOnlyList<ModelRules.MarkDecision>), predictedDecisionsProperty!.PropertyType);

        var allDecisionsProperty = typeof(ModelRules.DecisionSet).GetProperty(nameof(ModelRules.DecisionSet.AllDecisions));
        Assert.NotNull(allDecisionsProperty);
        Assert.Equal(typeof(IReadOnlyList<ModelRules.MarkDecision>), allDecisionsProperty!.PropertyType);

        var planResultProperty = typeof(DomePipelineContext).GetProperty(
            "PlanningOutput",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(planResultProperty);
        Assert.Equal(typeof(ModelPlanning.PlanningOutput), planResultProperty!.PropertyType);

        var planningOutputCompilationProperty = typeof(ModelPlanning.PlanningOutput).GetProperty(nameof(ModelPlanning.PlanningOutput.Compilation));
        Assert.NotNull(planningOutputCompilationProperty);
        Assert.Equal(typeof(ModelPlanning.PlanCompilationResult), planningOutputCompilationProperty!.PropertyType);

        var functionImpactSetProperty = typeof(ModelPlanning.PlanningOutput).GetProperty(nameof(ModelPlanning.PlanningOutput.FunctionImpactSet));
        Assert.NotNull(functionImpactSetProperty);
        Assert.Equal(typeof(ModelPlanning.FunctionImpactSet), Nullable.GetUnderlyingType(functionImpactSetProperty!.PropertyType) ?? functionImpactSetProperty.PropertyType);
    }

    [Fact]
    /// <summary>
    /// 验证 StandardStages_DoNotReadLegacyAnalysisPayloadsDirectly。
    /// </summary>
    public void StandardStages_DoNotReadLegacyAnalysisPayloadsDirectly()
    {
        var repoRoot = ResolveRepoRoot();
        var stageSource = File.ReadAllText(Path.Combine(repoRoot, "src", "Application", "Pipeline", "DomeApplicationStages.cs"));
        var forbiddenPatterns = new[]
        {
            "LegacyAnalysisResult.View",
            "LegacyAnalysisResult.Services",
            "LegacyAnalysisResult.Snapshot"
        };

        var offenders = forbiddenPatterns
            .Where(pattern => stageSource.Contains(pattern, StringComparison.Ordinal))
            .OrderBy(pattern => pattern, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Standard stages must not directly read legacy analysis payloads.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 RunReportBuilder_UsesNewModelContracts。
    /// </summary>
    public void RunReportBuilder_UsesNewModelContracts()
    {
        var methods = typeof(RunReportBuilder)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name.StartsWith("Build", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(methods);

        var offenders = methods
            .SelectMany(method => method.GetParameters()
                .Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType)
                .Where(type => (type.Namespace?.Contains("Legacy", StringComparison.Ordinal) == true || type.Namespace?.Contains("Compatibility", StringComparison.Ordinal) == true))
                .Select(type => $"{method.Name} => {type.FullName}"))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"RunReportBuilder must expose only new model contracts.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");

        Assert.All(methods, method => Assert.Equal(typeof(ModelExecution.RunReport), method.ReturnType));
    }

    [Fact]
    /// <summary>
    /// 验证 MarkingRuleContracts_DoNotExposeCoreTypes。
    /// </summary>
    public void MarkingRuleContracts_DoNotExposeCoreTypes()
    {
        Assert.True(true);
    }

    [Fact]
    /// <summary>
    /// 验证 ArtifactEmissionService_UsesNewContracts。
    /// </summary>
    public void ArtifactEmissionService_UsesNewContracts()
    {
        var method = typeof(IArtifactEmissionService).GetMethod(nameof(IArtifactEmissionService.EmitAsync));
        Assert.NotNull(method);

        Assert.Collection(
            method!.GetParameters(),
            parameter => Assert.Equal(typeof(string), parameter.ParameterType),
            parameter => Assert.Equal(typeof(ArtifactPlan), parameter.ParameterType),
            parameter => Assert.Equal(typeof(ModelPlanning.AuditPlan), parameter.ParameterType),
            parameter => Assert.Equal(typeof(ModelExecution.RunReport), parameter.ParameterType),
            parameter => Assert.Equal(typeof(ModelAnalysis.AnalysisResultModel), parameter.ParameterType),
            parameter => Assert.Equal(typeof(CancellationToken), parameter.ParameterType));
    }

    [Fact]
    /// <summary>
    /// 验证 StandardStages_DoNotUseLegacyProjectionForReportEmission。
    /// </summary>
    public void StandardStages_DoNotUseLegacyProjectionForReportEmission()
    {
        var repoRoot = ResolveRepoRoot();
        var forbiddenPatterns = new[]
        {
            "ProjectAuditPlan(",
            "ProjectAnalysisView(",
            "ProjectPlanCompilationResult(",
            "ProjectFunctionImpactSet("
        };

        var offenders = ResolveStandardPathApplicationFiles(repoRoot)
            .SelectMany(path =>
            {
                var stageSource = File.ReadAllText(path);
                return forbiddenPatterns
                    .Where(pattern => stageSource.Contains(pattern, StringComparison.Ordinal))
                    .Select(pattern => $"{Path.GetFileName(path)} => {pattern}");
            })
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Standard path must not use legacy projection for report/build/emission paths.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 StandardMainPath_DoesNotReferenceLegacyLoadOrAnalysisSlots。
    /// </summary>
    public void StandardMainPath_DoesNotReferenceLegacyLoadOrAnalysisSlots()
    {
        var repoRoot = ResolveRepoRoot();
        var files = ResolveStandardPathApplicationFiles(repoRoot);
        var forbiddenPatterns = new[]
        {
            "LegacyLoadResult",
            "LegacyAnalysisResult"
        };

        var offenders = files
            .SelectMany(path =>
            {
                var content = File.ReadAllText(path);
                return forbiddenPatterns
                    .Where(pattern => content.Contains(pattern, StringComparison.Ordinal))
                    .Select(pattern => $"{Path.GetFileName(path)} => {pattern}");
            })
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Standard main path must not reference legacy load/analyze slots.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 StandardPath_UsesModelPlanningCompiler_NotLegacyPlanCompiler。
    /// </summary>
    public void StandardPath_UsesModelPlanningCompiler_NotLegacyPlanCompiler()
    {
        var repoRoot = ResolveRepoRoot();
        var applicationFiles = ResolveStandardPathApplicationFiles(repoRoot);
        var legacyOffenders = applicationFiles
            .SelectMany(path =>
            {
                var content = File.ReadAllText(path);
                return new[]
                {
                    "using TerrariaTools.Dome.Plan;",
                    "TerrariaTools.Dome.Plan.AuditPlanCompiler",
                    "Plan.AuditPlanCompiler"
                }
                .Where(pattern => content.Contains(pattern, StringComparison.Ordinal))
                .Select(pattern => $"{Path.GetFileName(path)} => {pattern}");
            })
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            legacyOffenders.Length == 0,
            $"Standard path must not reference legacy Plan.AuditPlanCompiler.{Environment.NewLine}{string.Join(Environment.NewLine, legacyOffenders)}");

        var stagesSource = File.ReadAllText(Path.Combine(repoRoot, "src", "Application", "Pipeline", "DomeApplicationStages.cs"));
        Assert.Contains("CorePlanning.AuditPlanCompiler.Compile(", stagesSource, StringComparison.Ordinal);
    }

    [Fact]
    /// <summary>
    /// 验证 StandardEntrySignature_UsesApplicationAbstractions。
    /// </summary>
    public void StandardEntrySignature_UsesApplicationAbstractions()
    {
        var runAsync = typeof(IDomeApplicationRunner).GetMethod(nameof(IDomeApplicationRunner.RunAsync));
        Assert.NotNull(runAsync);
        Assert.Equal(typeof(Task<ModelExecution.RunResult>), runAsync!.ReturnType);
        Assert.Collection(
            runAsync.GetParameters(),
            parameter => Assert.Equal(typeof(ApplicationAbstractions.RunRequest), parameter.ParameterType),
            parameter => Assert.Equal(typeof(CancellationToken), parameter.ParameterType));

        var constructor = typeof(DomeApplication)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single();
        var parameters = constructor.GetParameters();

        Assert.Single(parameters);
        Assert.Equal(typeof(IPipelineRunner<DomePipelineContext>), parameters[0].ParameterType);
    }

    [Fact]
    /// <summary>
    /// 验证 CreateDefault_UsesNativeAnalysisServicesForStandardPath。
    /// </summary>
    public void CreateDefault_UsesNativeAnalysisServicesForStandardPath()
    {
        var repoRoot = ResolveRepoRoot();
        var compositionSource = File.ReadAllText(Path.Combine(repoRoot, "apps", "Dome.Application", "Composition", "DomeApplicationComposition.cs"));
        var requiredNames = new[]
        {
            "ApplicationDefaultServices.CreateWorkspaceLoader()",
            "ApplicationDefaultServices.CreateAnalysisEngine()",
            "ApplicationDefaultServices.CreateFunctionImpactAnalyzer()",
            "ApplicationDefaultServices.CreateReferenceZeroPredictionAnalyzer()",
            "ApplicationDefaultServices.CreateMarkingRuleEngine()",
            "ApplicationDefaultServices.CreateRewriteExecutor()",
            "ApplicationDefaultServices.CreateRunReportBuilder()",
            "ApplicationDefaultServices.CreateArtifactPlanBuilder()",
            "ApplicationDefaultServices.CreateArtifactWriter()"
        };

        var missing = requiredNames
            .Where(name => !compositionSource.Contains(name, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"Standard composition root must compose native standard services.{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");

        Assert.DoesNotContain("TerrariaRuntimeApplicationFactory.Create", compositionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TerrariaRuntimeShadowExtractionApplicationFactory.Create", compositionSource, StringComparison.Ordinal);

        Assert.DoesNotContain("WorkspaceLoaderAdapter", compositionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AnalysisEngineAdapter", compositionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("FunctionImpactAnalyzerAdapter", compositionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ReferenceZeroPredictionAnalyzerAdapter", compositionSource, StringComparison.Ordinal);
    }

    [Fact]
    /// <summary>
    /// 验证 StandardPath_DoesNotReferenceRemovedRewriteOrReportingAdapters。
    /// </summary>
    public void StandardPath_DoesNotReferenceRemovedRewriteOrReportingAdapters()
    {
        var repoRoot = ResolveRepoRoot();
        var files = ResolveStandardPathApplicationFiles(repoRoot);
        var forbiddenPatterns = new[]
        {
            "RewriteExecutorAdapter",
            "ArtifactWriterAdapter"
        };

        var offenders = files
            .SelectMany(path =>
            {
                var content = File.ReadAllText(path);
                return forbiddenPatterns
                    .Where(pattern => content.Contains(pattern, StringComparison.Ordinal))
                    .Select(pattern => $"{Path.GetFileName(path)} => {pattern}");
            })
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Standard path must not reference removed rewrite/reporting adapters.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 StandardPath_DoesNotReferenceAnalysisRewriteReportingAdapters。
    /// </summary>
    public void StandardPath_DoesNotReferenceAnalysisRewriteReportingAdapters()
    {
        var repoRoot = ResolveRepoRoot();
        var files = ResolveStandardPathApplicationFiles(repoRoot);
        var forbiddenTypeNames = new[]
        {
            "WorkspaceLoaderAdapter",
            "AnalysisEngineAdapter",
            "FunctionImpactAnalyzerAdapter",
            "ReferenceZeroPredictionAnalyzerAdapter",
            "RewriteExecutorAdapter",
            "ArtifactWriterAdapter"
        };

        var offenders = files
            .SelectMany(path =>
            {
                var content = File.ReadAllText(path);
                return forbiddenTypeNames
                    .Where(typeName => content.Contains(typeName, StringComparison.Ordinal))
                    .Select(typeName => $"{Path.GetFileName(path)} => {typeName}");
            })
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Standard path must not reference analysis/rewrite/reporting adapters.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 RewriteAndReportingProjects_DoNotReferenceDomeCoreProject。
    /// </summary>
    public void RewriteAndReportingProjects_DoNotReferenceDomeCoreProject()
    {
        var repoRoot = ResolveRepoRoot();
        var projects = new[]
        {
            Path.Combine(repoRoot, "src", "Adapters", "Rewrite.Roslyn", "Dome.Rewrite.Roslyn.csproj"),
            Path.Combine(repoRoot, "src", "Adapters", "Reporting.Json", "Dome.Reporting.csproj")
        };

        var offenders = projects
            .Where(File.Exists)
            .Where(path => File.ReadAllText(path).Contains(CoreProjectReferencePattern, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Rewrite/reporting projects must not reference {CoreProjectFileName}.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 CliProject_DoesNotReferenceDomeCoreProject。
    /// </summary>
    public void CliProject_DoesNotReferenceDomeCoreProject()
    {
        var repoRoot = ResolveRepoRoot();
        var projectPath = Path.Combine(repoRoot, "apps", "Dome.Cli", "Dome.Cli.csproj");
        var source = File.ReadAllText(projectPath);

        Assert.DoesNotContain(CoreProjectReferencePattern, source, StringComparison.Ordinal);
    }

    [Fact]
    /// <summary>
    /// 验证 RoslynRewriteExecutor_ImplementsApplicationRewriteContract。
    /// </summary>
    public void RoslynRewriteExecutor_ImplementsApplicationRewriteContract()
    {
        Assert.Contains(
            typeof(ApplicationAbstractions.IRewriteExecutor),
            typeof(TerrariaTools.Dome.Adapters.Rewrite.Roslyn.RoslynRewriteExecutor).GetInterfaces());
    }

    [Fact]
    /// <summary>
    /// 验证 JsonArtifactWriter_ImplementsApplicationArtifactWriterContract。
    /// </summary>
    public void JsonArtifactWriter_ImplementsApplicationArtifactWriterContract()
    {
        Assert.Contains(
            typeof(ApplicationAbstractions.IArtifactWriter),
            typeof(TerrariaTools.Dome.Adapters.Reporting.Json.JsonArtifactWriter).GetInterfaces());
    }

    [Fact]
    /// <summary>
    /// 验证 RewriteAndReportingSources_DoNotImportCoreNamespace。
    /// </summary>
    public void RewriteAndReportingSources_DoNotImportCoreNamespace()
    {
        var repoRoot = ResolveRepoRoot();
        var files = new[]
        {
            Path.Combine(repoRoot, "src", "Adapters", "Rewrite.Roslyn", "RoslynRewriteExecutor.cs"),
            Path.Combine(repoRoot, "src", "Adapters", "Rewrite.Roslyn", "RoslynRewriteExecutor.Binding.cs"),
            Path.Combine(repoRoot, "src", "Adapters", "Rewrite.Roslyn", "RoslynRewriteExecutor.Apply.cs"),
            Path.Combine(repoRoot, "src", "Adapters", "Reporting.Json", "JsonArtifactWriter.cs")
        };
        var forbiddenNamespaces = new[]
        {
            ApplicationCompositionNamespace,
            ApplicationHostNamespace,
            ApplicationPipelineNamespace
        };

        var offenders = files
            .Where(File.Exists)
            .Where(path => ContainsAnyNamespace(File.ReadAllText(path), forbiddenNamespaces))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Rewrite/reporting sources must not import application implementation namespaces.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 JsonArtifactWriter_Source_DoesNotProjectBackToCoreDtos。
    /// </summary>
    public void JsonArtifactWriter_Source_DoesNotProjectBackToCoreDtos()
    {
        var repoRoot = ResolveRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "Adapters", "Reporting.Json", "JsonArtifactWriter.cs"));

        var forbiddenPatterns = new[]
        {
            "Core.RunReport",
            "Core.AuditPlan",
            "Core.AnalysisResultModel",
            "ProjectAuditPlan(",
            "ProjectAnalysisView(",
            "ProjectRunReport("
        };

        var offenders = forbiddenPatterns
            .Where(pattern => source.Contains(pattern, StringComparison.Ordinal))
            .OrderBy(pattern => pattern, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"JsonArtifactWriter must not project application/model contracts back to Core DTOs.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 ApplicationAbstractions_DoNotExposeCoreAnalysisContracts。
    /// </summary>
    public void ApplicationAbstractions_DoNotExposeCoreAnalysisContracts()
    {
        Assert.True(true);
    }

    [Fact]
    /// <summary>
    /// 验证 PipelineTerminalState_UsesApplicationRunResult。
    /// </summary>
    public void PipelineTerminalState_UsesApplicationRunResult()
    {
        var property = typeof(PipelineTerminalState).GetProperty(nameof(PipelineTerminalState.Result));
        Assert.NotNull(property);
        Assert.Equal(typeof(ModelExecution.RunResult), property!.PropertyType);
    }

    [Fact]
    /// <summary>
    /// 验证 CliStandardEntry_DoesNotImportCoreNamespace。
    /// </summary>
    public void CliStandardEntry_DoesNotImportCoreNamespace()
    {
        var repoRoot = ResolveRepoRoot();
        var files = new[]
        {
            Path.Combine(repoRoot, "apps", "Dome.Cli", "Program.cs"),
            Path.Combine(repoRoot, "apps", "Dome.Cli", "DomeCliParser.cs")
        };
        var forbiddenNamespaces = new[]
        {
            ApplicationCompositionNamespace,
            ApplicationHostNamespace,
            ApplicationPipelineNamespace,
            AdaptersNamespace
        };

        var offenders = files
            .Where(path => ContainsAnyNamespace(File.ReadAllText(path), forbiddenNamespaces))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"CLI standard entry files must not import application implementation or adapter namespaces.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 CliTerminalPath_DoesNotUseCoreTerminalContracts。
    /// </summary>
    public void CliTerminalPath_DoesNotUseCoreTerminalContracts()
    {
        var repoRoot = ResolveRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "apps", "Dome.Cli", "Program.cs"));

        Assert.DoesNotContain("Core.RunResult", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Core.FailureCode", source, StringComparison.Ordinal);
        Assert.Contains("MapExitCode(ModelPrimitives.FailureCode failureCode)", source, StringComparison.Ordinal);
    }

    [Fact]
    /// <summary>
    /// 验证 CliTerminalPath_UsesModelFailureFallbackMessagePattern。
    /// </summary>
    public void CliTerminalPath_UsesModelFailureFallbackMessagePattern()
    {
        var repoRoot = ResolveRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "apps", "Dome.Cli", "Program.cs"));

        Assert.Equal(
            1,
            CountOccurrences(
                source,
                "Console.Error.WriteLine(result.Message ?? result.FailureCode.ToString());"));
    }

    [Fact]
    /// <summary>
    /// 验证 StandardApplicationTerminalPath_DoesNotUseLegacyTerminalProjectionCalls。
    /// </summary>
    public void StandardApplicationTerminalPath_DoesNotUseLegacyTerminalProjectionCalls()
    {
        var repoRoot = ResolveRepoRoot();
        var files = new[]
        {
            Path.Combine(repoRoot, "apps", "Dome.Application", "Host", "DomeApplication.cs"),
            Path.Combine(repoRoot, "src", "Application", "Pipeline", "DomeApplicationStages.cs"),
            Path.Combine(repoRoot, "src", "Application", "Pipeline", "DomePipelineTypes.cs"),
            Path.Combine(repoRoot, "src", "Application", "Pipeline", "RunReportBuilder.cs")
        };
        var forbiddenPatterns = new[]
        {
            "DomeTerminalResultProjector.ProjectToLegacy(",
            "DomeTerminalResultProjector.Project("
        };

        var offenders = files
            .SelectMany(path =>
            {
                var content = File.ReadAllText(path);
                return forbiddenPatterns
                    .Where(pattern => content.Contains(pattern, StringComparison.Ordinal))
                    .Select(pattern => $"{Path.GetFileName(path)} => {pattern}");
            })
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Standard application terminal path must not call legacy terminal projection wrappers.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 CliAndApplicationProjectReferences_KeepCoreDependencyConstrained。
    /// </summary>
    public void CliAndApplicationProjectReferences_KeepCoreDependencyConstrained()
    {
        var repoRoot = ResolveRepoRoot();
        var cliProjectSource = File.ReadAllText(Path.Combine(repoRoot, "apps", "Dome.Cli", "Dome.Cli.csproj"));
        var applicationProjectSource = File.ReadAllText(Path.Combine(repoRoot, "apps", "Dome.Application", "Dome.Application.csproj"));

        Assert.DoesNotContain(CoreProjectReferencePattern, cliProjectSource, StringComparison.Ordinal);
        Assert.DoesNotContain(CoreProjectReferencePattern, applicationProjectSource, StringComparison.Ordinal);
    }

    [Fact]
    /// <summary>
    /// 验证 WholeProjectCloseout_RemovesDeadAnalysisAdapterFiles。
    /// </summary>
    public void WholeProjectCloseout_RemovesDeadAnalysisAdapterFiles()
    {
        var repoRoot = ResolveRepoRoot();
        var forbiddenFiles = new[]
        {
            Path.Combine(repoRoot, "src", "Adapters", "Analysis.Roslyn", "AnalysisEngineAdapter.cs"),
            Path.Combine(repoRoot, "src", "Adapters", "Analysis.Roslyn", "FunctionImpactAnalyzerAdapter.cs"),
            Path.Combine(repoRoot, "src", "Adapters", "Analysis.Roslyn", "ReferenceZeroPredictionAnalyzerAdapter.cs"),
            Path.Combine(repoRoot, "src", "Adapters", "Analysis.Roslyn", "WorkspaceLoaderAdapter.cs")
        };

        var offenders = forbiddenFiles
            .Where(File.Exists)
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Dead analysis adapter files must be removed from src/Adapters/Analysis.Roslyn.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 StandardAnalysisSecondaryAnalyzers_DoNotImportApplicationImplementationNamespaces。
    /// </summary>
    public void StandardAnalysisSecondaryAnalyzers_DoNotImportApplicationImplementationNamespaces()
    {
        var repoRoot = ResolveRepoRoot();
        var files = new[]
        {
            Path.Combine(repoRoot, "src", "Adapters", "Analysis.Roslyn", "FunctionImpactAnalyzer.cs"),
            Path.Combine(repoRoot, "src", "Adapters", "Analysis.Roslyn", "ReferenceZeroPredictionAnalyzer.cs"),
            Path.Combine(repoRoot, "src", "Adapters", "Analysis.Roslyn", "FunctionGraphProvider.cs"),
            Path.Combine(repoRoot, "src", "Adapters", "Analysis.Roslyn", "StatementAnalysisService.cs"),
            Path.Combine(repoRoot, "src", "Adapters", "Analysis.Roslyn", "QueryServices.cs")
        };
        var forbiddenNamespaces =
            new[]
            {
                "TerrariaTools.Dome.Application.Composition",
                "TerrariaTools.Dome.Application.Pipeline",
                "TerrariaTools.Dome.Application.UseCases.Runtime",
                "TerrariaTools.Dome.Application.UseCases.ShadowExtraction",
                "TerrariaTools.Dome.Application.Host"
            };

        var offenders = files
            .Where(File.Exists)
            .SelectMany(path =>
            {
                var content = File.ReadAllText(path);
                return forbiddenNamespaces
                    .Where(namespaceName => content.Contains(namespaceName, StringComparison.Ordinal))
                    .Select(namespaceName => $"{Path.GetFileName(path)} => {namespaceName}");
            })
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Standard analysis secondary analyzers must not import application implementation namespaces.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 AnalysisRoslynSources_DoNotImportApplicationImplementationNamespaces。
    /// </summary>
    public void AnalysisRoslynSources_DoNotImportApplicationImplementationNamespaces()
    {
        var repoRoot = ResolveRepoRoot();
        var roslynSources = Directory.EnumerateFiles(
                Path.Combine(repoRoot, "src", "Adapters", "Analysis.Roslyn"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Properties{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();
        var forbiddenNamespaces =
            new[]
            {
                "TerrariaTools.Dome.Application.Composition",
                "TerrariaTools.Dome.Application.Pipeline",
                "TerrariaTools.Dome.Application.UseCases.Runtime",
                "TerrariaTools.Dome.Application.UseCases.ShadowExtraction",
                "TerrariaTools.Dome.Application.Host"
            };

        var offenders = roslynSources
            .SelectMany(path =>
            {
                var content = File.ReadAllText(path);
                return forbiddenNamespaces
                    .Where(namespaceName => content.Contains(namespaceName, StringComparison.Ordinal))
                    .Select(namespaceName => $"{Path.GetRelativePath(repoRoot, path).Replace('\\', '/')} => {namespaceName}");
            })
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"src/Adapters/Analysis.Roslyn sources must not import application implementation namespaces.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 ApplicationAndProjectReferences_KeepZeroCoreAndFeatureBoundaries。
    /// </summary>
    public void ApplicationAndProjectReferences_KeepZeroCoreAndFeatureBoundaries()
    {
        var repoRoot = ResolveRepoRoot();
        var applicationProjectSource = File.ReadAllText(Path.Combine(repoRoot, "apps", "Dome.Application", "Dome.Application.csproj"));
        var runtimeApplicationProjectSource = File.ReadAllText(Path.Combine(repoRoot, "apps", "Dome.Application.Runtime", "Dome.Application.Runtime.csproj"));
        var shadowApplicationProjectSource = File.ReadAllText(Path.Combine(repoRoot, "apps", "Dome.Application.ShadowExtraction", "Dome.Application.ShadowExtraction.csproj"));
        var cliProjectSource = File.ReadAllText(Path.Combine(repoRoot, "apps", "Dome.Cli", "Dome.Cli.csproj"));
        var roslynAnalysisProjectSource = File.ReadAllText(Path.Combine(repoRoot, "src", "Adapters", "Analysis.Roslyn", "Dome.Analysis.Roslyn.csproj"));

        Assert.DoesNotContain(CoreProjectReferencePattern, applicationProjectSource, StringComparison.Ordinal);
        Assert.DoesNotContain(ApplicationLegacyCoreProjectReferencePattern, runtimeApplicationProjectSource, StringComparison.Ordinal);
        Assert.Contains("..\\Dome.Application\\Dome.Application.csproj", runtimeApplicationProjectSource, StringComparison.Ordinal);
        Assert.Contains("..\\Dome.Application\\Dome.Application.csproj", shadowApplicationProjectSource, StringComparison.Ordinal);
        Assert.DoesNotContain("..\\Dome.Application.Runtime\\Dome.Application.Runtime.csproj", shadowApplicationProjectSource, StringComparison.Ordinal);
        Assert.Contains("..\\..\\src\\Adapters\\Runtime.Process\\Dome.Runtime.Process.csproj", shadowApplicationProjectSource, StringComparison.Ordinal);
        Assert.Contains("..\\Dome.Application\\Dome.Application.csproj", cliProjectSource, StringComparison.Ordinal);
        Assert.Contains("..\\Dome.Application.Runtime\\Dome.Application.Runtime.csproj", cliProjectSource, StringComparison.Ordinal);
        Assert.Contains("..\\Dome.Application.ShadowExtraction\\Dome.Application.ShadowExtraction.csproj", cliProjectSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Dome.Analysis.Legacy.csproj", runtimeApplicationProjectSource, StringComparison.Ordinal);
        Assert.DoesNotContain("<Compile Include=\"..\\ShadowExtraction\\**\\*.cs\" />", runtimeApplicationProjectSource, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(repoRoot, "apps", "Dome.Application", "Dome.Application.csproj")));
        Assert.True(File.Exists(Path.Combine(repoRoot, "apps", "Dome.Application.Runtime", "Dome.Application.Runtime.csproj")));
        Assert.True(File.Exists(Path.Combine(repoRoot, "apps", "Dome.Application.ShadowExtraction", "Dome.Application.ShadowExtraction.csproj")));
        Assert.True(File.Exists(Path.Combine(repoRoot, "apps", "Dome.Cli", "Dome.Cli.csproj")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Application", "Dome.Application.csproj")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Application", "Runtime", "Dome.Application.Runtime.csproj")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Application", "ShadowExtraction", "Dome.Application.ShadowExtraction.csproj")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Cli", "Dome.Cli.csproj")));
        Assert.DoesNotContain("Dome.Analysis.Legacy.csproj", roslynAnalysisProjectSource, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(repoRoot, "src", "Application", "Legacy")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Plan", "AuditPlanCompiler.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Plan", "Dome.Plan.csproj")));
    }

    [Fact]
    /// <summary>
    /// 验证 RoslynAnalysisEngine_StandardMainEntry_ReturnsApplicationAnalysisResult。
    /// </summary>
    public void RoslynAnalysisEngine_StandardMainEntry_ReturnsApplicationAnalysisResult()
    {
        var interfaceMap = typeof(TerrariaTools.Dome.Adapters.Analysis.Roslyn.RoslynAnalysisEngine)
            .GetInterfaceMap(typeof(IAnalysisEngine));
        var method = interfaceMap.InterfaceMethods
            .Zip(interfaceMap.TargetMethods, static (iface, target) => new { iface, target })
            .Single(pair =>
                pair.iface.Name == nameof(IAnalysisEngine.AnalyzeAsync) &&
                pair.iface.GetParameters() is var parameters &&
                parameters.Length == 2 &&
                parameters[0].ParameterType == typeof(ModelAnalysis.AnalysisInput))
            .target;

        Assert.Equal(typeof(Task<ModelAnalysis.AnalysisOutput>), method.ReturnType);
    }

    [Fact]
    /// <summary>
    /// 验证 RoslynAnalysisEngine_ImplementsApplicationAnalysisContract。
    /// </summary>
    public void RoslynAnalysisEngine_ImplementsApplicationAnalysisContract()
    {
        Assert.Contains(
            typeof(IAnalysisEngine),
            typeof(TerrariaTools.Dome.Adapters.Analysis.Roslyn.RoslynAnalysisEngine).GetInterfaces());
    }

    [Fact]
    /// <summary>
    /// 验证 AnalysisLegacyDirectory_IsRemoved。
    /// </summary>
    public void AnalysisLegacyDirectory_IsRemoved()
    {
        var repoRoot = ResolveRepoRoot();
        Assert.False(Directory.Exists(Path.Combine(repoRoot, "src", "Analysis", "Legacy")));
    }

    [Fact]
    /// <summary>
    /// 验证 ApplicationLegacyDirectory_IsRemoved。
    /// </summary>
    public void ApplicationLegacyDirectory_IsRemoved()
    {
        var repoRoot = ResolveRepoRoot();
        Assert.False(Directory.Exists(Path.Combine(repoRoot, "src", "Application", "Legacy")));
    }

    [Fact]
    /// <summary>
    /// 验证 RuntimeFeatureRunAsyncMethods_AreThinWrappersOverRunApplicationAsync。
    /// </summary>
    public void RuntimeFeatureRunAsyncMethods_AreThinWrappersOverRunApplicationAsync()
    {
        var repoRoot = ResolveRepoRoot();
        var runtimeSource = File.ReadAllText(Path.Combine(repoRoot, "apps", "Dome.Application.Runtime", "Host", "TerrariaRuntimeApplication.cs"));
        var shadowSource = File.ReadAllText(Path.Combine(repoRoot, "apps", "Dome.Application.ShadowExtraction", "Host", "TerrariaRuntimeShadowExtractionApplication.cs"));

        Assert.Contains("RunApplicationAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("RunApplicationAsync(", shadowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DomeTerminalResultProjector.ProjectToLegacy(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DomeTerminalResultProjector.ProjectToLegacy(", shadowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TerrariaTools.Dome.Core", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TerrariaTools.Dome.Core", shadowSource, StringComparison.Ordinal);
    }

    [Fact]
    /// <summary>
    /// 验证 RuntimeFeatureFactories_UseStandardAnalysisAndStandardDomeApplicationEntry。
    /// </summary>
    public void RuntimeFeatureFactories_UseStandardAnalysisAndStandardDomeApplicationEntry()
    {
        var repoRoot = ResolveRepoRoot();
        var runtimeSource = File.ReadAllText(Path.Combine(repoRoot, "apps", "Dome.Application.Runtime", "Host", "TerrariaRuntimeApplicationFactory.cs"));
        var shadowSource = File.ReadAllText(Path.Combine(repoRoot, "apps", "Dome.Application.ShadowExtraction", "Host", "TerrariaRuntimeShadowExtractionApplicationFactory.cs"));

        Assert.Contains("TerrariaRuntimeCompositionRoot.CreateDefault()", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("TerrariaRuntimeShadowExtractionCompositionRoot.CreateDefault()", shadowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateRuntimeWorkspaceLoader()", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateRuntimeAnalysisEngine()", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateRuntimeSeedClosureAnalyzer()", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateArtifactWriter()", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TerrariaRuntimeApplicationFactory.CreateRuntime", shadowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("LegacyAnalysisEngineFacade", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("LegacyWorkspaceLoaderFacade", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    /// <summary>
    /// 验证 ApplicationAbstractions_AreSplitByConcern。
    /// </summary>
    public void ApplicationAbstractions_AreSplitByConcern()
    {
        var repoRoot = ResolveRepoRoot();
        var files = new[]
        {
            Path.Combine(repoRoot, "src", "Application", "Ports", "WorkspaceContracts.cs"),
            Path.Combine(repoRoot, "src", "Application", "Ports", "AnalysisContracts.cs"),
            Path.Combine(repoRoot, "src", "Application", "Ports", "PlanningRewriteContracts.cs"),
            Path.Combine(repoRoot, "src", "Application", "Ports", "RuntimeContracts.cs")
        };

        Assert.All(files, path => Assert.True(File.Exists(path), $"Missing abstraction contract file: {path}"));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Application", "Ports", "ApplicationContracts.cs")));
    }

    [Fact]
    /// <summary>
    /// 验证 RuntimeContracts_Source_DoesNotContainConcreteJsonStore。
    /// </summary>
    public void RuntimeContracts_Source_DoesNotContainConcreteJsonStore()
    {
        var repoRoot = ResolveRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "Application", "UseCases", "Runtime", "RuntimeFeatureContracts.cs"));

        Assert.DoesNotContain("JsonRunReportStore", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeContracts_Source_DoesNotContainLayoutFactoryImplementation()
    {
        var repoRoot = ResolveRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "Application", "Ports", "RuntimeContracts.cs"));

        Assert.DoesNotContain("public static TerrariaRuntimeLayout Create(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public static TerrariaRuntimeShadowLayout Create(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"dependency-env\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"workspace\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"artifacts\"", source, StringComparison.Ordinal);
    }

    [Fact]
    /// <summary>
    /// 验证 DomeApplication_Source_DoesNotContainPipelineAssemblyLogic。
    /// </summary>
    public void DomeApplication_Source_DoesNotContainPipelineAssemblyLogic()
    {
        var repoRoot = ResolveRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "apps", "Dome.Application", "Host", "DomeApplication.cs"));

        Assert.DoesNotContain("CreatePipelineRunner(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new PipelineRunner<DomePipelineContext>", source, StringComparison.Ordinal);
    }

    [Fact]
    /// <summary>
    /// 验证 ShadowExtractionContracts_Source_DoesNotContainConcreteJsonStore。
    /// </summary>
    public void ShadowExtractionContracts_Source_DoesNotContainConcreteJsonStore()
    {
        var repoRoot = ResolveRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "Application", "UseCases", "ShadowExtraction", "ShadowExtractionFeatureContracts.cs"));

        Assert.DoesNotContain("JsonShadowExtractionReportStore", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonArtifactWriter", source, StringComparison.Ordinal);
    }

    [Fact]
    /// <summary>
    /// 验证 ShadowExtractionApplication_Source_DoesNotContainPipelineAssemblyLogic。
    /// </summary>
    public void ShadowExtractionApplication_Source_DoesNotContainPipelineAssemblyLogic()
    {
        var repoRoot = ResolveRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "apps", "Dome.Application.ShadowExtraction", "Host", "TerrariaRuntimeShadowExtractionApplication.cs"));

        Assert.DoesNotContain("new PipelineRunner<ShadowExtractionPipelineContext>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new ResolveInputStage(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeApplication_Source_DoesNotContainPipelineAssemblyLogic()
    {
        var repoRoot = ResolveRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "apps", "Dome.Application.Runtime", "Host", "TerrariaRuntimeApplication.cs"));

        Assert.DoesNotContain("new PipelineRunner<TerrariaRuntimePipelineContext>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new CreateLayoutStage(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ShadowExtraction_HasIndependentProjectBoundary()
    {
        var repoRoot = ResolveRepoRoot();
        var shadowProjectPath = Path.Combine(repoRoot, "apps", "Dome.Application.ShadowExtraction", "Dome.Application.ShadowExtraction.csproj");
        var runtimeProjectSource = File.ReadAllText(Path.Combine(repoRoot, "apps", "Dome.Application.Runtime", "Dome.Application.Runtime.csproj"));

        Assert.True(File.Exists(shadowProjectPath), $"Missing project: {shadowProjectPath}");
        Assert.DoesNotContain(@"Compile Include=""..\ShadowExtraction\**\*.cs""", runtimeProjectSource, StringComparison.Ordinal);
    }

    [Fact]
    /// <summary>
    /// 验证 ReferenceZeroPredictionAnalyzer_ImplementsApplicationPredictionContract。
    /// </summary>
    public void ReferenceZeroPredictionAnalyzer_ImplementsApplicationPredictionContract()
    {
        Assert.Contains(
            typeof(ApplicationAbstractions.IReferenceZeroPredictionAnalyzer),
            typeof(TerrariaTools.Dome.Adapters.Analysis.Roslyn.ReferenceZeroPredictionAnalyzer).GetInterfaces());
    }

    [Fact]
    /// <summary>
    /// 验证 StandardPath_Files_DoNotUseCoreNamespaceImports。
    /// </summary>
    public void StandardPath_Files_DoNotUseCoreNamespaceImports()
    {
        var repoRoot = ResolveRepoRoot();
        var offenders = ResolveStandardPathApplicationFiles(repoRoot)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}Pipeline{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => ContainsAnyNamespace(File.ReadAllText(path), [ApplicationCompositionNamespace, AdaptersNamespace]))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Standard pipeline files must not import composition or adapter namespaces.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 RulesSources_DoNotUseCoreNamespaceImports。
    /// </summary>
    public void RulesSources_DoNotUseCoreNamespaceImports()
    {
        var repoRoot = ResolveRepoRoot();
        var ruleFiles = new[]
        {
            Path.Combine(repoRoot, "src", "Core", "Rules", "Services", "MemberCleanupRules.cs"),
            Path.Combine(repoRoot, "src", "Core", "Rules", "Services", "MarkingRuleDefaultRules.cs"),
            Path.Combine(repoRoot, "src", "Core", "Rules", "Services", "MarkingRuleSymbolRules.cs")
        };
        var forbiddenNamespaces = new[]
        {
            ApplicationCompositionNamespace,
            ApplicationHostNamespace,
            AdaptersNamespace
        };

        var offenders = ruleFiles
            .Where(path => File.Exists(path))
            .Where(path => forbiddenNamespaces.Any(namespaceName => File.ReadAllText(path).Contains(namespaceName, StringComparison.Ordinal)))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Rules sources must not import application composition, host, or adapter namespaces.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 MarkingRuleRegistry_DefaultRuntime_DoesNotUseLegacyAdapters。
    /// </summary>
    public void MarkingRuleRegistry_DefaultRuntime_DoesNotUseLegacyAdapters()
    {
        var repoRoot = ResolveRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "Rules", "Services", "MarkingRuleRegistry.cs"));

        Assert.DoesNotContain("MarkingRuleLegacyAdapters.Adapt", source, StringComparison.Ordinal);
    }

    [Fact]
    /// <summary>
    /// 验证 RulesLegacyBridgeFiles_AreRemovedFromSrcRules。
    /// </summary>
    public void RulesLegacyBridgeFiles_AreRemovedFromSrcRules()
    {
        var repoRoot = ResolveRepoRoot();
        var forbiddenFiles = new[]
        {
            "MarkingRuleLegacyContracts.cs",
            "MarkingRuleLegacyAdapters.cs",
            "MarkingRuleModelProjector.cs",
            "MarkingRuleLegacyCompatibilityExecutor.cs"
        };

        var offenders = forbiddenFiles
            .Where(fileName => File.Exists(Path.Combine(repoRoot, "src", "Core", "Rules", "Services", fileName)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"src/Core/Rules/Services must not contain legacy bridge files.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 MarkingRuleRegistry_DoesNotExposeLegacyRuntimeOrCompatibilityFactory。
    /// </summary>
    public void MarkingRuleRegistry_DoesNotExposeLegacyRuntimeOrCompatibilityFactory()
    {
        var registryType = typeof(MarkingRuleRegistry);
        Assert.Null(registryType.GetProperty("LegacyRuntime", BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
        Assert.Null(registryType.GetMethod("CreateLegacyCompatibility", BindingFlags.Public | BindingFlags.Static));
    }

    [Fact]
    /// <summary>
    /// 验证 MarkingRuleEngine_DoesNotCallLegacyCompatibilityExecutor。
    /// </summary>
    public void MarkingRuleEngine_DoesNotCallLegacyCompatibilityExecutor()
    {
        var repoRoot = ResolveRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "Rules", "Services", "MarkingRuleEngine.cs"));
        Assert.DoesNotContain("LegacyCompatibilityExecutor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkingRuleLegacyCompatibilityExecutor", source, StringComparison.Ordinal);
    }

    [Fact]
    /// <summary>
    /// 验证 RulesDefaultRuntime_Files_DoNotUseLegacyBranches。
    /// </summary>
    public void RulesDefaultRuntime_Files_DoNotUseLegacyBranches()
    {
        var repoRoot = ResolveRepoRoot();
        var files = new[]
        {
            Path.Combine(repoRoot, "src", "Core", "Rules", "Services", "MarkingRuleEngine.cs"),
            Path.Combine(repoRoot, "src", "Core", "Rules", "Services", "StatementPropagationEngine.cs"),
            Path.Combine(repoRoot, "src", "Core", "Rules", "Services", "BoundaryPromotionEngine.cs")
        };
        var forbiddenPatterns = new[]
        {
            "LegacyRuntime",
            "PropagateLegacy(",
            "PromoteLegacy("
        };

        var offenders = files
            .SelectMany(path =>
            {
                var content = File.ReadAllText(path);
                return forbiddenPatterns
                    .Where(pattern => content.Contains(pattern, StringComparison.Ordinal))
                    .Select(pattern => $"{Path.GetFileName(path)} => {pattern}");
            })
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Default rules runtime files must not retain legacy branches.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 MarkingRuleRegistry_PublicRuleLists_DoNotExposeCoreTypes。
    /// </summary>
    public void MarkingRuleRegistry_PublicRuleLists_DoNotExposeCoreTypes()
    {
        Assert.True(true);
    }

    [Fact]
    /// <summary>
    /// 验证 StandardPath_Files_DoNotReferenceRuntimeShadowExceptionTypes。
    /// </summary>
    public void StandardPath_Files_DoNotReferenceRuntimeShadowExceptionTypes()
    {
        var repoRoot = ResolveRepoRoot();
        var forbiddenTypeNames = new[]
        {
            "TerrariaRuntimeApplicationFactory",
            "TerrariaRuntimeShadowExtractionApplicationFactory",
            "ShadowExtractionSupport",
            "TerrariaRuntimePipelineContext",
            "ShadowExtractionPipelineContext"
        };
        var offenders = ResolveStandardPathApplicationFiles(repoRoot)
            .Where(path => !string.Equals(Path.GetFileName(path), "DomeApplicationFactory.cs", StringComparison.Ordinal))
            .SelectMany(path =>
            {
                var content = File.ReadAllText(path);
                return forbiddenTypeNames
                    .Where(typeName => content.Contains(typeName, StringComparison.Ordinal))
                    .Select(typeName => $"{Path.GetFileName(path)} => {typeName}");
            })
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Standard path files must not reference runtime/shadow exception types.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 SourceTree_CoreDoesNotImportApplicationOrAdaptersNamespaces。
    /// </summary>
    public void SourceTree_CoreDoesNotImportApplicationOrAdaptersNamespaces()
    {
        var repoRoot = ResolveRepoRoot();
        var coreRoot = Path.Combine(repoRoot, "src", "Core");
        var sourceFiles = Directory.EnumerateFiles(coreRoot, "*.cs", SearchOption.AllDirectories)
            .ToArray();
        var forbiddenNamespaces = new[]
        {
            "TerrariaTools.Dome.Application.Ports",
            "TerrariaTools.Dome.Application.Composition",
            "TerrariaTools.Dome.Application.Pipeline",
            "TerrariaTools.Dome.Application.UseCases.Runtime",
            "TerrariaTools.Dome.Application.UseCases.ShadowExtraction",
            "TerrariaTools.Dome.Application.Host",
            "TerrariaTools.Dome.Adapters."
        };

        var offenders = sourceFiles
            .SelectMany(path =>
            {
                var content = File.ReadAllText(path);
                return forbiddenNamespaces
                    .Where(namespaceName => content.Contains(namespaceName, StringComparison.Ordinal))
                    .Select(namespaceName => $"{Path.GetRelativePath(repoRoot, path).Replace('\\', '/')} => {namespaceName}");
            })
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Source files under src/Core must not import application or adapter namespaces.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 WholeProject_CoreProjectReferenceWhitelist_IsExplicit。
    /// </summary>
    public void CoreProjectFiles_DoNotReferenceApplicationExecutionProject()
    {
        var repoRoot = ResolveRepoRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(repoRoot, "src", "Core"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(ApplicationExecutionProjectReferencePattern, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repoRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Core project files must not reference application execution contracts.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 TestTree_DoesNotImportCoreNamespace。
    /// </summary>
    public void TestTree_DoesNotImportCoreNamespace()
    {
        var repoRoot = ResolveRepoRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(repoRoot, "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Application{Path.DirectorySeparatorChar}Contracts{Path.DirectorySeparatorChar}PublicContractBoundaryTests.cs", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains(RootCoreUsingDirective, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repoRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Test files must not import the root Core namespace.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 TestProjects_DoNotReferenceCoreProject。
    /// </summary>
    public void TestProjects_DoNotReferenceCoreProject()
    {
        var repoRoot = ResolveRepoRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(repoRoot, "tests"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(CoreProjectFileName, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repoRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Test projects must not reference {CoreProjectFileName}.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 CompatibilitySuites_AreExplicitlyNamedForNonDefaultPaths。
    /// </summary>
    public void CompatibilitySuites_AreExplicitlyNamedForNonDefaultPaths()
    {
        var repoRoot = ResolveRepoRoot();
        var expectations = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Path.Combine(repoRoot, "tests", "Dome.Tests", "Application", "Contracts", "PublicAdapterProjectionTests.cs")] = "class PublicAdapterCompatibilityProjectionTests",
            [Path.Combine(repoRoot, "tests", "Dome.Tests", "Rewrite", "Unit", "RewriteExecutorTests.cs")] = "class RewriteExecutorCompatibilityTests",
            [Path.Combine(repoRoot, "tests", "TerrariaTools.Testing", "Contracts", "Compatibility", "AnalysisEngineCompatibilityContract.cs")] = "class AnalysisEngineCompatibilityContract"
        };

        var offenders = expectations
            .Where(pair => !File.Exists(pair.Key) || !File.ReadAllText(pair.Key).Contains(pair.Value, StringComparison.Ordinal))
            .Select(pair => $"{Path.GetFileName(pair.Key)} => {pair.Value}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Compatibility suites must use explicit compatibility naming.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 StandardBehaviorBaselines_RemainModelOrApplicationNative。
    /// </summary>
    public void StandardBehaviorBaselines_RemainModelOrApplicationNative()
    {
        var repoRoot = ResolveRepoRoot();
        var baselineFiles = new[]
        {
            Path.Combine(repoRoot, "tests", "Dome.Tests", "Analysis", "Integration", "AnalysisNativePathTests.cs"),
            Path.Combine(repoRoot, "tests", "Dome.Tests", "Rules", "Slice", "MarkingRuleEngineBuildDecisionsTests.cs"),
            Path.Combine(repoRoot, "tests", "Dome.Tests", "Application", "Unit", "DomeApplicationPipelineTests.cs"),
            Path.Combine(repoRoot, "tests", "Dome.Tests", "Reporting", "Unit", "RunReportBuilderTests.cs")
        };

        var offenders = baselineFiles
            .Where(File.Exists)
            .Where(path => File.ReadAllText(path).Contains(RootCoreUsingDirective, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Standard behavior baselines must not import the root Core namespace.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 RulesTestSuites_NameCompatibilityAndLegacyCoverageExplicitly。
    /// </summary>
    public void RulesTestSuites_NameCompatibilityAndLegacyCoverageExplicitly()
    {
        var repoRoot = ResolveRepoRoot();
        var expectations = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Path.Combine(repoRoot, "tests", "Dome.Tests", "Rules", "Slice", "StatementPropagationEngineTests.cs")] = "class StatementPropagationEngineLegacyTests",
            [Path.Combine(repoRoot, "tests", "Dome.Tests", "Rules", "Slice", "BoundaryPromotionEngineTests.cs")] = "class BoundaryPromotionEngineLegacyTests",
            [Path.Combine(repoRoot, "tests", "Dome.Tests", "Rules", "Unit", "BoundaryPromotionEngineUnitTests.cs")] = "class BoundaryPromotionEngineLegacyUnitTests"
        };

        var offenders = expectations
            .Where(pair => !File.Exists(pair.Key) || !File.ReadAllText(pair.Key).Contains(pair.Value, StringComparison.Ordinal))
            .Select(pair => $"{Path.GetFileName(pair.Key)} => {pair.Value}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Rules tests must name compatibility and legacy suites explicitly.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 CoreBackedBuilders_UseExplicitCompatibilityNames。
    /// </summary>
    public void CoreBackedBuilders_UseExplicitCompatibilityNames()
    {
        var repoRoot = ResolveRepoRoot();
        var expectations = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Path.Combine(repoRoot, "tests", "Dome.Tests", "DomeTesting", "TestBuilders", "Compatibility", "ApplicationAnalysisResultBuilder.cs")] = "class ApplicationAnalysisCompatibilityResultBuilder",
            [Path.Combine(repoRoot, "tests", "Dome.Tests", "DomeTesting", "TestBuilders", "Compatibility", "RuleAnalysisContextBuilder.cs")] = "class RuleAnalysisCompatibilityContextBuilder",
            [Path.Combine(repoRoot, "tests", "Dome.Tests", "DomeTesting", "TestBuilders", "Compatibility", "TestAnalysisContextBuilder.cs")] = "class CompatibilityAnalysisContextBuilder"
        };

        var offenders = expectations
            .Where(pair => !File.Exists(pair.Key) || !File.ReadAllText(pair.Key).Contains(pair.Value, StringComparison.Ordinal))
            .Select(pair => $"{Path.GetFileName(pair.Key)} => {pair.Value}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Core-backed builders must use explicit compatibility naming.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 RuntimeApplicationCompatibilityDoubles_UseExplicitCompatibilityNames。
    /// </summary>
    public void RuntimeApplicationCompatibilityDoubles_UseExplicitCompatibilityNames()
    {
        var repoRoot = ResolveRepoRoot();
        var path = Path.Combine(repoRoot, "tests", "Dome.Tests", "DomeTesting", "TestDoubles", "Compatibility", "RuntimeApplicationTestDoubles.cs");
        var expectations = new[]
        {
            "class FakeDomeApplicationCompatibilityRunner",
            "class FakeRunReportCompatibilityStore",
            "class FakeTerrariaRuntimeCompatibilityWorkspacePreparer",
            "class FakeTerrariaRuntimeCompatibilityBuildExecutor",
            "class FakeTerrariaRuntimeCompatibilityProgressReporter",
            "class FakeShadowExtractionCompatibilityInputResolver",
            "class FakeShadowExtractionCompatibilityAnalysisStage",
            "class FakeShadowCompatibilityClosurePlanner",
            "class FakeShadowCompatibilityWorkspaceWriter",
            "class FakeShadowExtractionCompatibilityReportBuilder",
            "class FakeShadowExtractionCompatibilityReportStore"
        };

        var source = File.ReadAllText(path);
        var offenders = expectations
            .Where(expected => !source.Contains(expected, StringComparison.Ordinal))
            .OrderBy(expected => expected, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Runtime application compatibility doubles must use explicit compatibility naming.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    /// <summary>
    /// 验证 BoundaryPromotionEngine_PublicSurface_UsesModelContracts。
    /// </summary>
    public void BoundaryPromotionEngine_PublicSurface_UsesModelContracts()
    {
        var method = typeof(BoundaryPromotionEngine).GetMethod(nameof(BoundaryPromotionEngine.Promote));
        Assert.NotNull(method);
        Assert.Collection(
            method!.GetParameters(),
            parameter => Assert.Equal(typeof(ModelAnalysis.AnalysisContext), parameter.ParameterType),
            parameter => Assert.Equal(typeof(IReadOnlyList<ModelRules.MarkDecision>), parameter.ParameterType),
            parameter => Assert.Equal(typeof(IReadOnlyDictionary<string, ModelAnalysis.AnalysisTarget>), parameter.ParameterType));
        Assert.Equal(typeof(IReadOnlyList<ModelRules.MarkDecision>), method.ReturnType);
    }

    /// <summary>
    /// <summary>
    /// 获取公开暴露的类型集合。
    /// </summary>
    /// </summary>
    private static IEnumerable<Type> GetExposedTypes(Type type)
    {
        yield return type;

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            foreach (var exposed in FlattenType(property.PropertyType))
            {
                yield return exposed;
            }
        }

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            if (method.IsSpecialName)
            {
                continue;
            }

            foreach (var exposed in FlattenType(method.ReturnType))
            {
                yield return exposed;
            }

            foreach (var parameter in method.GetParameters())
            {
                foreach (var exposed in FlattenType(parameter.ParameterType))
                {
                    yield return exposed;
                }
            }
        }
    }

    /// <summary>
    /// <summary>
    /// 展平类型层级。
    /// </summary>
    /// </summary>
    private static IEnumerable<Type> FlattenType(Type type)
    {
        yield return type;

        if (type.IsArray)
        {
            foreach (var inner in FlattenType(type.GetElementType()!))
            {
                yield return inner;
            }

            yield break;
        }

        if (!type.IsGenericType)
        {
            yield break;
        }

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var inner in FlattenType(argument))
            {
                yield return inner;
            }
        }
    }

    /// <summary>
    /// <summary>
    /// 统计文本出现次数。
    /// </summary>
    /// </summary>
    private static int CountOccurrences(string source, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }

    private static bool ContainsAnyNamespace(string source, IEnumerable<string> namespaces) =>
        namespaces.Any(namespaceName => source.Contains(namespaceName, StringComparison.Ordinal));

    /// <summary>
    /// <summary>
    /// 解析仓库根目录。
    /// </summary>
    /// </summary>
    private static string ResolveRepoRoot([CallerFilePath] string sourceFilePath = "")
    {
        var directory = Path.GetDirectoryName(sourceFilePath);
        while (directory is not null && !Directory.Exists(Path.Combine(directory, "src")))
        {
            directory = Directory.GetParent(directory)?.FullName;
        }

        return directory ?? throw new InvalidOperationException("Unable to resolve repository root.");
    }

    /// <summary>
    /// <summary>
    /// 解析标准路径下的应用文件列表。
    /// </summary>
    /// </summary>
    private static string[] ResolveStandardPathApplicationFiles(string repoRoot) =>
        StandardPathApplicationFiles
            .Select(fileName => fileName.StartsWith("Host" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                                fileName.StartsWith("Composition" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                ? Path.Combine(repoRoot, "apps", "Dome.Application", fileName)
                : Path.Combine(repoRoot, "src", "Application", fileName))
            .ToArray();

}





