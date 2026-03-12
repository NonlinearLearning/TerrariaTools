using System.Collections.Generic;
using TerrariaTools.Diagnostics;

namespace TerrariaTools.ConsistentBehaviorGuarantee
{
    /// <summary>
    /// 差异化测试工具，用于对比新旧逻辑的输出一致性。
    /// </summary>
    public class DifferentialTester
    {
        private readonly RewritingTraceContext _traceContext;

        public DifferentialTester(RewritingTraceContext traceContext)
        {
            _traceContext = traceContext;
        }

        /// <summary>
        /// 对比两个值是否一致，如果不一致则记录诊断信息。
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="originalOutput">原始输出</param>
        /// <param name="newOutput">新输出</param>
        /// <param name="contextName">上下文名称（如函数名或变量名）</param>
        /// <returns>是否一致</returns>
        public bool Compare<T>(T originalOutput, T newOutput, string contextName)
        {
            bool isEqual = EqualityComparer<T>.Default.Equals(originalOutput, newOutput);

            if (!isEqual)
            {
                _traceContext.AddDiagnostic(new RewritingDiagnostic
                {
                    Reason = $"差异化测试失败: {contextName}. 期望值: {originalOutput}, 实际值: {newOutput}",
                    Severity = "Error"
                });
            }

            return isEqual;
        }
    }
}
