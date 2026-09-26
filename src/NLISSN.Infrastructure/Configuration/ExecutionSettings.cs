namespace NLISSN.Infrastructure.Configuration;

internal sealed record ExecutionSettings(
    bool WriteBack,
    bool SkipRewrite,
    int DirectoryMaxDegreeOfParallelism,
    int CpgMaxDegreeOfParallelism,
    int GroupMaxDegreeOfParallelism,
    int HelperMaxDegreeOfParallelism,
    int ReplayMaxDegreeOfParallelism,
    int MaxConcurrentOperations,
    bool DirectoryParallelism,
    bool GroupParallelism,
    bool HelperParallelism,
    bool FastDeleteClassDirectory,
    bool FilterDeleteClassFilesByTargetName);
