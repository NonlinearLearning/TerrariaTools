namespace Analysis.X2Cpg.TypeStubs;

/// <summary>
/// 表示类型桩配置。
///
/// 对应 Joern `TypeStubConfig.scala`。
/// </summary>
public sealed record TypeStubConfig(bool UseTypeStubs = true)
{
    /// <summary>
    /// 返回更新后的配置。
    /// </summary>
    public TypeStubConfig WithTypeStubs(bool value) => this with { UseTypeStubs = value };
}

/// <summary>
/// 表示类型桩元数据。
/// </summary>
public sealed record TypeStubMetaData(bool UseTypeStubs, Uri PackagePath);
