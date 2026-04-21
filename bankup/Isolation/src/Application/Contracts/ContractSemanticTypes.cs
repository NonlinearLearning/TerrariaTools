namespace Application.Contracts;

public enum ContractCandidateKind
{
    Unknown = 0,
    Type = 1,
    Method = 2,
    Member = 3,
    Caller = 4,
    ClosureRoot = 5,
}

public enum ContractCandidateReason
{
    Unknown = 0,
    RuleConfigured = 1,
    EntryPointMatched = 2,
    CallChainMatched = 3,
    DataFlowReachable = 4,
    ManualReviewRequired = 5,
    CompositeLayerConflict = 6,
    PublicContractDetected = 7,
    RuntimeClosureRequired = 8,
    ShadowBoundaryRequired = 9,
}

public enum ContractScenarioTag
{
    Unknown = 0,
    ClassDeletion = 1,
    MethodDeletion = 2,
    MethodPrivatization = 3,
    MethodBodyClearing = 4,
    MemberSlice = 5,
    ShadowClassGeneration = 6,
    MinimalRuntimeClosure = 7,
    PlanDrivenRewrite = 8,
    EvidenceDrivenAudit = 9,
}

public enum ContractSliceDirection
{
    Unknown = 0,
    Forward = 1,
    Backward = 2,
    Bidirectional = 3,
}

public enum ContractAnalysisSourceKind
{
    Unknown = 0,
    Solution = 1,
    Project = 2,
    Directory = 3,
    SourceFile = 4,
}

public enum ContractMinimumAnalysisTarget
{
    Unknown = 0,
    File = 1,
    Type = 2,
    Method = 3,
    Statement = 4,
}

public enum ContractCpgNodeType
{
    Unknown = 0,
    File = 1,
    TypeDecl = 2,
    Method = 3,
    Call = 4,
}

public enum ContractConfidenceLevel
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}

public enum ContractClosureIntegrityStatus
{
    Unknown = 0,
    Intact = 1,
    Broken = 2,
}

public enum ContractApprovalReason
{
    Unknown = 0,
    StaticFactConfirmed = 1,
    PropagationBounded = 2,
    CoveredByParentDeletion = 3,
    ClosureIntegrityVerified = 4,
    ShadowBoundaryStable = 5,
}

public enum ContractRejectionReason
{
    Unknown = 0,
    ExternalContractDetected = 1,
    ExternalCallerDetected = 2,
    ClosureIntegrityBroken = 3,
    PropagationRiskTooHigh = 4,
    ManualReviewRequired = 5,
}

public enum ContractPlanAction
{
    Unknown = 0,
    DeleteClass = 1,
    DeleteMethod = 2,
    PrivatizeMethod = 3,
    ClearMethodBody = 4,
    SliceMember = 5,
    GenerateShadowClass = 6,
    ExtractRuntimeClosure = 7,
}

public enum ContractPlanReason
{
    Unknown = 0,
    CandidateApproved = 1,
    LinkedActionDetected = 2,
    ParentCoverageResolved = 3,
    ClosureBoundaryRequired = 4,
    ShadowBoundaryRequired = 5,
}

public enum ContractPlanConflict
{
    None = 0,
    DuplicateTarget = 1,
    OverlappingRange = 2,
    ParentCoverage = 3,
    MutuallyExclusiveAction = 4,
}

public enum ContractRunMode
{
    Unknown = 0,
    AnalysisOnly = 1,
    DecisionOnly = 2,
    FullWorkflow = 3,
}

public enum ContractInputOrigin
{
    Unknown = 0,
    Solution = 1,
    Project = 2,
    Directory = 3,
    SourceFile = 4,
}

public enum ContractCodeRewriteKind
{
    Unknown = 0,
    DeleteClass = 1,
    DeleteMethod = 2,
    PrivatizeMethod = 3,
    ClearMethodBody = 4,
}

public enum ContractRiskLevel
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}

public enum ContractAuditConclusion
{
    Unknown = 0,
    ApprovedForExecution = 1,
    ApprovedForMerge = 2,
    ReferenceOnly = 3,
    RequiresManualReview = 4,
}

public sealed record ContractExposureDto(bool IsPublicSurface, string Source);

public sealed record ContractExternalCallerPresenceDto(IReadOnlyCollection<string> Callers)
{
    public bool Exists => Callers.Count > 0;
}

public sealed record ContractClosureIntegrityAssessmentDto(bool IsBroken, string Summary);

public sealed record ContractRiskScoreDto(int Score, string Reason)
{
    public bool IsHighRisk => Score >= 80;
}
