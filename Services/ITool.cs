using System.Threading.Tasks;

namespace TerrariaTools.Services
{
    /// <summary>
    /// 定义所有分析和重构工具的统一接口。
    /// </summary>
    public interface ITool
    {
        /// <summary>
        /// 工具名称
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 工具描述
        /// </summary>
        string Description { get; }

        /// <summary>
        /// 运行工具
        /// </summary>
        /// <param name="targetPath">目标路径（可选），如果不提供则工具自行获取或提示</param>
        /// <returns>异步任务</returns>
        Task RunAsync(string? targetPath = null);
    }
}
