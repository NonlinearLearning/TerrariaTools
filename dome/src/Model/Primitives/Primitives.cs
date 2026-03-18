namespace TerrariaTools.Dome.Model.Primitives;

public enum MemberKind
{
    Unknown,
    Class,
    Field,
    Method,
    Constructor,
    Property,
    Accessor
}

public enum TargetKind
{
    Statement,
    Method,
    Field,
    Property,
    Class
}

public enum StatementKindRef
{
    Unknown,
    Initializer,
    Declaration,
    Assignment,
    If,
    While,
    For,
    Return,
    ObjectInitializerAssignment
}

public enum StatementScopeMode
{
    MinimalBlock,
    ParentBlockPiercing
}

public enum StatementGraphMaterialization
{
    None,
    SnapshotOnly,
    Full
}

public enum FunctionGraphMaterialization
{
    None,
    WholeProject,
    ExpandedMembers
}

public enum BoundaryKind
{
    Invocation
}

public sealed record ScopeAnalysisOptions(StatementScopeMode StatementScopeMode)
{
    public static ScopeAnalysisOptions Default { get; } = new(StatementScopeMode.MinimalBlock);
}

public enum PlanActionKind
{
    Delete,
    CommentOut,
    ReplaceWithDefault,
    AddReturn,
    ChangeVisibilityToPrivate,
    ReorderPublicMethods
}

public enum DecisionOrigin
{
    Rule,
    Seed,
    Projection,
    Propagation,
    BoundaryPromotion,
    Prediction,
    Cleanup
}

public enum DecisionCategory
{
    Delete,
    CommentOut,
    ReplaceWithDefault,
    AddReturn,
    VisibilityChange,
    Reorder
}

public enum WorkspaceLoadMode
{
    CodeAnalysis,
    SourceOnly,
    CodeAnalysisFallbackToSourceOnly
}

public enum WorkspaceLoadDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public readonly record struct MemberId(string Value)
{
    public override string ToString() => Value;
}

public sealed record TargetIdentity(
    string DocumentPath,
    MemberId MemberId,
    MemberKind MemberKind,
    TargetKind TargetKind)
{
    public string IdentityKey => $"{DocumentPath}|{MemberId.Value}|{TargetKind}";
}

public sealed record TargetLocator(
    int SpanStart,
    int SpanLength,
    string DisplayText,
    TargetResolutionKey? ResolutionKey = null)
{
    public string TargetKey => $"{SpanStart}|{SpanLength}|{DisplayText}";

    public TargetResolutionKey EffectiveResolutionKey => ResolutionKey ?? new(SpanStart, SpanLength);
}

public sealed record TargetResolutionKey(
    int SpanStart,
    int SpanLength);

public enum RunMode
{
    Standard,
    AnalyzeOnly,
    PlanOnly
}

public enum FailureCode
{
    None,
    WorkspaceLoadFailed,
    AnalysisFailed,
    PlanCompileFailed,
    RewriteFailed,
    BuildFailed,
    ReportFailed
}

public enum FunctionDependencyKind
{
    Calls,
    Creates,
    ReadsMember,
    WritesMember,
    UsesPropertyAccessor
}
