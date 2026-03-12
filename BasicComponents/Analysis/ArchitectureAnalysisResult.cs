using System;
using System.Collections.Generic;
using System.Linq;

namespace TerrariaTools.Analysis
{
    /// <summary>
    /// 架构分析结果的数据传输对象 (DTO)。
    /// 用于存储和传输对代码库进行架构级分析后的统计数据和结构信息。
    /// </summary>
    public class ArchitectureAnalysisResult
    {
        /// <summary>
        /// 依赖图中的节点总数。
        /// </summary>
        public int NodeCount { get; set; }
        
        /// <summary>
        /// 依赖图中的边总数。
        /// </summary>
        public int EdgeCount { get; set; }
        
        /// <summary>
        /// 强连通分量 (SCC) 列表。
        /// 每个内部列表包含构成一个 SCC 的节点名称集合。
        /// 用于识别循环依赖模块。
        /// </summary>
        public List<List<string>> StrongConnectedComponents { get; set; } = new();
        
        /// <summary>
        /// 拓扑排序结果列表。
        /// 表示无环图的线性依赖顺序。
        /// </summary>
        public List<string> TopologicalSort { get; set; } = new();
        
        /// <summary>
        /// 计算属性：判断是否存在环。
        /// 如果任何一个强连通分量的节点数大于 1，则表示图中存在环。
        /// </summary>
        public bool HasCycles => StrongConnectedComponents.Any(s => s.Count > 1);
        
        /// <summary>
        /// 错误信息字符串。默认为空字符串。
        /// </summary>
        public string Error { get; set; } = string.Empty;
    }
}
