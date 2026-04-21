namespace Domain.Analysis.Engine.Model;

/// <summary>
/// 表示分析域内部统一使用的目标标识。
///
/// 这个层次结构直接遵循当前文档里的架构决策：
/// - <see cref="TargetId"/> 是总父类；
/// - <see cref="TypeId"/>、<see cref="SymbolId"/>、
///   <see cref="OperationId"/> 是三个稳定分支。
///
/// 这里要特别区分：
/// 这些标识不是图节点编号，而是面向领域层的稳定身份。
/// 未来如果需要和 CPG 节点建立绑定关系，再单独做映射层。
/// </summary>
public abstract record TargetId(string Value);
