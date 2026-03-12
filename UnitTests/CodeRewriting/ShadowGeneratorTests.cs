using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TerrariaTools.Analysis;
using TerrariaTools.UnitTests.Infrastructure;
using Xunit;

namespace TerrariaTools.UnitTests.CodeRewriting
{
    public class ShadowGeneratorTests : RoslynTestBase
    {
        [Fact]
        public async Task Should_Preserve_Static_Field_Initializer_Dependencies()
        {
            var source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.ShadowGeneratorTests_Source_1;
            var (workspace, solution, project) = await CreateSolutionAsync(("Source.cs", source));
            var compilation = await project.GetCompilationAsync();
            var typeSymbol = compilation.GetTypeByMetadataName("Registration");
            var seedSymbol = typeSymbol.GetMembers("MainEntry").First();

            var generator = new ShadowClassGenerator(solution);
            var result = await generator.GenerateShadowSourceAsync(seedSymbol);

            Assert.Contains("Source.cs", result.Keys);
            var code = result["Source.cs"];

            Assert.Contains("Register", code);
            Assert.Contains("SecretSeed", code);
        }
    }
}

