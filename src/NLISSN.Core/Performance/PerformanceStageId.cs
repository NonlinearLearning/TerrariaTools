using NLISSN.Core.Pipeline;

namespace NLISSN.Core.Performance;

/// Stable protocol names for report stages. These names are part of the report contract.
public static class PerformanceStageId
{
  public const string Run = "Run";
  public const string WorkspaceLoad = "Workspace.Load";
  public const string DirectoryRead = "Directory.Read";
  public const string CpgBuild = "CPG.Build";
  public const string CpgSyntax = "CPG.Syntax";
  public const string CpgOperation = "CPG.Operation";
  public const string CpgDataFlow = "CPG.DataFlow";
  public const string RuleMark = "Rule.Mark";
  public const string RulePropagate = "Rule.Propagate";
  public const string RuleLift = "Rule.Lift";
  public const string RulePropose = "Rule.Propose";
  public const string ArtifactRewrite = "Artifact.Rewrite";
  public const string ArtifactDiff = "Artifact.Diff";
  public const string ArtifactEvidence = "Artifact.Evidence";
  public const string ArtifactWriteBack = "Artifact.WriteBack";

  public static string ForRule(RuleKind kind)
  {
    return kind switch
    {
      RuleKind.Mark => RuleMark,
      RuleKind.Propagate => RulePropagate,
      RuleKind.Lift => RuleLift,
      RuleKind.Propose => RulePropose,
      _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
  }

  public static IReadOnlyList<string> Required { get; } =
  [
    WorkspaceLoad,
    DirectoryRead,
    CpgBuild,
    CpgSyntax,
    CpgOperation,
    CpgDataFlow,
    RuleMark,
    RulePropagate,
    RuleLift,
    RulePropose,
    ArtifactRewrite,
    ArtifactDiff,
    ArtifactEvidence,
    ArtifactWriteBack
  ];
}
