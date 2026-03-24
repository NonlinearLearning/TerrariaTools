namespace TerrariaTools.Dome.Application.Ports;

/// <summary>
/// 指定工作区加载阶段实际采用的输入来源。
/// </summary>
public enum WorkspaceLoadMode
{
    /// <summary>
    /// 使用基于代码分析的工作区加载结果。
    /// </summary>
    CodeAnalysis,

    /// <summary>
    /// 直接基于源码文件集合构造输入。
    /// </summary>
    SourceOnly,

    /// <summary>
    /// 先尝试代码分析，再回退到源码模式。
    /// </summary>
    CodeAnalysisFallbackToSourceOnly
}

/// <summary>
/// 表示工作区加载诊断的严重级别。
/// </summary>
public enum WorkspaceLoadDiagnosticSeverity
{
    /// <summary>
    /// 仅提供补充信息，不影响流程继续执行。
    /// </summary>
    Info,

    /// <summary>
    /// 表示存在需要关注但仍可继续执行的问题。
    /// </summary>
    Warning,

    /// <summary>
    /// 表示当前阶段发生了阻塞性错误。
    /// </summary>
    Error
}

/// <summary>
/// 指定 Dome 应用运行到哪个阶段后结束。
/// </summary>
public enum RunMode
{
    /// <summary>
    /// 执行分析、规划和重写全流程。
    /// </summary>
    Standard,

    /// <summary>
    /// 仅执行分析并输出分析产物。
    /// </summary>
    AnalyzeOnly,

    /// <summary>
    /// 执行到计划编译后结束，不进行重写。
    /// </summary>
    PlanOnly
}

/// <summary>
/// 表示应用层统一使用的失败代码。
/// </summary>
public enum FailureCode
{
    /// <summary>
    /// 没有失败。
    /// </summary>
    None,

    /// <summary>
    /// 工作区加载失败。
    /// </summary>
    WorkspaceLoadFailed,

    /// <summary>
    /// 分析阶段失败。
    /// </summary>
    AnalysisFailed,

    /// <summary>
    /// 计划编译阶段失败。
    /// </summary>
    PlanCompileFailed,

    /// <summary>
    /// 重写阶段失败。
    /// </summary>
    RewriteFailed,

    /// <summary>
    /// 构建阶段失败。
    /// </summary>
    BuildFailed,

    /// <summary>
    /// 报告读写阶段失败。
    /// </summary>
    ReportFailed
}

