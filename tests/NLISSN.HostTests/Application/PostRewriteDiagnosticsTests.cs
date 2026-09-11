using NLISSN.Application;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class PostRewriteDiagnosticsTests
{
  [Fact]
  public void GetRewriteDiagnostics_WhenErrorExistsBeforeAndAfter_DoesNotReportItAsNew()
  {
    var originalSources = CreateSources(
      "Sample.cs",
      "public sealed class Sample { MissingType Value; }");
    var rewrittenSources = CreateSources(
      "Sample.cs",
      "public sealed class Sample { MissingType Value; }");

    var diagnostics = PostRewriteDiagnostics.GetRewriteDiagnostics(originalSources, rewrittenSources);

    Assert.Empty(diagnostics);
  }

  [Fact]
  public void GetRewriteDiagnostics_WhenRewriteIntroducesError_ReportsIt()
  {
    var originalSources = CreateSources(
      "Sample.cs",
      "public sealed class Sample { }");
    var rewrittenSources = CreateSources(
      "Sample.cs",
      "public sealed class Sample { MissingType Value; }");

    var diagnostics = PostRewriteDiagnostics.GetRewriteDiagnostics(originalSources, rewrittenSources);

    var diagnostic = Assert.Single(diagnostics);
    Assert.Equal("CS0246", diagnostic.Id);
    Assert.Equal("Sample.cs", diagnostic.FilePath);
  }

  [Fact]
  public void GetRewriteDiagnostics_WhenUnchangedFileHasExistingError_DoesNotReportItAsNew()
  {
    var originalSources = new Dictionary<string, string>(StringComparer.Ordinal)
    {
      ["Changed.cs"] = "public sealed class Changed { }",
      ["Unchanged.cs"] = "public sealed class Unchanged { MissingType Value; }"
    };
    var rewrittenSources = new Dictionary<string, string>(StringComparer.Ordinal)
    {
      ["Changed.cs"] = "public sealed class Changed { public int Value; }",
      ["Unchanged.cs"] = "public sealed class Unchanged { MissingType Value; }"
    };

    var diagnostics = PostRewriteDiagnostics.GetRewriteDiagnostics(originalSources, rewrittenSources);

    Assert.Empty(diagnostics);
  }

  private static IReadOnlyDictionary<string, string> CreateSources(string path, string source)
  {
    return new Dictionary<string, string>(StringComparer.Ordinal)
    {
      [path] = source
    };
  }
}
