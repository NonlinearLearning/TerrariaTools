using Domain.Marking;
using Domain.Propagation;

namespace Logic.Marking.Events;

/// <summary>
/// 表示标记阶段领域事件发布输入。
/// </summary>
public sealed class MarkingDomainEventPublishInput
{
    public Guid RunCorrelationId { get; init; }

    public Guid WorkspaceContextId { get; init; }

    public RuleTarget RuleTarget { get; init; } = null!;

    public IReadOnlyCollection<ChangeCandidate> Candidates { get; init; } = Array.Empty<ChangeCandidate>();
}
