using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TerrariaTools.Services
{
    /// <summary>
    /// 提供工具的自动发现和管理服务。
    /// </summary>
    public class ToolDiscoveryService
    {
        private readonly List<ITool> _tools;

        public ToolDiscoveryService(IEnumerable<ITool> tools)
        {
            _tools = tools.OrderBy(t => t.Name).ToList();
        }

        /// <summary>
        /// 获取所有已发现的工具。
        /// </summary>
        public IReadOnlyList<ITool> GetAllTools()
        {
            return _tools;
        }

        /// <summary>
        /// 根据名称查找工具。
        /// </summary>
        public ITool? GetToolByName(string name)
        {
            return _tools.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
