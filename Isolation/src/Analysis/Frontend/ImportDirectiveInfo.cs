namespace Analysis.Frontend;

/// <summary>
/// 表示一条在前端阶段采集到的导入指令。
///
/// 这里不直接保存 Roslyn 语法节点，是为了让后续 pass 只依赖稳定数据，
/// 不依赖具体语法树对象生命周期。
/// </summary>
public sealed record ImportDirectiveInfo(
    string FilePath,
    string ImportedEntity,
    string ImportedAs,
    string Code,
    int Order,
    int Line,
    int Column,
    bool IsStatic,
    bool IsGlobal);
