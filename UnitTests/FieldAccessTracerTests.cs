using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using TerrariaTools.DynamicAnalysis;
using System.Reflection;

namespace TerrariaTools.UnitTests.DynamicAnalysis
{
    public class FieldAccessTracerTests
    {
        // 模拟被追踪的类
        public class MockPlayer
        {
            public string Name = "TestPlayer";
            public int Health = 100;
            public bool IsActive = true;
        }

        // 模拟被追踪的方法
        public class MockGameLogic
        {
            public void ProcessPlayer(MockPlayer player)
            {
                // 模拟字段访问
                string name = player.Name;
                if (player.IsActive)
                {
                    int hp = player.Health;
                }
            }
        }

        [Fact]
        public void Tracer_ShouldCaptureFieldAccesses_WhenMethodIsCalled()
        {
            // Arrange
            var tracer = new FieldAccessTracer("test.tracer");
            var targetMethod = typeof(MockGameLogic).GetMethod(nameof(MockGameLogic.ProcessPlayer));
            var player = new MockPlayer();
            var logic = new MockGameLogic();

            FieldAccessTracer.ClearRecords();

            // Act
            tracer.StartTracing(targetMethod, new List<Type> { typeof(MockPlayer) });
            logic.ProcessPlayer(player);

            var accessedFields = FieldAccessTracer.GetAccessedFields().ToList();
            tracer.StopAll();

            // Assert
            Assert.Contains("MockPlayer.Name", accessedFields);
            Assert.Contains("MockPlayer.IsActive", accessedFields);
            Assert.Contains("MockPlayer.Health", accessedFields);

            // 验证未访问的字段不会出现（如果 MockPlayer 有其他字段的话）
        }
    }
}
