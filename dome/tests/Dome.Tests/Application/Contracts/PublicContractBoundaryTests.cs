using System.Reflection;
using System.Runtime.CompilerServices;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using TerrariaTools.Dome.Application;
using TerrariaTools.Dome.Application.Abstractions;
using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;
using ModelRules = TerrariaTools.Dome.Model.Rules;
using TerrariaTools.Dome.Rules;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application.Contracts;

public sealed class PublicContractBoundaryTests
{
    private const string CoreUsingDirective = "using TerrariaTools.Dome." + "Core;";
    private const string CoreProjectFileName = "Dome." + "Core.csproj";
    private static readonly string CoreProjectReferencePattern = "..\\Core\\" + CoreProjectFileName;
    private static readonly string LegacyCoreProjectReferencePattern = "..\\..\\Core\\" + CoreProjectFileName;

    private static readonly string[] StandardPathApplicationFiles =
    [
        "ArtifactPlanBuilder.cs",
        "DomeApplication.cs",
        "DomeApplicationFactory.cs",
        "DomeApplicationSeams.cs",
        "DomeApplicationStages.cs",
        "DomePipelineTypes.cs",
        "PipelineAbstractions.cs",
        "RunReportBuilder.cs"
    ];

    [Fact]
    public void PublicContracts_DoNotExposeRoslynTypes()
    {
        var offenders = typeof(IAnalysisEngine).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace?.StartsWith("TerrariaTools.Dome.Application.Abstractions", StringComparison.Ordinal) == true)
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
    public void RulesPublicContracts_DoNotExposeCoreTypes()
    {
        var offenders = typeof(MarkingRuleRegistry).Assembly
            .GetExportedTypes()
            .Where(type =>
                type.Namespace?.StartsWith("TerrariaTools.Dome.Rules", StringComparison.Ordinal) == true &&
                (type.IsInterface || type == typeof(MarkingRuleRegistry)))
            .SelectMany(GetExposedTypes)
            .Where(type => type.FullName?.StartsWith("TerrariaTools.Dome.Core", StringComparison.Ordinal) == true)
            .Select(type => type.FullName!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Rules public contracts must not expose Core types. Offenders:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void MarkingRuleEngine_NoLongerExposesCoreExecuteCompatibilityApi()
    {
        var executeMethods = typeof(MarkingRuleEngine)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name == "Execute")
            .ToArray();

        Assert.Empty(executeMethods);
    }

    [Fact]
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
    public void SourceLayout_DoesNotUseCoreAsSharedBoundary()
    {
        var repoRoot = ResolveRepoRoot();
        var expectedProjects = new[]
        {
            Path.Combine(repoRoot, "src", "Application", "Abstractions", "Dome.Application.Abstractions.csproj"),
            Path.Combine(repoRoot, "src", "Model", "Primitives", "Dome.Model.Primitives.csproj"),
            Path.Combine(repoRoot, "src", "Model", "Analysis", "Dome.Model.Analysis.csproj")
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
    public void RulesContractSources_DoNotImportCoreNamespace()
    {
        var repoRoot = ResolveRepoRoot();
        var files = new[]
        {
            Path.Combine(repoRoot, "src", "Rules", "MarkingRuleContracts.cs"),
            Path.Combine(repoRoot, "src", "Rules", "MarkingRuleRegistry.cs")
        };

        var offenders = files
            .Where(path => File.ReadAllText(path).Contains(CoreUsingDirective, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Rules contract sources must not import TerrariaTools.Dome.Core.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }


    [Fact]
    public void AbstractionContracts_ExposeOnlyPureSourceSetEntryPoints()
    {
        var analysisMethod = typeof(IAnalysisEngine).GetMethod(nameof(IAnalysisEngine.AnalyzeAsync));
        Assert.NotNull(analysisMethod);
        Assert.Collection(
            analysisMethod!.GetParameters(),
            parameter => Assert.Equal(typeof(SourceDocumentSet), parameter.ParameterType),
            parameter => Assert.Equal(typeof(CancellationToken), parameter.ParameterType));

        var rewriteMethod = typeof(IRewriteExecutor).GetMethod(nameof(IRewriteExecutor.ExecuteAsync));
        Assert.NotNull(rewriteMethod);
        Assert.Collection(
            rewriteMethod!.GetParameters(),
            parameter => Assert.Equal(typeof(SourceDocumentSet), parameter.ParameterType),
            parameter => Assert.Equal(typeof(TerrariaTools.Dome.Model.Planning.AuditPlan), parameter.ParameterType),
            parameter => Assert.Equal(typeof(CancellationToken), parameter.ParameterType));

        Assert.Null(typeof(WorkspaceLoadResult).GetProperty("AnalysisInput"));
        Assert.Null(typeof(AnalysisEngineResult).GetProperty("Documents"));
    }

    [Fact]
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
            "AnalysisResult",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(analysisResultProperty);
        Assert.Equal(typeof(ApplicationAbstractions.AnalysisEngineResult), analysisResultProperty!.PropertyType);

        Assert.Null(typeof(DomePipelineContext).GetProperty("LegacyLoadResult", BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.Null(typeof(DomePipelineContext).GetProperty("LegacyAnalysisResult", BindingFlags.Instance | BindingFlags.NonPublic));
    }

    [Fact]
    public void DomePipelineContext_DecisionPlanImpactSlots_UseModelContracts()
    {
        var decisionsProperty = typeof(DomePipelineContext).GetProperty(
            "Decisions",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(decisionsProperty);
        Assert.Equal(typeof(DomeDecisionSet), decisionsProperty!.PropertyType);

        var initialDecisionsProperty = typeof(DomeDecisionSet).GetProperty(nameof(DomeDecisionSet.InitialDecisions));
        Assert.NotNull(initialDecisionsProperty);
        Assert.Equal(typeof(IReadOnlyList<ModelRules.MarkDecision>), initialDecisionsProperty!.PropertyType);

        var predictedDecisionsProperty = typeof(DomeDecisionSet).GetProperty(nameof(DomeDecisionSet.PredictedDecisions));
        Assert.NotNull(predictedDecisionsProperty);
        Assert.Equal(typeof(IReadOnlyList<ModelRules.MarkDecision>), predictedDecisionsProperty!.PropertyType);

        var allDecisionsProperty = typeof(DomeDecisionSet).GetProperty(nameof(DomeDecisionSet.AllDecisions));
        Assert.NotNull(allDecisionsProperty);
        Assert.Equal(typeof(IReadOnlyList<ModelRules.MarkDecision>), allDecisionsProperty!.PropertyType);

        var planResultProperty = typeof(DomePipelineContext).GetProperty(
            "PlanResult",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(planResultProperty);
        Assert.Equal(typeof(ModelPlanning.PlanCompilationResult), planResultProperty!.PropertyType);

        var functionImpactSetProperty = typeof(DomePipelineContext).GetProperty(
            "FunctionImpactSet",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(functionImpactSetProperty);
        Assert.Equal(typeof(ModelPlanning.FunctionImpactSet), functionImpactSetProperty!.PropertyType);
    }

    [Fact]
    public void StandardStages_DoNotReadLegacyAnalysisPayloadsDirectly()
    {
        var repoRoot = ResolveRepoRoot();
        var stageSource = File.ReadAllText(Path.Combine(repoRoot, "src", "Application", "DomeApplicationStages.cs"));
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
                .Where(type => type.Namespace?.StartsWith("TerrariaTools.Dome.Core", StringComparison.Ordinal) == true)
                .Select(type => $"{method.Name} => {type.FullName}"))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"RunReportBuilder must expose only new model contracts.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");

        Assert.All(methods, method => Assert.Equal(typeof(ApplicationAbstractions.RunReport), method.ReturnType));
    }

    [Fact]
    public void MarkingRuleContracts_DoNotExposeCoreTypes()
    {
        var contracts = typeof(TerrariaTools.Dome.Rules.ISeedRule).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace == "TerrariaTools.Dome.Rules")
            .Where(type => type.Name is
                nameof(TerrariaTools.Dome.Rules.ISeedRule) or
                nameof(TerrariaTools.Dome.Rules.IPropagationRule) or
                nameof(TerrariaTools.Dome.Rules.IProtectionRule) or
                nameof(TerrariaTools.Dome.Rules.IExpressionProjectionRule) or
                nameof(TerrariaTools.Dome.Rules.IMethodRule) or
                nameof(TerrariaTools.Dome.Rules.IMemberTargetRule) or
                nameof(TerrariaTools.Dome.Rules.IClassRule) or
                nameof(TerrariaTools.Dome.Rules.IBoundaryPromotionRule) or
                nameof(TerrariaTools.Dome.Rules.IStatementScopeRule))
            .ToArray();

        var offenders = contracts
            .SelectMany(GetExposedTypes)
            .Where(type => type.FullName?.StartsWith("TerrariaTools.Dome.Core.", StringComparison.Ordinal) == true)
            .Select(type => type.FullName!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Marking rule contracts must not expose TerrariaTools.Dome.Core.* types.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void ArtifactEmissionService_UsesNewContracts()
    {
        var method = typeof(IArtifactEmissionService).GetMethod(nameof(IArtifactEmissionService.EmitAsync));
        Assert.NotNull(method);

        Assert.Collection(
            method!.GetParameters(),
            parameter => Assert.Equal(typeof(string), parameter.ParameterType),
            parameter => Assert.Equal(typeof(ArtifactPlan), parameter.ParameterType),
            parameter => Assert.Equal(typeof(ModelPlanning.AuditPlan), parameter.ParameterType),
            parameter => Assert.Equal(typeof(ApplicationAbstractions.RunReport), parameter.ParameterType),
            parameter => Assert.Equal(typeof(ModelAnalysis.AnalysisResultModel), parameter.ParameterType),
            parameter => Assert.Equal(typeof(CancellationToken), parameter.ParameterType));
    }

    [Fact]
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

        var stagesSource = File.ReadAllText(Path.Combine(repoRoot, "src", "Application", "DomeApplicationStages.cs"));
        Assert.Contains("ModelPlanning.AuditPlanCompiler.Compile(", stagesSource, StringComparison.Ordinal);
    }

    [Fact]
    public void StandardEntrySignature_UsesApplicationAbstractions()
    {
        var runAsync = typeof(IDomeApplicationRunner).GetMethod(nameof(IDomeApplicationRunner.RunAsync));
        Assert.NotNull(runAsync);
        Assert.Equal(typeof(Task<ApplicationAbstractions.RunResult>), runAsync!.ReturnType);
        Assert.Collection(
            runAsync.GetParameters(),
            parameter => Assert.Equal(typeof(ApplicationAbstractions.RunRequest), parameter.ParameterType),
            parameter => Assert.Equal(typeof(CancellationToken), parameter.ParameterType));

        var constructor = typeof(DomeApplication)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single(ctor => ctor.GetParameters().Length >= 9);
        var parameters = constructor.GetParameters();

        Assert.Equal(typeof(ApplicationAbstractions.IWorkspaceLoader), parameters[0].ParameterType);
        Assert.Equal(typeof(ApplicationAbstractions.IAnalysisEngine), parameters[1].ParameterType);
        Assert.Equal(typeof(ApplicationAbstractions.IFunctionImpactAnalyzer), parameters[2].ParameterType);
        Assert.Equal(typeof(ApplicationAbstractions.IReferenceZeroPredictionAnalyzer), parameters[3].ParameterType);
        Assert.Equal(typeof(ApplicationAbstractions.IRewriteExecutor), parameters[5].ParameterType);
        Assert.Equal(typeof(ApplicationAbstractions.IArtifactWriter), parameters[8].ParameterType);
    }

    [Fact]
    public void CreateDefault_UsesNativeAnalysisServicesForStandardPath()
    {
        var repoRoot = ResolveRepoRoot();
        var factorySource = File.ReadAllText(Path.Combine(repoRoot, "src", "Application", "DomeApplicationFactory.cs"));
        var createDefaultStart = factorySource.IndexOf("public static DomeApplication CreateDefault()", StringComparison.Ordinal);
        var createDefaultSource = factorySource.Substring(createDefaultStart);
        var requiredNames = new[]
        {
            "new WorkspaceLoadCoordinator(",
            "(ApplicationAbstractions.IAnalysisEngine)new RoslynAnalysisEngine()",
            "(ApplicationAbstractions.IFunctionImpactAnalyzer)new FunctionImpactAnalyzer()",
            "(ApplicationAbstractions.IReferenceZeroPredictionAnalyzer)new ReferenceZeroPredictionAnalyzer()",
            "new RoslynRewriteExecutor()",
            "new JsonArtifactWriter()",
            "new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault())"
        };

        var missing = requiredNames
            .Where(name => !createDefaultSource.Contains(name, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"Standard CreateDefault() must compose native standard services.{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");

        Assert.DoesNotContain("TerrariaRuntimeLegacyFactory.Create", createDefaultSource, StringComparison.Ordinal);

        Assert.DoesNotContain("WorkspaceLoaderAdapter", factorySource, StringComparison.Ordinal);
        Assert.DoesNotContain("AnalysisEngineAdapter", factorySource, StringComparison.Ordinal);
        Assert.DoesNotContain("FunctionImpactAnalyzerAdapter", factorySource, StringComparison.Ordinal);
        Assert.DoesNotContain("ReferenceZeroPredictionAnalyzerAdapter", factorySource, StringComparison.Ordinal);
    }

    [Fact]
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
    public void RewriteAndReportingProjects_DoNotReferenceDomeCoreProject()
    {
        var repoRoot = ResolveRepoRoot();
        var projects = new[]
        {
            Path.Combine(repoRoot, "src", "Rewrite", "Roslyn", "Dome.Rewrite.Roslyn.csproj"),
            Path.Combine(repoRoot, "src", "Reporting", "Dome.Reporting.csproj")
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
    public void CliProject_DoesNotReferenceDomeCoreProject()
    {
        var repoRoot = ResolveRepoRoot();
        var projectPath = Path.Combine(repoRoot, "src", "Cli", "Dome.Cli.csproj");
        var source = File.ReadAllText(projectPath);

        Assert.DoesNotContain(CoreProjectReferencePattern, source, StringComparison.Ordinal);
    }

    [Fact]
    public void RoslynRewriteExecutor_ImplementsApplicationRewriteContract()
    {
        Assert.Contains(
            typeof(ApplicationAbstractions.IRewriteExecutor),
            typeof(TerrariaTools.Dome.Rewrite.Roslyn.RoslynRewriteExecutor).GetInterfaces());
    }

    [Fact]
    public void JsonArtifactWriter_ImplementsApplicationArtifactWriterContract()
    {
        Assert.Contains(
            typeof(ApplicationAbstractions.IArtifactWriter),
            typeof(TerrariaTools.Dome.Reporting.JsonArtifactWriter).GetInterfaces());
    }

    [Fact]
    public void RewriteAndReportingSources_DoNotImportCoreNamespace()
    {
        var repoRoot = ResolveRepoRoot();
        var files = new[]
        {
            Path.Combine(repoRoot, "src", "Rewrite", "Roslyn", "RoslynRewriteExecutor.cs"),
            Path.Combine(repoRoot, "src", "Rewrite", "Roslyn", "RoslynRewriteExecutor.Binding.cs"),
            Path.Combine(repoRoot, "src", "Rewrite", "Roslyn", "RoslynRewriteExecutor.Apply.cs"),
            Path.Combine(repoRoot, "src", "Reporting", "JsonArtifactWriter.cs")
        };

        var offenders = files
            .Where(File.Exists)
            .Where(path => File.ReadAllText(path).Contains(CoreUsingDirective, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Rewrite/reporting sources must not import TerrariaTools.Dome.Core.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void JsonArtifactWriter_Source_DoesNotProjectBackToCoreDtos()
    {
        var repoRoot = ResolveRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "Reporting", "JsonArtifactWriter.cs"));

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
    public void ApplicationAbstractions_DoNotExposeCoreAnalysisContracts()
    {
        var offenders = typeof(IAnalysisEngine).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace?.StartsWith("TerrariaTools.Dome.Application.Abstractions", StringComparison.Ordinal) == true)
            .SelectMany(GetExposedTypes)
            .Where(type => type.FullName?.StartsWith("TerrariaTools.Dome.Core.", StringComparison.Ordinal) == true)
            .Select(type => type.FullName!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Application abstractions must not expose TerrariaTools.Dome.Core analysis contracts.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void PipelineTerminalState_UsesApplicationRunResult()
    {
        var property = typeof(PipelineTerminalState).GetProperty(nameof(PipelineTerminalState.Result));
        Assert.NotNull(property);
        Assert.Equal(typeof(ApplicationAbstractions.RunResult), property!.PropertyType);
    }

    [Fact]
    public void CliStandardEntry_DoesNotImportCoreNamespace()
    {
        var repoRoot = ResolveRepoRoot();
        var files = new[]
        {
            Path.Combine(repoRoot, "src", "Cli", "Program.cs"),
            Path.Combine(repoRoot, "src", "Cli", "DomeCliParser.cs")
        };

        var offenders = files
            .Where(path => File.ReadAllText(path).Contains(CoreUsingDirective, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"CLI standard entry files must not import TerrariaTools.Dome.Core.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void CliTerminalPath_DoesNotUseCoreTerminalContracts()
    {
        var repoRoot = ResolveRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "Cli", "Program.cs"));

        Assert.DoesNotContain("Core.RunResult", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Core.FailureCode", source, StringComparison.Ordinal);
        Assert.Contains("MapExitCode(ModelPrimitives.FailureCode failureCode)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CliTerminalPath_UsesModelFailureFallbackMessagePattern()
    {
        var repoRoot = ResolveRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "Cli", "Program.cs"));

        Assert.Equal(
            1,
            CountOccurrences(
                source,
                "Console.Error.WriteLine(result.Message ?? result.FailureCode.ToString());"));
    }

    [Fact]
    public void StandardApplicationTerminalPath_DoesNotUseLegacyTerminalProjectionCalls()
    {
        var repoRoot = ResolveRepoRoot();
        var files = new[]
        {
            Path.Combine(repoRoot, "src", "Application", "DomeApplication.cs"),
            Path.Combine(repoRoot, "src", "Application", "DomeApplicationStages.cs"),
            Path.Combine(repoRoot, "src", "Application", "DomePipelineTypes.cs"),
            Path.Combine(repoRoot, "src", "Application", "RunReportBuilder.cs")
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
    public void CliAndApplicationProjectReferences_KeepCoreDependencyConstrained()
    {
        var repoRoot = ResolveRepoRoot();
        var cliProjectSource = File.ReadAllText(Path.Combine(repoRoot, "src", "Cli", "Dome.Cli.csproj"));
        var applicationProjectSource = File.ReadAllText(Path.Combine(repoRoot, "src", "Application", "Dome.Application.csproj"));

        Assert.DoesNotContain(CoreProjectReferencePattern, cliProjectSource, StringComparison.Ordinal);
        Assert.DoesNotContain(CoreProjectReferencePattern, applicationProjectSource, StringComparison.Ordinal);
    }

    [Fact]
    public void WholeProjectCloseout_RemovesDeadAnalysisAdapterFiles()
    {
        var repoRoot = ResolveRepoRoot();
        var forbiddenFiles = new[]
        {
            Path.Combine(repoRoot, "src", "Analysis", "Roslyn", "AnalysisEngineAdapter.cs"),
            Path.Combine(repoRoot, "src", "Analysis", "Roslyn", "FunctionImpactAnalyzerAdapter.cs"),
            Path.Combine(repoRoot, "src", "Analysis", "Roslyn", "ReferenceZeroPredictionAnalyzerAdapter.cs"),
            Path.Combine(repoRoot, "src", "Analysis", "Roslyn", "WorkspaceLoaderAdapter.cs")
        };

        var offenders = forbiddenFiles
            .Where(File.Exists)
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Dead analysis adapter files must be removed from src/Analysis/Roslyn.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void StandardAnalysisSecondaryAnalyzers_DoNotImportCoreNamespace()
    {
        var repoRoot = ResolveRepoRoot();
        var files = new[]
        {
            Path.Combine(repoRoot, "src", "Analysis", "Roslyn", "FunctionImpactAnalyzer.cs"),
            Path.Combine(repoRoot, "src", "Analysis", "Roslyn", "ReferenceZeroPredictionAnalyzer.cs"),
            Path.Combine(repoRoot, "src", "Analysis", "Roslyn", "FunctionGraphProvider.cs"),
            Path.Combine(repoRoot, "src", "Analysis", "Roslyn", "StatementAnalysisService.cs"),
            Path.Combine(repoRoot, "src", "Analysis", "Roslyn", "QueryServices.cs")
        };

        var offenders = files
            .Where(File.Exists)
            .Where(path => File.ReadAllText(path).Contains(CoreUsingDirective, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Standard analysis secondary analyzers must not import TerrariaTools.Dome.Core.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void AnalysisRoslynSources_DoNotImportCoreNamespace()
    {
        var repoRoot = ResolveRepoRoot();
        var roslynSources = Directory.EnumerateFiles(
                Path.Combine(repoRoot, "src", "Analysis", "Roslyn"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Properties{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        var offenders = roslynSources
            .Where(path => File.ReadAllText(path).Contains(CoreUsingDirective, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repoRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"src/Analysis/Roslyn sources must not import TerrariaTools.Dome.Core.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void ApplicationAndPlanProjectReferences_KeepCoreDependencyExplicitlyConstrained()
    {
        var repoRoot = ResolveRepoRoot();
        var applicationProjectSource = File.ReadAllText(Path.Combine(repoRoot, "src", "Application", "Dome.Application.csproj"));
        var legacyApplicationProjectSource = File.ReadAllText(Path.Combine(repoRoot, "src", "Application", "Legacy", "Dome.Application.Legacy.csproj"));
        var cliProjectSource = File.ReadAllText(Path.Combine(repoRoot, "src", "Cli", "Dome.Cli.csproj"));
        var roslynAnalysisProjectSource = File.ReadAllText(Path.Combine(repoRoot, "src", "Analysis", "Roslyn", "Dome.Analysis.Roslyn.csproj"));

        Assert.DoesNotContain(CoreProjectReferencePattern, applicationProjectSource, StringComparison.Ordinal);
        Assert.DoesNotContain(LegacyCoreProjectReferencePattern, legacyApplicationProjectSource, StringComparison.Ordinal);
        Assert.Contains("..\\..\\Analysis\\Legacy\\Dome.Analysis.Legacy.csproj", legacyApplicationProjectSource, StringComparison.Ordinal);
        Assert.DoesNotContain("..\\..\\Analysis\\Roslyn\\Dome.Analysis.Roslyn.csproj", legacyApplicationProjectSource, StringComparison.Ordinal);
        Assert.DoesNotContain("<Compile Include=\"..\\", legacyApplicationProjectSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Dome.Application.Legacy.csproj", cliProjectSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Dome.Analysis.Legacy.csproj", roslynAnalysisProjectSource, StringComparison.Ordinal);
        Assert.Contains("TerrariaRuntimeLegacyFactory", File.ReadAllText(Path.Combine(repoRoot, "src", "Application", "Legacy", "TerrariaRuntimeLegacyFactory.cs")), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Plan", "AuditPlanCompiler.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Plan", "Dome.Plan.csproj")));
    }

    [Fact]
    public void RoslynAnalysisEngine_StandardMainEntry_ReturnsApplicationAnalysisResult()
    {
        var interfaceMap = typeof(TerrariaTools.Dome.Analysis.Roslyn.RoslynAnalysisEngine)
            .GetInterfaceMap(typeof(IAnalysisEngine));
        var method = interfaceMap.InterfaceMethods
            .Zip(interfaceMap.TargetMethods, static (iface, target) => new { iface, target })
            .Single(pair => pair.iface.Name == nameof(IAnalysisEngine.AnalyzeAsync))
            .target;

        Assert.Equal(typeof(Task<AnalysisEngineResult>), method.ReturnType);
    }

    [Fact]
    public void RoslynAnalysisEngine_ImplementsApplicationAnalysisContract()
    {
        Assert.Contains(
            typeof(IAnalysisEngine),
            typeof(TerrariaTools.Dome.Analysis.Roslyn.RoslynAnalysisEngine).GetInterfaces());
    }

    [Fact]
    public void AnalysisLegacySurface_UsesOnlyApplicationContracts()
    {
        Assert.Contains(
            typeof(ApplicationAbstractions.IAnalysisEngine),
            typeof(TerrariaTools.Dome.Analysis.Legacy.RoslynAnalysisEngine).GetInterfaces());
        Assert.Contains(
            typeof(TerrariaTools.Dome.Analysis.Legacy.IWorkspaceLoader),
            typeof(TerrariaTools.Dome.Analysis.Legacy.WorkspaceLoadCoordinator).GetInterfaces());
    }

    [Fact]
    public void RuntimeLegacyRunAsyncMethods_AreThinWrappersOverRunApplicationAsync()
    {
        var repoRoot = ResolveRepoRoot();
        var runtimeSource = File.ReadAllText(Path.Combine(repoRoot, "src", "Application", "Legacy", "TerrariaRuntimeApplication.cs"));
        var shadowSource = File.ReadAllText(Path.Combine(repoRoot, "src", "Application", "Legacy", "TerrariaRuntimeShadowExtractionApplication.cs"));

        Assert.Contains("RunApplicationAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("RunApplicationAsync(", shadowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DomeTerminalResultProjector.ProjectToLegacy(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DomeTerminalResultProjector.ProjectToLegacy(", shadowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TerrariaTools.Dome.Core", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TerrariaTools.Dome.Core", shadowSource, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyFactory_UsesCompatibilityFacadesAndStandardDomeApplicationEntry()
    {
        var repoRoot = ResolveRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "Application", "Legacy", "TerrariaRuntimeLegacyFactory.cs"));

        Assert.Contains("CreateLegacyWorkspaceLoader()", source, StringComparison.Ordinal);
        Assert.Contains("CreateLegacyAnalysisEngine()", source, StringComparison.Ordinal);
        Assert.Contains("DomeApplicationFactory.CreateDefault()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateFunctionImpactAnalyzer()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateReferenceZeroPredictionAnalyzer()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferenceZeroPredictionAnalyzer_ImplementsApplicationPredictionContract()
    {
        Assert.Contains(
            typeof(ApplicationAbstractions.IReferenceZeroPredictionAnalyzer),
            typeof(TerrariaTools.Dome.Analysis.Roslyn.ReferenceZeroPredictionAnalyzer).GetInterfaces());
    }

    [Fact]
    public void StandardPath_Files_DoNotUseCoreNamespaceImports()
    {
        var repoRoot = ResolveRepoRoot();
        var offenders = ResolveStandardPathApplicationFiles(repoRoot)
            .Where(path => File.ReadAllText(path).Contains(CoreUsingDirective, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Standard path files must not import TerrariaTools.Dome.Core.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void RulesSources_DoNotUseCoreNamespaceImports()
    {
        var repoRoot = ResolveRepoRoot();
        var ruleFiles = new[]
        {
            Path.Combine(repoRoot, "src", "Rules", "MemberCleanupRules.cs"),
            Path.Combine(repoRoot, "src", "Rules", "MarkingRuleDefaultRules.cs"),
            Path.Combine(repoRoot, "src", "Rules", "MarkingRuleSymbolRules.cs")
        };

        var offenders = ruleFiles
            .Where(path => File.Exists(path))
            .Where(path => File.ReadAllText(path).Contains(CoreUsingDirective, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Rules sources must not import TerrariaTools.Dome.Core.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void MarkingRuleRegistry_DefaultRuntime_DoesNotUseLegacyAdapters()
    {
        var repoRoot = ResolveRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "Rules", "MarkingRuleRegistry.cs"));

        Assert.DoesNotContain("MarkingRuleLegacyAdapters.Adapt", source, StringComparison.Ordinal);
    }

    [Fact]
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
            .Where(fileName => File.Exists(Path.Combine(repoRoot, "src", "Rules", fileName)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"src/Rules must not contain legacy bridge files.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void MarkingRuleRegistry_DoesNotExposeLegacyRuntimeOrCompatibilityFactory()
    {
        var registryType = typeof(MarkingRuleRegistry);
        Assert.Null(registryType.GetProperty("LegacyRuntime", BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
        Assert.Null(registryType.GetMethod("CreateLegacyCompatibility", BindingFlags.Public | BindingFlags.Static));
    }

    [Fact]
    public void MarkingRuleEngine_DoesNotCallLegacyCompatibilityExecutor()
    {
        var repoRoot = ResolveRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "Rules", "MarkingRuleEngine.cs"));
        Assert.DoesNotContain("LegacyCompatibilityExecutor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkingRuleLegacyCompatibilityExecutor", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RulesDefaultRuntime_Files_DoNotUseLegacyBranches()
    {
        var repoRoot = ResolveRepoRoot();
        var files = new[]
        {
            Path.Combine(repoRoot, "src", "Rules", "MarkingRuleEngine.cs"),
            Path.Combine(repoRoot, "src", "Rules", "StatementPropagationEngine.cs"),
            Path.Combine(repoRoot, "src", "Rules", "BoundaryPromotionEngine.cs")
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
    public void MarkingRuleRegistry_PublicRuleLists_DoNotExposeCoreTypes()
    {
        var registryType = typeof(TerrariaTools.Dome.Rules.MarkingRuleRegistry);
        var properties = new[] { "MethodRules", "MemberTargetRules", "ClassRules" };

        var offenders = properties
            .Select(name => registryType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance))
            .Where(property => property is not null)
            .SelectMany(property => FlattenType(property!.PropertyType)
                .Where(type => type.FullName?.StartsWith("TerrariaTools.Dome.Core.", StringComparison.Ordinal) == true)
                .Select(type => $"{property.Name} => {type.FullName}"))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"MarkingRuleRegistry public rule lists must not expose TerrariaTools.Dome.Core.* types.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void StandardPath_Files_DoNotReferenceRuntimeShadowExceptionTypes()
    {
        var repoRoot = ResolveRepoRoot();
        var forbiddenTypeNames = new[]
        {
            "TerrariaRuntimeLegacyFactory",
            "ShadowExtractionLegacySupport",
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
    public void SourceTree_DoesNotImportCoreNamespace()
    {
        var repoRoot = ResolveRepoRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(CoreUsingDirective, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repoRoot, path).Replace('\\', '/'))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Source files must not import TerrariaTools.Dome.Core.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void WholeProject_CoreProjectReferenceWhitelist_IsExplicit()
    {
        var repoRoot = ResolveRepoRoot();
        var actual = Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(CoreProjectFileName, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repoRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(actual);
    }

    [Fact]
    public void TestTree_DoesNotImportCoreNamespace()
    {
        var repoRoot = ResolveRepoRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(repoRoot, "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Application{Path.DirectorySeparatorChar}Contracts{Path.DirectorySeparatorChar}PublicContractBoundaryTests.cs", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains(CoreUsingDirective, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repoRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Test files must not import TerrariaTools.Dome.Core.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
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
            .Where(path => File.ReadAllText(path).Contains(CoreUsingDirective, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Standard behavior baselines must not import TerrariaTools.Dome.Core.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
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
    public void CoreBackedBuilders_UseExplicitCompatibilityNames()
    {
        var repoRoot = ResolveRepoRoot();
        var expectations = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Path.Combine(repoRoot, "tests", "Dome.Tests", "DomeTesting", "TestBuilders", "Compatibility", "ApplicationAnalysisResultBuilder.cs")] = "class ApplicationAnalysisCompatibilityResultBuilder",
            [Path.Combine(repoRoot, "tests", "Dome.Tests", "DomeTesting", "TestBuilders", "Compatibility", "RuleAnalysisContextBuilder.cs")] = "class RuleAnalysisCompatibilityContextBuilder",
            [Path.Combine(repoRoot, "tests", "Dome.Tests", "DomeTesting", "TestBuilders", "Compatibility", "TestAnalysisContextBuilder.cs")] = "class LegacyAnalysisContextBuilder"
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

    private static string ResolveRepoRoot([CallerFilePath] string sourceFilePath = "")
    {
        var directory = Path.GetDirectoryName(sourceFilePath);
        while (directory is not null && !Directory.Exists(Path.Combine(directory, "src")))
        {
            directory = Directory.GetParent(directory)?.FullName;
        }

        return directory ?? throw new InvalidOperationException("Unable to resolve repository root.");
    }

    private static string[] ResolveStandardPathApplicationFiles(string repoRoot) =>
        StandardPathApplicationFiles
            .Select(fileName => Path.Combine(repoRoot, "src", "Application", fileName))
            .ToArray();

}

