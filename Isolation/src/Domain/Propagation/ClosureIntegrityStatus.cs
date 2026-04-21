namespace Domain.Propagation;

/// <summary>
/// 表示闭包完整性状态。
/// </summary>
public enum ClosureIntegrityStatus
{
    Unknown = 0,
    Verified = 1,
    Risky = 2,
    Broken = 3,
}
