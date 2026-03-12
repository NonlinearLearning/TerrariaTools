using Microsoft.CodeAnalysis;
using QuikGraph;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;

public sealed class DefUseGraph
{
    private readonly AdjacencyGraph<DefUseNode, Edge<DefUseNode>> _graph = new();
    private readonly Dictionary<ISymbol, List<DefUseNode>> _definitionsBySymbol =
        new(SymbolEqualityComparer.Default);

    public IEnumerable<DefUseNode> Nodes => _graph.Vertices;

    public DefUseNode AddDefinition(ISymbol symbol, SyntaxNode syntax)
    {
        var node = new DefUseNode(symbol, syntax, DefUseNodeKind.Definition);
        _graph.AddVertex(node);

        if (!_definitionsBySymbol.TryGetValue(symbol, out var list))
        {
            list = new List<DefUseNode>();
            _definitionsBySymbol[symbol] = list;
        }

        list.Add(node);
        return node;
    }

    public DefUseNode AddUse(ISymbol symbol, SyntaxNode syntax)
    {
        var node = new DefUseNode(symbol, syntax, DefUseNodeKind.Use);
        _graph.AddVertex(node);
        return node;
    }

    public void AddDefUseEdge(DefUseNode definition, DefUseNode use)
    {
        _graph.AddEdge(new Edge<DefUseNode>(definition, use));
    }

    public IReadOnlyList<DefUseNode> GetDefinitions(ISymbol symbol)
    {
        return _definitionsBySymbol.TryGetValue(symbol, out var defs) ? defs : Array.Empty<DefUseNode>();
    }

    public IReadOnlyList<DefUseNode> GetUses(DefUseNode definition)
    {
        return _graph.OutEdges(definition).Select(edge => edge.Target).ToList();
    }

    public IReadOnlyList<DefUseNode> GetIncomingDefinitions(DefUseNode use)
    {
        return _graph.Edges
            .Where(edge => ReferenceEquals(edge.Target, use))
            .Select(edge => edge.Source)
            .Where(node => node.Kind == DefUseNodeKind.Definition)
            .ToList();
    }

    public bool HasEdge(DefUseNode definition, DefUseNode use)
    {
        return _graph.Edges.Any(edge => ReferenceEquals(edge.Source, definition) && ReferenceEquals(edge.Target, use));
    }

    public IReadOnlyList<DefUseNode> GetUnusedDefinitions()
    {
        return _graph.Vertices
            .Where(node => node.Kind == DefUseNodeKind.Definition && !_graph.OutEdges(node).Any())
            .ToList();
    }

    public IReadOnlyDictionary<ISymbol, IReadOnlyList<DefUseNode>> GetDefinitionMap()
    {
        return _definitionsBySymbol.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<DefUseNode>)kv.Value,
            SymbolEqualityComparer.Default);
    }
}

