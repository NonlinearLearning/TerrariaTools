using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Passes;

/// <summary>
/// 根据前端采集到的导入指令创建 `Import` 节点。
///
/// 当前实现参考 Joern `XImportsPass.scala` 的目标，但结合 C# 的真实语法做了简化：
/// - 直接消费前端采集好的 `using` 信息；
/// - 把导入挂到所属 `File` 节点下；
/// - 保存最关键的导入事实，供后续解析与类型恢复使用。
/// </summary>
public sealed class BuildImportsPass : CpgPass
{
    /// <summary>
    /// 初始化导入 pass。
    /// </summary>
    public BuildImportsPass(IEnumerable<ImportDirectiveInfo> imports)
    {
        Imports = imports?.ToArray() ?? throw new ArgumentNullException(nameof(imports));
    }

    /// <summary>
    /// 获取待写入图中的导入事实。
    /// </summary>
    public IReadOnlyCollection<ImportDirectiveInfo> Imports { get; }


    protected override void Execute(CpgGraphBuilder builder)
    {
        foreach (ImportDirectiveInfo importDirective in Imports)
        {
            CpgNode? fileNode = builder.Graph
                .GetNodes(CpgNodeKind.File)
                .FirstOrDefault(node => node.TryGetProperty<string>("FileName", out string? fileName) &&
                                        string.Equals(fileName, importDirective.FilePath, StringComparison.Ordinal));
            if (fileNode is null)
            {
                continue;
            }

            bool exists = builder.Graph
                .GetNodes(CpgNodeKind.Import)
                .Any(node =>
                    node.TryGetProperty<long>("AstParentId", out long parentId) &&
                    parentId == fileNode.Id &&
                    node.TryGetProperty<string>("ImportedEntity", out string? importedEntity) &&
                    string.Equals(importedEntity, importDirective.ImportedEntity, StringComparison.Ordinal) &&
                    node.TryGetProperty<string>("ImportedAs", out string? importedAs) &&
                    string.Equals(importedAs, importDirective.ImportedAs, StringComparison.Ordinal));
            if (exists)
            {
                continue;
            }

            CpgNode importNode = builder.CreateNode(CpgNodeKind.Import);
            importNode.SetProperty("Name", importDirective.ImportedAs);
            importNode.SetProperty("ImportedEntity", importDirective.ImportedEntity);
            importNode.SetProperty("ImportedAs", importDirective.ImportedAs);
            importNode.SetProperty("Code", importDirective.Code);
            importNode.SetProperty("Order", importDirective.Order);
            importNode.SetProperty("Line", importDirective.Line);
            importNode.SetProperty("Column", importDirective.Column);
            importNode.SetProperty("IsStatic", importDirective.IsStatic);
            importNode.SetProperty("IsGlobal", importDirective.IsGlobal);
            importNode.SetProperty("FileName", importDirective.FilePath);
            importNode.SetProperty("AstParentId", fileNode.Id);
        }
    }
}
