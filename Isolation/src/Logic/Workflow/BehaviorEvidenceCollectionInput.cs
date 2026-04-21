using Domain.Execution;

namespace Logic.Workflow;

/// <summary>
/// 表示行为证据采集输入。
/// </summary>
public sealed record BehaviorEvidenceCollectionInput(
    string ScenarioName,
    RewriteResult RewriteResult);
