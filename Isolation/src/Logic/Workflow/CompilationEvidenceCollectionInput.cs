using Domain.Execution;

namespace Logic.Workflow;

/// <summary>
/// 表示编译证据采集输入。
/// </summary>
public sealed record CompilationEvidenceCollectionInput(
    RewriteResult RewriteResult,
    bool Success,
    int DiagnosticCount);
