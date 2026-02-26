using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions;
using TerrariaTools.Services;
using Moq;
using Xunit;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.IO;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace TerrariaTools.UnitTests
{
    public class MethodRefactorerTests
    {
        private async Task<(AdhocWorkspace, Project, Document)> CreateProjectAsync(string source, string fileName = "TestFile.cs")
        {
            var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var version = VersionStamp.Create();
            var projectInfo = ProjectInfo.Create(projectId, version, "TestProject", "TestAssembly", LanguageNames.CSharp);

            var project = workspace.AddProject(projectInfo);
            project = project.AddMetadataReferences(new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.Task).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll"))
            });

            var documentId = DocumentId.CreateNewId(projectId);
            var sourceText = SourceText.From(source, Encoding.UTF8);
            var document = workspace.AddDocument(project.Id, fileName, sourceText);

            return (workspace, project, document);
        }

        private async Task<Dictionary<string, string>> RunRefactorerAsync(string source, string fileName = "TestFile.cs", bool aggressive = false, bool enableRatioAnalysis = true)
        {
            var (workspace, project, document) = await CreateProjectAsync(source, fileName);

            // Set absolute path for the document
            var absolutePath = Path.GetFullPath(fileName);
            var newSolution = workspace.CurrentSolution.WithDocumentFilePath(document.Id, absolutePath);
            workspace.TryApplyChanges(newSolution);

            var results = new Dictionary<string, string>();
            results[fileName] = source;

            var mockLoader = new Mock<IWorkspaceLoader>();
            mockLoader.Setup(l => l.LoadSolutionAsync(It.IsAny<string>()))
                      .ReturnsAsync(workspace.CurrentSolution);

            mockLoader.Setup(l => l.SaveDocumentAsync(It.IsAny<string>(), It.IsAny<string>()))
                      .Callback<string, string>((path, content) =>
                      {
                          if (path == absolutePath)
                          {
                              results[fileName] = content;
                          }
                      })
                      .Returns(Task.CompletedTask);

            await MethodRefactorer.ExecuteSolutionRefactoringAsync("dummy.sln", mockLoader.Object, aggressive: aggressive, enableRatioAnalysis: enableRatioAnalysis);

            return results;
        }

        private Task<Dictionary<string, string>> RunMultiFileRefactorerAsync(bool aggressive, params (string name, string content)[] files)
        {
            return RunMultiFileRefactorerAsync(aggressive, true, files);
        }

        private async Task<Dictionary<string, string>> RunMultiFileRefactorerAsync(bool aggressive, bool enableRatioAnalysis, params (string name, string content)[] files)
        {
            var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var project = workspace.AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), "Test", "Test", LanguageNames.CSharp)
                .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .WithMetadataReferences(new[] {
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.Task).Assembly.Location),
                    MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll"))
                }));

            var results = new Dictionary<string, string>();
            var fileMap = new Dictionary<string, string>(); // Absolute -> Relative

            foreach (var file in files)
            {
                var sourceText = SourceText.From(file.content, Encoding.UTF8);
                var doc = workspace.AddDocument(project.Id, file.name, sourceText);

                var absolutePath = Path.GetFullPath(file.name);
                workspace.TryApplyChanges(workspace.CurrentSolution.WithDocumentFilePath(doc.Id, absolutePath));

                results[file.name] = file.content;
                fileMap[absolutePath] = file.name;
            }

            var compilation = await workspace.CurrentSolution.Projects.First().GetCompilationAsync();
            var diagnostics = compilation.GetDiagnostics();
            var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            if (errors.Any())
            {
                throw new Exception("Compilation errors:\n" + string.Join("\n", errors));
            }

            var mockLoader = new Mock<IWorkspaceLoader>();
            mockLoader.Setup(l => l.LoadSolutionAsync(It.IsAny<string>()))
                      .ReturnsAsync(workspace.CurrentSolution);

            mockLoader.Setup(l => l.SaveDocumentAsync(It.IsAny<string>(), It.IsAny<string>()))
                      .Callback<string, string>((path, content) =>
                      {
                          if (fileMap.TryGetValue(path, out var relativeName))
                          {
                              results[relativeName] = content;
                          }
                      })
                      .Returns(Task.CompletedTask);

            await MethodRefactorer.ExecuteSolutionRefactoringAsync("dummy.sln", mockLoader.Object, aggressive: aggressive, enableRatioAnalysis: enableRatioAnalysis);

            return results;
        }

        #region 进阶逻辑测试 (接口、抽象类、重写)

        [Fact]
        public async Task SkipInterfaceDeclaration_DoesNotModifyInterface()
        {
            string source = "public interface ITest { void M(); }";
            var results = await RunRefactorerAsync(source);
            Assert.Contains("void M();", results["TestFile.cs"]);
        }

        [Fact]
        public async Task AbstractMethod_Unused_IsNotDeleted()
        {
            string source = "public abstract class T { public abstract void M(); }";
            var results = await RunRefactorerAsync(source);
            Assert.Contains("public abstract void M();", results["TestFile.cs"]);
        }

        [Fact]
        public async Task AbstractMethod_UsedInternally_IsNotPrivatized()
        {
            string source = "public abstract class T { public abstract void M(); void Test() => M(); }";
            var results = await RunRefactorerAsync(source);
            Assert.Contains("public abstract void M();", results["TestFile.cs"]);
        }

        [Fact]
        public async Task OverrideMethod_Unused_ClearsBodyButKeepsSignature()
        {
            var results = await RunMultiFileRefactorerAsync(true, false,
                ("Base.cs", "public class Base { public virtual void M() {} }"),
                ("Derived.cs", "public class Derived : Base { public override void M() { System.Console.WriteLine(1); } }")
            );

            var normalizedOutput = results["Derived.cs"].Replace("\r", "").Replace("\n", "").Replace(" ", "");
            Assert.Contains("publicoverridevoidM(){}", normalizedOutput);
        }

        [Fact]
        public async Task InterfaceImplementation_Unused_ClearsBodyButKeepsSignature()
        {
            string sourceBase = "public interface I { void M(); }";
            string sourceImpl = "public class C : I { public void M() { System.Console.WriteLine(1); } }";

            var results = await RunMultiFileRefactorerAsync(true, false,
                ("I.cs", sourceBase),
                ("C.cs", sourceImpl)
            );

            var normalizedOutput = results["C.cs"].Replace("\r", "").Replace("\n", "").Replace(" ", "");
            Assert.Contains("publicvoidM(){}", normalizedOutput);
        }

        [Fact]
        public async Task UnusedMethod_WithReturnType_ClearsBodyAndAddsReturnDefault()
        {
            string sourceBase = "public interface I { int M(); }";
            string sourceImpl = "public class C : I { public int M() { return 1; } }";

            var results = await RunMultiFileRefactorerAsync(true, false,
                ("I.cs", sourceBase),
                ("C.cs", sourceImpl)
            );

            var normalizedOutput = results["C.cs"].Replace("\r", "").Replace("\n", "").Replace(" ", "");
            Assert.Contains("publicintM(){returndefault;}", normalizedOutput);
        }

        [Fact]
        public async Task UnusedMethod_WithComplexReturnType_ClearsBodyAndAddsReturnDefault()
        {
            string sourceBase = "public interface I { string M(); }";
            string sourceImpl = "public class C : I { public string M() { return \"test\"; } }";

            var results = await RunMultiFileRefactorerAsync(true, false,
                ("I.cs", sourceBase),
                ("C.cs", sourceImpl)
            );

            var normalizedOutput = results["C.cs"].Replace("\r", "").Replace("\n", "").Replace(" ", "");
            Assert.Contains("publicstringM(){returndefault;}", normalizedOutput);
        }

        [Fact]
        public async Task UnusedMethod_WithGenericReturnType_ClearsBodyAndAddsReturnDefault()
        {
            string sourceBase = "public interface I { T M<T>(); }";
            string sourceImpl = "public class C : I { public T M<T>() { return default; } }";

            var results = await RunMultiFileRefactorerAsync(true, false,
                ("I.cs", sourceBase),
                ("C.cs", sourceImpl)
            );

            var normalizedOutput = results["C.cs"].Replace("\r", "").Replace("\n", "").Replace(" ", "");
            Assert.Contains("publicTM<T>(){returndefault;}", normalizedOutput);
        }

        [Fact]
        public async Task UnusedMethod_WithNullableReturnType_ClearsBodyAndAddsReturnDefault()
        {
            string sourceBase = "public interface I { int? M(); }";
            string sourceImpl = "public class C : I { public int? M() { return null; } }";

            var results = await RunMultiFileRefactorerAsync(true, false,
                ("I.cs", sourceBase),
                ("C.cs", sourceImpl)
            );

            var normalizedOutput = results["C.cs"].Replace("\r", "").Replace("\n", "").Replace(" ", "");
            Assert.Contains("publicint?M(){returndefault;}", normalizedOutput);
        }

        [Fact]
        public async Task UnusedMethod_WithAsyncReturnType_ClearsBodyAndAddsReturnDefault()
        {
            string sourceBase = "using System.Threading.Tasks; public interface I { Task<int> M(); }";
            string sourceImpl = "using System.Threading.Tasks; public class C : I { public async Task<int> M() { await Task.Delay(1); return 1; } }";

            var results = await RunMultiFileRefactorerAsync(true, false,
                ("I.cs", sourceBase),
                ("C.cs", sourceImpl)
            );

            var normalizedOutput = results["C.cs"].Replace("\r", "").Replace("\n", "").Replace(" ", "");
            Assert.Contains("publicasyncTask<int>M(){returndefault;}", normalizedOutput);
        }

        [Fact]
        public async Task UnusedMethod_WithParameters_ClearsBodyAndAddsReturnDefault()
        {
            string sourceBase = "public interface I { int M(int a, string b); }";
            string sourceImpl = "public class C : I { public int M(int a, string b) { return a; } }";

            var results = await RunMultiFileRefactorerAsync(true, false,
                ("I.cs", sourceBase),
                ("C.cs", sourceImpl)
            );

            var normalizedOutput = results["C.cs"].Replace("\r", "").Replace("\n", "").Replace(" ", "");
            Assert.Contains("publicintM(inta,stringb){returndefault;}", normalizedOutput);
        }

        [Fact]
        public async Task UnusedMethod_WithBoolReturnType_ClearsBodyAndAddsReturnFalse()
        {
            string sourceBase = "public interface I { bool M(); }";
            string sourceImpl = "public class C : I { public bool M() { return true; } }";

            var results = await RunMultiFileRefactorerAsync(true, false,
                ("I.cs", sourceBase),
                ("C.cs", sourceImpl)
            );

            var normalizedOutput = results["C.cs"].Replace("\r", "").Replace("\n", "").Replace(" ", "");
            Assert.Contains("publicboolM(){returndefault;}", normalizedOutput);
        }

        [Fact]
        public async Task UnusedMethod_WithStructReturnType_ClearsBodyAndAddsReturnNull()
        {
            string sourceBase = "public struct MyStruct {} public interface I { MyStruct M(); }";
            string sourceImpl = "public class C : I { public MyStruct M() { return new MyStruct(); } }";

            var results = await RunMultiFileRefactorerAsync(true, false,
                ("I.cs", sourceBase),
                ("C.cs", sourceImpl)
            );

            var normalizedOutput = results["C.cs"].Replace("\r", "").Replace("\n", "").Replace(" ", "");
            Assert.Contains("publicMyStructM(){returndefault;}", normalizedOutput);
        }

        [Fact]
        public async Task InterfaceImplementation_WithInternalReferences_ShouldStayPublic()
        {
            string sourceBase = "public interface INeed { void Reset(); }";
            string sourceImpl = @"
public class C : INeed {
    public void Reset() { }
    public void Use() { Reset(); }
}";

            var results = await RunMultiFileRefactorerAsync(true,
                ("INeed.cs", sourceBase),
                ("C.cs", sourceImpl)
            );

            var normalizedOutput = results["C.cs"].Replace("\r", "").Replace("\n", "").Replace(" ", "");
            // Reset 实现了接口，且被内部引用，应该保持 public 而不是变为 private
            Assert.Contains("publicvoidReset()", normalizedOutput);
            Assert.DoesNotContain("privatevoidReset()", normalizedOutput);
        }

        [Fact]
        public async Task UnusedMethod_Void_ClearsBodyWithoutReturn()
        {
            string sourceBase = "public interface I { void M(); }";
            string sourceImpl = "public class C : I { public void M() { System.Console.WriteLine(1); } }";

            var results = await RunMultiFileRefactorerAsync(true, false,
                ("I.cs", sourceBase),
                ("C.cs", sourceImpl)
            );

            var normalizedOutput = results["C.cs"].Replace("\r", "").Replace("\n", "").Replace(" ", "");
            Assert.Contains("publicvoidM(){}", normalizedOutput);
        }

        [Fact]
        public async Task UnusedMethod_AsyncVoid_ClearsBodyWithoutReturn()
        {
            string sourceBase = "public class B { public virtual async void M() { await System.Threading.Tasks.Task.Delay(1); } }";
            string sourceImpl = "public class C : B { public override async void M() { await System.Threading.Tasks.Task.Delay(1); } }";

            var results = await RunMultiFileRefactorerAsync(true, false,
                ("B.cs", sourceBase),
                ("C.cs", sourceImpl)
            );

            var normalizedOutput = results["C.cs"].Replace("\r", "").Replace("\n", "").Replace(" ", "");
            Assert.Contains("publicoverrideasyncvoidM(){}", normalizedOutput);
        }

        [Fact]
        public async Task VirtualMethod_UsedInternally_IsNOTPrivatized()
        {
            string source = @"
public class Base {
    public virtual void M() {}
    public void Caller() { M(); }
}";
            var results = await RunRefactorerAsync(source);
            Assert.Contains("public virtual void M()", results["TestFile.cs"]);
        }

        [Fact]
        public async Task OverrideMethod_UsedInternally_IsNOTPrivatized()
        {
            string sourceBase = "public class Base { public virtual void M() {} }";
            string sourceDerived = @"
public class Derived : Base {
    public override void M() {}
    public void Caller() { M(); }
}";
            var results = await RunMultiFileRefactorerAsync(true,
                ("Base.cs", sourceBase),
                ("Derived.cs", sourceDerived)
            );
            Assert.Contains("public override void M()", results["Derived.cs"]);
        }

        [Fact]
        public async Task VirtualMethod_WithNoCallersButHasOverride_ClearsBodyButKeepsSignature()
        {
            string sourceBase = "public class Base { public virtual void M() { System.Console.WriteLine(1); } }";
            string sourceDerived = "public class Derived : Base { public override void M() { System.Console.WriteLine(2); } }";

            var results = await RunMultiFileRefactorerAsync(true, false,
                ("Base.cs", sourceBase),
                ("Derived.cs", sourceDerived)
            );

            // 基类虚方法应该保留签名但清空体
            var normalizedBase = results["Base.cs"].Replace("\r", "").Replace("\n", "").Replace(" ", "");
            Assert.Contains("publicvirtualvoidM(){}", normalizedBase);

            // 子类重写方法也应该保留签名但清空体
            var normalizedDerived = results["Derived.cs"].Replace("\r", "").Replace("\n", "").Replace(" ", "");
            Assert.Contains("publicoverridevoidM(){}", normalizedDerived);
        }

        [Fact]
        public async Task PublicMethod_UsedInternally_IsPrivatized_WithoutDuplicates()
        {
            string source = @"
public class T
{
    public void M() { }
    public void Caller() { M(); }
}";
            var results = await RunRefactorerAsync(source, aggressive: true);
            var output = results["TestFile.cs"];

            // 验证已私有化
            Assert.Contains("private void M()", output);
            // 验证没有重复定义 (只出现一次 M())
            int count = System.Text.RegularExpressions.Regex.Matches(output, @"void M\(\)").Count;
            Assert.Equal(1, count);
        }

        [Fact]
        public async Task HandleMethodWithOutParameters()
        {
            var results = await RunMultiFileRefactorerAsync(true, false,
                ("Base.cs", "public class Base { public virtual bool M(out string rejectReason) { rejectReason = null; return true; } }"),
                ("Derived.cs", "public class Derived : Base { public override bool M(out string rejectReason) { rejectReason = \"error\"; return false; } }")
            );

            // Normalize output to ignore whitespace differences
            var normalized = results["Derived.cs"].Replace(" ", "").Replace("\r", "").Replace("\n", "");
            Assert.Contains("rejectReason=default;", normalized);
        }

        [Fact]
        public async Task HandleMethodWithNumericOutParameters()
        {
            var results = await RunMultiFileRefactorerAsync(true, false,
                ("Base.cs", "public class Base { public virtual void M(out int x) { x = 0; } }"),
                ("Derived.cs", "public class Derived : Base { public override void M(out int x) { x = 1; } }")
            );

            // Normalize output
            var normalized = results["Derived.cs"].Replace(" ", "").Replace("\r", "").Replace("\n", "");
            Assert.Contains("x=default;", normalized);
        }

        #endregion

        #region 删除未引用方法测试 (15个)

        [Fact]
        public async Task DeleteUnusedMethod_SimpleMethod_RemovesIt()
        {
            string source = @"
class Test {
    void Unused() { }
    public void Used() { Used(); }
}";
            var results = await RunRefactorerAsync(source);
            Assert.True(results.ContainsKey("TestFile.cs"));
            Assert.DoesNotContain("Unused", results["TestFile.cs"]);
            Assert.Contains("Used", results["TestFile.cs"]);
        }

        [Fact]
        public async Task DeleteUnusedMethod_WithParameters_RemovesIt()
        {
            string source = "class T { void M(int x, string s) {} }";
            var results = await RunRefactorerAsync(source);
            Assert.DoesNotContain("void M", results["TestFile.cs"]);
        }

        [Fact]
        public async Task DeleteUnusedMethod_StaticMethod_RemovesIt()
        {
            string source = "class T { static void M() {} }";
            var results = await RunRefactorerAsync(source);
            Assert.DoesNotContain("static void M", results["TestFile.cs"]);
        }

        [Fact]
        public async Task DeleteUnusedMethod_AsyncMethod_RemovesIt()
        {
            string source = "using System.Threading.Tasks; class T { async Task M() {} }";
            var results = await RunRefactorerAsync(source);
            Assert.DoesNotContain("async Task M", results["TestFile.cs"]);
        }

        [Fact]
        public async Task DeleteUnusedMethod_MultipleMethods_RemovesAllUnused()
        {
            string source = "class T { void M1(){} void M2(){} void M3(){ M3(); } }";
            var results = await RunRefactorerAsync(source);
            Assert.DoesNotContain("M1", results["TestFile.cs"]);
            Assert.DoesNotContain("M2", results["TestFile.cs"]);
            Assert.DoesNotContain("M3", results["TestFile.cs"]); // Recursive call only -> Unused
        }

        [Fact]
        public async Task DeleteUnusedMethod_PrivateMethod_RemovesIt()
        {
            string source = "class T { private void M() {} }";
            var results = await RunRefactorerAsync(source);
            Assert.DoesNotContain("M", results["TestFile.cs"]);
        }

        [Fact]
        public async Task DeleteUnusedMethod_ProtectedMethod_RemovesIt()
        {
            string source = "class T { protected void M() {} }";
            var results = await RunRefactorerAsync(source);
            Assert.DoesNotContain("M", results["TestFile.cs"]);
        }

        [Fact]
        public async Task DeleteUnusedMethod_InternalMethod_RemovesIt()
        {
            string source = "class T { internal void M() {} }";
            var results = await RunRefactorerAsync(source);
            Assert.DoesNotContain("M", results["TestFile.cs"]);
        }

        [Fact]
        public async Task DeleteUnusedMethod_GenericMethod_RemovesIt()
        {
            string source = "class T { void M<T>() {} }";
            var results = await RunRefactorerAsync(source);
            Assert.DoesNotContain("M<T>", results["TestFile.cs"]);
        }

        [Fact]
        public async Task DeleteUnusedMethod_MethodWithAttributes_RemovesIt()
        {
            string source = "class T { [System.Obsolete] void M() {} }";
            var results = await RunRefactorerAsync(source);
            Assert.DoesNotContain("M", results["TestFile.cs"]);
        }

        [Fact]
        public async Task DeleteUnusedMethod_ExpressionBodiedMethod_RemovesIt()
        {
            string source = "class T { int M() => 1; }";
            var results = await RunRefactorerAsync(source);
            Assert.DoesNotContain("M", results["TestFile.cs"]);
        }

        [Fact]
        public async Task DeleteUnusedMethod_MethodWithXmlDoc_RemovesIt()
        {
            string source = "class T { /// <summary>Doc</summary>\nvoid M() {} }";
            var results = await RunRefactorerAsync(source);
            Assert.DoesNotContain("M", results["TestFile.cs"]);
        }

        [Fact]
        public async Task DeleteUnusedMethod_InNestedClass_RemovesIt()
        {
            string source = "class Outer { class Inner { void M() {} } }";
            var results = await RunRefactorerAsync(source);
            Assert.DoesNotContain("void M", results["TestFile.cs"]);
        }

        [Fact]
        public async Task DeleteUnusedMethod_InPartialClass_RemovesIt()
        {
            string source = "partial class T { void M() {} }";
            var results = await RunRefactorerAsync(source);
            Assert.DoesNotContain("void M", results["TestFile.cs"]);
        }

        [Fact]
        public async Task DeleteUnusedMethod_WithReturnValues_RemovesIt()
        {
            string source = "class T { int M() { return 1; } }";
            var results = await RunRefactorerAsync(source);
            Assert.DoesNotContain("int M", results["TestFile.cs"]);
        }

        #endregion

        #region 内部引用公开方法重构测试 (15个)

        [Fact]
        public async Task RefactorInternalMethod_PublicUsedInternally_BecomesPrivate()
        {
            string source = @"
namespace N {
    public class T {
        public void InternalUsed() { }
        public void Test() { InternalUsed(); }
    }
}";
            var results = await RunRefactorerAsync(source, aggressive: true);
            Assert.Contains("private void InternalUsed", results["TestFile.cs"]);
        }

        [Fact]
        public async Task RefactorInternalMethod_WithParameters_KeepsThem()
        {
            string source = "namespace N { public class T { public void M(int x) {} public void Test() => M(1); } }";
            var results = await RunRefactorerAsync(source, aggressive: true);
            Assert.Contains("private void M(int x)", results["TestFile.cs"]);
        }

        [Fact]
        public async Task RefactorInternalMethod_StaticMethod_KeepsStatic()
        {
            string source = "namespace N { public class T { public static void M() {} public void Test() => M(); } }";
            var results = await RunRefactorerAsync(source, aggressive: true);
            Assert.Contains("private static void M", results["TestFile.cs"]);
        }

        [Fact]
        public async Task RefactorInternalMethod_AsyncMethod_KeepsAsync()
        {
            string source = "using System.Threading.Tasks; namespace N { public class T { public async Task M() {} public void Test() => M(); } }";
            var results = await RunRefactorerAsync(source, aggressive: true);
            Assert.Contains("private async Task M", results["TestFile.cs"]);
        }

        [Fact]
        public async Task RefactorInternalMethod_GenericMethod_KeepsGenerics()
        {
            string source = "namespace N { public class T { public void M<T>() {} public void Test() => M<int>(); } }";
            var results = await RunRefactorerAsync(source, aggressive: true);
            Assert.Contains("private void M<T>", results["TestFile.cs"]);
        }

        [Fact]
        public async Task RefactorInternalMethod_MultipleInternalUsed_BecomesPrivate()
        {
            string source = "namespace N { public class T { public void M1(){} public void M2(){} public void Test(){ M1(); M2(); } } }";
            var results = await RunRefactorerAsync(source, aggressive: true);
            Assert.Contains("private void M1", results["TestFile.cs"]);
            Assert.Contains("private void M2", results["TestFile.cs"]);
        }

        [Fact]
        public async Task RefactorInternalMethod_PreservesUsings()
        {
            string source = "using System; namespace N { public class T { public void M() { Console.WriteLine(); } public void Test() => M(); } }";
            var results = await RunRefactorerAsync(source, aggressive: true);
            Assert.Contains("using System;", results["TestFile.cs"]);
        }

        [Fact]
        public async Task RefactorInternalMethod_PreservesNamespace()
        {
            string source = "namespace MyNamespace { public class T { public void M() {} public void Test() => M(); } }";
            var results = await RunRefactorerAsync(source, aggressive: true);
            Assert.Contains("namespace MyNamespace", results["TestFile.cs"]);
        }

        [Fact]
        public async Task RefactorInternalMethod_DoesNotAddPartialKeyword()
        {
            string source = "namespace N { public class T { public void M() {} public void Test() => M(); } }";
            var results = await RunRefactorerAsync(source, aggressive: true);
            Assert.DoesNotContain("public partial class T", results["TestFile.cs"]);
        }

        [Fact]
        public async Task RefactorInternalMethod_KeepsExistingPartialKeyword()
        {
            string source = "namespace N { public partial class T { public void M() {} public void Test() => M(); } }";
            var results = await RunRefactorerAsync(source, aggressive: true);
            Assert.Contains("public partial class T", results["TestFile.cs"]);
        }

        [Fact]
        public async Task RefactorInternalMethod_FileScopedNamespace_KeepsIt()
        {
            string source = "namespace N; public class T { public void M() {} public void Test() => M(); }";
            var results = await RunRefactorerAsync(source, aggressive: true);
            Assert.Contains("namespace N;", results["TestFile.cs"]);
        }

        [Fact]
        public async Task RefactorInternalMethod_WithExpressionBody_KeepsIt()
        {
            string source = "namespace N { public class T { public int M() => 1; public void Test() => M(); } }";
            var results = await RunRefactorerAsync(source, aggressive: true);
            Assert.Contains("private int M() => 1;", results["TestFile.cs"]);
        }

        [Fact]
        public async Task RefactorInternalMethod_NestedClass_RemainsNested()
        {
            string source = "namespace N { public class Outer { public class Inner { public void M() {} public void Test() => M(); } } }";
            var results = await RunRefactorerAsync(source, aggressive: true);
            Assert.Contains("private void M()", results["TestFile.cs"]);
        }

        [Fact]
        public async Task RefactorInternalMethod_WithAttributes_KeepsThem()
        {
            string source = "using System; namespace N { public class T { [Obsolete] public void M() {} public void Test() => M(); } }";
            var results = await RunRefactorerAsync(source, aggressive: true);
            Assert.Contains("[Obsolete]", results["TestFile.cs"]);
            Assert.Contains("private void M()", results["TestFile.cs"]);
        }

        [Fact]
        public async Task RefactorInternalMethod_GenericClass_KeepsGenerics()
        {
            string source = "namespace N { public class T<U> { public void M() {} public void Test() => M(); } }";
            var results = await RunRefactorerAsync(source, aggressive: true);
            Assert.Contains("private void M()", results["TestFile.cs"]);
        }

        [Fact]
        public async Task RefactorInternalMethod_Ordering_ExternalRefFirst()
        {
            var results = await RunMultiFileRefactorerAsync(true,
                ("File1.cs", @"
public class T {
    public void InternalOnly() { }
    public void ExternalUsed() { }
    public void Test() { InternalOnly(); ExternalUsed(); }
}"),
                ("File2.cs", "class T2 { void M() { new T().ExternalUsed(); } }")
            );

            string content = results["File1.cs"];
            int indexExternal = content.IndexOf("void ExternalUsed");
            int indexInternal = content.IndexOf("void InternalOnly");

            Assert.True(indexExternal > indexInternal, "ExternalUsed should come after InternalOnly (no reordering)");
            Assert.Contains("public void ExternalUsed", content);
            Assert.Contains("private void InternalOnly", content);
        }

        #endregion

        #region 边界情况和复杂场景测试 (20个)

        [Fact]
        public async Task DontDelete_IfUsedInOtherFile()
        {
            var results = await RunMultiFileRefactorerAsync(true,
                ("File1.cs", "public class T { public void M1() {} }"),
                ("File2.cs", "class Other { void Test() { new T().M1(); } }")
            );

            // M1 不应该被删除，也不应该被重构（因为它是公开的且被外部引用）
            Assert.Contains("public void M1", results["File1.cs"]);
        }

        [Fact]
        public async Task DontDelete_OverrideMethod()
        {
            string source = "class Base { public virtual void M() {} } class Derived : Base { public override void M() {} }";
            var results = await RunRefactorerAsync(source);
            Assert.Contains("override void M", results["TestFile.cs"]);
        }

        [Fact]
        public async Task DontDelete_InterfaceImplementation()
        {
            string source = "interface I { void M(); } class T : I { public void M() {} }";
            var results = await RunRefactorerAsync(source);
            Assert.Contains("public void M", results["TestFile.cs"]);
        }

        [Fact]
        public async Task DontMove_IfPublicMethodUsedExternally()
        {
            var results = await RunMultiFileRefactorerAsync(true,
                ("File1.cs", "namespace N { public class T { public void PublicUsed() {} void Test() => PublicUsed(); } }"),
                ("File2.cs", "class T2 { void M() { new N.T().PublicUsed(); } }")
            );

            Assert.Contains("public void PublicUsed", results["File1.cs"]);
        }

        [Fact]
        public async Task HandleMultipleClassesInOneFile()
        {
            var results = await RunRefactorerAsync(
                "class A { public void M1() {} } class B { void Test() { new A().M1(); } }");

            // M1 不应该被重构为私有，因为它被 B 引用（虽然在同一个文件中，但是不同类，B 对 A 来说是外部的）
            Assert.Contains("public void M1", results["TestFile.cs"]);
        }

        [Fact]
        public async Task HandleStructs_ShouldBeIgnoredByCurrentImplementation()
        {
            string source = "struct S { void M() {} }";
            var results = await RunRefactorerAsync(source);
            Assert.Contains("void M", results["TestFile.cs"]); // 只有 ClassDeclarationSyntax 会被处理
        }

        [Fact]
        public async Task HandleEnums_ShouldBeIgnored()
        {
            string source = "enum E { A, B }";
            var results = await RunRefactorerAsync(source);
            Assert.Contains("enum E", results["TestFile.cs"]);
        }

        [Fact]
        public async Task HandleStaticClass()
        {
            string source = "static class T { static void M() {} }";
            var results = await RunRefactorerAsync(source);
            Assert.DoesNotContain("void M", results["TestFile.cs"]);
        }

        [Fact]
        public async Task HandleSealedClass()
        {
            string source = "sealed class T { void M() {} }";
            var results = await RunRefactorerAsync(source);
            Assert.DoesNotContain("void M", results["TestFile.cs"]);
        }

        [Fact]
        public async Task RefactorInternalMethod_InternalUsedInternally_BecomesPrivate()
        {
            string source = "namespace N { public class T { internal void InternalUsed() {} public void Test() => InternalUsed(); } }";
            var results = await RunRefactorerAsync(source);
            Assert.Contains("private void InternalUsed", results["TestFile.cs"]);
        }

        [Fact]
        public async Task DontMove_ProtectedMethod()
        {
            string source = "namespace N { public class T { protected void ProtUsed() {} public void Test() => ProtUsed(); } }";
            var results = await RunRefactorerAsync(source);
            Assert.Contains("protected void ProtUsed", results["TestFile.cs"]);
        }

        [Fact]
        public async Task HandleMethodWithRefOutParameters()
        {
            string source = "class T { void M(ref int x, out int y) { y = 0; } }";
            var results = await RunRefactorerAsync(source);
            Assert.DoesNotContain("void M", results["TestFile.cs"]);
        }

        [Fact]
        public async Task HandleMethodWithOptionalParameters()
        {
            string source = "class T { void M(int x = 0) {} }";
            var results = await RunRefactorerAsync(source);
            Assert.DoesNotContain("void M", results["TestFile.cs"]);
        }

        [Fact]
        public async Task HandleMethodWithParams()
        {
            string source = "class T { void M(params int[] x) {} }";
            var results = await RunRefactorerAsync(source);
            Assert.DoesNotContain("void M", results["TestFile.cs"]);
        }

        [Fact]
        public async Task DontDelete_MainMethod()
        {
            string source = "class Program { static void Main(string[] args) {} }";
            var results = await RunRefactorerAsync(source);
            // Main 虽然没被引用，但是程序的入口点，必须保留
            Assert.Contains("static void Main", results["TestFile.cs"]);
        }

        [Fact]
        public async Task HandleMethodInNamespaceBlock()
        {
            string source = "namespace A.B { class T { void M() {} } }";
            var results = await RunRefactorerAsync(source);
            Assert.DoesNotContain("void M", results["TestFile.cs"]);
        }

        [Fact]
        public async Task HandleMethodWithComplexReturnType()
        {
            string source = "using System.Collections.Generic; class T { List<int> M() => null; }";
            var results = await RunRefactorerAsync(source);
            Assert.DoesNotContain("List<int> M", results["TestFile.cs"]);
        }

        [Fact]
        public async Task HandleExplicitInterfaceImplementation()
        {
            string source = "interface I { void M(); } class T : I { void I.M() {} }";
            var results = await RunRefactorerAsync(source, fileName: "TestFile.cs", aggressive: false, enableRatioAnalysis: false);
            Assert.Contains("void I.M()", results["TestFile.cs"]);
        }

        [Fact]
        public async Task HandleMethodWithConstraints()
        {
            string source = "class T { void M<T>() where T : class {} }";
            var results = await RunRefactorerAsync(source);
            Assert.DoesNotContain("M<T>", results["TestFile.cs"]);
        }

        [Fact]
        public async Task HandlePartialClassInDifferentFiles()
        {
            var results = await RunMultiFileRefactorerAsync(true,
                ("File1.cs", "namespace N { public partial class T { public void M1() {} } }"),
                ("File2.cs", "namespace N { public partial class T { void Test() => M1(); } }")
            );

            // M1 应该被改为 private，因为引用在同一个类（虽然不同文件）
            Assert.Contains("private void M1", results["File1.cs"]);
        }

        [Fact]
        public async Task MultiClassInSameFile_ShouldNotMoveMethodsBetweenClasses()
        {
            // 为了防止方法被删除，我们需要在外部调用它们
            string source = @"
public class ClassA {
    public void MethodA() { }
}
public class ClassB {
    public void MethodB() { }
}
public class Caller {
    public void Call(ClassA a, ClassB b) {
        a.MethodA();
        b.MethodB();
    }
}";
            var results = await RunRefactorerAsync(source);
            var output = results["TestFile.cs"];

            // 检查输出结果
            Assert.Contains("public class ClassA", output);
            Assert.Contains("public void MethodA", output);
            Assert.Contains("public class ClassB", output);
            Assert.Contains("public void MethodB", output);

            // 验证 MethodA 还在 ClassA 中，MethodB 还在 ClassB 中
            // 注意：NormalizeWhitespace 会处理换行
            Assert.Matches(@"public class ClassA\s*\{\s*public void MethodA", output);
            Assert.Matches(@"public class ClassB\s*\{\s*public void MethodB", output);
        }

        #endregion
    }
}

