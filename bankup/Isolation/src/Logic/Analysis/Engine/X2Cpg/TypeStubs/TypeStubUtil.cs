namespace Logic.Analysis.Engine.X2Cpg.TypeStubs;

/// <summary>
/// 解析类型桩目录。
///
/// 对应 Joern `TypeStubUtil.scala`。
/// </summary>
public static class TypeStubUtil
{
    /// <summary>
    /// 根据包路径推导 `type_stubs` 目录。
    /// </summary>
    public static string TypeStubDir(TypeStubMetaData metaData)
    {
        ArgumentNullException.ThrowIfNull(metaData);

        string directory = metaData.PackagePath.IsFile
            ? metaData.PackagePath.LocalPath
            : metaData.PackagePath.ToString();
        int libIndex = directory.LastIndexOf("lib", StringComparison.Ordinal);
        int targetIndex = directory.LastIndexOf("target", StringComparison.Ordinal);
        string fixedDirectory = libIndex >= 0
            ? directory[..libIndex]
            : targetIndex >= 0
                ? directory[..targetIndex]
                : ".";
        return Path.Combine(fixedDirectory, "type_stubs");
    }
}
