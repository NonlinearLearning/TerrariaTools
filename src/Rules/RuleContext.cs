using Microsoft.CodeAnalysis;
using MinimalRoslynCpg.Analysis;
using MinimalRoslynCpg.Model;

namespace Rules;

/// <summary>
/// 规则执行时共享的最小上下文。
/// </summary>
public sealed class RuleContext
{
  public CpgAnalysisContext AnalysisContext { get; }
  public RoslynCpgStructureView? StructureView { get; }

  /// <summary>
  /// 与当前源码对应的最小 CPG 图。
  /// </summary>
  public RoslynCpgGraph Graph => AnalysisContext.Graph;

  /// <summary>
  /// 当前源码的 Roslyn 语义模型。
  /// </summary>
  public SemanticModel SemanticModel => AnalysisContext.SemanticModel;

  /// <summary>
  /// 当前分析源码的语法树根节点。
  /// </summary>
  public SyntaxNode Root => AnalysisContext.CompilationRoot;

  public IReadOnlyDictionary<string, string> Options { get; }

  public RuleContext(
    CpgAnalysisContext analysisContext,
    IReadOnlyDictionary<string, string> options,
    RoslynCpgStructureView? structureView = null)
  {
    AnalysisContext = analysisContext;
    Options = options;
    StructureView = structureView;
  }

  public bool TryGetOption(string key, out string value)
  {
    return Options.TryGetValue(key, out value!);
  }

  public RuleContext WithStructureView(RoslynCpgStructureView structureView)
  {
    return new RuleContext(AnalysisContext, Options, structureView);
  }
}
