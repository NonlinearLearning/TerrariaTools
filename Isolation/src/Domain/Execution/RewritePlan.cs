using Domain.Common;
using Domain.Decision;
using Domain.Execution.Events;
using Domain.Workspaces;

namespace Domain.Execution;

/// <summary>
/// 表示执行阶段的改写计划。
/// </summary>
public sealed class RewritePlan : AggregateRoot<Guid>
{
    private readonly List<PlanChangeItem> changeItems = new();
    private readonly List<PlanConflict> conflicts = new();
    private bool isCompiled;

    private RewritePlan(Guid id, PlanMetadata metadata)
        : base(id)
    {
        Metadata = metadata;
    }

    public PlanMetadata Metadata { get; }

    public IReadOnlyCollection<PlanChangeItem> ChangeItems => changeItems.AsReadOnly();

    public IReadOnlyCollection<PlanConflict> Conflicts => conflicts.AsReadOnly();

    public bool HasConflicts => conflicts.Count > 0;

    public bool IsCompiled => isCompiled;

    public static RewritePlan Create(PlanMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return new RewritePlan(Guid.NewGuid(), metadata);
    }

    public PlanChangeItem RegisterChange(
        Guid candidateId,
        PlanTarget planTarget,
        PlanAction planAction,
        PlanReason planReason)
    {
        PlanChangeItem planChangeItem = PlanChangeItem.Create(candidateId, planTarget, planAction, planReason);
        AddChangeItem(planChangeItem);
        return planChangeItem;
    }

    public void AddChangeItem(PlanChangeItem planChangeItem)
    {
        ArgumentNullException.ThrowIfNull(planChangeItem);
        EnsureMutable();
        bool duplicateTarget = changeItems.Any(current =>
            string.Equals(current.PlanTarget.DocumentPath.Value, planChangeItem.PlanTarget.DocumentPath.Value, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(current.PlanTarget.TargetName, planChangeItem.PlanTarget.TargetName, StringComparison.Ordinal) &&
            current.PlanAction == planChangeItem.PlanAction);
        if (duplicateTarget)
        {
            throw new InvalidOperationException("同一目标与动作不允许重复进入执行计划。");
        }

        if (planChangeItem.Order == 0)
        {
            planChangeItem.SetOrder(changeItems.Count + 1);
        }

        changeItems.Add(planChangeItem);
    }

    public PlanChangeItem ApplyDecisionOutcome(
        Guid candidateId,
        RewriteDecision decision,
        PlanTarget planTarget,
        PlanAction planAction)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(planTarget);
        EnsureMutable();

        PlanChangeItem item = RegisterChange(
            candidateId,
            planTarget,
            planAction,
            decision.Approvals.ContainsKey(candidateId)
                ? PlanReason.CandidateApproved
                : PlanReason.LinkedActionDetected);

        if (decision.Conflicts.Count > 0)
        {
            AddConflict(PlanConflict.ParentCoverage);
        }

        return item;
    }

    public void AddConflict(PlanConflict planConflict)
    {
        AddConflict(planConflict, Guid.Empty);
    }

    public void AddConflict(PlanConflict planConflict, Guid correlationId)
    {
        if (planConflict == PlanConflict.None)
        {
            throw new InvalidOperationException("无效计划冲突不能加入执行计划。");
        }

        EnsureMutable();

        if (!conflicts.Contains(planConflict))
        {
            conflicts.Add(planConflict);
        }

        if (correlationId != Guid.Empty && !HasDomainEvent("PlanConflictDetected", correlationId))
        {
            AddDomainEvent(new PlanConflictDetectedDomainEvent(
                Id,
                correlationId,
                conflicts.ToArray()));
        }
    }

    public void OrderChangeItem(Guid planChangeItemId, int order)
    {
        EnsureMutable();
        PlanChangeItem? item = changeItems.SingleOrDefault(currentItem => currentItem.Id == planChangeItemId);
        if (item is null)
        {
            throw new InvalidOperationException($"未找到计划项：{planChangeItemId}");
        }

        item.SetOrder(order);
    }

    public void ValidateReadyForExecution()
    {
        if (changeItems.Count == 0)
        {
            throw new InvalidOperationException("执行计划至少需要一个计划项。");
        }

        if (conflicts.Count > 0)
        {
            throw new InvalidOperationException("执行计划仍存在未解决冲突。");
        }

        if (changeItems.Any(static current => current.Order <= 0))
        {
            throw new InvalidOperationException("执行计划中的排序必须从 1 开始且保持连续。");
        }

        bool duplicatedOrder = changeItems
            .Where(static current => current.Order > 0)
            .GroupBy(static current => current.Order)
            .Any(static group => group.Count() > 1);
        if (duplicatedOrder)
        {
            throw new InvalidOperationException("执行计划存在重复排序。");
        }

        int[] ordered = changeItems
            .Select(static current => current.Order)
            .OrderBy(static current => current)
            .ToArray();
        for (int index = 0; index < ordered.Length; index++)
        {
            if (ordered[index] != index + 1)
            {
                throw new InvalidOperationException("执行计划中的排序必须连续，不能跳号。");
            }
        }
    }

    public void RecordCompiled(Guid correlationId)
    {
        Compile(correlationId);
    }

    public void Compile(Guid correlationId)
    {
        EnsureCompilable();
        Guid resolvedCorrelationId = correlationId == Guid.Empty ? Id : correlationId;
        if (HasDomainEvent("RewritePlanCompiled", resolvedCorrelationId))
        {
            return;
        }

        isCompiled = true;
        AddDomainEvent(new RewritePlanCompiledDomainEvent(
            Id,
            resolvedCorrelationId,
            Metadata.PlanName,
            changeItems.Count));
    }

    private void EnsureMutable()
    {
        if (isCompiled)
        {
            throw new InvalidOperationException("计划编译后不能继续修改。");
        }
    }

    private void EnsureCompilable()
    {
        if (changeItems.Count == 0)
        {
            throw new InvalidOperationException("执行计划至少需要一个计划项后才能编译。");
        }

        if (changeItems.Any(static current => current.Order <= 0))
        {
            throw new InvalidOperationException("执行计划中的排序必须从 1 开始且保持连续。");
        }

        bool duplicatedOrder = changeItems
            .Where(static current => current.Order > 0)
            .GroupBy(static current => current.Order)
            .Any(static group => group.Count() > 1);
        if (duplicatedOrder)
        {
            throw new InvalidOperationException("执行计划存在重复排序。");
        }

        int[] ordered = changeItems
            .Select(static current => current.Order)
            .OrderBy(static current => current)
            .ToArray();
        for (int index = 0; index < ordered.Length; index++)
        {
            if (ordered[index] != index + 1)
            {
                throw new InvalidOperationException("执行计划中的排序必须连续，不能跳号。");
            }
        }
    }
}
