namespace Logic.Analysis.Engine.Frontend;

/// <summary>
/// 表示类型桩配置。
/// </summary>
public sealed record TypeStubsParserConfig(string? TypeStubsFilePath = null);

/// <summary>
/// 表示一个类型桩条目。
/// </summary>
public sealed record TypeStubEntry(
    string FullName,
    string Kind,
    IReadOnlyCollection<string> Members);
