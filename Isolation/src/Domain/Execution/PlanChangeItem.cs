using Domain.Common;

namespace Domain.Execution;

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
