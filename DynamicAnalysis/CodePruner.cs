using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.DynamicAnalysis
{
#pragma warning disable CS8602, CS8603, CS8714
    /// <summary>
    /// 根据调用链分析结果裁剪代码，删除未被使用的函数定义。
    /// </summary>
    public class CodePruner
    {
        private readonly string _solutionPath;
        private readonly Load _loader;
        private readonly SymbolDisplayFormat _displayFormat;

        public CodePruner(string solutionPath, Load loader)
        {
            _solutionPath = solutionPath;
            _loader = loader;
            _displayFormat = new SymbolDisplayFormat(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                memberOptions: SymbolDisplayMemberOptions.IncludeContainingType,
                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted
            );
        }

        /// <summary>
        /// 执行裁剪逻辑
        /// </summary>
        /// <param name="validFunctions">有效函数全名的集合</param>
        public async Task ExecutePruningAsync(HashSet<string> validFunctions)
        {
            if (validFunctions == null || validFunctions.Count == 0)
            {
                Console.WriteLine("[错误] 有效函数列表为空，取消裁剪。");
                return;
            }

            Console.WriteLine($"[信息] 准备裁剪，有效函数数量: {validFunctions.Count}");

            // 1. 加载解决方案
            Console.WriteLine($"[信息] 正在加载解决方案进行裁剪: {_solutionPath}");
            using var workspace = await _loader.LoadSolutionAsync(_solutionPath);
            if (workspace == null) return;

            var solution = workspace.CurrentSolution;
            var newSolution = solution;

            // 2. 遍历项目和文档
            foreach (var project in solution.Projects)
            {
                Console.WriteLine($"[信息] 正在处理项目: {project.Name}");
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                foreach (var document in project.Documents)
                {
                    if (document.FilePath == null || !document.FilePath.EndsWith(".cs")) continue;

                    var root = await document.GetSyntaxRootAsync();
                    var model = compilation.GetSemanticModel(await document.GetSyntaxTreeAsync());
                    if (root == null || model == null) continue;

                    // 识别要删除的节点
                    var rewriter = new PruningRewriter(model, validFunctions, _displayFormat);
                    var newRoot = rewriter.Visit(root);

                    if (newRoot != root)
                    {
                        newSolution = newSolution.WithDocumentSyntaxRoot(document.Id, newRoot);
                        Console.WriteLine($"[裁剪] 已更新文件: {document.Name}");
                    }
                }
            }

            // 3. 应用更改到磁盘
            if (newSolution != solution)
            {
                Console.WriteLine("[信息] 正在将更改应用到磁盘...");
                if (workspace.TryApplyChanges(newSolution))
                {
                    Console.WriteLine("[完成] 代码裁剪任务成功。");
                }
                else
                {
                    Console.WriteLine("[错误] 无法应用更改到磁盘。");
                }
            }
            else
            {
                Console.WriteLine("[信息] 未发现需要删除的函数。");
            }
        }

        /// <summary>
        /// 从日志文件读取起始函数
        /// </summary>
        public static HashSet<string> LoadFunctionsFromLog(string logPath)
        {
            var functions = new HashSet<string>();
            if (!File.Exists(logPath)) return functions;

            try
            {
                var lines = File.ReadAllLines(logPath);
                foreach (var line in lines)
                {
                    if (line.Contains("[ENTER]"))
                    {
                        var parts = line.Split(new[] { "[ENTER] " }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 1)
                        {
                            functions.Add(parts[1].Trim());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[错误] 读取日志失败: {ex.Message}");
            }
            return functions;
        }

        /// <summary>
        /// 从简单列表文件读取函数
        /// </summary>
        public static HashSet<string> LoadFunctionsFromList(string listPath)
        {
            if (!File.Exists(listPath)) return new HashSet<string>();
            return new HashSet<string>(File.ReadAllLines(listPath));
        }

        /// <summary>
        /// 语法树重写器，负责移除不在白名单中的方法定义。
        /// </summary>
        private class PruningRewriter : CSharpSyntaxRewriter
        {
            private readonly SemanticModel _model;
            private readonly HashSet<string> _validFunctions;
            private readonly SymbolDisplayFormat _displayFormat;

            public PruningRewriter(SemanticModel model, HashSet<string> validFunctions, SymbolDisplayFormat displayFormat)
            {
                _model = model;
                _validFunctions = validFunctions;
                _displayFormat = displayFormat;
            }

            public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
            {
                var symbol = _model.GetDeclaredSymbol(node);
                if (symbol != null)
                {
                    string fullName = symbol.ToDisplayString(_displayFormat);
                    if (!_validFunctions.Contains(fullName))
                    {
                        // 不在白名单中，删除该节点
                        return null;
                    }
                }
                return base.VisitMethodDeclaration(node);
            }

            public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
            {
                var symbol = _model.GetDeclaredSymbol(node);
                if (symbol != null)
                {
                    string fullName = symbol.ToDisplayString(_displayFormat);
                    if (!_validFunctions.Contains(fullName))
                    {
                        return null;
                    }
                }
                return base.VisitConstructorDeclaration(node);
            }

            public override SyntaxNode? VisitDestructorDeclaration(DestructorDeclarationSyntax node)
            {
                var symbol = _model.GetDeclaredSymbol(node);
                if (symbol != null)
                {
                    string fullName = symbol.ToDisplayString(_displayFormat);
                    if (!_validFunctions.Contains(fullName))
                    {
                        return null;
                    }
                }
                return base.VisitDestructorDeclaration(node);
            }

            // 注意：属性、字段等暂不删除，因为用户要求“只删除函数”
        }
    }
}
