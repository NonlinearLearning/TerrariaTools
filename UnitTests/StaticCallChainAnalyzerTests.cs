using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using TerrariaTools.DynamicAnalysis;
using Xunit;

namespace TerrariaTools.UnitTests.DynamicAnalysis
{
    public class StaticCallChainAnalyzerTests : IDisposable
    {
        private readonly string _tempLogPath;

        public StaticCallChainAnalyzerTests()
        {
            _tempLogPath = Path.Combine(Path.GetTempPath(), $"test_call_chain_{Guid.NewGuid()}.log");
        }

        public void Dispose()
        {
            if (File.Exists(_tempLogPath))
            {
                File.Delete(_tempLogPath);
            }
            if (File.Exists("analyzed_functions.txt"))
            {
                File.Delete("analyzed_functions.txt");
            }
        }

        private Solution CreateTestSolution(params (string FileName, string Content)[] files)
        {
            var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var projectInfo = ProjectInfo.Create(projectId, VersionStamp.Create(), "TestProject", "TestProject", LanguageNames.CSharp)
                .WithMetadataReferences(new[]
                {
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
                });

            var solution = workspace.CurrentSolution.AddProject(projectInfo);

            foreach (var file in files)
            {
                var documentId = DocumentId.CreateNewId(projectId);
                solution = solution.AddDocument(documentId, file.FileName, SourceText.From(file.Content), filePath: file.FileName);
            }

            return solution;
        }

        [Fact]
        public async Task Test_CircularRecursion_ShouldHandleGracefully()
        {
            // Arrange: A calls B, B calls A
            var code = @"
namespace TestNamespace {
    public class TestClass {
        public void MethodA() { MethodB(); }
        public void MethodB() { MethodA(); }
    }
}";
            var solution = CreateTestSolution(("TestFile.cs", code));
            File.WriteAllLines(_tempLogPath, new[] { "[2026-02-15 12:00:00] [ENTER] TestNamespace.TestClass.MethodA" });

            var analyzer = new StaticCallChainAnalyzer("dummy.sln", new Load());

            // Act
            await analyzer.AnalyzeSolutionAsync(solution, _tempLogPath);

            // Assert
            var results = File.ReadAllLines("analyzed_functions.txt");
            Assert.Contains("TestNamespace.TestClass.MethodA", results);
            Assert.Contains("TestNamespace.TestClass.MethodB", results);
            Assert.Equal(2, results.Length);
        }

        [Fact]
        public async Task Test_IndirectRecursion_ShouldHandleGracefully()
        {
            // Arrange: A -> B -> C -> A
            var code = @"
namespace TestNamespace {
    public class TestClass {
        public void MethodA() { MethodB(); }
        public void MethodB() { MethodC(); }
        public void MethodC() { MethodA(); }
    }
}";
            var solution = CreateTestSolution(("TestFile.cs", code));
            File.WriteAllLines(_tempLogPath, new[] { "[2026-02-15 12:00:00] [ENTER] TestNamespace.TestClass.MethodA" });

            var analyzer = new StaticCallChainAnalyzer("dummy.sln", new Load());

            // Act
            await analyzer.AnalyzeSolutionAsync(solution, _tempLogPath);

            // Assert
            var results = File.ReadAllLines("analyzed_functions.txt");
            Assert.Contains("TestNamespace.TestClass.MethodA", results);
            Assert.Contains("TestNamespace.TestClass.MethodB", results);
            Assert.Contains("TestNamespace.TestClass.MethodC", results);
            Assert.Equal(3, results.Length);
        }

        [Fact]
        public async Task Test_PropertyAccess_ShouldBeCaptured()
        {
            // Arrange: Method calls Property getter and setter
            var code = @"
namespace TestNamespace {
    public class TestClass {
        public int MyProperty { get; set; }
        public void MethodA() {
            int val = MyProperty;
            MyProperty = 10;
        }
    }
}";
            var solution = CreateTestSolution(("TestFile.cs", code));
            File.WriteAllLines(_tempLogPath, new[] { "[2026-02-15 12:00:00] [ENTER] TestNamespace.TestClass.MethodA" });

            var analyzer = new StaticCallChainAnalyzer("dummy.sln", new Load());

            // Act
            await analyzer.AnalyzeSolutionAsync(solution, _tempLogPath);

            // Assert
            var results = File.ReadAllLines("analyzed_functions.txt");
            Assert.Contains("TestNamespace.TestClass.MethodA", results);
            Assert.Contains("TestNamespace.TestClass.MyProperty.get", results);
            Assert.Contains("TestNamespace.TestClass.MyProperty.set", results);
        }

        [Fact]
        public async Task Test_ConstructorCall_ShouldBeCaptured()
        {
            // Arrange: MethodA calls new OtherClass()
            var code = @"
namespace TestNamespace {
    public class TestClass {
        public void MethodA() { var x = new OtherClass(); }
    }
    public class OtherClass {
        public OtherClass() { }
    }
}";
            var solution = CreateTestSolution(("TestFile.cs", code));
            File.WriteAllLines(_tempLogPath, new[] { "[2026-02-15 12:00:00] [ENTER] TestNamespace.TestClass.MethodA" });

            var analyzer = new StaticCallChainAnalyzer("dummy.sln", new Load());

            // Act
            await analyzer.AnalyzeSolutionAsync(solution, _tempLogPath);

            // Assert
            var results = File.ReadAllLines("analyzed_functions.txt");
            Assert.Contains("TestNamespace.TestClass.MethodA", results);
            Assert.Contains("TestNamespace.OtherClass.OtherClass", results);
        }

        [Fact]
        public async Task Test_ExternalLibraryCall_ShouldBeIgnored()
        {
            // Arrange: Method calls Console.WriteLine (external)
            var code = @"
using System;
namespace TestNamespace {
    public class TestClass {
        public void MethodA() { Console.WriteLine(""Hello""); }
    }
}";
            var solution = CreateTestSolution(("TestFile.cs", code));
            File.WriteAllLines(_tempLogPath, new[] { "[2026-02-15 12:00:00] [ENTER] TestNamespace.TestClass.MethodA" });

            var analyzer = new StaticCallChainAnalyzer("dummy.sln", new Load());

            // Act
            await analyzer.AnalyzeSolutionAsync(solution, _tempLogPath);

            // Assert
            var results = File.ReadAllLines("analyzed_functions.txt");
            Assert.Contains("TestNamespace.TestClass.MethodA", results);
            // Console.WriteLine should not be in results because it's not in the _methodSyntaxCache
            Assert.DoesNotContain("System.Console.WriteLine", results);
            Assert.Single(results);
        }

        [Fact]
        public async Task Test_MultipleInitialSeeds_ShouldBeProcessed()
        {
            // Arrange: Two separate call chains
            var code = @"
namespace TestNamespace {
    public class TestClass {
        public void Method1() { MethodA(); }
        public void MethodA() { }
        public void Method2() { MethodB(); }
        public void MethodB() { }
    }
}";
            var solution = CreateTestSolution(("TestFile.cs", code));
            File.WriteAllLines(_tempLogPath, new[] {
                "[2026-02-15 12:00:00] [ENTER] TestNamespace.TestClass.Method1",
                "[2026-02-15 12:00:01] [ENTER] TestNamespace.TestClass.Method2"
            });

            var analyzer = new StaticCallChainAnalyzer("dummy.sln", new Load());

            // Act
            await analyzer.AnalyzeSolutionAsync(solution, _tempLogPath);

            // Assert
            var results = File.ReadAllLines("analyzed_functions.txt");
            Assert.Contains("TestNamespace.TestClass.Method1", results);
            Assert.Contains("TestNamespace.TestClass.MethodA", results);
            Assert.Contains("TestNamespace.TestClass.Method2", results);
            Assert.Contains("TestNamespace.TestClass.MethodB", results);
            Assert.Equal(4, results.Length);
        }

        [Fact]
        public async Task Test_EmptyLog_ShouldReturnEmptyResults()
        {
            // Arrange: No seeds in log
            var code = @"
namespace TestNamespace {
    public class TestClass {
        public void MethodA() { }
    }
}";
            var solution = CreateTestSolution(("TestFile.cs", code));
            File.WriteAllText(_tempLogPath, "");

            var analyzer = new StaticCallChainAnalyzer("dummy.sln", new Load());

            // Act
            await analyzer.AnalyzeSolutionAsync(solution, _tempLogPath);

            // Assert
            var results = File.ReadAllLines("analyzed_functions.txt");
            Assert.Empty(results);
        }
    }
}
