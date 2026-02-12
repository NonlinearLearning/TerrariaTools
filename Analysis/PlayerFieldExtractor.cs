using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Analysis
{
    /// <summary>
    /// 分析 MessageBuffer.cs 以提取 GetData 方法中引用的 Player 字段。
    /// </summary>
    public class PlayerFieldExtractor
    {
        public class AnalysisResult
        {
            public HashSet<string> AllPlayerFields { get; set; } = new HashSet<string>();
            public HashSet<string> ReferencedFields { get; set; } = new HashSet<string>();
            public List<string> MissingFields { get; set; } = new List<string>();
        }

        /// <summary>
        /// 执行分析。
        /// </summary>
        /// <param name="sourcePaths">包含 Player 及其基类定义的文件路径列表。</param>
        /// <param name="messageBufferPath">MessageBuffer.cs 的路径。</param>
        /// <returns>分析结果。</returns>
        public AnalysisResult Analyze(IEnumerable<string> sourcePaths, string messageBufferPath)
        {
            var result = new AnalysisResult();

            // 1. 加载并提取所有给定类中的公共字段和属性
            foreach (var path in sourcePaths)
            {
                var fields = ExtractAllFields(path);
                foreach (var f in fields) result.AllPlayerFields.Add(f);
            }

            // 2. 加载 MessageBuffer 并提取 GetData 中的引用
            var references = ExtractReferencedFieldsFromMessageBuffer(messageBufferPath);

            // 3. 确定哪些引用确实属于 Player (或其基类)
            foreach (var field in references)
            {
                if (result.AllPlayerFields.Contains(field))
                {
                    result.ReferencedFields.Add(field);
                }
                else
                {
                    result.MissingFields.Add(field);
                }
            }

            return result;
        }

        private HashSet<string> ExtractAllFields(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException(path);

            string source = File.ReadAllText(path);
            SyntaxTree tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetCompilationUnitRoot();

            var classDecls = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

            HashSet<string> members = new HashSet<string>();

            foreach (var cls in classDecls)
            {
                var fields = cls.DescendantNodes().OfType<FieldDeclarationSyntax>()
                    .SelectMany(f => f.Declaration.Variables)
                    .Select(v => v.Identifier.Text);

                var properties = cls.DescendantNodes().OfType<PropertyDeclarationSyntax>()
                    .Select(p => p.Identifier.Text);

                foreach (var f in fields) members.Add(f);
                foreach (var p in properties) members.Add(p);
            }

            return members;
        }

        private HashSet<string> ExtractReferencedFieldsFromMessageBuffer(string messageBufferPath)
        {
            if (!File.Exists(messageBufferPath)) throw new FileNotFoundException(messageBufferPath);

            string source = File.ReadAllText(messageBufferPath);
            SyntaxTree tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetCompilationUnitRoot();

            var getDataMethod = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.Text == "GetData");

            if (getDataMethod == null) return new HashSet<string>();

            var switchStatement = getDataMethod.DescendantNodes()
                .OfType<SwitchStatementSyntax>()
                .FirstOrDefault();

            if (switchStatement == null) return new HashSet<string>();

            HashSet<string> references = new HashSet<string>();

            foreach (var section in switchStatement.Sections)
            {
                // 识别 Player 变量
                var playerVars = section.DescendantNodes()
                    .OfType<VariableDeclaratorSyntax>()
                    .Where(v => IsPlayerType(v))
                    .Select(v => v.Identifier.Text)
                    .ToList();

                var memberAccesses = section.DescendantNodes()
                    .OfType<MemberAccessExpressionSyntax>();

                foreach (var access in memberAccesses)
                {
                    string expr = access.Expression.ToString();
                    if (playerVars.Contains(expr) || IsDirectPlayerAccess(access))
                    {
                        references.Add(access.Name.Identifier.Text);
                    }
                }
            }

            return references;
        }

        private bool IsPlayerType(VariableDeclaratorSyntax declarator)
        {
            var declaration = declarator.Parent as VariableDeclarationSyntax;
            if (declaration == null) return false;

            string typeName = declaration.Type.ToString();
            if (typeName == "Player") return true;

            if (typeName == "var" && declarator.Initializer?.Value.ToString().Contains("Main.player") == true)
                return true;

            return false;
        }

        private bool IsDirectPlayerAccess(MemberAccessExpressionSyntax access)
        {
            string expr = access.Expression.ToString();
            return expr.StartsWith("Main.player[") || expr.Contains(".player[");
        }
    }
}
