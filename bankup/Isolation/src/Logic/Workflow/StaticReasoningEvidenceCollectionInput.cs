namespace Logic.Workflow;

/// <summary>
/// 表示静态推理证据采集输入。
/// </summary>
public sealed record StaticReasoningEvidenceCollectionInput(
    string SubjectName,
    IReadOnlyCollection<string> ReasonChain);
