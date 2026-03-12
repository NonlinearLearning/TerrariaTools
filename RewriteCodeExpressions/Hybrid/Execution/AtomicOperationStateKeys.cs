namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Execution;

/// <summary>
/// State keys used by atomic operation middlewares.
/// </summary>
public static class AtomicOperationStateKeys
{
    public const string ReplacementNode = "hybrid.atomic.replacement.node";
    public const string InsertBeforeStatements = "hybrid.atomic.insert.before.statements";
    public const string InsertAfterStatements = "hybrid.atomic.insert.after.statements";
    public const string InsertBeforeRegistry = "hybrid.atomic.insert.before.registry";
    public const string InsertAfterRegistry = "hybrid.atomic.insert.after.registry";
}

