/**
 * 功能描述：动态模型传播方案（方案二）的单元测试
 */
using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using TerrariaTools.RewriteCodeExpressions;
using TerrariaTools.RewriteCodeExpressions.Pipeline;
using Xunit;

namespace TerrariaTools.UnitTests
{
    public class DynamicModelPropagatorTests : SemanticPropagationTestBase
    {
        /// <summary>
        /// 运行动态模型传播器
        /// </summary>
        /// <param name="model">语义模型</param>
        /// <param name="root">语法树根节点</param>
        /// <param name="nodesToMark">待标记节点集合</param>
        protected override void RunPropagator(SemanticModel Model, SyntaxNode Root, HashSet<SyntaxNode> NodesToMark)
        {
            var Propagator = new DynamicModelPropagator(Model, NodesToMark);
            Propagator.Visit(Root);
        }

        /// <summary>
        /// 测试动态模型传播方案
        /// </summary>
        /// <param name="Source">C# 源码字符串</param>
        /// <param name="TargetName">初始标记的目标名称</param>
        /// <param name="ExpectedUsageSubstrings">预期被标记的引用节点所包含的字符串</param>
        /// <param name="ExpectedRemainingSubstrings">预期不被标记的引用节点所包含的字符串</param>
        [Theory]
        [MemberData(nameof(PropagationTestCases.GetCases), MemberType = typeof(PropagationTestCases))]
        public void Test_Propagation(string Source, string TargetName, string[] ExpectedUsageSubstrings, string[] ExpectedRemainingSubstrings)
        {
            VerifyPropagation(Source, TargetName, ExpectedUsageSubstrings);
        }
    }
}
