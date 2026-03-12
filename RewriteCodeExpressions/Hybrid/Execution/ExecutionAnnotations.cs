using Microsoft.CodeAnalysis;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Execution;

/// <summary>
/// Hybrid 执行阶段使用的语法标记。
/// </summary>
public static class ExecutionAnnotations
{
    public static readonly SyntaxAnnotation DeleteNode = new("Hybrid.DeleteNode");
    public static readonly SyntaxAnnotation InsertBeforeStatements = new("Hybrid.InsertBeforeStatements");
    public static readonly SyntaxAnnotation InsertAfterStatements = new("Hybrid.InsertAfterStatements");
}
