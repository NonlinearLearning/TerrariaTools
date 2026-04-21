using Logic.Analysis.Engine.Passes;

namespace Logic.Analysis.Engine.Frontend;

/// <summary>
/// 收敛 import 指令的稳定装配规则。
/// </summary>
public static class ImportDirectiveConventions
{
    /// <summary>
    /// 构造 import 指令信息对象。
    /// </summary>
    public static ImportDirectiveInfo CreateImportDirectiveInfo(
        string filePath,
        string importedEntity,
        string? explicitAlias,
        string code,
        int order,
        int lineNumber,
        int columnNumber,
        bool isStatic,
        bool isGlobal)
    {
        return new ImportDirectiveInfo(
            filePath,
            importedEntity,
            FrontendGraphConventions.BuildImportAlias(explicitAlias, importedEntity),
            code,
            order,
            lineNumber,
            columnNumber,
            isStatic,
            isGlobal);
    }
}
