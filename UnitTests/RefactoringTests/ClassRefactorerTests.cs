using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Moq;
using Xunit;
using TerrariaTools.Services;
using TerrariaTools.RewriteCodeExpressions;

namespace TerrariaTools.UnitTests.RefactoringTests
{
    public class ClassRefactorerTests
    {
        [Fact]
        public async Task ExecuteSolutionRefactoringAsync_ShouldRemoveUnreferencedClasses()
        {
            // Arrange
            using var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var versionStamp = VersionStamp.Create();
            var projectInfo = ProjectInfo.Create(projectId, versionStamp, "TestProject", "TestProject.dll", LanguageNames.CSharp)
                .WithMetadataReferences(new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
            var project = workspace.AddProject(projectInfo);

            // Document A: public class ClassA { }
            // This class is referenced by ClassB, so it should NOT be deleted.
            var sourceA = "namespace Test { public class ClassA { } }";
            var docA = workspace.AddDocument(project.Id, "ClassA.cs", SourceText.From(sourceA));
            // We must set the file path because ClassRefactorer relies on it
            workspace.TryApplyChanges(workspace.CurrentSolution.WithDocumentFilePath(docA.Id, @"C:\Test\ClassA.cs"));

            // Document B: public class ClassB { public ClassA Prop { get; set; } }
            // This class references ClassA. ClassB itself is not referenced, so it SHOULD be deleted eventually if we iterate enough times?
            // Wait, if ClassB is not referenced, it should be deleted.
            // If ClassA is referenced by ClassB, ClassA is safe *as long as ClassB exists*.
            // But if ClassB is deleted in the same pass, then ClassA might become unreferenced in the NEXT pass.
            // Let's make ClassB an entry point or something to keep it alive, OR just assert on the first pass behavior.
            // Or let's make ClassB have a Main method so it's kept.
            var sourceB = "using Test; namespace Test { public class ClassB { public static void Main(string[] args) { } public ClassA Prop { get; set; } } }";
            var docB = workspace.AddDocument(project.Id, "ClassB.cs", SourceText.From(sourceB));
            workspace.TryApplyChanges(workspace.CurrentSolution.WithDocumentFilePath(docB.Id, @"C:\Test\ClassB.cs"));

            // Document C: public class ClassC { }
            // Not referenced by anyone, no Main method. Should be deleted.
            var sourceC = "namespace Test { public class ClassC { } }";
            var docC = workspace.AddDocument(project.Id, "ClassC.cs", SourceText.From(sourceC));
            workspace.TryApplyChanges(workspace.CurrentSolution.WithDocumentFilePath(docC.Id, @"C:\Test\ClassC.cs"));

            // Update solution reference
            var solution = workspace.CurrentSolution;

            // Mock IWorkspaceLoader
            var mockLoader = new Mock<IWorkspaceLoader>();
            mockLoader.Setup(l => l.LoadSolutionAsync(It.IsAny<string>()))
                      .ReturnsAsync(solution);

            var savedFiles = new Dictionary<string, string>();
            mockLoader.Setup(l => l.SaveDocumentAsync(It.IsAny<string>(), It.IsAny<string>()))
                      .Callback<string, string>((path, content) => savedFiles[path] = content)
                      .Returns(Task.CompletedTask);

            // Act
            // We pass a dummy path because LoadSolutionAsync is mocked
            var stats = await ClassRefactorer.ExecuteSolutionRefactoringAsync(@"C:\Test\Test.sln", mockLoader.Object);

            // Assert
            // 1. ClassC should be deleted (file modified).
            Assert.True(savedFiles.ContainsKey(@"C:\Test\ClassC.cs"), "ClassC.cs should have been modified (class deleted)");

            // 2. ClassA should NOT be deleted (file not modified) because it is referenced by ClassB.
            Assert.False(savedFiles.ContainsKey(@"C:\Test\ClassA.cs"), "ClassA.cs should NOT have been modified");

            // 3. ClassB should NOT be deleted (file not modified) because it has Main method.
            Assert.False(savedFiles.ContainsKey(@"C:\Test\ClassB.cs"), "ClassB.cs should NOT have been modified");

            // Verify content of ClassC.cs (should not contain class definition anymore)
            var newContentC = savedFiles[@"C:\Test\ClassC.cs"];
            Assert.DoesNotContain("class ClassC", newContentC);
        }
    }
}
