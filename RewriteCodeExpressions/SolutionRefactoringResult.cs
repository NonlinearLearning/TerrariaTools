using System;
using System.Collections.Generic;

namespace TerrariaTools.RewriteCodeExpressions
{
    /// <summary>
    /// 解决方案重构结果：包含重构过程中的各种统计信息和详细日志。
    /// </summary>
    public class SolutionRefactoringResult
    {
        /// <summary>
        /// 重构是否成功。
        /// </summary>
        public bool Success { get; set; } = true;

        /// <summary>
        /// 如果失败，包含错误消息。
        /// </summary>
        public string Error { get; set; } = string.Empty;

        /// <summary>
        /// 删除的类总数。
        /// </summary>
        public int TotalDeletedClasses { get; set; }

        /// <summary>
        /// 删除的方法总数。
        /// </summary>
        public int TotalDeletedMethods { get; set; }

        /// <summary>
        /// 私有化的方法总数。
        /// </summary>
        public int TotalPrivatizedMethods { get; set; }

        /// <summary>
        /// 方法体被清空的方法总数。
        /// </summary>
        public int TotalBodyClearedMethods { get; set; }

        /// <summary>
        /// 已重构的文件总数。
        /// </summary>
        public int TotalRefactoredFiles { get; set; }

        /// <summary>
        /// Hybrid 计划项目数。
        /// </summary>
        public int HybridPlanItemCount { get; set; }

        /// <summary>
        /// Hybrid 执行的规则数。
        /// </summary>
        public int HybridExecutedRuleCount { get; set; }

        /// <summary>
        /// Hybrid 替换的节点数。
        /// </summary>
        public int HybridReplacedNodeCount { get; set; }

        /// <summary>
        /// Hybrid 删除的节点数。
        /// </summary>
        public int HybridDeletedNodeCount { get; set; }

        /// <summary>
        /// 详细重构日志列表。
        /// </summary>
        public List<string> DetailedLogs { get; set; } = new();

        /// <summary>
        /// 添加带有时间戳的详细日志条目。
        /// </summary>
        /// <param name="message">日志消息。</param>
        public void AddLog(string message)
        {
            DetailedLogs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        }
    }
}
