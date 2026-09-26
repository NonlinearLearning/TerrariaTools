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
      new[] { "nlissn", "nlcpg", "nlcpg-project-export" },
      root.GetProperty("properties").GetProperty("tool").GetProperty("enum")
        .EnumerateArray()
        .Select(element => element.GetString())
        .ToArray());
    Assert.False(root.GetProperty("additionalProperties").GetBoolean());

    var projectExport = root.GetProperty("$defs").GetProperty("projectExport");
    Assert.False(projectExport.GetProperty("additionalProperties").GetBoolean());
    Assert.Equal(
      1,
      projectExport.GetProperty("properties").GetProperty("projectWorkerCount")
        .GetProperty("minimum")
        .GetInt32());

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
        .EnumerateArray().Select(element => element.GetString()).Contains("projectExport"));
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
