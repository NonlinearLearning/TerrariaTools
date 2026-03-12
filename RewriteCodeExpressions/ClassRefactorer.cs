using Microsoft.CodeAnalysis;
using TerrariaTools.RewriteCodeExpressions.Pipeline;

namespace TerrariaTools.RewriteCodeExpressions
{
    /// <summary>
    /// 类重构器：负责将类中的特定成员重写或简化。
    /// 目前主要通过 PipelineExpressionSimplifier (Hybrid) 统一处理。
    /// </summary>
    public class ClassRefactorer
    {
        private readonly Solution _solution;

        /// <summary>
        /// 初始化 ClassRefactorer 的新实例。
        /// </summary>
        /// <param name="solution">要分析的解决方案。</param>
        public ClassRefactorer(Solution solution)
        {
            _solution = solution;
        }

        /// <summary>
        /// 异步处理指定文件名的语法重写。
        /// </summary>
        /// <param name="fileName">要处理的文件名。</param>
        /// <returns>重写后的语法节点，如果未找到文件则返回 null。</returns>
        public async Task<SyntaxNode?> ProcessFileAsync(string fileName)
        {
            var document = _solution.Projects.SelectMany(p => p.Documents).FirstOrDefault(d => d.Name == fileName);
            if (document == null) return null;

            var model = await document.GetSemanticModelAsync();
            var root = await document.GetSyntaxRootAsync();
            if (root == null || model == null) return root;

            return await PipelineExpressionSimplifier.RewriteAsync(
                root,
                model,
                _solution,
                _ => false,
                useHybrid: true);
        }
    }
}
