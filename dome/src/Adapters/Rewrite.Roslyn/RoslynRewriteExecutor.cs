using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using Orchestration = TerrariaTools.Dome.Application.Ports;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelPlanning = TerrariaTools.Dome.Core.Planning;
using ModelPrimitives = TerrariaTools.Dome.Core.Common;
using ModelRules = TerrariaTools.Dome.Core.Rules.Model;

namespace TerrariaTools.Dome.Adapters.Rewrite.Roslyn;

// 通过 Roslyn 语法重写将 AuditPlan 应用到源文档。
// 保留这些兼容性重载，是为了让旧测试和旧调用方仍能映射到统一的 Core 规划模型。
public sealed partial class RoslynRewriteExecutor : ApplicationAbstractions.IRewriteExecutor
{
    public Task<ModelExecution.RewriteOutput> ExecuteAsync(
        string source,
        object legacyPlan,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            new ModelExecution.RewriteInput(
                new ModelAnalysis.SourceDocumentSet(
                    "input.cs",
                    "input.cs",
                    [new ModelAnalysis.SourceDocument("input.cs", "input.cs", source)]),
                ProjectLegacyPlan(legacyPlan)),
            cancellationToken);

    public Task<ModelExecution.RewriteOutput> ExecuteAsync(
        object legacyDocumentContext,
        object legacyPlan,
        CancellationToken cancellationToken)
    {
        var sourceSet = ProjectLegacyDocumentContext(legacyDocumentContext);
        return ExecuteAsync(new ModelExecution.RewriteInput(sourceSet, ProjectLegacyPlan(legacyPlan)), cancellationToken);
    }

    public Task<ModelExecution.RewriteOutput> ExecuteAsync(
        string source,
        ModelPlanning.AuditPlan plan,
        CancellationToken cancellationToken)
    {
        var documentPath = plan.Changes.FirstOrDefault()?.Target.DocumentPath ?? "input.cs";
        return ExecuteAsync(
            new ModelExecution.RewriteInput(
                new ModelAnalysis.SourceDocumentSet(
                    documentPath,
                    Path.GetDirectoryName(documentPath) ?? documentPath,
                    [
                        new ModelAnalysis.SourceDocument(documentPath, documentPath, source)
                    ]),
                plan),
            cancellationToken);
    }

    public Task<ModelExecution.RewriteOutput> ExecuteAsync(
        ModelAnalysis.SourceDocumentSet sourceSet,
        ModelPlanning.AuditPlan plan,
        CancellationToken cancellationToken) =>
        ExecuteAsync(new ModelExecution.RewriteInput(sourceSet, plan), cancellationToken);

    public Task<ModelExecution.RewriteOutput> ExecuteAsync(
        ModelExecution.RewriteInput input,
        CancellationToken cancellationToken)
    {
        var sourceSet = input.SourceSet;
        var plan = input.Plan;
        if (sourceSet.Documents.Count != 1)
        {
            return Task.FromResult(ModelExecution.RewriteOutput.Failure(
                Orchestration.FailureCode.RewriteFailed,
                "RoslynRewriteExecutor currently expects a single-document source set."));
        }

        if (plan.Conflicts.Count > 0)
        {
            return Task.FromResult(ModelExecution.RewriteOutput.Failure(
                Orchestration.FailureCode.RewriteFailed,
                "Rewrite cannot execute a plan with unresolved conflicts."));
        }

        var documentContext = CreateDocumentContext(sourceSet.Documents[0], cancellationToken);
        var orderedChanges = plan.Changes
            .OrderBy(change => change.Target.DocumentPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(change => change.Target.MemberId.Value, StringComparer.Ordinal)
            .ThenByDescending(change => change.Locator.SpanStart)
            .ThenBy(change => change.ExecutionOrder)
            .ToArray();

        var bindResult = BindPlan(documentContext, orderedChanges);
        if (!bindResult.IsSuccess || bindResult.Plan == null)
        {
            return Task.FromResult(ModelExecution.RewriteOutput.Failure(Orchestration.FailureCode.RewriteFailed, bindResult.Message));
        }

        var trackedRoot = documentContext.Root.TrackNodes(bindResult.Plan.Changes.Select(boundChange => boundChange.OriginalNode));
        SyntaxNode root = trackedRoot;
        foreach (var change in bindResult.Plan.Changes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentNode = root.GetCurrentNode(change.OriginalNode);
            if (currentNode == null)
            {
                return Task.FromResult(ModelExecution.RewriteOutput.Failure(
                    Orchestration.FailureCode.RewriteFailed,
                    $"Target '{BuildTargetKey(change.Change.Target, change.Change.Locator)}' could not be resolved during rewrite because the bound node is no longer available in the current syntax tree."));
            }

            var applyResult = ApplyChange(root, currentNode, change.Change);
            if (!applyResult.IsSuccess || applyResult.Root == null)
            {
                return Task.FromResult(ModelExecution.RewriteOutput.Failure(Orchestration.FailureCode.RewriteFailed, applyResult.Message));
            }

            root = applyResult.Root;
        }

        var rewrittenDocument = new ModelExecution.RewrittenDocument(sourceSet.Documents[0].RelativePath, root.ToFullString());
        return Task.FromResult(ModelExecution.RewriteOutput.Success([rewrittenDocument]));
    }

    private static RewriteDocumentContext CreateDocumentContext(
        ModelAnalysis.SourceDocument document,
        CancellationToken cancellationToken)
    {
        var tree = CSharpSyntaxTree.ParseText(document.SourceText, path: document.RelativePath);
        var root = tree.GetCompilationUnitRoot(cancellationToken);
        SemanticModel? semanticModel = null;

        try
        {
            var compilation = CSharpCompilation.Create(
                "DomeRewriteExecution",
                [tree],
                GetRewriteMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
        }
        catch
        {
            semanticModel = null;
        }

        return new RewriteDocumentContext(document, root, semanticModel);
    }

    private static string BuildTargetKey(ModelPrimitives.TargetIdentity target, ModelPrimitives.TargetLocator locator) =>
        $"{target.MemberId.Value}:{target.TargetKind}:{locator.SpanStart}:{locator.SpanLength}";

    private static ModelAnalysis.SourceDocumentSet ProjectLegacyDocumentContext(object legacyDocumentContext)
    {
        var document = GetRequiredPropertyValue(legacyDocumentContext, "Document");
        var relativePath = GetRequiredString(document, "RelativePath");
        var sourceText = GetRequiredString(document, "SourceText");
        return new ModelAnalysis.SourceDocumentSet(
            relativePath,
            relativePath,
            [new ModelAnalysis.SourceDocument(relativePath, relativePath, sourceText)]);
    }

    private static ModelPlanning.AuditPlan ProjectLegacyPlan(object legacyPlan)
    {
        var metadata = GetRequiredPropertyValue(legacyPlan, "Metadata");
        var changes = ((IEnumerable<object>)GetRequiredPropertyValue(legacyPlan, "Changes"))
            .Select(ProjectLegacyChange)
            .ToArray();
        var conflicts = ((IEnumerable<object>)GetRequiredPropertyValue(legacyPlan, "Conflicts"))
            .Select(ProjectLegacyConflict)
            .ToArray();

        return new ModelPlanning.AuditPlan(
            new ModelPlanning.PlanMetadata(
                GetRequiredString(metadata, "ToolName"),
                GetRequiredString(metadata, "PlanVersion"),
                GetRequiredString(metadata, "InputPath"),
                GetRequiredString(metadata, "OutputPath"),
                Enum.Parse<ModelPrimitives.RunMode>(GetRequiredPropertyValue(metadata, "RunMode").ToString() ?? nameof(ModelPrimitives.RunMode.Standard)))
            {
                GeneratedAtUtc = GetOptionalPropertyValue<DateTimeOffset>(metadata, "GeneratedAtUtc")
            },
            changes,
            conflicts);
    }

    private static ModelPlanning.PlannedChange ProjectLegacyChange(object legacyChange)
    {
        var target = GetRequiredPropertyValue(legacyChange, "Target");
        var action = GetRequiredPropertyValue(legacyChange, "Action");
        var actionKind = Enum.Parse<ModelPrimitives.PlanActionKind>(GetRequiredPropertyValue(action, "Kind").ToString() ?? nameof(ModelPrimitives.PlanActionKind.Delete));
        return new ModelPlanning.PlannedChange(
            GetRequiredPropertyValue<int>(legacyChange, "ExecutionOrder"),
            ProjectLegacyTargetIdentity(target),
            ProjectLegacyTargetLocator(target),
            new ModelPrimitives.PlanAction(
                actionKind,
                GetOptionalPropertyValue<string>(action, "Payload")),
            ProjectLegacyReason(legacyChange, target, actionKind),
            ProjectLegacyChain(GetOptionalPropertyValue<object>(legacyChange, "Chain")));
    }

    private static ModelPlanning.PlanReason ProjectLegacyReason(
        object legacyChange,
        object legacyTarget,
        ModelPrimitives.PlanActionKind actionKind)
    {
        var reason = GetOptionalPropertyValue<object>(legacyChange, "Reason");
        if (reason is ModelPlanning.PlanReason typedReason)
        {
            return typedReason;
        }

        if (reason is ModelRules.PlanReason legacyReason)
        {
            return ProjectPlanReason(legacyReason);
        }

        if (reason != null)
        {
            return new ModelPlanning.PlanReason(
                GetOptionalString(reason, "RuleId") ?? BuildLegacyRuleId(actionKind),
                GetOptionalString(reason, "ReasonText") ?? "Projected from legacy planned change.",
                SourceTargetKey: GetOptionalString(reason, "SourceTargetKey"),
                SourceTargetDisplayText: GetOptionalString(reason, "SourceTargetDisplayText"),
                RelatedSymbolKeys: GetOptionalStringList(reason, "RelatedSymbolKeys"),
                RelatedSymbolNames: GetOptionalStringList(reason, "RelatedSymbolNames"),
                Severity: GetOptionalString(reason, "Severity"),
                SourceMemberId: GetOptionalString(reason, "SourceMemberId"),
                BoundaryKind: GetOptionalEnum<ModelPrimitives.BoundaryKind>(reason, "BoundaryKind"),
                TriggeredSymbolKeys: GetOptionalStringList(reason, "TriggeredSymbolKeys"),
                Origin: GetOptionalEnum<ModelPrimitives.DecisionOrigin>(reason, "Origin") ?? ModelPrimitives.DecisionOrigin.Rule,
                Category: GetOptionalEnum<ModelPrimitives.DecisionCategory>(reason, "Category") ?? MapDecisionCategory(actionKind));
        }

        return new ModelPlanning.PlanReason(
            BuildLegacyRuleId(actionKind),
            $"Projected legacy {actionKind} change.",
            SourceTargetDisplayText: GetOptionalString(legacyTarget, "DisplayText"),
            SourceMemberId: GetOptionalString(GetRequiredPropertyValue(legacyTarget, "MemberId"), "Value"),
            Category: MapDecisionCategory(actionKind));
    }

    private static ModelPlanning.PlanReason ProjectPlanReason(ModelRules.PlanReason reason) =>
        new(
            reason.RuleId,
            reason.ReasonText,
            reason.SourceTargetKey,
            reason.SourceTargetDisplayText,
            reason.RelatedSymbolKeys,
            reason.RelatedSymbolNames,
            reason.Severity,
            reason.SourceMemberId,
            reason.BoundaryKind,
            reason.TriggeredSymbolKeys,
            reason.Origin,
            reason.Category);

    private static string BuildLegacyRuleId(ModelPrimitives.PlanActionKind actionKind) =>
        $"legacy-{actionKind.ToString().ToLowerInvariant()}";

    private static ModelPrimitives.DecisionCategory MapDecisionCategory(ModelPrimitives.PlanActionKind actionKind) =>
        actionKind switch
        {
            ModelPrimitives.PlanActionKind.Delete => ModelPrimitives.DecisionCategory.Delete,
            ModelPrimitives.PlanActionKind.CommentOut => ModelPrimitives.DecisionCategory.CommentOut,
            ModelPrimitives.PlanActionKind.ReplaceWithDefault => ModelPrimitives.DecisionCategory.ReplaceWithDefault,
            ModelPrimitives.PlanActionKind.AddReturn => ModelPrimitives.DecisionCategory.AddReturn,
            ModelPrimitives.PlanActionKind.ChangeVisibilityToPrivate => ModelPrimitives.DecisionCategory.VisibilityChange,
            ModelPrimitives.PlanActionKind.ReorderPublicMethods => ModelPrimitives.DecisionCategory.Reorder,
            _ => ModelPrimitives.DecisionCategory.Delete
        };

    private static ModelPlanning.PropagationChain? ProjectLegacyChain(object? legacyChain)
    {
        if (legacyChain is null)
        {
            return null;
        }

        if (legacyChain is ModelPlanning.PropagationChain typedChain)
        {
            return typedChain;
        }

        if (legacyChain is ModelRules.PropagationChain legacyTypedChain)
        {
            return ProjectPropagationChain(legacyTypedChain);
        }

        return new ModelPlanning.PropagationChain(
            GetRequiredString(legacyChain, "RootTargetKey"),
            GetRequiredString(legacyChain, "RootTargetDisplayText"),
            ((IEnumerable<object>?)GetOptionalPropertyValue<object>(legacyChain, "Hops"))?.Select(ProjectLegacyPropagationHop).ToArray()
            ?? Array.Empty<ModelPlanning.PropagationHop>());
    }

    private static ModelPlanning.PropagationChain ProjectPropagationChain(ModelRules.PropagationChain chain) =>
        new(
            chain.RootTargetKey,
            chain.RootTargetDisplayText,
            chain.Hops.Select(ProjectPropagationHop).ToArray());

    private static ModelPlanning.PropagationHop ProjectLegacyPropagationHop(object hop) =>
        new(
            GetRequiredString(hop, "FromTargetKey"),
            GetRequiredString(hop, "FromTargetDisplayText"),
            GetRequiredString(hop, "ToTargetKey"),
            GetRequiredString(hop, "ToTargetDisplayText"),
            GetRequiredString(hop, "RuleId"),
            Enum.Parse<ModelPrimitives.PlanActionKind>(GetRequiredPropertyValue(hop, "ActionKind").ToString() ?? nameof(ModelPrimitives.PlanActionKind.Delete)),
            ProjectLegacyPropagationEvidence(GetRequiredPropertyValue(hop, "Evidence")));

    private static ModelPlanning.PropagationHop ProjectPropagationHop(ModelRules.PropagationHop hop) =>
        new(
            hop.FromTargetKey,
            hop.FromTargetDisplayText,
            hop.ToTargetKey,
            hop.ToTargetDisplayText,
            hop.RuleId,
            hop.ActionKind,
            ProjectPropagationEvidence(hop.Evidence));

    private static ModelPlanning.PropagationEvidence ProjectLegacyPropagationEvidence(object evidence) =>
        new(
            GetOptionalStringList(evidence, "RelatedSymbolKeys") ?? Array.Empty<string>(),
            GetOptionalStringList(evidence, "RelatedSymbolNames") ?? Array.Empty<string>());

    private static ModelPlanning.PropagationEvidence ProjectPropagationEvidence(ModelRules.PropagationEvidence evidence) =>
        new(evidence.RelatedSymbolKeys, evidence.RelatedSymbolNames);

    private static ModelPlanning.PlanConflict ProjectLegacyConflict(object legacyConflict)
    {
        var target = GetRequiredPropertyValue(legacyConflict, "Target");
        var actionKinds = ((IEnumerable<object>)GetRequiredPropertyValue(legacyConflict, "ActionKinds"))
            .Select(kind => Enum.Parse<ModelPrimitives.PlanActionKind>(kind.ToString() ?? nameof(ModelPrimitives.PlanActionKind.Delete)))
            .ToArray();
        return new ModelPlanning.PlanConflict(
            GetRequiredString(legacyConflict, "ConflictCode"),
            ProjectLegacyTargetIdentity(target),
            ProjectLegacyTargetLocator(target),
            actionKinds,
            GetRequiredString(legacyConflict, "Reason"));
    }

    private static ModelPrimitives.TargetIdentity ProjectLegacyTargetIdentity(object legacyTarget) =>
        new(
            GetRequiredString(legacyTarget, "DocumentPath"),
            new ModelPrimitives.MemberId(GetRequiredPropertyValue(legacyTarget, "MemberId").GetType().GetProperty("Value")?.GetValue(GetRequiredPropertyValue(legacyTarget, "MemberId"))?.ToString() ?? string.Empty),
            Enum.Parse<ModelPrimitives.MemberKind>(GetRequiredPropertyValue(legacyTarget, "MemberKind").ToString() ?? nameof(ModelPrimitives.MemberKind.Unknown)),
            Enum.Parse<ModelPrimitives.TargetKind>(GetRequiredPropertyValue(legacyTarget, "TargetKind").ToString() ?? nameof(ModelPrimitives.TargetKind.Statement)));

    private static ModelPrimitives.TargetLocator ProjectLegacyTargetLocator(object legacyTarget)
    {
        var resolutionKey = GetOptionalPropertyValue<object>(legacyTarget, "EffectiveResolutionKey");
        return new ModelPrimitives.TargetLocator(
            GetRequiredPropertyValue<int>(legacyTarget, "SpanStart"),
            GetRequiredPropertyValue<int>(legacyTarget, "SpanLength"),
            GetRequiredString(legacyTarget, "DisplayText"),
            resolutionKey is null
                ? null
                : new ModelPrimitives.TargetResolutionKey(
                    GetRequiredPropertyValue<int>(resolutionKey, "SpanStart"),
                    GetRequiredPropertyValue<int>(resolutionKey, "SpanLength")));
    }

    private static object GetRequiredPropertyValue(object instance, string propertyName) =>
        instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance)
        ?? throw new InvalidOperationException($"Legacy rewrite compatibility projection could not read property '{propertyName}'.");

    private static T GetRequiredPropertyValue<T>(object instance, string propertyName) =>
        (T)(GetRequiredPropertyValue(instance, propertyName) ?? throw new InvalidOperationException($"Legacy rewrite compatibility projection could not read property '{propertyName}'."));

    private static T? GetOptionalPropertyValue<T>(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property == null)
        {
            return default;
        }

        var value = property.GetValue(instance);
        return value is null ? default : (T)value;
    }

    private static string? GetOptionalString(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        return property?.GetValue(instance)?.ToString();
    }

    private static IReadOnlyList<string>? GetOptionalStringList(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property?.GetValue(instance) is not IEnumerable<object> values)
        {
            return null;
        }

        return values.Select(static value => value.ToString() ?? string.Empty).ToArray();
    }

    private static TEnum? GetOptionalEnum<TEnum>(object instance, string propertyName)
        where TEnum : struct
    {
        var raw = GetOptionalString(instance, propertyName);
        return Enum.TryParse<TEnum>(raw, out var parsed) ? parsed : null;
    }

    private static string GetRequiredString(object instance, string propertyName) =>
        GetRequiredPropertyValue(instance, propertyName)?.ToString()
        ?? throw new InvalidOperationException($"Legacy rewrite compatibility projection could not read string property '{propertyName}'.");

    private sealed record ApplyChangeResult(bool IsSuccess, SyntaxNode? Root, string Message)
    {
        public static ApplyChangeResult Success(SyntaxNode root) => new(true, root, string.Empty);

        public static ApplyChangeResult Failure(string message) => new(false, null, message);
    }

    private sealed record RewriteDocumentContext(
        ModelAnalysis.SourceDocument Document,
        CompilationUnitSyntax Root,
        SemanticModel? SemanticModel);
}





