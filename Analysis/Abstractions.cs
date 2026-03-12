using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace TerrariaTools.Analysis
{
    // 定义代码依赖分析器的核心接口，用于静态分析源代码中的符号依赖关系。
    public interface ICodeDependencyAnalyzer
    {
        // 获取分析生成的依赖图，包含所有已分析的符号节点及其关系。
        DependencyGraph Graph { get; }

        // 异步分析整个解决方案。
        // mode: 分析模式，默认为 Standard（标准模式）。
        // Standard: 分析所有项目。
        // Aggressive: 更深入的分析，可能包含更多间接依赖。
        // EntryOnly: 仅分析入口点。
        Task AnalyzeSolutionAsync(CodeDependencyAnalyzer.AnalysisMode mode = CodeDependencyAnalyzer.AnalysisMode.Standard);

        // 递归分析指定的种子符号（Seed Symbol）。
        // 从该符号开始，深度遍历其所有依赖项（调用、继承、字段引用等）。
        // seedSymbol: 分析的起始符号（如方法、类）。
        Task AnalyzeRecursiveAsync(ISymbol seedSymbol);

        // 递归分析一组种子符号。
        // seeds: 分析的起始符号集合。
        Task AnalyzeRecursiveAsync(IEnumerable<ISymbol> seeds);
    }

    // 定义调用链分析器的接口，用于处理运行时动态调用日志。
    public interface ICallChainAnalyzer
    {
        // 将动态调用日志应用到静态依赖图中。
        // 解析日志文件，识别实际执行的方法，并在依赖图中标记这些节点为“动态可达”。
        // graph: 目标依赖图。
        // logPath: 运行时生成的调用链日志文件路径（如 call_chain.log）。
        Task ApplyToGraphAsync(DependencyGraph graph, string logPath);
    }

    // 定义玩家字段提取器的接口，用于从特定游戏逻辑中提取关键字段引用。
    // 专门针对 Terraria 游戏代码中的 Player 数据处理逻辑。
    public interface IPlayerFieldExtractor
    {
        // 异步执行字段提取分析。
        // 扫描指定的方法（默认为 Terraria.MessageBuffer.GetData），找出所有对 Terraria.Player 类的字段写入操作。
        // targetTypeName: 目标类型名称（通常是 "Terraria.Player"）。
        // containerTypeName: 包含分析逻辑的容器类型名称（通常是 "Terraria.MessageBuffer"）。
        // methodName: 包含分析逻辑的方法名称（通常是 "GetData"）。
        // 返回值: 包含引用字段和相关符号的分析结果。
        Task<PlayerFieldExtractor.AnalysisResult> AnalyzeAsync(
            string targetTypeName = "Terraria.Player",
            string containerTypeName = "Terraria.MessageBuffer",
            string methodName = "GetData");
    }

    // 定义类型冲突分析器的接口，用于检测全局命名空间下的类型名称冲突。
    public interface ITypeConflictAnalyzer
    {
        // 异步分析整个解决方案中的全局类型冲突。
        // 扫描所有项目和引用的程序集，找出同名但位于不同命名空间或程序集的类型。
        // solution: Roslyn 解决方案对象。
        // 返回值: 包含冲突类型标识符的哈希集合。
        Task<HashSet<string>> AnalyzeGlobalConflictsAsync(Solution solution);
    }
}
