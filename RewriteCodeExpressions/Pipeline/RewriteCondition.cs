using Microsoft.CodeAnalysis.CSharp;

namespace TerrariaTools.RewriteCodeExpressions.Pipeline
{
    /// <summary>
    /// Terraria 特定重写条件。
    /// </summary>
    public class RewriteCondition
    {
        /// <summary>
        /// 符号名称（如 "netMode"）。
        /// </summary>
        public string SymbolName { get; set; } = string.Empty;

        /// <summary>
        /// 比较运算符（如 SyntaxKind.EqualsExpression）。
        /// </summary>
        public SyntaxKind Operator { get; set; }

        /// <summary>
        /// 目标值（如 "1" 或 "whoAmI"）。
        /// </summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// 指定 Value 是否为字面量。
        /// </summary>
        public bool IsValueLiteral { get; set; }
    }
}
