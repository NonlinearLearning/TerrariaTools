namespace NLISSN.Infrastructure.Configuration;

internal sealed record ExecutionSettings(
    bool WriteBack,
    bool SkipRewrite,
    int MaxDegreeOfParallelism,
    int? CpgMaxDegreeOfParallelism,
    bool DirectoryParallelism,
    bool GroupParallelism,
    bool HelperParallelism,
    bool FastDeleteClassDirectory,
    bool FilterDeleteClassFilesByTargetName);
