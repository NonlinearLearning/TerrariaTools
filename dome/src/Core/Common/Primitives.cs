namespace TerrariaTools.Dome.Core.Common;

/// <summary>
/// 表示成员节点的类型。
/// </summary>
public enum MemberKind
{
    /// <summary>
    /// 未知成员类型。
    /// </summary>
    Unknown,

    /// <summary>
    /// 类成员。
    /// </summary>
    Class,

    /// <summary>
    /// 字段成员。
    /// </summary>
    Field,

    /// <summary>
    /// 方法成员。
    /// </summary>
    Method,

    /// <summary>
    /// 构造函数成员。
    /// </summary>
    Constructor,

    /// <summary>
    /// 属性成员。
    /// </summary>
    Property,

    /// <summary>
    /// 访问器成员。
    /// </summary>
    Accessor
}

/// <summary>
/// 表示规则和计划处理的目标类型。
///   - 这张图有没有真的被构建出来
/// - 如果构建了，是只保留了简化信息，还是保留了完整图结构
/// </summary>
public enum TargetKind
{
    /// <summary>
    /// 语句目标。
    /// </summary>
    Statement,

    /// <summary>
    /// 方法目标。
    /// </summary>
    Method,

    /// <summary>
    /// 字段目标。
    /// </summary>
    Field,

    /// <summary>
    /// 属性目标。
    /// </summary>
    Property,

    /// <summary>
    /// 类型目标。
    /// </summary>
    Class
}

/// <summary>
/// 表示语句节点的分类。
/// </summary>
public enum StatementKindRef
{
    /// <summary>
    /// 未知语句类型。
    /// </summary>
    Unknown,

    /// <summary>
    /// 初始化语句。
    /// </summary>
    Initializer,

    /// <summary>
    /// 声明语句。
    /// </summary>
    Declaration,

    /// <summary>
    /// 赋值语句。
    /// </summary>
    Assignment,

    /// <summary>
    /// if 语句。
    /// </summary>
    If,

    /// <summary>
    /// while 语句。
    /// </summary>
    While,

    /// <summary>
    /// for 语句。
    /// </summary>
    For,

    /// <summary>
    /// return 语句。
    /// </summary>
    Return,

    /// <summary>
    /// 对象初始化赋值语句。
    /// </summary>
    ObjectInitializerAssignment
}

/// <summary>
/// 表示语句传播分析的作用域模式。
/// </summary>
public enum StatementScopeMode
{
    /// <summary>
    /// 仅在最小块内传播。
    /// </summary>
    MinimalBlock,

    /// <summary>
    /// 允许穿透到父级块传播。
    /// </summary>
    ParentBlockPiercing
}

/// <summary>
/// 表示语句图的物化程度。
/// </summary>
public enum StatementGraphMaterialization
{
    /// <summary>
    /// 不物化语句图。
    /// </summary>
    None,

    /// <summary>
    /// 仅物化快照。
    /// </summary>
    SnapshotOnly,

    /// <summary>
    /// 物化完整语句图。
    /// </summary>
    Full
}

/// <summary>
/// 表示函数图的物化程度。
/// </summary>
public enum FunctionGraphMaterialization
{
    /// <summary>
    /// 不物化函数图。
    /// </summary>
    None,

    /// <summary>
    /// 物化整项目函数图。
    /// </summary>
    WholeProject,

    /// <summary>
    /// 物化成员展开函数图。
    /// </summary>
    ExpandedMembers
}

/// <summary>
/// 表示决策提升穿越的边界类型。
/// </summary>
public enum BoundaryKind
{
    /// <summary>
    /// 调用边界。
    /// </summary>
    Invocation
}

/// <summary>
/// 表示语句作用域分析配置。
/// </summary>
public sealed record ScopeAnalysisOptions(StatementScopeMode StatementScopeMode)
{
    /// <summary>
    /// 获取默认的语句作用域分析配置。
    /// </summary>
    public static ScopeAnalysisOptions Default { get; } = new(StatementScopeMode.MinimalBlock);
}

/// <summary>
/// 表示计划动作类型。
/// </summary>
public enum PlanActionKind
{
    /// <summary>
    /// 删除目标。
    /// </summary>
    Delete,

    /// <summary>
    /// 注释掉目标。
    /// </summary>
    CommentOut,

    /// <summary>
    /// 使用默认值替换实现。
    /// </summary>
    ReplaceWithDefault,

    /// <summary>
    /// 添加返回语句。
    /// </summary>
    AddReturn,

    /// <summary>
    /// 调整可见性为私有。
    /// </summary>
    ChangeVisibilityToPrivate,

    /// <summary>
    /// 重排公共方法顺序。
    /// </summary>
    ReorderPublicMethods
}

/// <summary>
/// 表示具体计划动作。
/// </summary>
public sealed record PlanAction(PlanActionKind Kind, string? Payload = null);

/// <summary>
/// 表示决策来源。
/// </summary>
public enum DecisionOrigin
{
    /// <summary>
    /// 规则直接产生。
    /// </summary>
    Rule,

    /// <summary>
    /// 显式种子产生。
    /// </summary>
    Seed,

    /// <summary>
    /// 表达式投影产生。
    /// </summary>
    Projection,

    /// <summary>
    /// 传播产生。
    /// </summary>
    Propagation,

    /// <summary>
    /// 边界提升产生。
    /// </summary>
    BoundaryPromotion,

    /// <summary>
    /// 预测逻辑产生。
    /// </summary>
    Prediction,

    /// <summary>
    /// 清理规则产生。
    /// </summary>
    Cleanup
}

/// <summary>
/// 表示决策分类。
/// </summary>
public enum DecisionCategory
{
    /// <summary>
    /// 删除类决策。
    /// </summary>
    Delete,

    /// <summary>
    /// 注释类决策。
    /// </summary>
    CommentOut,

    /// <summary>
    /// 默认值替换类决策。
    /// </summary>
    ReplaceWithDefault,

    /// <summary>
    /// 添加返回类决策。
    /// </summary>
    AddReturn,

    /// <summary>
    /// 可见性变更类决策。
    /// </summary>
    VisibilityChange,

    /// <summary>
    /// 重排类决策。
    /// </summary>
    Reorder
}

/// <summary>
/// 表示工作区加载模式。
/// </summary>
public enum WorkspaceLoadMode
{
    /// <summary>
    /// 使用代码分析加载。
    /// </summary>
    CodeAnalysis,

    /// <summary>
    /// 仅按源码加载。
    /// </summary>
    SourceOnly,

    /// <summary>
    /// 先尝试代码分析，失败后回退到源码加载。
    /// </summary>
    CodeAnalysisFallbackToSourceOnly
}

/// <summary>
/// 表示工作区诊断严重级别。
/// </summary>
public enum WorkspaceLoadDiagnosticSeverity
{
    /// <summary>
    /// 信息级。
    /// </summary>
    Info,

    /// <summary>
    /// 警告级。
    /// </summary>
    Warning,

    /// <summary>
    /// 错误级。
    /// </summary>
    Error
}

/// <summary>
/// 表示成员标识包装类型。
/// </summary>
public readonly record struct MemberId(string Value)
{
    /// <summary>
    /// 返回成员标识原始文本。
    /// </summary>
    public override string ToString() => Value;
}

/// <summary>
/// 表示目标身份信息。
/// </summary>
public sealed record TargetIdentity(
    string DocumentPath,
    MemberId MemberId,
    MemberKind MemberKind,
    TargetKind TargetKind)
{
    /// <summary>
    /// 获取用于唯一标识目标身份的稳定键。
    /// </summary>
    public string IdentityKey => $"{DocumentPath}|{MemberId.Value}|{TargetKind}";
}

/// <summary>
/// 表示目标定位信息。
/// </summary>
public sealed record TargetLocator(
    int SpanStart,
    int SpanLength,
    string DisplayText,
    TargetResolutionKey? ResolutionKey = null)
{
    /// <summary>
    /// 获取定位信息对应的稳定目标键。
    /// </summary>
    public string TargetKey => $"{SpanStart}|{SpanLength}|{DisplayText}";

    /// <summary>
    /// 获取生效的解析键。
    /// </summary>
    public TargetResolutionKey EffectiveResolutionKey => ResolutionKey ?? new(SpanStart, SpanLength);
}

/// <summary>
/// 表示目标解析键。
/// </summary>
public sealed record TargetResolutionKey(
    int SpanStart,
    int SpanLength);

/// <summary>
/// 表示运行模式。
/// </summary>
public enum RunMode
{
    /// <summary>
    /// 标准完整运行。
    /// </summary>
    Standard,

    /// <summary>
    /// 仅执行分析。
    /// </summary>
    AnalyzeOnly,

    /// <summary>
    /// 仅执行规划。
    /// </summary>
    PlanOnly
}

/// <summary>
/// 表示管线失败码。
/// </summary>
public enum FailureCode
{
    /// <summary>
    /// 无失败。
    /// </summary>
    None,

    /// <summary>
    /// 工作区加载失败。
    /// </summary>
    WorkspaceLoadFailed,

    /// <summary>
    /// 分析失败。
    /// </summary>
    AnalysisFailed,

    /// <summary>
    /// 计划编译失败。
    /// </summary>
    PlanCompileFailed,

    /// <summary>
    /// 重写失败。
    /// </summary>
    RewriteFailed,

    /// <summary>
    /// 构建失败。
    /// </summary>
    BuildFailed,

    /// <summary>
    /// 报告输出失败。
    /// </summary>
    ReportFailed
}

/// <summary>
/// 表示函数依赖边类型。
/// </summary>
public enum FunctionDependencyKind
{
    /// <summary>
    /// 调用依赖。
    /// </summary>
    Calls,

    /// <summary>
    /// 创建依赖。
    /// </summary>
    Creates,

    /// <summary>
    /// 读取成员依赖。
    /// </summary>
    ReadsMember,

    /// <summary>
    /// 写入成员依赖。
    /// </summary>
    WritesMember,

    /// <summary>
    /// 使用属性访问器依赖。
    /// </summary>
    UsesPropertyAccessor
}
