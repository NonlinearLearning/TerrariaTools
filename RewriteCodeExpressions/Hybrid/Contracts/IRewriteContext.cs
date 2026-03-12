using Microsoft.CodeAnalysis;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;

/// <summary>
/// Defines context for the rewrite pipeline.
/// </summary>
public interface IRewriteContext
{
    /// <summary>
    /// Semantic model for the current syntax tree.
    /// </summary>
    SemanticModel SemanticModel { get; }

    /// <summary>
    /// Original syntax tree before rewrite.
    /// </summary>
    SyntaxTree OriginalTree { get; }

    /// <summary>
    /// Current lexical scope.
    /// </summary>
    IScope CurrentScope { get; }

    /// <summary>
    /// Gets a state value by key.
    /// </summary>
    T? GetState<T>(string key);

    /// <summary>
    /// Stores a state value by key.
    /// </summary>
    void SetState<T>(string key, T value);

    /// <summary>
    /// Reports a diagnostic.
    /// </summary>
    void ReportDiagnostic(Diagnostic diagnostic);

    /// <summary>
    /// Returns whether a variable/parameter name is defined in available analysis context.
    /// </summary>
    bool IsVariableDefined(string name);

    /// <summary>
    /// Finds reference syntax nodes for a declaration syntax node.
    /// </summary>
    IEnumerable<SyntaxNode> FindReferences(SyntaxNode declaration);
}

/// <summary>
/// Scope abstraction.
/// </summary>
public interface IScope
{
    /// <summary>
    /// Checks whether a symbol name is defined in this scope chain.
    /// </summary>
    bool IsDefined(string symbolName);

    /// <summary>
    /// Parent scope.
    /// </summary>
    IScope? Parent { get; }
}
