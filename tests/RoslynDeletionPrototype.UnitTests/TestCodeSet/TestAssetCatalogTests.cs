using RoslynPrototype.Testing.TestCodeSet;
using RoslynPrototype.Testing.TestInfrastructure;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class TestAssetCatalogTests
{
  [Fact]
  public void Constructor_WhenAssetIdsDuplicate_ThrowsArgumentException()
  {
    // Arrange
    var asset = new TestAsset(
      "duplicate",
      "test",
      new Dictionary<string, string> { ["input.cs"] = "class C { }" },
      []);

    // Act
    var exception = Assert.Throws<ArgumentException>(() => new TestAssetCatalog([asset, asset]));

    // Assert
    Assert.Contains("duplicate", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Write_WhenAssetIsWrittenTwice_CreatesDistinctRootsWithStableFiles()
  {
    // Arrange
    var asset = new TestAsset(
      "multi-file",
      "test",
      new Dictionary<string, string>
      {
        ["one.cs"] = "class One { }",
        ["nested/two.cs"] = "class Two { }"
      },
      ["shared"]);
    var writer = new TestWorkspaceWriter();

    // Act
    using var first = writer.Write(asset);
    using var second = writer.Write(asset);

    // Assert
    Assert.NotEqual(first.RootPath, second.RootPath);
    Assert.Equal("class One { }", File.ReadAllText(Path.Combine(first.RootPath, "one.cs")));
    Assert.Equal(
      "class Two { }",
      File.ReadAllText(Path.Combine(second.RootPath, "nested", "two.cs")));
  }

  [Fact]
  public void Dispose_WhenWorkspaceIsDisposed_DeletesTheTemporaryRoot()
  {
    // Arrange
    var asset = new TestAsset(
      "temporary",
      "test",
      new Dictionary<string, string> { ["input.cs"] = "class C { }" },
      []);
    var writer = new TestWorkspaceWriter();
    string rootPath;

    // Act
    using (var workspace = writer.Write(asset))
    {
      rootPath = workspace.RootPath;
      Assert.True(Directory.Exists(rootPath));
    }

    // Assert
    Assert.False(Directory.Exists(rootPath));
  }

  [Theory]
  [InlineData("C:/escape.cs")]
  [InlineData("../escape.cs")]
  [InlineData("nested/../escape.cs")]
  public void Write_WhenAssetContainsUnsafePath_ThrowsArgumentException(string filePath)
  {
    // Arrange
    var asset = new TestAsset(
      "unsafe",
      "test",
      new Dictionary<string, string> { [filePath] = "class C { }" },
      []);
    var writer = new TestWorkspaceWriter();

    // Act
    var exception = Assert.Throws<ArgumentException>(() => writer.Write(asset));

    // Assert
    Assert.Contains("path", exception.Message, StringComparison.OrdinalIgnoreCase);
  }
}
