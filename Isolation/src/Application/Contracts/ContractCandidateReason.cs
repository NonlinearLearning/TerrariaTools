namespace Application.Contracts;

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
