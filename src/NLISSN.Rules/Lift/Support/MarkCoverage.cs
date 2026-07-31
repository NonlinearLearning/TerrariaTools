using Microsoft.CodeAnalysis;
using NLISSN.Core.Marking;

namespace NLISSN.Rules;

/// <summary>Proves syntax coverage from marks without promoting an arbitrary descendant.</summary>
public static class MarkCoverage
{
  public static bool IsCovered(SyntaxNode requiredNode, IEnumerable<MarkRecord> marks)
  {
    var markedKeys = marks
      .Select(mark => LiftingCommon.BuildNodeKey(mark.SyntaxNode))
      .ToHashSet();
    return IsCovered(requiredNode, markedKeys);
  }

  private static bool IsCovered(
    SyntaxNode requiredNode,
    IReadOnlySet<(int Start, int Length, int RawKind)> markedKeys)
  {
    if (markedKeys.Contains(LiftingCommon.BuildNodeKey(requiredNode)))
    {
      return true;
    }

    var children = requiredNode.ChildNodes().ToList();
    return children.Count > 0 && children.All(child => IsCovered(child, markedKeys));
  }
}
