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
    public class ClassRefactorerDataDrivenTests
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

        private async Task<Dictionary<string, string>> RunRefactorerAsync(string source, string fileName = "TestFile.cs")
        {
            var (workspace, project, document) = await CreateProjectAsync(source, fileName);
            var results = new Dictionary<string, string>();
            results[fileName] = source;

            bool changed;
            do
            {
                changed = false;
                var currentSolution = workspace.CurrentSolution;
                var refactorer = new ClassRefactorer(currentSolution);
                var result = await refactorer.ProcessFileAsync(fileName);

                if (result.AnyChanged && result.NewRoot != null)
                {
                    results[fileName] = result.NewRoot.ToFullString();
                    var doc = workspace.CurrentSolution.GetDocument(result.DocumentId!);
                    if (doc != null)
                    {
                        var newSolution = workspace.CurrentSolution.WithDocumentSyntaxRoot(doc.Id, result.NewRoot);
                        if (workspace.TryApplyChanges(newSolution))
                        {
                            changed = true;
                        }
                    }
                }
            } while (changed);

            return results;
        }

        [Theory]
        [MemberData(nameof(GetClassRefactoringTestCases))]
        public async Task ClassRefactoring_TestCase(string name, string source, string expectedInOutput, string notExpectedInOutput)
        {
            var results = await RunRefactorerAsync(source);
            var output = results["TestFile.cs"];

            if (!string.IsNullOrEmpty(expectedInOutput))
                Assert.Contains(expectedInOutput, output);
            if (!string.IsNullOrEmpty(notExpectedInOutput))
                Assert.DoesNotContain(notExpectedInOutput, output);
        }

        public static IEnumerable<object[]> GetClassRefactoringTestCases()
        {
            var cases = new List<object[]>();

            // 格式: { name, source, expectedInOutput, notExpectedInOutput }

            // 1. public class - none usage -> Delete
            cases.Add(new object[] { "PublicClass_NoneUsage", "public class Target { }", "", "class Target" });

            // 2. internal static class - static member access -> Keep
            cases.Add(new object[] { "InternalStaticClass_StaticAccess", "internal static class Target { public static int X; } static class Usage { void M() => Target.X = 1; }", "class Target", "" });

            // 3. private class (nested) - nested usage -> Keep
            cases.Add(new object[] { "PrivateNestedClass_Usage", "static class Outer { private class Target { } void M() { var t = new Target(); } }", "class Target", "" });

            // 4. public abstract class - inheritance usage -> Keep
            cases.Add(new object[] { "PublicAbstractClass_Inheritance", "public abstract class Target { } class Derived : Target { } static class Usage { void M() { var d = new Derived(); } }", "class Target", "" });

            // 5. internal interface - generic argument -> Keep
            cases.Add(new object[] { "InternalInterface_GenericArg", "internal interface Target { } class G<T> { } static class Usage { G<Target> g; }", "interface Target", "" });

            // 6. public struct - type reference (field) -> Keep
            cases.Add(new object[] { "PublicStruct_FieldRef", "public struct Target { } static class Usage { Target t; }", "struct Target", "" });

            // 7. internal enum - type reference (param) -> Keep
            cases.Add(new object[] { "InternalEnum_ParamRef", "internal enum Target { A } static class Usage { void M(Target t) { } }", "enum Target", "" });

            // 8. public delegate - type reference (return) -> Keep
            cases.Add(new object[] { "PublicDelegate_ReturnRef", "public delegate void Target(); static class Usage { Target M() => null; }", "delegate void Target", "" });

            // 9. internal class - attribute usage -> Keep
            cases.Add(new object[] { "InternalClass_AttributeUsage", "using System; internal class TargetAttribute : Attribute { } [Target] static class Usage { }", "class TargetAttribute", "" });

            // 10. public static class - none usage -> Delete
            cases.Add(new object[] { "PublicStaticClass_NoneUsage", "public static class Target { }", "", "class Target" });

            // 11. internal abstract class - none usage -> Delete
            cases.Add(new object[] { "InternalAbstractClass_NoneUsage", "internal abstract class Target { }", "", "class Target" });

            // 12. public interface - none usage -> Delete
            cases.Add(new object[] { "PublicInterface_NoneUsage", "public interface Target { }", "", "interface Target" });

            // 13. internal struct - none usage -> Delete
            cases.Add(new object[] { "InternalStruct_NoneUsage", "internal struct Target { }", "", "struct Target" });

            // 14. public enum - none usage -> Delete
            cases.Add(new object[] { "PublicEnum_NoneUsage", "public enum Target { A }", "", "enum Target" });

            // 15. internal delegate - none usage -> Delete
            cases.Add(new object[] { "InternalDelegate_NoneUsage", "internal delegate void Target();", "", "delegate void Target" });

            // 16. public class - instantiation usage -> Keep
            cases.Add(new object[] { "PublicClass_Instantiation", "public class Target { } static class Usage { void M() { var t = new Target(); } }", "class Target", "" });

            // 17. generic class - type argument -> Keep
            cases.Add(new object[] { "GenericClass_TypeArgument", "public class G<T> { } public class Target { } static class Usage { G<Target> g; }", "class Target", "" });

            // 18. partial class - partial usage -> Keep
            cases.Add(new object[] { "PartialClass_Usage", "static class Container { partial class Target { } partial class Target { void M() { } } void Use() { new Target(); } }", "class Target", "" });

            // 20. public class - no usage -> Remove
            cases.Add(new object[] { "PublicClass_NoUsage", "public class Target { }", "", "class Target" });

            // 21. public class - same file usage -> Keep
            cases.Add(new object[] { "PublicClass_SameFileUsage", "public class Target { } static class Usage { void M() { var t = new Target(); } }", "class Target", "" });

            // 19. nested class - deep inheritance -> Keep
            cases.Add(new object[] { "NestedClass_Inheritance", "class Outer { public class Base { } public class Target : Base { } } static class Usage : Outer.Target { }", "class Target", "" });

            return cases;
        }
    }
}
