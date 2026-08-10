namespace NLISSN.Infrastructure.Configuration;

internal sealed record ArtifactSettings(
    string RunRoot,
    string DiffRoot,
    string RuntimeLogPath,
    string EvidencePath,
    string RewritePlanRoot,
    string? ReplayPlanPath,
    string ResolvedConfigurationPath,
    bool WriteDiff,
    bool WriteRuntimeLog,
    bool WriteEvidence,
    RewritePlanMode RewritePlanMode,
    string DiffView,
    string RunId = "");
