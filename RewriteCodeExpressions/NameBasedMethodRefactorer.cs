using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TerrariaTools.RewriteCodeExpressions.Pipeline;

namespace TerrariaTools.RewriteCodeExpressions
{
    /// <summary>
    /// 基于名称的方法重构器：通过方法名正则表达式模式来批量处理方法重构。
    /// 目前主要通过 PipelineExpressionSimplifier (Hybrid) 统一处理。
    /// </summary>
    public class NameBasedMethodRefactorer
    {
        private readonly Solution _solution;
        private readonly string _namePattern;

        /// <summary>
        /// 初始化 NameBasedMethodRefactorer 的新实例。
        /// </summary>
        /// <param name="solution">要分析的解决方案。</param>
        /// <param name="namePattern">用于匹配方法名的正则表达式模式。</param>
        public NameBasedMethodRefactorer(Solution solution, string namePattern)
        {
            _solution = solution;
            _namePattern = namePattern;
        }

        /// <summary>
        /// 异步处理整个解决方案的重构。
        /// </summary>
        /// <returns>解决方案级别的重构结果。</returns>
        public async Task<SolutionRefactoringResult> ProcessSolutionAsync()
        {
            var result = new SolutionRefactoringResult();
            foreach (var project in _solution.Projects)
            {
                foreach (var document in project.Documents)
                {
                    var fileResult = await ProcessFileAsync(document.Name);
                    if (fileResult.NewRoot != null)
                    {
                        result.TotalRefactoredFiles++;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 异步处理单个文件的重构。
        /// </summary>
        /// <param name="fileName">要处理的文件名。</param>
        /// <param name="useFileLock">是否使用文件锁（目前未使用）。</param>
        /// <returns>文件级别的重构结果。</returns>
        public async Task<FileRefactoringResult> ProcessFileAsync(string fileName, bool useFileLock = true)
        {
            var document = _solution.Projects.SelectMany(p => p.Documents).FirstOrDefault(d => d.Name == fileName);
            if (document == null)
            {
                return new FileRefactoringResult();
            }

            var model = await document.GetSemanticModelAsync();
            var root = await document.GetSyntaxRootAsync();
            if (root == null || model == null)
            {
                return new FileRefactoringResult();
            }

            var nodesToMark = await CollectCrossFileReferencedMatchedMethodsAsync(document, root, model);

            var newRoot = await PipelineExpressionSimplifier.RewriteAsync(
                root,
                model,
                _solution,
                _ => false,
                nodesToMark,
                useHybrid: true,
                namePattern: _namePattern,
                deleteMatched: true,
                clearBodyMatched: false);

            return new FileRefactoringResult { NewRoot = newRoot };
        }

        /// <summary>
        /// 收集跨文件引用的、符合名称模式的方法节点。
        /// </summary>
        /// <param name="document">包含语法树的文档。</param>
        /// <param name="root">语法树根节点。</param>
        /// <param name="model">语法树的语义模型。</param>
        /// <returns>被标记的语法节点集合。</returns>
        private async Task<HashSet<SyntaxNode>> CollectCrossFileReferencedMatchedMethodsAsync(
            Document document,
            SyntaxNode root,
            SemanticModel model)
        {
            var result = new HashSet<SyntaxNode>();
            var regex = new Regex(_namePattern, RegexOptions.IgnoreCase);
            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

            foreach (var method in methods)
            {
                if (!regex.IsMatch(method.Identifier.Text))
                {
                    continue;
                }

                var symbol = model.GetDeclaredSymbol(method);
                if (symbol == null)
                {
                    continue;
                }

                var references = await Microsoft.CodeAnalysis.FindSymbols.SymbolFinder.FindReferencesAsync(symbol, _solution);
                var hasExternalRef = references
                    .SelectMany(r => r.Locations)
                    .Any(location => location.Document.Id != document.Id);

                if (hasExternalRef)
                {
                    result.Add(method);
                }
            }

            return result;
        }
    }

    public class FileRefactoringResult
    {
        public SyntaxNode? NewRoot { get; set; }
    }
}
