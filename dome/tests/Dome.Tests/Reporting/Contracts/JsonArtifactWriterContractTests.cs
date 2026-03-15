using TerrariaTools.Dome.Reporting;
using TerrariaTools.Testing.Contracts;
using TerrariaTools.Testing.TestFixtures;
using Xunit;

namespace TerrariaTools.Dome.Tests.Reporting;

public sealed class JsonArtifactWriterContractTests : IClassFixture<TemporaryDirectoryFixture>
{
    private readonly TemporaryDirectoryFixture _directories;

    public JsonArtifactWriterContractTests(TemporaryDirectoryFixture directories)
    {
        _directories = directories;
    }

    [Fact]
    public async Task Writer_SatisfiesArtifactWriterContract()
    {
        await ArtifactWriterContract.AssertWritesArtifactsAsync(new JsonArtifactWriter());
    }

    [Fact]
    public async Task Writer_CreatesExpectedArtifactFiles()
    {
        var writer = new JsonArtifactWriter();
        var root = _directories.CreateDirectory("json-artifacts");

        await ArtifactWriterContract.AssertWritesArtifactsAsyncAtPath(writer, root);

        Assert.True(File.Exists(Path.Combine(root, "analysis.json")));
        Assert.True(File.Exists(Path.Combine(root, "audit-plan.json")));
        Assert.True(File.Exists(Path.Combine(root, "report.json")));
    }
}
