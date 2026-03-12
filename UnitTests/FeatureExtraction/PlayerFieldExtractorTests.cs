using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TerrariaTools.Analysis;
using TerrariaTools.UnitTests.Infrastructure;

namespace TerrariaTools.UnitTests.FeatureExtraction
{
    public class PlayerFieldExtractorTests : RoslynTestBase
    {
        [Fact]
        public async Task AnalyzeAsync_ShouldExtractReferencedFields()
        {
            // Arrange
            var playerSource = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.PlayerFieldExtractorTests_playerSource_1;
            var bufferSource = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.PlayerFieldExtractorTests_bufferSource_1;
            var (workspace, solution, project) = await CreateSolutionAsync(
                ("Player.cs", playerSource),
                ("MessageBuffer.cs", bufferSource)
            );

            var extractor = new PlayerFieldExtractor(solution);

            // Act
            var result = await extractor.AnalyzeAsync();

            // Assert
            Assert.Contains("name", result.ReferencedFieldNames);
            Assert.Contains("difficulty", result.ReferencedFieldNames);
            Assert.Contains("whoAmI", result.ReferencedFieldNames);
            Assert.DoesNotContain("unused", result.ReferencedFieldNames);
        }
    }
}

