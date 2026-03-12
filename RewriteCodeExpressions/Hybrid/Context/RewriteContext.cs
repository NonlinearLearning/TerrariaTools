using Microsoft.CodeAnalysis;
using System.Collections.Concurrent;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Context;

/// <summary>
/// Basic rewrite context implementation.
/// </summary>
public class RewriteContext : IRewriteContext
{
    private readonly ConcurrentDictionary<string, object?> _state = new();
    private readonly List<Diagnostic> _diagnostics = new();

    public RewriteContext(SemanticModel semanticModel, SyntaxTree originalTree)
    {
        SemanticModel = semanticModel;
        OriginalTree = originalTree;
        CurrentScope = new GlobalScope();
    }

    public SemanticModel SemanticModel { get; }
    public SyntaxTree OriginalTree { get; }
    public IScope CurrentScope { get; set; }

    public T? GetState<T>(string key)
    {
        if (_state.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }

        return default;
    }

    public void SetState<T>(string key, T value)
    {
        _state[key] = value;
    }

    public void ReportDiagnostic(Diagnostic diagnostic)
    {
        _diagnostics.Add(diagnostic);
    }

    public IEnumerable<Diagnostic> GetDiagnostics() => _diagnostics;

    public bool IsVariableDefined(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (CurrentScope.IsDefined(name))
        {
            return true;
        }

        var definitionMap = GetState<IReadOnlyDictionary<ISymbol, IReadOnlyList<DefUseNode>>>(AnalysisStateKeys.DefinitionMap);
        if (definitionMap is null)
        {
            return false;
        }

        return definitionMap.Keys.Any(symbol =>
            symbol is ILocalSymbol or IParameterSymbol &&
            string.Equals(symbol.Name, name, StringComparison.Ordinal));
    }

    public IEnumerable<SyntaxNode> FindReferences(SyntaxNode declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        var graph = GetState<DefUseGraph>(AnalysisStateKeys.DefUseGraph);
        if (graph is null)
        {
            return Array.Empty<SyntaxNode>();
        }

        var symbol = SemanticModel.GetDeclaredSymbol(declaration);
        if (symbol is not (ILocalSymbol or IParameterSymbol))
        {
            return Array.Empty<SyntaxNode>();
        }

        var definitions = graph.GetDefinitions(symbol);
        if (definitions.Count == 0)
        {
            return Array.Empty<SyntaxNode>();
        }

        return definitions
            .SelectMany(def => graph.GetUses(def))
            .Select(use => use.Syntax)
            .Distinct();
    }
}

/// <summary>
/// Global scope stub.
/// </summary>
public class GlobalScope : IScope
{
    public IScope? Parent => null;

    public bool IsDefined(string symbolName)
    {
        return false;
    }
}
