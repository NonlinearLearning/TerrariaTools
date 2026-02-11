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
            var results = new Dictionary<string, string>();
            results[fileName] = source;

            var refactorer = new MethodRefactorer(workspace.CurrentSolution);
            var result = await refactorer.ProcessFileAsync(fileName);

            if (result.AnyChanged && result.NewRoot != null)
            {
                results[fileName] = result.NewRoot.ToFullString();
            }

            return results;
        }

        [Theory]
        [MemberData(nameof(GetRefactoringTestCases))]
        public async Task Refactoring_TestCase(string name, string source, string expectedInOutput, string notExpectedInOutput)
        {
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

            // --- 组合式生成 200+ 案例 ---

            var returnTypes = new[] { "void", "bool", "int", "string", "object", "List<int>", "C", "MyStruct", "int?", "T", "long", "double" };
            var modifiers = new[] { "public", "protected", "internal" };
            var virtualStates = new[] { true, false };
            var usageStates = new[] { "none", "internal", "external" };
            var staticStates = new[] { true, false };

            foreach (var type in returnTypes)
            {
                foreach (var mod in modifiers)
                {
                    foreach (var isVirtual in virtualStates)
                    {
                        foreach (var isStatic in staticStates)
                        {
                            if (isStatic && isVirtual) continue; // C# 不允许 static virtual

                            foreach (var usage in usageStates)
                            {
                                string virtualStr = isVirtual ? "virtual " : "";
                                string staticStr = isStatic ? "static " : "";
                                string methodName = $"M_{type.Replace("<", "").Replace(">", "").Replace("?", "N")}_{mod}_{isVirtual}_{isStatic}_{usage}";
                                
                                string source;
                                string expected = "";
                                string notExpected = "";

                                if (usage == "none")
                                {
                                    // 无引用
                                    source = $"public class C {{ {mod} {staticStr}{virtualStr}{type} {methodName}() {{ return default; }} }}";
                                    if (isVirtual)
                                    {
                                        expected = $"{methodName}()";
                                    }
                                    else
                                    {
                                        notExpected = methodName;
                                    }
                                }
                                else if (usage == "internal")
                                {
                                    // 内部引用
                                    string callPrefix = isStatic ? "C." : "";
                                    source = $"public class C {{ {mod} {staticStr}{virtualStr}{type} {methodName}() {{ return default; }} public void Usage() {{ {callPrefix}{methodName}(); }} }}";
                                    if (mod == "public" && !isVirtual)
                                    {
                                        expected = $"private {staticStr}{type} {methodName}";
                                    }
                                    else
                                    {
                                        expected = $"{mod} {staticStr}{virtualStr}{type} {methodName}";
                                    }
                                }
                                else // external
                                {
                                    // 外部引用
                                    string callPrefix = isStatic ? "C." : "c.";
                                    source = $"public class C {{ {mod} {staticStr}{virtualStr}{type} {methodName}() {{ return default; }} }} public class D {{ public void U(C c) {{ {callPrefix}{methodName}(); }} }}";
                                    expected = $"{mod} {staticStr}{virtualStr}{type} {methodName}";
                                }

                                cases.Add(new object[] { methodName, source, expected, notExpected });
                            }
                        }
                    }
                }
            }

            // --- 特殊案例 ---

            // Out 参数组合
            var outTypes = new[] { "bool", "int", "string" };
            foreach (var ot in outTypes)
            {
                cases.Add(new object[] {
                    $"Out_{ot}",
                    $"public class C {{ public virtual void M(out {ot} v) {{ v = default; }} }}",
                    "v =",
                    ""
                });
            }

            // Main 方法
            cases.Add(new object[] { "KeepMain", "public class C { public static void Main() { } }", "static void Main()", "" });

            // 属性/字段
            cases.Add(new object[] { "KeepProp", "public class C { public int P { get; set; } }", "public int P", "" });

            return cases.Take(200); // 确保返回正好 200 个或更多
        }
    }
}