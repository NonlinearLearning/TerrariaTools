using NLCPG.Analysis;
using Microsoft.CodeAnalysis;
using NLISSN.Core.Pipeline;

namespace NLISSN.Core.Analysis.View;

public interface IRuleStructureView
{
  NLCPGStructureView? StructureView { get; }

  NLCPGStructureView BuildStructureView(
    IReadOnlyCollection<SyntaxNode> fragments,
    CpgRelationProfile profile,
    CpgQueryDirection direction,
    NLCPGTraversalBudget budget);

  CpgStructureViewQueryResult QueryStructureView(
    IReadOnlyCollection<SyntaxNode> fragments,
    CpgRelationProfile profile,
    CpgQueryDirection direction,
    NLCPGTraversalBudget budget);

  RuleContext WithStructureView(NLCPGStructureView structureView);
}
