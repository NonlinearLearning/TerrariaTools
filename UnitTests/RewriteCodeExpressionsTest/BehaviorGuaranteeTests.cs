using System;
using Xunit;
using TerrariaTools.ConsistentBehaviorGuarantee;
using TerrariaTools.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RewriteCodeExpressionsTest
{
    public class BehaviorGuaranteeTests
    {
        private readonly RewritingTraceContext _traceContext;

        public BehaviorGuaranteeTests()
        {
            _traceContext = new RewritingTraceContext();
        }

        [Fact]
        public void DifferentialTester_ShouldDetectDifferences()
        {
            var tester = new DifferentialTester(_traceContext);

            Assert.True(tester.Compare(100, 100, "SpawnRate"));
            Assert.False(tester.Compare(100, 200, "SpawnRate"));

            Assert.Single(_traceContext.GetDiagnostics());
        }

        [Fact]
        public void LogicParityVerifier_ShouldHandleTrivia()
        {
            var verifier = new LogicParityVerifier(_traceContext);

            var original = SyntaxFactory.ParseExpression("x * 2 /* multiply by 2 */");
            var rewritten = SyntaxFactory.ParseExpression("x * 2");

            Assert.True(verifier.VerifyParity(original, rewritten));
        }

        [Fact]
        public void LogicParityVerifier_ShouldDetectStructuralChanges()
        {
            var verifier = new LogicParityVerifier(_traceContext);

            var original = SyntaxFactory.ParseExpression("x * 2");
            var rewritten = SyntaxFactory.ParseExpression("x << 1"); // semantically same for int, but structurally different

            Assert.False(verifier.VerifyParity(original, rewritten));
            Assert.Single(_traceContext.GetDiagnostics());
        }

        [Fact]
        public void DeterministicReplayer_ShouldVerifyRecordedSequence()
        {
            var replayer = new DeterministicReplayer(_traceContext);

            replayer.RecordEvent("Rand_1", 0.5f);
            replayer.RecordEvent("Rand_2", 0.8f);

            Assert.True(replayer.ReplayAndVerify("Rand_1", 0.5f));
            Assert.True(replayer.ReplayAndVerify("Rand_2", 0.8f));
            Assert.False(replayer.ReplayAndVerify("Rand_1", 0.6f));
        }

        [Fact]
        public void ShadowExecutor_ShouldReturnOriginalResult()
        {
            var executor = new ShadowExecutor(_traceContext);

            int originalLogic() => 10 + 20;
            int newLogic() => 30;

            int result = executor.ExecuteShadow(originalLogic, newLogic, "AdditionTest");

            Assert.Equal(30, result);
            Assert.Empty(_traceContext.GetDiagnostics());
        }

        [Fact]
        public void ShadowExecutor_ShouldLogMismatchButReturnOriginal()
        {
            var executor = new ShadowExecutor(_traceContext);

            int originalLogic() => 10 + 20;
            int newLogic() => 40; // Wrong implementation

            int result = executor.ExecuteShadow(originalLogic, newLogic, "MismatchTest");

            Assert.Equal(30, result);
            Assert.Single(_traceContext.GetDiagnostics());
        }

        [Fact]
        public void LogicParityVerifier_ShouldHandleNestedExpressions()
        {
            var verifier = new LogicParityVerifier(_traceContext);

            // 原始：复杂的逻辑组合
            var original = SyntaxFactory.ParseExpression("(a && b) || (c && !d)");
            // 重写：移除不必要的括号，但保持逻辑
            var rewritten = SyntaxFactory.ParseExpression("a && b || c && !d");

            Assert.True(verifier.VerifyParity(original, rewritten));
        }

        [Fact]
        public void ConsistencyGuaranteeService_ShouldCoordinateTools()
        {
            var service = new ConsistencyGuaranteeService(_traceContext);

            service.Snapshot("Initial", new { X = 10, Y = 20 });

            bool result = service.DiffTester.Compare(1, 1, "SimpleTest");
            Assert.True(result);

            var diagnostics = _traceContext.GetDiagnostics();
            Assert.Contains(diagnostics, d => d.Reason.Contains("状态快照 [Initial]"));
        }
    }
}
