using System;
using System.Collections.Generic;
using TerrariaTools.Diagnostics;

namespace TerrariaTools.ConsistentBehaviorGuarantee
{
    /// <summary>
    /// 行为一致性保证服务，集成多种验证工具。
    /// </summary>
    public class ConsistencyGuaranteeService
    {
        private readonly RewritingTraceContext _traceContext;

        public DifferentialTester DiffTester { get; }
        public LogicParityVerifier LogicVerifier { get; }
        public DeterministicReplayer Replayer { get; }
        public ShadowExecutor ShadowExecutor { get; }

        public ConsistencyGuaranteeService(RewritingTraceContext traceContext)
        {
            _traceContext = traceContext;
            DiffTester = new DifferentialTester(traceContext);
            LogicVerifier = new LogicParityVerifier(traceContext);
            Replayer = new DeterministicReplayer(traceContext);
            ShadowExecutor = new ShadowExecutor(traceContext);
        }

        /// <summary>
        /// 记录当前状态快照。
        /// </summary>
        /// <param name="stateName">状态名称</param>
        /// <param name="stateObject">状态对象</param>
        public void Snapshot(string stateName, object stateObject)
        {
            _traceContext.AddDiagnostic(new RewritingDiagnostic
            {
                Reason = $"状态快照 [{stateName}]: {stateObject}",
                Severity = "Info"
            });
        }

        /// <summary>
        /// 验证并报告逻辑等价性。
        /// </summary>
        public bool EnsureParity(Microsoft.CodeAnalysis.SyntaxNode original, Microsoft.CodeAnalysis.SyntaxNode rewritten)
        {
            return LogicVerifier.VerifyParity(original, rewritten);
        }
    }
}
