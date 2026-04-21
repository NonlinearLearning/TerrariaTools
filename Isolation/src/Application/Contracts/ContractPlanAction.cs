namespace Application.Contracts;

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
