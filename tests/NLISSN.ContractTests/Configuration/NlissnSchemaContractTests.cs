using System.Text.Json;
using Xunit;

namespace NLISSN.Tests.Configuration;

public sealed class NlissnSchemaContractTests
{
  [Fact]
  public void Schema2_ClosesAllConfigurationObjectsAndDocumentsRiskDefaults()
  {
    using var document = JsonDocument.Parse(File.ReadAllText(RepositoryPath("schemas", "nlissn.schema.2.json")));
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

    var analysis = definitions.GetProperty("analysis").GetProperty("properties");
    foreach (var name in new[]
    {
      "deleteUnreachableMethods", "deleteUnreferencedMethods", "clearUnusedInterfaceImplementations", "privatizeInternalOnlyPublicMethods"
    })
    {
      Assert.False(analysis.GetProperty(name).GetProperty("default").GetBoolean());
    }
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
