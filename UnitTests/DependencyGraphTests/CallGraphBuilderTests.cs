using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;
using TerrariaTools.Analysis;

namespace TerrariaTools.UnitTests.DependencyGraphTests
{
    public class CallGraphBuilderTests
    {
        [Fact]
        public async Task AnalyzeMethods_ShouldIdentifyUnusedAndPrivatizableMethods()
        {
            // Arrange
            using var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var versionStamp = VersionStamp.Create();
            var projectInfo = ProjectInfo.Create(projectId, versionStamp, "TestProject", "TestProject.dll", LanguageNames.CSharp)
                .WithMetadataReferences(new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
            var project = workspace.AddProject(projectInfo);

            // Code structure:
            // class Program {
            //     public static void Main() { HelperA(); }
            //     public static void HelperA() { } // Called by Main (same class) -> Should be Privatized
            //     public static void Unused() { UnusedHelper(); } // Unused -> Delete
            //     private static void UnusedHelper() { } // Called by Unused -> Delete (cascading)
            // }

            var source = @"
using System;

namespace TestProject
{
    public class Program
    {
        public static void Main()
        {
            HelperA();
        }

        public static void HelperA()
        {
        }

        internal static void Unused()
        {
            UnusedHelper();
        }

        private static void UnusedHelper()
        {
        }
    }
}";
            var doc = workspace.AddDocument(project.Id, "Program.cs", SourceText.From(source));

            // Apply changes to ensure compilation is up to date
            workspace.TryApplyChanges(workspace.CurrentSolution);
            var solution = workspace.CurrentSolution;

            // Act
            var builder = new CallGraphBuilder(solution);
            await builder.BuildAsync();
            var results = builder.AnalyzeMethods();

            // Assert
            // We need to find the IMethodSymbols to check against results
            var compilation = await solution.Projects.First().GetCompilationAsync();
            if (compilation == null) throw new Exception("Compilation failed");

            var type = compilation.GetTypeByMetadataName("TestProject.Program");
            if (type == null) throw new Exception("Could not find type TestProject.Program");

            var mainMethod = type.GetMembers("Main").OfType<IMethodSymbol>().First();
            var helperAMethod = type.GetMembers("HelperA").OfType<IMethodSymbol>().First();
            var unusedMethod = type.GetMembers("Unused").OfType<IMethodSymbol>().First();
            var unusedHelperMethod = type.GetMembers("UnusedHelper").OfType<IMethodSymbol>().First();

            // 1. Main: Should be kept (None)
            Assert.False(results.ContainsKey(mainMethod), "Main method should not be in results (Action: None)");

            // 2. HelperA: Should be Privatized
            Assert.True(results.ContainsKey(helperAMethod), "HelperA should be in results");
            Assert.Equal(CallGraphBuilder.GraphMethodAction.Privatize, results[helperAMethod]);

            // 3. Unused: Should be Deleted
            Assert.True(results.ContainsKey(unusedMethod), "Unused should be in results");
            Assert.Equal(CallGraphBuilder.GraphMethodAction.Delete, results[unusedMethod]);

            // 4. UnusedHelper: Should be Deleted (cascading)
            Assert.True(results.ContainsKey(unusedHelperMethod), "UnusedHelper should be in results");
            Assert.Equal(CallGraphBuilder.GraphMethodAction.Delete, results[unusedHelperMethod]);
        }
    }
}
