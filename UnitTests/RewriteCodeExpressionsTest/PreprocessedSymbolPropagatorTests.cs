/**
 * 功能描述：预处理符号映射传播方案（方案三）的单元测试
 */
using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using TerrariaTools.RewriteCodeExpressions;
using Xunit;

namespace TerrariaTools.UnitTests
{
    public class PreprocessedSymbolPropagatorTests : SemanticPropagationTestBase
    {
        /// <summary>
        /// 运行预处理符号映射传播器
        /// </summary>
        /// <param name="model">语义模型</param>
        /// <param name="root">语法树根节点</param>
        /// <param name="nodesToMark">待标记节点集合</param>
        protected override void RunPropagator(SemanticModel Model, SyntaxNode Root, HashSet<SyntaxNode> NodesToMark)
        {
            var Propagator = new PreprocessedSymbolPropagator(Model, NodesToMark, Root);
            Propagator.Propagate();
        }

        /// <summary>
        /// 测试预处理符号映射传播方案
        /// </summary>
        /// <param name="Source">C# 源码字符串</param>
        /// <param name="TargetName">初始标记的目标名称</param>
        /// <param name="ExpectedUsageSubstrings">预期被标记的引用节点所包含的字符串</param>
        /// <param name="Ignored">被忽略的参数</param>
        [Theory]
        [MemberData(nameof(PropagationTestCases.GetCases), MemberType = typeof(PropagationTestCases))]
        public void Test_Propagation(string Source, string TargetName, string[] ExpectedUsageSubstrings, string[] Ignored)
        {
            _ = Ignored;
            VerifyPropagation(Source, TargetName, ExpectedUsageSubstrings);
        }
    }
}
