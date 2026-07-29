using Microsoft.CodeAnalysis;
using NLISSN.Core.Pipeline;

namespace NLISSN.Core.Analysis.View;

public interface IRuleStructureView
{
  NLCPGStructureView? StructureView { get; }

  NLCPGStructureView BuildStructureView(IReadOnlyCollection<SyntaxNode> fragments);

  RuleContext WithStructureView(NLCPGStructureView structureView);
}
