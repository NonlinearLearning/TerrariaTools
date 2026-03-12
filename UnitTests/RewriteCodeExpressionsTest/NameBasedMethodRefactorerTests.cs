using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions;
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
    public class NameBasedMethodRefactorerTests
    {
        private async Task<(AdhocWorkspace, Project)> CreateProjectAsync(params (string name, string content)[] files)
        {
            var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var projectInfo = ProjectInfo.Create(projectId, VersionStamp.Create(), "TestProject", "TestAssembly", LanguageNames.CSharp);

            var project = workspace.AddProject(projectInfo);
            project = project.AddMetadataReferences(new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.Task).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll"))
            });

            foreach (var file in files)
            {
                var documentId = DocumentId.CreateNewId(projectId);
                var sourceText = SourceText.From(file.content, Encoding.UTF8);
                project = project.AddDocument(file.name, sourceText).Project;
            }

            return (workspace, project);
        }

        private async Task<string> RunRefactorerAsync(string source, string pattern = "Draw")
        {
            var (workspace, project) = await CreateProjectAsync(("TestFile.cs", source));
            var refactorer = new NameBasedMethodRefactorer(project.Solution, pattern);
            var result = await refactorer.ProcessFileAsync("TestFile.cs", useFileLock: false);
            return result.NewRoot?.ToFullString() ?? source;
        }

        private string Normalize(string code)
        {
            return code.Replace("\r", "").Replace("\n", "").Replace(" ", "").Replace("\t", "");
        }

        [Fact]
        public async Task DeleteMethod_WhenNoInheritanceAndNameMatches()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            Assert.DoesNotContain("DrawMe", result);
            Assert.Contains("KeepMe", result);
        }

        [Fact]
        public async Task ClearBody_WhenInheritsFromBase()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            var normalized = Normalize(result);
            Assert.Contains("publicvoidDrawMe(){}", normalized);
        }

        [Fact]
        public async Task ClearBody_WhenImplementsInterface()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            var normalized = Normalize(result);
            Assert.Contains("publicvoidDrawMe(){}", normalized);
        }

        [Fact]
        public async Task ClearBody_WhenIsPotentialParent()
        {
            // �?sealed 类被视为潜在父类
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            var normalized = Normalize(result);
            Assert.Contains("publicvoidDrawMe(){}", normalized);
        }

        [Fact]
        public async Task SkipInterfaceMethods()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            Assert.Contains("void DrawMe();", result);
        }

        [Fact]
        public async Task SkipAbstractMethods()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            Assert.Contains("public abstract void DrawMe();", result);
        }

        [Fact]
        public async Task ClearBody_WithReturnValue_Object()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            var normalized = Normalize(result);
            Assert.Contains("publicobjectDrawMe(){returnnull;}", normalized);
        }

        [Fact]
        public async Task ClearBody_WithReturnValue_ValueType()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            var normalized = Normalize(result);
            Assert.Contains("publicintDrawInt(){return0;}", normalized);
            Assert.Contains("publicboolDrawBool(){returnfalse;}", normalized);
        }

        [Fact]
        public async Task ClearBody_WithOutParameters()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            var normalized = Normalize(result);
            Assert.Contains("x=0;", normalized);
            Assert.Contains("s=string.Empty;", normalized);
        }

        [Fact]
        public async Task ClearBody_WithGenericReturnValue()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            var normalized = Normalize(result);
            Assert.Contains("publicTDrawMe<T>(){returndefault(T);}", normalized);
        }

        [Fact]
        public async Task ClearBody_AsyncTask()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            var normalized = Normalize(result);
            Assert.Contains("publicasyncTaskDrawAsync(){returnSystem.Threading.Tasks.Task.CompletedTask;}", normalized);
        }

        [Fact]
        public async Task ClearBody_AsyncTaskT()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            var normalized = Normalize(result);
            Assert.Contains("publicasyncTask<int>DrawValueAsync(){returnSystem.Threading.Tasks.Task.FromResult<int>(0);}", normalized);
        }

        [Fact]
        public async Task DeleteStaticMethod_InSealedClass()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            Assert.DoesNotContain("DrawStatic", result);
        }

        [Fact]
        public async Task ClearBody_StaticMethod_InNonSealedClass()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            var normalized = Normalize(result);
            Assert.Contains("publicstaticvoidDrawStatic(){}", normalized);
        }

        [Fact]
        public async Task ClearBody_ExtensionMethod()
        {
            // 静态类被视为非密封（因为它�?Roslyn 中可能不被标记为 IsSealed，或者我们需要显式处理）
            // 实际�?static class �?C# 中是 abstract sealed�?
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            var normalized = Normalize(result);
            Assert.Contains("publicstaticvoidDrawMe(thisstrings){}", normalized);
        }

        [Fact]
        public async Task DeleteMethod_InNestedSealedClass()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            Assert.DoesNotContain("DrawInner", result);
        }

        [Fact]
        public async Task ClearBody_StructMethod()
        {
            // Structs are sealed by default
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            var normalized = Normalize(result);
            // 虽然 struct 是密封的，但它可能实现接口。目前实现中，如果没有继�?object 以外的基类且没有接口，会被删除�?
            // �?IsInheritanceRelated 检查了 AllInterfaces�?
            Assert.DoesNotContain("DrawMe", result);
        }

        [Fact]
        public async Task ClearBody_StructImplementingInterface()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            var normalized = Normalize(result);
            Assert.Contains("publicvoidDrawMe(){}", normalized);
        }

        [Fact]
        public async Task ClearBody_ExplicitInterfaceImplementation()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            var normalized = Normalize(result);
            Assert.Contains("voidI.DrawMe(){}", normalized);
        }

        [Fact]
        public async Task ClearBody_WithRefParameter()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            var normalized = Normalize(result);
            Assert.Contains("publicvoidDraw(refintx){}", normalized);
        }

        [Fact]
        public async Task ClearBody_WithParams()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            var normalized = Normalize(result);
            Assert.Contains("publicvoidDraw(paramsint[]args){}", normalized);
        }

        [Fact]
        public async Task ClearBody_WithDefaultValue()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            var normalized = Normalize(result);
            Assert.Contains("publicvoidDraw(intx=1){}", normalized);
        }

        [Fact]
        public async Task ClearBody_RecordMethod()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            var normalized = Normalize(result);
            // record 默认非密�?
            Assert.Contains("publicvoidDrawMe(){}", normalized);
        }

        [Fact]
        public async Task ClearBody_MultipleMatches()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            var normalized = Normalize(result);
            Assert.Contains("publicvoidDraw1(){}", normalized);
            Assert.Contains("publicvoidDraw2(){}", normalized);
        }

        [Fact]
        public async Task ClearBody_PartialClass()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            var normalized = Normalize(result);
            Assert.Contains("publicvoidDrawPart1(){}", normalized);
            Assert.Contains("publicvoidKeepPart2(){}", normalized);
        }

        [Fact]
        public async Task ClearBody_WithGenericConstraints()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            var normalized = Normalize(result);
            Assert.Contains("publicvoidDraw<T>()whereT:class,new(){}", normalized);
        }

        [Fact]
        public async Task ClearBody_ReturnIEnumerable()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            var normalized = Normalize(result);
            Assert.Contains("publicIEnumerable<int>DrawList(){returnSystem.Linq.Enumerable.Empty<int>();}", normalized);
        }

        [Fact]
        public async Task ClearBody_ReturnList()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            var normalized = Normalize(result);
            Assert.Contains("publicList<int>DrawList(){returnnewSystem.Collections.Generic.List<int>();}", normalized);
        }

        [Fact]
        public async Task ClearBody_ReturnArray()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            var normalized = Normalize(result);
            Assert.Contains("publicint[]DrawArray(){returnnewint[0];}", normalized);
        }

        [Fact]
        public async Task ClearBody_ReturnValueTuple()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            var normalized = Normalize(result);
            // ExpressionSimplifier 目前�?ValueTuple 可能返回 default((int, string))
            Assert.Contains("public(int,string)DrawTuple(){returndefault((int,string));}", normalized);
        }

        [Fact]
        public async Task ClearBody_UnsafeMethod()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            var normalized = Normalize(result);
            Assert.Contains("publicunsafevoidDrawPointer(int*p){}", normalized);
        }

        [Fact]
        public async Task ClearBody_MultiFile_BaseDerived()
        {
            var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var projectInfo = ProjectInfo.Create(projectId, VersionStamp.Create(), "TestProject", "TestAssembly", LanguageNames.CSharp);
            var project = workspace.AddProject(projectInfo);
            project = project.AddMetadataReferences(new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll"))
            });

            var baseSource = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_baseSource_1;
            var derivedSource = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_derivedSource_1;

            project = project.AddDocument("Base.cs", baseSource).Project;
            project = project.AddDocument("Derived.cs", derivedSource).Project;

            var refactorer = new NameBasedMethodRefactorer(project.Solution, "Draw");
            var result = await refactorer.ProcessFileAsync("Derived.cs", useFileLock: false);
            var root = result.NewRoot;
            var normalized = Normalize(root!.ToFullString());

            Assert.Contains("publicvoidDrawMe(){}", normalized);
        }

        [Fact]
        public async Task Skip_Static_Main_EvenIfPatternMatches()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source, "Main");
            var normalized = Normalize(result);
            // Main should be preserved (skipped)
            Assert.Contains("publicstaticvoidMain(string[]args){System.Console.WriteLine(1);}", normalized);
            // DrawMe matches pattern "Main" (no, it doesn't, Name == "DrawMe", Pattern == "Main").
            // Regex is used. new Regex(namePattern, RegexOptions.IgnoreCase).
            // "DrawMe" does not contain "Main".
            // Let's use pattern "Program" or something that matches both?
            // No, the test is to ensure Main is skipped even if it MATCHES.
            // So pattern "Main" matches "Main".
            // "DrawMe" is not matched.
            Assert.Contains("publicvoidDrawMe(){System.Console.WriteLine(2);}", normalized);
        }

        [Fact]
        public async Task ClearBody_WhenReferenced_Externally()
        {
            var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var projectInfo = ProjectInfo.Create(projectId, VersionStamp.Create(), "TestProject", "TestAssembly", LanguageNames.CSharp);
            var project = workspace.AddProject(projectInfo);
            project = project.AddMetadataReferences(new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll"))
            });

            var callerSource = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_callerSource_1;
            var targetSource = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_targetSource_1;

            project = project.AddDocument("Caller.cs", callerSource).Project;
            project = project.AddDocument("Target.cs", targetSource).Project;

            var refactorer = new NameBasedMethodRefactorer(project.Solution, "Draw");
            var result = await refactorer.ProcessFileAsync("Target.cs", useFileLock: false);

            var normalized = Normalize(result.NewRoot!.ToFullString());
            // Referenced externally -> ClearBody
            Assert.Contains("publicvoidDrawMe(){}", normalized);
        }

        [Fact]
        public async Task ClearBody_Virtual()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            Assert.Contains("publicvirtualvoidDrawMe(){}", Normalize(result));
        }

        [Fact]
        public async Task ClearBody_Override()
        {
            string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.NameBasedMethodRefactorerTests_Source_1;
            var result = await RunRefactorerAsync(source);
            Assert.Contains("publicoverridevoidDrawMe(){}", Normalize(result));
        }
    }
}




