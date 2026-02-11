using System;
using TerrariaTools.Diagnostics;

namespace TerrariaTools.ConsistentBehaviorGuarantee
{
    /// <summary>
    /// 影子执行器，同时运行原逻辑和新逻辑并比对结果，但不影响主流程。
    /// </summary>
    public class ShadowExecutor
    {
        private readonly RewritingTraceContext _traceContext;
        private readonly DifferentialTester _diffTester;

        public ShadowExecutor(RewritingTraceContext traceContext)
        {
            _traceContext = traceContext;
            _diffTester = new DifferentialTester(traceContext);
        }

        /// <summary>
        /// 执行影子对比。
        /// </summary>
        /// <typeparam name="TResult">返回结果类型</typeparam>
        /// <param name="originalFunc">原始逻辑函数</param>
        /// <param name="newFunc">重写后的逻辑函数</param>
        /// <param name="contextName">上下文描述</param>
        /// <returns>始终返回原始逻辑的结果</returns>
        public TResult ExecuteShadow<TResult>(Func<TResult> originalFunc, Func<TResult> newFunc, string contextName)
        {
            TResult originalResult = default!;
            TResult newResult = default!;

            try
            {
                originalResult = originalFunc();
            }
            catch (Exception ex)
            {
                _traceContext.AddDiagnostic(new RewritingDiagnostic
                {
                    Reason = $"影子执行主逻辑异常: {contextName}. {ex.Message}",
                    Severity = "Error"
                });
                throw;
            }

            try
            {
                newResult = newFunc();
            }
            catch (Exception ex)
            {
                _traceContext.AddDiagnostic(new RewritingDiagnostic
                {
                    Reason = $"影子执行对比逻辑异常: {contextName}. {ex.Message}",
                    Severity = "Warning"
                });
            }

            // 对比结果
            _diffTester.Compare(originalResult, newResult, contextName);

            return originalResult;
        }
    }
}
