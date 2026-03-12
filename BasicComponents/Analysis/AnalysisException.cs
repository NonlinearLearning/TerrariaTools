using System;

namespace TerrariaTools.Analysis
{
    /// <summary>
    /// 自定义异常类，用于封装 Analysis 模块中的错误。
    /// 继承自 System.Exception，提供标准的异常处理机制。
    /// 该类为密封类 (sealed)，防止被继承。
    /// </summary>
    public sealed class AnalysisException : Exception
    {
        /// <summary>
        /// 构造函数：初始化异常消息和内部异常。
        /// </summary>
        /// <param name="message">描述错误原因的消息。</param>
        /// <param name="innerException">导致当前异常的原始异常（用于保留堆栈跟踪和错误根源）。</param>
        public AnalysisException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
