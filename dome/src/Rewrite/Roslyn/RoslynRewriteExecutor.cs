using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;

namespace TerrariaTools.Dome.Rewrite.Roslyn;

public sealed partial class RoslynRewriteExecutor : ApplicationAbstractions.IRewriteExecutor
{
    public Task<ApplicationAbstractions.RewriteExecutionResult> ExecuteAsync(
        string source,
        object legacyPlan,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            new ApplicationAbstractions.SourceDocumentSet(
                "input.cs",
                "input.cs",
                [new ApplicationAbstractions.SourceDocument("input.cs", "input.cs", source)]),
            ProjectLegacyPlan(legacyPlan),
            cancellationToken);

    public Task<ApplicationAbstractions.RewriteExecutionResult> ExecuteAsync(
        object legacyDocumentContext,
        object legacyPlan,
        CancellationToken cancellationToken)
    {
        var sourceSet = ProjectLegacyDocumentContext(legacyDocumentContext);
        return ExecuteAsync(sourceSet, ProjectLegacyPlan(legacyPlan), cancellationToken);
    }

    public Task<ApplicationAbstractions.RewriteExecutionResult> ExecuteAsync(
        string source,
        ModelPlanning.AuditPlan plan,
        CancellationToken cancellationToken)
    {
        var documentPath = plan.Changes.FirstOrDefault()?.Target.DocumentPath ?? "input.cs";
        return ExecuteAsync(
            new ApplicationAbstractions.SourceDocumentSet(
                documentPath,
                Path.GetDirectoryName(documentPath) ?? documentPath,
                [
                    new ApplicationAbstractions.SourceDocument(documentPath, documentPath, source)
                ]),
            plan,
            cancellationToken);
    }

    public Task<ApplicationAbstractions.RewriteExecutionResult> ExecuteAsync(
        ApplicationAbstractions.SourceDocumentSet sourceSet,
        ModelPlanning.AuditPlan plan,
        CancellationToken cancellationToken)
    {
        if (sourceSet.Documents.Count != 1)
        {
            return Task.FromResult(ApplicationAbstractions.RewriteExecutionResult.Failure(
                "RoslynRewriteExecutor currently expects a single-document source set."));
        }

        if (plan.Conflicts.Count > 0)
        {
            return Task.FromResult(ApplicationAbstractions.RewriteExecutionResult.Failure(
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
            return Task.FromResult(ApplicationAbstractions.RewriteExecutionResult.Failure(bindResult.Message));
        }

        var trackedRoot = documentContext.Root.TrackNodes(bindResult.Plan.Changes.Select(boundChange => boundChange.OriginalNode));
        SyntaxNode root = trackedRoot;
        foreach (var change in bindResult.Plan.Changes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentNode = root.GetCurrentNode(change.OriginalNode);
            if (currentNode == null)
            {
                return Task.FromResult(ApplicationAbstractions.RewriteExecutionResult.Failure(
                    $"Target '{BuildTargetKey(change.Change.Target, change.Change.Locator)}' could not be resolved during rewrite because the bound node is no longer available in the current syntax tree."));
            }

            var applyResult = ApplyChange(root, currentNode, change.Change);
            if (!applyResult.IsSuccess || applyResult.Root == null)
            {
                return Task.FromResult(ApplicationAbstractions.RewriteExecutionResult.Failure(applyResult.Message));
            }

            root = applyResult.Root;
        }

        return Task.FromResult(ApplicationAbstractions.RewriteExecutionResult.Success(root.ToFullString()));
    }

    private static RewriteDocumentContext CreateDocumentContext(
        ApplicationAbstractions.SourceDocument document,
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

    private static ApplicationAbstractions.SourceDocumentSet ProjectLegacyDocumentContext(object legacyDocumentContext)
    {
        var document = GetRequiredPropertyValue(legacyDocumentContext, "Document");
        var relativePath = GetRequiredString(document, "RelativePath");
        var sourceText = GetRequiredString(document, "SourceText");
        return new ApplicationAbstractions.SourceDocumentSet(
            relativePath,
            relativePath,
            [new ApplicationAbstractions.SourceDocument(relativePath, relativePath, sourceText)]);
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
        return new ModelPlanning.PlannedChange(
            GetRequiredPropertyValue<int>(legacyChange, "ExecutionOrder"),
            ProjectLegacyTargetIdentity(target),
            ProjectLegacyTargetLocator(target),
            new ModelPlanning.PlanAction(
                Enum.Parse<ModelPrimitives.PlanActionKind>(GetRequiredPropertyValue(action, "Kind").ToString() ?? nameof(ModelPrimitives.PlanActionKind.Delete)),
                GetOptionalPropertyValue<string>(action, "Payload")),
            GetOptionalPropertyValue<object>(legacyChange, "Reason"),
            GetOptionalPropertyValue<object>(legacyChange, "Chain"));
    }

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

    private static string GetRequiredString(object instance, string propertyName) =>
        GetRequiredPropertyValue(instance, propertyName)?.ToString()
        ?? throw new InvalidOperationException($"Legacy rewrite compatibility projection could not read string property '{propertyName}'.");

    private sealed record ApplyChangeResult(bool IsSuccess, SyntaxNode? Root, string Message)
    {
        public static ApplyChangeResult Success(SyntaxNode root) => new(true, root, string.Empty);

        public static ApplyChangeResult Failure(string message) => new(false, null, message);
    }

    private sealed record RewriteDocumentContext(
        ApplicationAbstractions.SourceDocument Document,
        CompilationUnitSyntax Root,
        SemanticModel? SemanticModel);
}
