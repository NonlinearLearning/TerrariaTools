namespace Domain.Execution;

/// <summary>
/// 表示计划动作。
/// </summary>
public enum PlanAction
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
