namespace TerrariaTools.Dome.Core.Cpg;

public sealed record RoslynFrontendConfig(
    string SourceCode,
    string FileName = "input.cs");
