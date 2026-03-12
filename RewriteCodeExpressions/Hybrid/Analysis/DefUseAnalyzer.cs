using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Context;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;

public sealed class DefUseAnalyzer
{
    private readonly RewriteContext _context;
    private readonly DefUseGraph _graph = new();

    public DefUseAnalyzer(RewriteContext context)
    {
        _context = context;
    }

    public void OnParameter(ParameterSyntax parameter)
    {
        var symbol = _context.SemanticModel.GetDeclaredSymbol(parameter);
        if (symbol is IParameterSymbol)
        {
            _graph.AddDefinition(symbol, parameter);
        }
    }

    public void OnVariableDeclarator(VariableDeclaratorSyntax declarator)
    {
        var symbol = _context.SemanticModel.GetDeclaredSymbol(declarator);
        if (symbol is ILocalSymbol)
        {
            _graph.AddDefinition(symbol, declarator);
        }
    }

    public void OnIdentifierName(IdentifierNameSyntax identifier)
    {
        var symbolInfo = _context.SemanticModel.GetSymbolInfo(identifier);
        var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
        if (symbol is not (ILocalSymbol or IParameterSymbol))
        {
            return;
        }

        var definitions = _graph.GetDefinitions(symbol);
        if (definitions.Count == 0)
        {
            return;
        }

        var useNode = _graph.AddUse(symbol, identifier);
        foreach (var definition in definitions)
        {
            _graph.AddDefUseEdge(definition, useNode);
        }
    }

    public void PublishToContext()
    {
        _context.SetState(AnalysisStateKeys.DefUseGraph, _graph);
        _context.SetState(AnalysisStateKeys.DefinitionMap, _graph.GetDefinitionMap());
        _context.SetState(AnalysisStateKeys.UnusedDefinitions, _graph.GetUnusedDefinitions());
    }
}

