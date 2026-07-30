using System.Xml.Linq;
using Xunit;

namespace NLISSN.Tests.Architecture;

public sealed class CacheInfrastructureBoundaryTests
{
    [Fact]
    public void CacheProject_HasOnlyBclDependencies()
    {
        var project = XDocument.Load(ProjectPath("src", "NLISSN.Infrastructure", "Caching", "NL.Caching.csproj"));

        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Empty(project.Descendants("ProjectReference"));
    }

    [Fact]
    public void CacheProject_SourceDoesNotReferenceDomainNamespaces()
    {
        var sourceDirectory = ProjectPath("src", "NLISSN.Infrastructure", "Caching");
        var forbiddenNamespaces = new[] { "Microsoft.CodeAnalysis", "NLCPG", "NLISSN" };

        foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, "*.cs"))
        {
            var source = File.ReadAllText(sourcePath);
            foreach (var forbiddenNamespace in forbiddenNamespaces)
            {
                Assert.DoesNotContain(forbiddenNamespace, source, StringComparison.Ordinal);
            }
        }
    }

    private static string ProjectPath(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, Path.Combine(parts));
    }
}
