using System.Collections.Concurrent;
using TerrariaTools.Diagnostics;

namespace TerrariaTools.ConsistentBehaviorGuarantee
{
    /// <summary>
    /// 确定性重放工具，用于记录和复现特定的执行序列。
    /// </summary>
    public class DeterministicReplayer
    {
        private readonly ConcurrentDictionary<string, object> _recordedEvents = new ConcurrentDictionary<string, object>();
        private readonly RewritingTraceContext _traceContext;

        public DeterministicReplayer(RewritingTraceContext traceContext)
        {
            _traceContext = traceContext;
        }

        /// <summary>
        /// 录制一个事件的数据。
        /// </summary>
        /// <param name="eventKey">事件唯一标识</param>
        /// <param name="data">录制的数据</param>
        public void RecordEvent(string eventKey, object data)
        {
            _recordedEvents[eventKey] = data;
        }

        /// <summary>
        /// 重放并验证当前数据是否与录制的数据一致。
        /// </summary>
        /// <param name="eventKey">事件唯一标识</param>
        /// <param name="newData">当前产生的数据</param>
        /// <returns>是否匹配</returns>
        public bool ReplayAndVerify(string eventKey, object newData)
        {
            if (!_recordedEvents.TryGetValue(eventKey, out var oldData))
            {
                _traceContext.AddDiagnostic(new RewritingDiagnostic
                {
                    Reason = $"重放失败: 未找到事件键 {eventKey}",
                    Severity = "Error"
                });
                return false;
            }

            bool match = Equals(oldData, newData);
            if (!match)
            {
                _traceContext.AddDiagnostic(new RewritingDiagnostic
                {
                    Reason = $"重放数据不一致: {eventKey}. 录制值: {oldData}, 当前值: {newData}",
                    Severity = "Error"
                });
            }

            return match;
        }
    }
}
