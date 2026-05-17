using Microsoft.CodeAnalysis;
using MinimalRoslynCpg.Model;

namespace Rules;

public sealed class RuleContext
{
  public RoslynCpgGraph Graph { get; }
  public SemanticModel SemanticModel { get; }
  public SyntaxNode Root { get; }
  public IReadOnlyDictionary<string, string> Options { get; }

  public RuleContext(
    RoslynCpgGraph graph,
    SemanticModel semanticModel,
    SyntaxNode root,
    IReadOnlyDictionary<string, string> options)
  {
    Graph = graph;
    SemanticModel = semanticModel;
    Root = root;
    Options = options;
  }

  public bool TryGetOption(string key, out string value)
  {
    return Options.TryGetValue(key, out value!);
  }
}
