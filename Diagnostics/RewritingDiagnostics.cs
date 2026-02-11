/**
 * 功能描述：提供语法树重写过程中的诊断和追踪功能，用于记录节点被移除或修改的原因。
 */
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Generic;

namespace TerrariaTools.Diagnostics
{
    /// <summary>
    /// 表示重写过程中的一个诊断记录。
    /// </summary>
    public class RewritingDiagnostic
    {
        /// <summary>
        /// 严重程度。
        /// </summary>
        public string Severity { get; set; } = "Info";

        /// <summary>
        /// 发生变更的节点类型名称。
        /// </summary>
        public string NodeType { get; set; } = string.Empty;

        /// <summary>
        /// 变更原因描述。
        /// </summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// 原始代码片段。
        /// </summary>
        public string OriginalCode { get; set; } = string.Empty;

        /// <summary>
        /// 变更后的代码片段（如果是移除则为 null 或 empty）。
        /// </summary>
        public string? NewCode { get; set; }

        /// <summary>
        /// 节点在原始语法树中的位置。
        /// </summary>
        public FileLinePositionSpan Location { get; set; }

        /// <summary>
        /// 返回诊断信息的字符串表示。
        /// </summary>
        public override string ToString()
        {
            string loc = Location.IsValid ? $"[{Location.StartLinePosition.Line + 1}:{Location.StartLinePosition.Character}] " : "";
            return $"{loc}{Severity} | {NodeType}: {Reason} | 原始: {OriginalCode.Trim()} -> 目标: {NewCode?.Trim() ?? "(无)"}";
        }
    }

    /// <summary>
    /// 重写过程的追踪上下文，用于收集诊断信息。
    /// </summary>
    public class RewritingTraceContext
    {
        private readonly List<RewritingDiagnostic> _diagnostics = new List<RewritingDiagnostic>();
        private readonly object _lock = new object();

        /// <summary>
        /// 获取所有收集到的诊断信息。
        /// </summary>
        /// <returns>诊断信息列表</returns>
        public IReadOnlyList<RewritingDiagnostic> GetDiagnostics()
        {
            lock (_lock)
            {
                return _diagnostics.ToArray();
            }
        }

        /// <summary>
        /// 添加一条诊断记录。
        /// </summary>
        public void AddDiagnostic(RewritingDiagnostic diagnostic)
        {
            lock (_lock)
            {
                _diagnostics.Add(diagnostic);
            }
        }

        /// <summary>
        /// 添加一条诊断记录。
        /// </summary>
        /// <param name="node">受影响的原始节点</param>
        /// <param name="reason">变更原因</param>
        /// <param name="newNode">重写后的节点（可选）</param>
        public void AddDiagnostic(SyntaxNode node, string reason, SyntaxNode? newNode = null)
        {
            var diagnostic = new RewritingDiagnostic
            {
                NodeType = node.Kind().ToString(),
                Reason = reason,
                OriginalCode = node.ToString(),
                NewCode = newNode?.ToString(),
                Location = node.GetLocation().GetMappedLineSpan()
            };

            AddDiagnostic(diagnostic);
        }
    }
}
