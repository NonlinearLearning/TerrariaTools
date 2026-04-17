using Domain.Common;
using Domain.Workspaces;

namespace Domain.Execution;

/// <summary>
/// 表示执行阶段的改写计划。
/// </summary>
public sealed class RewritePlan : AggregateRoot<Guid>
{
    private readonly List<PlanChangeItem> changeItems = new();
    private readonly List<PlanConflict> conflicts = new();

    private RewritePlan(Guid id, PlanMetadata metadata)
        : base(id)
    {
        Metadata = metadata;
    }

    public PlanMetadata Metadata { get; }

    public IReadOnlyCollection<PlanChangeItem> ChangeItems => changeItems.AsReadOnly();

    public IReadOnlyCollection<PlanConflict> Conflicts => conflicts.AsReadOnly();

    public static RewritePlan Create(PlanMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return new RewritePlan(Guid.NewGuid(), metadata);
    }

    public void AddChangeItem(PlanChangeItem planChangeItem)
    {
        ArgumentNullException.ThrowIfNull(planChangeItem);
        changeItems.Add(planChangeItem);
    }

    public void AddConflict(PlanConflict planConflict)
    {
        conflicts.Add(planConflict);
    }

    public void OrderChangeItem(Guid planChangeItemId, int order)
    {
        PlanChangeItem? item = changeItems.SingleOrDefault(currentItem => currentItem.Id == planChangeItemId);
        item?.SetOrder(order);
    }
}

/// <summary>
/// 表示计划元数据。
/// </summary>
public sealed class PlanMetadata
{
    public PlanMetadata(string planName, string compilerVersion, DateTimeOffset createdAt, string? note)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planName);
        ArgumentException.ThrowIfNullOrWhiteSpace(compilerVersion);
        PlanName = planName.Trim();
        CompilerVersion = compilerVersion.Trim();
        CreatedAt = createdAt;
        Note = note?.Trim();
    }

    public string PlanName { get; }

    public string CompilerVersion { get; }

    public DateTimeOffset CreatedAt { get; }

    public string? Note { get; }
}

/// <summary>
/// 表示计划目标。
/// </summary>
public sealed class PlanTarget
{
    public PlanTarget(DocumentPath documentPath, string targetName, string? memberSignature, string? anchorText)
    {
        ArgumentNullException.ThrowIfNull(documentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        DocumentPath = documentPath;
        TargetName = targetName.Trim();
        MemberSignature = memberSignature?.Trim();
        AnchorText = anchorText?.Trim();
    }

    public DocumentPath DocumentPath { get; }

    public string TargetName { get; }

    public string? MemberSignature { get; }

    public string? AnchorText { get; }
}

/// <summary>
/// 表示计划项。
/// </summary>
public sealed class PlanChangeItem : Entity<Guid>
{
    private readonly List<PlanReason> reasons = new();

    private PlanChangeItem(Guid id, Guid candidateId, PlanTarget planTarget, PlanAction planAction, PlanReason planReason)
        : base(id)
    {
        CandidateId = candidateId;
        PlanTarget = planTarget;
        PlanAction = planAction;
        reasons.Add(planReason);
    }

    public Guid CandidateId { get; }

    public PlanTarget PlanTarget { get; }

    public PlanAction PlanAction { get; }

    public int Order { get; private set; }

    public IReadOnlyCollection<PlanReason> Reasons => reasons.AsReadOnly();

    public static PlanChangeItem Create(
        Guid candidateId,
        PlanTarget planTarget,
        PlanAction planAction,
        PlanReason planReason)
    {
        ArgumentNullException.ThrowIfNull(planTarget);
        return new PlanChangeItem(Guid.NewGuid(), candidateId, planTarget, planAction, planReason);
    }

    public void AddReason(PlanReason planReason)
    {
        if (!reasons.Contains(planReason))
        {
            reasons.Add(planReason);
        }
    }

    public void SetOrder(int order)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(order);
        Order = order;
    }
}

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

/// <summary>
/// 表示计划原因。
/// </summary>
public enum PlanReason
{
    Unknown = 0,
    CandidateApproved = 1,
    LinkedActionDetected = 2,
    ParentCoverageResolved = 3,
    ClosureBoundaryRequired = 4,
    ShadowBoundaryRequired = 5,
}

/// <summary>
/// 表示计划冲突。
/// </summary>
public enum PlanConflict
{
    None = 0,
    DuplicateTarget = 1,
    OverlappingRange = 2,
    ParentCoverage = 3,
    MutuallyExclusiveAction = 4,
}
