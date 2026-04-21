namespace Application.Contracts;

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
