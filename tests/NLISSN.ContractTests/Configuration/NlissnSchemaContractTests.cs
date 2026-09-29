using System.Text.Json;
using Xunit;

namespace NLISSN.Tests.Configuration;

public sealed class NlissnSchemaContractTests
{
  [Fact]
  public void Schema2_ClosesAllConfigurationObjectsAndDocumentsRiskDefaults()
  {
    using var document = JsonDocument.Parse(File.ReadAllText(
      RepositoryPath("Miscellaneous", "schemas", "nlissn.schema.2.json")));
    var root = document.RootElement;

    Assert.Equal("https://json-schema.org/draft/2020-12/schema", root.GetProperty("$schema").GetString());
    Assert.Equal("https://nlissn.dev/schemas/nlissn.schema.2.json", root.GetProperty("$id").GetString());
    Assert.True(root.GetProperty("additionalProperties").GetBoolean() is false);
    Assert.Equal(2, root.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetInt32());
    Assert.Contains("analysis", root.GetProperty("required").EnumerateArray().Select(element => element.GetString()));
    Assert.Contains("execution", root.GetProperty("required").EnumerateArray().Select(element => element.GetString()));
    Assert.Contains("artifacts", root.GetProperty("required").EnumerateArray().Select(element => element.GetString()));

    var definitions = root.GetProperty("$defs");
    foreach (var name in new[] { "input", "analysis", "execution", "artifacts", "logging" })
    {
      Assert.False(definitions.GetProperty(name).GetProperty("additionalProperties").GetBoolean());
    }

    var performance = definitions.GetProperty("performance").GetProperty("properties");
    Assert.False(performance.GetProperty("enabled").GetProperty("default").GetBoolean());
    Assert.Equal("normal", performance.GetProperty("mode").GetProperty("default").GetString());

    var analysis = definitions.GetProperty("analysis").GetProperty("properties");
    foreach (var name in new[]
    {
      "deleteUnreachableMethods", "deleteUnreferencedMethods", "clearUnusedInterfaceImplementations", "privatizeInternalOnlyPublicMethods"
    })
    {
      Assert.False(analysis.GetProperty(name).GetProperty("default").GetBoolean());
    }

    var execution = definitions.GetProperty("execution");
    var requiredExecutionFields = execution.GetProperty("required")
      .EnumerateArray()
      .Select(element => element.GetString())
      .ToArray();
    Assert.Equal(
      new[]
      {
        "directoryMaxDegreeOfParallelism",
        "cpgMaxDegreeOfParallelism",
        "groupMaxDegreeOfParallelism",
        "helperMaxDegreeOfParallelism",
        "replayMaxDegreeOfParallelism",
        "maxConcurrentOperations"
      },
      requiredExecutionFields);
    var executionProperties = execution.GetProperty("properties");
    Assert.False(executionProperties.TryGetProperty("maxDegreeOfParallelism", out _));
    foreach (var name in requiredExecutionFields)
    {
      Assert.Equal(1, executionProperties.GetProperty(name!).GetProperty("minimum").GetInt32());
    }
  }

  [Fact]
  public void Schema3_DeclaresToolBranchesAndProjectExportWorkerSetting()
  {
    using var document = JsonDocument.Parse(File.ReadAllText(
      RepositoryPath("Miscellaneous", "schemas", "nlissn.schema.3.json")));
    var root = document.RootElement;

    Assert.Equal(3, root.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetInt32());
    Assert.Equal(
      new[] { "schemaVersion", "tool" },
      root.GetProperty("required").EnumerateArray().Select(element => element.GetString()).ToArray());
    Assert.Equal(
      new[] { "nlissn", "nlcpg" },
      root.GetProperty("properties").GetProperty("tool").GetProperty("enum")
        .EnumerateArray()
        .Select(element => element.GetString())
        .ToArray());
    Assert.False(root.GetProperty("additionalProperties").GetBoolean());
    Assert.False(root.GetProperty("properties").TryGetProperty("projectExport", out _));

    // projectJson 是 artifacts 下的内联开关（已无独立 nlcpg-project-export 入口）。
    var artifacts = root.GetProperty("$defs").GetProperty("artifacts").GetProperty("properties");
    Assert.True(artifacts.TryGetProperty("projectJson", out _));
    var projectJsonDefinition = root.GetProperty("$defs").GetProperty("projectJson");
    var projectJson = projectJsonDefinition.GetProperty("properties");
    // 开关的全部字段必须恰好是这六个：多一个都说明契约漂移（如残留的 resume）。
    // documentShardCount 于单文档分片导出引入，默认 1（不分片），故既有配置行为不变。
    // documentShardParallelism 是分片写盘并行度，默认 1（逐片串行），只改变时序、不改输出。
    // performanceDiagnostics 是导出阶段计时开关，默认 false，只影响度量、不影响任何输出。
    // requestedCapabilities 是导出侧额外请求的 CPG 能力位名称，默认空（沿用 NLCPGCapability.Default）。
    Assert.Equal(
      new[]
      {
        "documentShardCount",
        "documentShardParallelism",
        "enabled",
        "output",
        "performanceDiagnostics",
        "projectWorkerCount",
        "requestedCapabilities",
      },
      projectJson.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
    Assert.False(projectJsonDefinition.GetProperty("additionalProperties").GetBoolean());
    Assert.False(projectJson.GetProperty("enabled").GetProperty("default").GetBoolean());
    Assert.Equal(1, projectJson.GetProperty("projectWorkerCount").GetProperty("minimum").GetInt32());
    Assert.Equal(12, projectJson.GetProperty("projectWorkerCount").GetProperty("default").GetInt32());
    Assert.Equal(1, projectJson.GetProperty("output").GetProperty("minLength").GetInt32());
    // 分片数下限 1、默认 1：默认配置下输出与分片改造前完全一致。
    Assert.Equal(1, projectJson.GetProperty("documentShardCount").GetProperty("minimum").GetInt32());
    Assert.Equal(1, projectJson.GetProperty("documentShardCount").GetProperty("default").GetInt32());
    // 分片并行度下限 1、默认 1：默认逐片串行，峰值与串行路径一致。
    Assert.Equal(1, projectJson.GetProperty("documentShardParallelism").GetProperty("minimum").GetInt32());
    Assert.Equal(1, projectJson.GetProperty("documentShardParallelism").GetProperty("default").GetInt32());
    // 计时开关默认关闭：不收集时不读时钟、不建列表，保持既有开销。
    Assert.False(projectJson.GetProperty("performanceDiagnostics").GetProperty("default").GetBoolean());
    // 能力位默认为空数组 ⇒ 导出沿用 NLCPGCapability.Default，既有配置的产物不变。
    Assert.Empty(
      projectJson.GetProperty("requestedCapabilities").GetProperty("default").EnumerateArray());
    Assert.Equal(
      "string",
      projectJson.GetProperty("requestedCapabilities").GetProperty("items").GetProperty("type").GetString());
    Assert.False(projectJson.TryGetProperty("resume", out _));

    var nlcpg = root.GetProperty("$defs").GetProperty("nlcpg");
    Assert.False(nlcpg.GetProperty("additionalProperties").GetBoolean());
    Assert.Equal(
      "local",
      root.GetProperty("$defs").GetProperty("nlcpgView").GetProperty("properties")
        .GetProperty("mode").GetProperty("enum").EnumerateArray()
        .Select(element => element.GetString())
        .Single(value => value == "local"));

    var branches = root.GetProperty("allOf").EnumerateArray().ToArray();
    Assert.Contains(
      branches,
      branch => branch.GetProperty("then").GetProperty("required")
        .EnumerateArray().Select(element => element.GetString()).Contains("runId"));
    Assert.Contains(
      branches,
      branch => branch.GetProperty("then").GetProperty("required")
        .EnumerateArray().Select(element => element.GetString()).Contains("nlcpg"));
  }

  private static string RepositoryPath(params string[] parts)
  {
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null && !File.Exists(Path.Combine(current.FullName, "global.json")))
    {
      current = current.Parent;
    }

    Assert.NotNull(current);
    return Path.Combine(current!.FullName, Path.Combine(parts));
  }
}
