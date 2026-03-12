using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TerrariaTools.Analysis;
using TerrariaTools.RewriteCodeExpressions.Pipeline;

namespace TerrariaTools.RewriteCodeExpressions
{
    /// <summary>
    /// 方法重构器：负责将方法及其调用点进行重写或简化。
    /// 目前主要通过 PipelineExpressionSimplifier (Hybrid) 统一处理。
    /// </summary>
    public class MethodRefactorer
    {
        private readonly Solution _solution;
        private readonly Dictionary<IMethodSymbol, FunctionBuildGraph.GraphMethodAction> _actions;

        /// <summary>
        /// 初始化 MethodRefactorer 的新实例。
        /// </summary>
        /// <param name="solution">要分析的解决方案。</param>
        /// <param name="actions">要应用于特定方法符号的操作映射。</param>
        public MethodRefactorer(Solution solution, Dictionary<IMethodSymbol, FunctionBuildGraph.GraphMethodAction> actions)
        {
            _solution = solution;
            _actions = actions;
        }

        /// <summary>
        /// 异步处理指定文件名的语法重写。
        /// </summary>
        /// <param name="fileName">要处理的文件名。</param>
        /// <returns>重写后的语法节点。</returns>
        public async Task<SyntaxNode> ProcessFileAsync(string fileName)
        {
            var document = _solution.Projects.SelectMany(p => p.Documents).FirstOrDefault(d => d.Name == fileName);
            if (document == null)
            {
                return null!;
            }

            var model = await document.GetSemanticModelAsync();
            var root = await document.GetSyntaxRootAsync();
            if (root == null || model == null)
            {
                return root!;
            }

            return await PipelineExpressionSimplifier.RewriteAsync(
                root,
                model,
                _solution,
                _ => false,
                globalMethodActions: _actions,
                useHybrid: true);
        }
    }
}
