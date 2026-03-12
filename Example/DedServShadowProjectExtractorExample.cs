using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using TerrariaTools.Analysis;
using TerrariaTools.Services;

namespace Example
{
    /// <summary>
    /// 提取 DedServ 最小影子项目。
    /// 实现了从DedServ 依赖链提取最小影子项目的需求。
    /// </summary>
    public class DedServShadowProjectExtractorExample : ITool
    {
        private readonly IWorkspaceLoader _loader;
        private readonly Microsoft.Extensions.Options.IOptions<TerrariaTools.Configuration.RefactoringSettings> _settings;

        public DedServShadowProjectExtractorExample(IWorkspaceLoader loader, Microsoft.Extensions.Options.IOptions<TerrariaTools.Configuration.RefactoringSettings> settings)
        {
            _loader = loader;
            _settings = settings;
        }

        public string Name => "影子项目提取";
        public string Description => "基于DedServ依赖链提取最小化影子项目到TR目录。";

        public async Task RunAsync(string? solutionPath = null)
        {
            // 1. 确定路径
            if (string.IsNullOrEmpty(solutionPath))
            {
                solutionPath = @"d:\ProjectItem\SourceCode\Net\TerrariaTools\TR\TerrariaServer.sln";
                // solutionPath = _settings.Value.DefaultSolutionPath;
                if (string.IsNullOrEmpty(solutionPath))
                {
                    Console.WriteLine("请输入解决方案路径:");
                    solutionPath = Console.ReadLine();
                }
            }

            if (string.IsNullOrEmpty(solutionPath) || !File.Exists(solutionPath))
            {
                Console.WriteLine($"[错误] 找不到解决方案文件: {solutionPath}");
                return;
            }

            string outputDir = Path.Combine(Directory.GetCurrentDirectory(), "TR_DedServ");
            if (Directory.Exists(outputDir))
            {
                Console.WriteLine($"[清理] 正在清理现有输出目录: {outputDir}");
                Directory.Delete(outputDir, true);
            }
            Directory.CreateDirectory(outputDir);

            // 2. 加载解决方案
            Console.WriteLine($"[加载] 正在加载解决方案: {solutionPath}");
            var solution = await _loader.LoadSolutionAsync(solutionPath);
            if (solution == null)
            {
                Console.WriteLine("[错误] 解决方案加载失败。");
                return;
            }

            // 3. 寻找 DedServ 种子方法
            Console.WriteLine("[搜索] 正在寻找种子方法: Terraria.Main.DedServ...");
            var analyzer = new CallChainAnalyzer(solution);
            var seeds = await analyzer.FindMethodSymbolAsync("Terraria.Main.DedServ");
            if (!seeds.Any())
            {
                Console.WriteLine("[错误] 未能在项目中找到 Terraria.Main.DedServ 方法。");
                return;
            }
            var seed = seeds.First();

            // 4. 生成影子源码
            Console.WriteLine("[分析] 正在执行深度依赖分析并生成影子源码...");
            var generator = new ShadowClassGenerator(solution);
            var shadowFiles = await generator.GenerateShadowSourceAsync(seed);

            // 5. 复制整个项目结构 (非代码文件)
            var project = solution.Projects.FirstOrDefault(); // 假设主项目
            if (project == null) return;

            string? projectDir = Path.GetDirectoryName(project.FilePath);
            if (string.IsNullOrEmpty(projectDir))
            {
                Console.WriteLine("[错误] 无法确定项目根目录。");
                return;
            }

            Console.WriteLine($"[同步] 正在复制原始项目文件到 {outputDir}...");
            CopyDirectory(projectDir, outputDir, ".cs"); // 排除 .cs 文件，后续由影子源码覆盖

            // 6. 写入生成的影子代码
            Console.WriteLine($"[写入] 正在写入 {shadowFiles.Count} 个精简后的代码文件...");
            foreach (var kvp in shadowFiles)
            {
                string originalPath = kvp.Key;
                string relativePath = Path.GetRelativePath(projectDir, originalPath);
                string targetPath = Path.Combine(outputDir, relativePath);

                string? targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                await File.WriteAllTextAsync(targetPath, kvp.Value);
            }

            Console.WriteLine("\n" + new string('=', 50));
            Console.WriteLine("             提取完成");
            Console.WriteLine(new string('=', 50));
            Console.WriteLine($"目标目录 : {outputDir}");
            Console.WriteLine($"代码文件 : {shadowFiles.Count} 个");
            Console.WriteLine("提示: 该目录现在包含一个仅保留 DedServ 必要逻辑的最小化项目环境。");
        }

        private void CopyDirectory(string sourceDir, string destDir, string excludeExtension)
        {
            // 复制目录结构和非排除文件
            foreach (string dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                // 忽略一些常见的非项目目录
                if (dirPath.Contains("\\bin\\") || dirPath.Contains("\\obj\\") || dirPath.Contains("\\.git") || dirPath.Contains("\\.vs"))
                    continue;

                Directory.CreateDirectory(dirPath.Replace(sourceDir, destDir));
            }

            foreach (string newPath in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
            {
                if (newPath.EndsWith(excludeExtension, StringComparison.OrdinalIgnoreCase))
                    continue;

                // 忽略一些常见的非项目文件
                if (newPath.Contains("\\bin\\") || newPath.Contains("\\obj\\") || newPath.Contains("\\.git") || newPath.Contains("\\.vs"))
                    continue;

                string targetPath = newPath.Replace(sourceDir, destDir);
                string? targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                File.Copy(newPath, targetPath, true);
            }
        }
    }
}
