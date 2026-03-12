using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace TerrariaTools.Dome
{
    /// <summary>
    /// SymbolFinder API 演示
    /// 展示如何使用 SymbolFinder 进行跨文件/跨语句的引用追踪。
    /// </summary>
    public static class SymbolFinderDemo
    {
        public static async Task RunAsync()
        {
            Console.WriteLine("=== 启动 SymbolFinder 引用追踪 Demo ===");

            // 1. 构建多文件示例代码
            string codeFile1 = @"
using System;
namespace Demo
{
    public class Service
    {
        public void Execute()
        {
            Console.WriteLine(""Executing..."");
        }
    }
}";
            string codeFile2 = @"
using System;
namespace Demo
{
    public class Client
    {
        public void Run()
        {
            var service = new Service();
            service.Execute(); // 引用点 1

            CallService(service);
        }

        private void CallService(Service s)
        {
            s.Execute(); // 引用点 2
        }
    }
}";

            // 2. 创建 AdhocWorkspace 和 Project
            using var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var versionStamp = VersionStamp.Create();
            var projectInfo = ProjectInfo.Create(projectId, versionStamp, "SymbolFinderDemo", "SymbolFinderDemo", LanguageNames.CSharp)
                .WithMetadataReferences(new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });

            var project = workspace.AddProject(projectInfo);
            var document1 = workspace.AddDocument(project.Id, "Service.cs", Microsoft.CodeAnalysis.Text.SourceText.From(codeFile1));
            var document2 = workspace.AddDocument(project.Id, "Client.cs", Microsoft.CodeAnalysis.Text.SourceText.From(codeFile2));

            // 更新 Project 获取最新 Compilation
            project = document2.Project;
            var compilation = await project.GetCompilationAsync();

            // 3. 查找目标符号 (Service.Execute 方法)
            var serviceClass = compilation.GetTypeByMetadataName("Demo.Service");
            var executeMethod = serviceClass.GetMembers("Execute").FirstOrDefault();

            if (executeMethod == null)
            {
                Console.WriteLine("未找到目标方法 Service.Execute");
                return;
            }

            Console.WriteLine($"正在查找符号 {executeMethod.ToDisplayString()} 的所有引用...");

            // 4. 使用 SymbolFinder 查找引用
            var references = await SymbolFinder.FindReferencesAsync(executeMethod, project.Solution);

            foreach (var referencedSymbol in references)
            {
                foreach (var location in referencedSymbol.Locations)
                {
                    var doc = location.Document;
                    var span = location.Location.SourceSpan;
                    var lineSpan = location.Location.GetLineSpan();

                    Console.WriteLine($"[引用发现] 文档: {doc.Name}");
                    Console.WriteLine($"  - 位置: 行 {lineSpan.StartLinePosition.Line + 1}, 列 {lineSpan.StartLinePosition.Character + 1}");

                    // 获取引用处的源代码片段
                    var sourceText = await doc.GetTextAsync();
                    var lineText = sourceText.Lines[lineSpan.StartLinePosition.Line].ToString().Trim();
                    Console.WriteLine($"  - 代码: {lineText}");
                    Console.WriteLine();
                }
            }

            Console.WriteLine("=== SymbolFinder 演示结束 ===");
        }
    }
}
