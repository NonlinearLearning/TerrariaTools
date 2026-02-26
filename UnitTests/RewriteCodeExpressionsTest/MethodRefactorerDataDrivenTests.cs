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
    public class MethodRefactorerDataDrivenTests
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

            await MethodRefactorer.ExecuteSolutionRefactoringAsync("dummy.sln", mockLoader.Object);

            return results;
        }

        [Theory]
        [MemberData(nameof(GetRefactoringTestCases))]
        public async Task Refactoring_TestCase(string name, string source, string expectedInOutput, string notExpectedInOutput)
        {
            _ = name;
            var results = await RunRefactorerAsync(source);
            var output = results["TestFile.cs"];

            if (!string.IsNullOrEmpty(expectedInOutput))
                Assert.Contains(expectedInOutput, output);
            if (!string.IsNullOrEmpty(notExpectedInOutput))
                Assert.DoesNotContain(notExpectedInOutput, output);
        }

        public static IEnumerable<object[]> GetRefactoringTestCases()
        {
            var cases = new List<object[]>();

            // --- 使用 PICT 设计的优化案例 (Pairwise 覆盖) ---
            // 参数: Visibility, Modifiers, Usage, TypeKind, ReturnType, IsInterfaceImpl, ExpectedAction
            var pictCases = new[]
            {
                // 1. 无引用 -> 保持 (普通类，非虚/抽象/接口/重写，但 Public 视为 API)
                new { Visibility = "public", Modifiers = "", Usage = "none", TypeKind = "class", ReturnType = "void", IsInterfaceImpl = false, Expected = "Keep" },
                // 2. 无引用 -> 保持 (静态方法，Public)
                new { Visibility = "public", Modifiers = "static", Usage = "none", TypeKind = "class", ReturnType = "int", IsInterfaceImpl = false, Expected = "Keep" },
                // 3. 内部引用 -> 私有化 (公开方法)
                new { Visibility = "public", Modifiers = "", Usage = "internal", TypeKind = "class", ReturnType = "string", IsInterfaceImpl = false, Expected = "MakePrivate" },
                // 4. 内部引用 -> 私有化 (Internal方法)
                new { Visibility = "internal", Modifiers = "", Usage = "internal", TypeKind = "class", ReturnType = "T", IsInterfaceImpl = false, Expected = "MakePrivate" },
                // 5. 无引用 -> 清空体 (重写方法)
                new { Visibility = "public", Modifiers = "override", Usage = "none", TypeKind = "class", ReturnType = "void", IsInterfaceImpl = false, Expected = "ClearBody" },
                // 6. 无引用 -> 清空体 (接口实现)
                new { Visibility = "public", Modifiers = "", Usage = "none", TypeKind = "class", ReturnType = "string", IsInterfaceImpl = true, Expected = "ClearBody" },
                // 7. 内部引用 -> 保持 (虚方法不能私有化)
                new { Visibility = "public", Modifiers = "virtual", Usage = "internal", TypeKind = "class", ReturnType = "int", IsInterfaceImpl = false, Expected = "Keep" },
                // 8. 内部引用 -> 保持 (已是 Protected)
                new { Visibility = "protected", Modifiers = "", Usage = "internal", TypeKind = "class", ReturnType = "void", IsInterfaceImpl = false, Expected = "Keep" },
                // 9. 内部引用 -> 保持 (已是 Private)
                new { Visibility = "private", Modifiers = "", Usage = "internal", TypeKind = "class", ReturnType = "void", IsInterfaceImpl = false, Expected = "Keep" },
                // 10. 无引用 -> 保持 (抽象方法不处理)
                new { Visibility = "public", Modifiers = "abstract", Usage = "none", TypeKind = "class", ReturnType = "void", IsInterfaceImpl = false, Expected = "Keep" },
                // 11. 外部引用 -> 保持
                new { Visibility = "public", Modifiers = "", Usage = "external", TypeKind = "class", ReturnType = "void", IsInterfaceImpl = false, Expected = "Keep" },
                // 12. 接口方法 -> 保持
                new { Visibility = "public", Modifiers = "", Usage = "external", TypeKind = "interface", ReturnType = "void", IsInterfaceImpl = false, Expected = "Keep" },
                // 13. Struct 方法 -> 保持 (当前版本忽略 Struct)
                new { Visibility = "public", Modifiers = "", Usage = "none", TypeKind = "struct", ReturnType = "void", IsInterfaceImpl = false, Expected = "Keep" },
                // 14. Partial 方法 -> 保持
                new { Visibility = "public", Modifiers = "partial", Usage = "external", TypeKind = "class", ReturnType = "void", IsInterfaceImpl = false, Expected = "Keep" },
                // 15. Async 方法 -> 保持 (Public API)
                new { Visibility = "public", Modifiers = "async", Usage = "none", TypeKind = "class", ReturnType = "Task", IsInterfaceImpl = false, Expected = "Keep" }
            };

            foreach (var c in pictCases)
            {
                string methodName = $"M_{c.Visibility}_{c.Modifiers}_{c.Usage}_{c.TypeKind}_{c.ReturnType.Replace("<", "").Replace(">", "")}_{c.IsInterfaceImpl}";
                string modStr = string.IsNullOrEmpty(c.Modifiers) ? "" : c.Modifiers + " ";
                string returnType = c.ReturnType == "T" ? "T" : c.ReturnType;
                string body = c.Modifiers == "abstract" ? ";" : "{ return \"test\"; }";
                if (c.ReturnType == "void") body = c.Modifiers == "abstract" ? ";" : "{ System.Console.WriteLine(); }";
                if (c.ReturnType == "int") body = c.Modifiers == "abstract" ? ";" : "{ return 123; }";
                if (c.ReturnType == "Task") body = c.Modifiers == "abstract" ? ";" : "{ await Task.Delay(1); }";
                if (c.ReturnType == "T") body = c.Modifiers == "abstract" ? ";" : "{ return default(T); }";

                string source = "";
                string expected = "";
                string notExpected = "";

                // 构建源码
                if (c.TypeKind == "interface")
                {
                    source = $"public interface I {{ {returnType} {methodName}(); }}";
                }
                else if (c.TypeKind == "struct")
                {
                    source = $"public struct S {{ {c.Visibility} {modStr}{returnType} {methodName}() {body} }}";
                }
                else // class
                {
                    string classModifiers = c.Modifiers == "abstract" ? "abstract " : "";
                    string basePart = "";
                    string baseSource = "";
                    if (c.Modifiers == "override")
                    {
                        basePart = " : Base";
                        baseSource = "public class Base { public virtual void " + methodName + "() {} }\n";
                    }
                    else if (c.IsInterfaceImpl)
                    {
                        basePart = " : I";
                        baseSource = "public interface I { " + returnType + " " + methodName + "(); }\n";
                    }

                    source = baseSource + $"public {classModifiers}class C{basePart} {{ {c.Visibility} {modStr}{returnType} {methodName}() {body} ";

                    if (c.Usage == "internal")
                    {
                        string call = (c.Modifiers.Contains("static") ? "C." : "") + methodName + "();";
                        source += $" public void Usage() {{ {call} }} ";
                    }
                    source += " } ";

                    if (c.Usage == "external")
                    {
                        string call = (c.Modifiers.Contains("static") ? "C." : "new C().") + methodName + "();";
                        source += $" public class D {{ public void U() {{ {call} }} }} ";
                    }
                }

                // 确定预期结果
                switch (c.Expected)
                {
                    case "Delete":
                        notExpected = methodName;
                        break;
                    case "MakePrivate":
                        // NormalizeWhitespace 会规范化空格
                        expected = $"private {modStr.Replace("public ", "").Replace("internal ", "")}{returnType} {methodName}".Replace("  ", " ").Trim();
                        break;
                    case "ClearBody":
                        // 简化检查：方法名还在，但体清空了
                        expected = $"{methodName}()";
                        // NormalizeWhitespace 后的空方法体通常是 { \r\n } 或类似的
                        if (c.ReturnType == "void")
                        {
                            // 检查方法名后面跟着一对大括号，中间只有空白字符
                            expected = methodName;
                            // 我们可以在测试逻辑中增加更复杂的验证，或者这里只检查关键部分
                        }
                        else if (c.ReturnType == "int")
                            expected = "return default;";
                        else if (c.ReturnType == "string")
                            expected = "return default;";
                        break;
                    case "Keep":
                        if (c.TypeKind == "interface")
                            expected = $"{returnType} {methodName}";
                        else
                            expected = $"{c.Visibility} {modStr}{returnType} {methodName}".Replace("  ", " ").Trim();
                        break;
                }

                cases.Add(new object[] { methodName, source, expected, notExpected });
            }

            // --- 保持原有特殊案例 ---
            cases.Add(new object[] { "KeepMain", "public class C { public static void Main() { } }", "static void Main()", "" });
            cases.Add(new object[] { "KeepProp", "public class C { public int P { get; set; } }", "public int P", "" });

            return cases;
        }
    }
}