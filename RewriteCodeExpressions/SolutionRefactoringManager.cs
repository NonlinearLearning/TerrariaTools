using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Options;
using TerrariaTools.Services;
using TerrariaTools.Configuration;
using TerrariaTools.RewriteCodeExpressions.Pipeline;

namespace TerrariaTools.RewriteCodeExpressions
{
    /// <summary>
    /// 解决方案重构管理器
    /// 负责协调全解决方案级别的代码重构流程。
    /// </summary>
    public class SolutionRefactoringManager
    {
        /// <summary>
        /// 工作区加载器
        /// </summary>
        private readonly IWorkspaceLoader _loader;

        /// <summary>
        /// 重构设置
        /// </summary>
        private readonly RefactoringSettings _settings;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="loader">工作区加载器实例</param>
        /// <param name="settings">重构设置选项</param>
        public SolutionRefactoringManager(IWorkspaceLoader loader, IOptions<RefactoringSettings> settings)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>
        /// 执行全量重构流程
        /// </summary>
        /// <param name="solutionPath">解决方案路径</param>
        /// <param name="externalProgress">可选的外部进度报告器</param>
        /// <returns>重构结果对象</returns>
        public async Task<SolutionRefactoringResult> ExecuteFullRefactoringAsync(string solutionPath, IProgress<string>? externalProgress = null)
        {
            var result = new SolutionRefactoringResult();

            if (string.IsNullOrEmpty(solutionPath))
            {
                result.Success = false;
                result.Error = "解决方案路径不能为空。";
                return result;
            }

            // 组合进度报告器：既更新结果日志，又通过外部回调（如果存在）实时输出
            var progress = new Progress<string>(message =>
            {
                result.AddLog(message);
                externalProgress?.Report(message);
            });

            try
            {
                result.AddLog($"正在开始解决方案重构: {solutionPath}");

                // 1. Pipeline 重构 (替换单独的重构器)
                result.AddLog("正在执行基于 Pipeline 的解决方案重构...");

                var solution = await _loader.LoadSolutionAsync(solutionPath);
                if (solution == null) throw new Exception("无法加载解决方案。");

                // 分析全方案方法动作 (MethodRefactorer 的核心逻辑)
                var builder = new Analysis.FunctionBuildGraph(solution);
                await builder.BuildAsync(progress);
                var methodActions = builder.AnalyzeMethods(aggressive: _settings.AggressiveMode);
                var terrariaConditions = BuildTerrariaConditionsFromSettings(_settings.HybridTerrariaConditions);

                int processedFiles = 0;
                int totalPlanItems = 0;
                int totalExecutedRules = 0;
                int totalReplacedNodes = 0;
                int totalDeletedNodes = 0;
                foreach (var project in solution.Projects)
                {
                    foreach (var document in project.Documents)
                    {
                        var tree = await document.GetSyntaxTreeAsync();
                        var model = await document.GetSemanticModelAsync();
                        if (tree == null) continue;

                        var oldRoot = await tree.GetRootAsync();

                        // 使用新的 Pipeline 执行重构
                        var newRoot = await Pipeline.PipelineExpressionSimplifier.RewriteAsync(
                            oldRoot,
                            model,
                            solution,
                            node => false, // 默认不按谓词删除，由各层自行决定
                            null,
                            methodActions,
                            default,
                            null, // traceContext
                            _settings.UseHybridRewriteEngine,
                            terrariaConditions,
                            _settings.HybridNamePattern,
                            _settings.HybridDeleteMatchedMethods,
                            _settings.HybridClearBodyMatchedMethods,
                            metrics =>
                            {
                                totalPlanItems += metrics.PlanItemCount;
                                totalExecutedRules += metrics.ExecutedRuleCount;
                                totalReplacedNodes += metrics.ReplacedNodeCount;
                                totalDeletedNodes += metrics.DeletedNodeCount;
                            }
                        );

                        if (newRoot != oldRoot)
                        {
                            // 这里简单模拟统计，实际应用中可能需要更精确的差异对比
                            processedFiles++;
                            // 实际项目中这里应有保存逻辑，如 solution = solution.WithDocumentSyntaxRoot(document.Id, newRoot);
                        }
                    }
                }

                result.TotalRefactoredFiles = processedFiles;
                result.AddLog($"Pipeline 重构已完成。处理了 {processedFiles} 个文件。");
                if (_settings.UseHybridRewriteEngine)
                {
                    result.HybridPlanItemCount = totalPlanItems;
                    result.HybridExecutedRuleCount = totalExecutedRules;
                    result.HybridReplacedNodeCount = totalReplacedNodes;
                    result.HybridDeletedNodeCount = totalDeletedNodes;
                    result.AddLog($"Hybrid 指标: 计划项={totalPlanItems}, 已执行规则={totalExecutedRules}, 已替换节点={totalReplacedNodes}, 已删除节点={totalDeletedNodes}");
                }

                // 由于 Pipeline 整合了所有逻辑，这里不再调用旧的类/方法重构器
                /*
                // 1. Class Refactoring
                result.AddLog("Executing Class Refactoring (Removing unreferenced classes)...");
                var classStats = await ClassRefactorer.ExecuteSolutionRefactoringAsync(solutionPath, _loader, _settings, progress);
                ...
                */

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
                result.AddLog($"错误: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 从设置构建 Terraria 重写条件
        /// </summary>
        /// <param name="conditionSettings">条件设置列表</param>
        /// <returns>重写条件列表（如果无条件则返回 null）</returns>
        private static List<RewriteCondition>? BuildTerrariaConditionsFromSettings(List<HybridTerrariaConditionSetting>? conditionSettings)
        {
            if (conditionSettings == null || conditionSettings.Count == 0)
            {
                return null;
            }

            var list = new List<RewriteCondition>();
            foreach (var item in conditionSettings)
            {
                if (string.IsNullOrWhiteSpace(item.SymbolName) || string.IsNullOrWhiteSpace(item.Value))
                {
                    continue;
                }

                if (!Enum.TryParse<SyntaxKind>(item.Operator, ignoreCase: true, out var parsedOperator))
                {
                    parsedOperator = SyntaxKind.EqualsExpression;
                }

                list.Add(new RewriteCondition
                {
                    SymbolName = item.SymbolName,
                    Operator = parsedOperator,
                    Value = item.Value,
                    IsValueLiteral = item.IsValueLiteral
                });
            }

            return list.Count == 0 ? null : list;
        }
    }
}
