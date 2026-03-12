using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Generic;
using TerrariaTools.RewriteCodeExpressions.Pipeline;

namespace TerrariaTools.RewriteCodeExpressions
{
    /// <summary>
    /// Terraria 条件重写器：专门处理 Terraria 项目中常见的条件分支（如 Main.netMode == 0）。
    /// 目前主要通过 PipelineExpressionSimplifier (Hybrid) 统一处理。
    /// </summary>
    public class TerrariaConditionRewriter
    {
        private readonly SemanticModel _model;
        private readonly List<RewriteCondition> _conditions;

        /// <summary>
        /// 使用预定义的重写条件列表初始化 TerrariaConditionRewriter 的新实例。
        /// </summary>
        /// <param name="model">语法树的语义模型。</param>
        /// <param name="conditions">重写条件列表。</param>
        public TerrariaConditionRewriter(SemanticModel model, List<RewriteCondition> conditions)
        {
            _model = model;
            _conditions = conditions;
        }

        /// <summary>
        /// 使用单个目标符号和目标值初始化 TerrariaConditionRewriter 的新实例。
        /// </summary>
        /// <param name="model">语法树的语义模型。</param>
        /// <param name="targetSymbol">目标符号名称（如 "Main.netMode"）。</param>
        /// <param name="targetValue">目标匹配值。</param>
        public TerrariaConditionRewriter(SemanticModel model, string targetSymbol, int targetValue)
        {
            _model = model;
            _conditions = new List<RewriteCondition>
            {
                new RewriteCondition
                {
                    SymbolName = targetSymbol,
                    Operator = SyntaxKind.EqualsExpression,
                    Value = targetValue.ToString(),
                    IsValueLiteral = true
                }
            };
        }

        /// <summary>
        /// 访问并重写语法树，根据条件移除特定的代码块。
        /// </summary>
        /// <param name="root">要处理的语法树根节点。</param>
        /// <returns>重写后的语法节点。</returns>
        public SyntaxNode Visit(SyntaxNode root)
        {
            return PipelineExpressionSimplifier.Rewrite(
                root,
                _model,
                solution: null,
                _ => false,
                useHybrid: true,
                terrariaConditions: _conditions);
        }
    }
}
