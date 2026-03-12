using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Runtime.CompilerServices;

namespace TerrariaTools.Analysis
{
    /// <summary>
    /// 数据流分析器
    /// 基于控制流图 (CFG) 进行数据流分析，
    /// 用于追踪变量的定义-使用链 (Def-Use Chains) 和值的传播。
    /// </summary>
    public class DataFlowAnalyzer
    {
        /// <summary>
        /// 编译对象
        /// </summary>
        private readonly Compilation _compilation;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="compilation">编译对象</param>
        public DataFlowAnalyzer(Compilation compilation)
        {
            _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
        }

        /// <summary>
        /// 异步分析指定方法的数据流
        /// </summary>
        /// <param name="typeName">类型全名</param>
        /// <param name="methodName">方法名</param>
        /// <returns>数据流分析结果</returns>
        public async Task<DataFlowResult> AnalyzeAsync(string typeName, string methodName)
        {
            var typeSymbol = _compilation.GetTypeByMetadataName(typeName);
            if (typeSymbol == null)
            {
                return new DataFlowResult { Succeeded = false };
            }

            var methodSymbol = typeSymbol.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault();
            if (methodSymbol == null)
            {
                return new DataFlowResult { Succeeded = false };
            }

            var syntaxReference = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxReference == null)
            {
                return new DataFlowResult { Succeeded = false };
            }

            var methodNode = await syntaxReference.GetSyntaxAsync() as MethodDeclarationSyntax;
            if (methodNode?.Body == null)
            {
                return new DataFlowResult { Succeeded = false };
            }

            var model = _compilation.GetSemanticModel(methodNode.SyntaxTree);
            var dataFlow = model.AnalyzeDataFlow(methodNode.Body);

            var result = new DataFlowResult
            {
                Succeeded = dataFlow.Succeeded,
                ReadInsideCount = dataFlow.ReadInside.Length,
                WrittenInsideCount = dataFlow.WrittenInside.Length,
                CapturedCount = dataFlow.Captured.Length
            };

            foreach (var sym in dataFlow.Captured)
            {
                result.CapturedVariables.Add(sym.Name);
            }

            foreach (var sym in dataFlow.DataFlowsIn)
            {
                result.DataFlowInVariables.Add(sym.Name);
            }

            foreach (var sym in methodSymbol.Parameters)
            {
                result.Variables.Add(new VariableInfo { Name = sym.Name, Kind = "Parameter", Source = "Method Signature" });
                result.ParameterCount++;
            }

            var localSymbols = methodNode.Body.DescendantNodes().OfType<VariableDeclaratorSyntax>()
                .Select(v => model.GetDeclaredSymbol(v)).Where(s => s != null);

            foreach (var sym in localSymbols)
            {
                result.Variables.Add(new VariableInfo { Name = sym!.Name, Kind = "Local", Source = "Method Body" });
                result.LocalVariableCount++;
            }

            AnalyzeControlFlowAndSymbolic(methodNode, model, result);
            return result;
        }

        /// <summary>
        /// 分析控制流和符号执行
        /// </summary>
        /// <param name="methodNode">方法声明语法节点</param>
        /// <param name="model">语义模型</param>
        /// <param name="result">分析结果对象</param>
        private static void AnalyzeControlFlowAndSymbolic(MethodDeclarationSyntax methodNode, SemanticModel model, DataFlowResult result)
        {
            ControlFlowGraph? cfg;
            try
            {
                cfg = ControlFlowGraph.Create(methodNode, model);
            }
            catch
            {
                cfg = null;
            }

            if (cfg == null)
            {
                return;
            }

            result.ControlFlowBlockCount = cfg.Blocks.Length;

            var symbolicStates = ExecuteSymbolic(cfg);
            result.ExplodedStateCount = symbolicStates;

            var visitedOrdinals = CollectVisitedOrdinals(cfg);
            var unreachableBlocks = cfg.Blocks.Where(b => !visitedOrdinals.Contains(b.Ordinal)).ToList();
            result.UnreachableBlockCount = unreachableBlocks.Count;

            foreach (var block in unreachableBlocks)
            {
                var syntax = block.Operations.FirstOrDefault()?.Syntax ?? block.BranchValue?.Syntax;
                var location = syntax?.GetLocation();
                if (location != null)
                {
                    var span = location.GetLineSpan();
                    result.PotentiallyUnreachableLocations.Add(
                        $"{span.Path}:{span.StartLinePosition.Line + 1}:{span.StartLinePosition.Character + 1}");
                }
            }
        }

        /// <summary>
        /// 执行符号化执行
        /// </summary>
        /// <param name="cfg">控制流图</param>
        /// <returns>符号化状态数量</returns>
        private static int ExecuteSymbolic(ControlFlowGraph cfg)
        {
            var worklist = new Queue<(BasicBlock Block, SymbolicState State)>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var visitedOrdinals = new HashSet<int>();

            var start = cfg.Blocks.FirstOrDefault();
            if (start == null)
            {
                return 0;
            }

            worklist.Enqueue((start, new SymbolicState()));

            while (worklist.Count > 0)
            {
                var (block, state) = worklist.Dequeue();
                var key = $"{block.Ordinal}:{state.GetFingerprint()}";
                if (!visited.Add(key))
                {
                    continue;
                }

                visitedOrdinals.Add(block.Ordinal);

                var current = state.Clone();
                foreach (var operation in block.Operations)
                {
                    ApplyOperation(current, operation);
                }

                if (block.BranchValue != null)
                {
                    var decision = EvaluateBoolean(block.BranchValue, current);
                    if (decision != false && block.ConditionalSuccessor.Destination != null)
                    {
                        worklist.Enqueue((block.ConditionalSuccessor.Destination, current.Clone()));
                    }

                    if (decision != true && block.FallThroughSuccessor.Destination != null)
                    {
                        worklist.Enqueue((block.FallThroughSuccessor.Destination, current.Clone()));
                    }
                }
                else
                {
                    if (block.FallThroughSuccessor.Destination != null)
                    {
                        worklist.Enqueue((block.FallThroughSuccessor.Destination, current));
                    }
                }
            }

            cfg.SetState("VisitedOrdinals", visitedOrdinals);
            return visited.Count;
        }

        /// <summary>
        /// 收集访问过的代码块索引
        /// </summary>
        /// <param name="cfg">控制流图</param>
        /// <returns>已访问的索引集合</returns>
        private static HashSet<int> CollectVisitedOrdinals(ControlFlowGraph cfg)
        {
            return cfg.GetState<HashSet<int>>("VisitedOrdinals") ?? new HashSet<int>();
        }

        /// <summary>
        /// 应用操作对符号状态的影响
        /// </summary>
        /// <param name="state">符号状态</param>
        /// <param name="operation">操作</param>
        private static void ApplyOperation(SymbolicState state, IOperation operation)
        {
            if (operation is ISimpleAssignmentOperation assignment && assignment.Target is ILocalReferenceOperation target)
            {
                var value = TryGetLiteralValue(assignment.Value);
                state.Values[target.Local.Name] = value ?? SymbolicState.Unknown;
            }
        }

        /// <summary>
        /// 评估布尔操作的值
        /// </summary>
        /// <param name="operation">操作</param>
        /// <param name="state">符号状态</param>
        /// <returns>布尔值（如果可以确定）</returns>
        /// <summary>
        /// 评估布尔操作的值
        /// </summary>
        /// <param name="operation">操作</param>
        /// <param name="state">符号状态</param>
        /// <returns>布尔值（如果可以确定）</returns>
        private static bool? EvaluateBoolean(IOperation operation, SymbolicState state)
        {
            if (operation is IBinaryOperation binary)
            {
                var leftLiteral = TryGetLiteralValue(binary.LeftOperand, state);
                var rightLiteral = TryGetLiteralValue(binary.RightOperand, state);
                if (leftLiteral == null || rightLiteral == null)
                {
                    return null;
                }

                if (binary.OperatorKind == BinaryOperatorKind.Equals)
                {
                    return string.Equals(leftLiteral, rightLiteral, StringComparison.Ordinal);
                }

                if (binary.OperatorKind == BinaryOperatorKind.NotEquals)
                {
                    return !string.Equals(leftLiteral, rightLiteral, StringComparison.Ordinal);
                }
            }

            return null;
        }

        /// <summary>
        /// 尝试获取字面量值
        /// </summary>
        /// <param name="operation">操作</param>
        /// <param name="state">可选的符号状态</param>
        /// <returns>字面量字符串值</returns>
        private static string? TryGetLiteralValue(IOperation operation, SymbolicState? state = null)
        {
            if (operation is ILiteralOperation literal)
            {
                return literal.ConstantValue.HasValue ? literal.ConstantValue.Value?.ToString() : null;
            }

            if (state != null && operation is ILocalReferenceOperation local && state.Values.TryGetValue(local.Local.Name, out var value))
            {
                return value == SymbolicState.Unknown ? null : value;
            }

            return null;
        }

        /// <summary>
        /// 内部符号状态类
        /// </summary>
        private sealed class SymbolicState
        {
            /// <summary>
            /// 表示未知状态的常量
            /// </summary>
            public const string Unknown = "?";

            /// <summary>
            /// 存储变量及其当前字面量值的字典
            /// </summary>
            public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// 克隆当前的符号状态
            /// </summary>
            /// <returns>克隆后的状态对象</returns>
            public SymbolicState Clone()
            {
                var copy = new SymbolicState();
                foreach (var kv in Values)
                {
                    copy.Values[kv.Key] = kv.Value;
                }

                return copy;
            }

            /// <summary>
            /// 获取状态的指纹（用于判断是否已访问）
            /// </summary>
            /// <returns>状态指纹字符串</returns>
            public string GetFingerprint()
            {
                return string.Join(";", Values.OrderBy(k => k.Key).Select(kv => $"{kv.Key}={kv.Value}"));
            }
        }
    }

    /// <summary>
    /// 数据流分析结果
    /// </summary>
    public class DataFlowResult
    {
        /// <summary>
        /// 变量信息列表
        /// </summary>
        public List<VariableInfo> Variables { get; } = new List<VariableInfo>();

        /// <summary>
        /// 参数数量
        /// </summary>
        public int ParameterCount { get; set; }

        /// <summary>
        /// 局部变量数量
        /// </summary>
        public int LocalVariableCount { get; set; }

        /// <summary>
        /// 外部变量数量
        /// </summary>
        public int ExternalVariableCount { get; set; }

        /// <summary>
        /// 是否分析成功
        /// </summary>
        public bool Succeeded { get; set; }

        /// <summary>
        /// 内部读取次数
        /// </summary>
        public int ReadInsideCount { get; set; }

        /// <summary>
        /// 内部写入次数
        /// </summary>
        public int WrittenInsideCount { get; set; }

        /// <summary>
        /// 被捕获的变量数量
        /// </summary>
        public int CapturedCount { get; set; }

        /// <summary>
        /// 控制流块数量
        /// </summary>
        public int ControlFlowBlockCount { get; set; }

        /// <summary>
        /// 爆炸状态数量
        /// </summary>
        public int ExplodedStateCount { get; set; }

        /// <summary>
        /// 不可达代码块数量
        /// </summary>
        public int UnreachableBlockCount { get; set; }

        /// <summary>
        /// 被捕获的变量名称列表
        /// </summary>
        public List<string> CapturedVariables { get; } = new List<string>();

        /// <summary>
        /// 外部流入变量名称列表
        /// </summary>
        public List<string> DataFlowInVariables { get; } = new List<string>();

        /// <summary>
        /// 潜在的不可达代码位置列表
        /// </summary>
        public List<string> PotentiallyUnreachableLocations { get; } = new List<string>();
    }

    /// <summary>
    /// 变量信息
    /// </summary>
    public class VariableInfo
    {
        /// <summary>
        /// 变量名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 变量种类（如 Parameter, Local）
        /// </summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>
        /// 变量来源（如 Method Signature, Method Body）
        /// </summary>
        public string Source { get; set; } = string.Empty;
    }

    /// <summary>
    /// 控制流图状态扩展方法
    /// </summary>
    internal static class ControlFlowGraphStateExtensions
    {
        /// <summary>
        /// 用于存储 CFG 额外状态的表
        /// </summary>
        private static readonly ConditionalWeakTable<ControlFlowGraph, Dictionary<string, object>> StateTable = new();

        /// <summary>
        /// 设置 CFG 的额外状态
        /// </summary>
        /// <param name="cfg">控制流图</param>
        /// <param name="key">状态键</param>
        /// <param name="value">状态值</param>
        public static void SetState(this ControlFlowGraph cfg, string key, object value)
        {
            var bag = StateTable.GetOrCreateValue(cfg);
            bag[key] = value;
        }

        /// <summary>
        /// 获取 CFG 的额外状态
        /// </summary>
        /// <typeparam name="T">状态类型</typeparam>
        /// <param name="cfg">控制流图</param>
        /// <param name="key">状态键</param>
        /// <returns>状态值（如果存在）</returns>
        public static T? GetState<T>(this ControlFlowGraph cfg, string key)
        {
            if (StateTable.TryGetValue(cfg, out var bag) && bag.TryGetValue(key, out var value) && value is T typed)
            {
                return typed;
            }

            return default;
        }
    }
}
