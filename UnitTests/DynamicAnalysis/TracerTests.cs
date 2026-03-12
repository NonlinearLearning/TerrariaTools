using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using TerrariaTools.DynamicAnalysis;
using System.Reflection;

namespace TerrariaTools.UnitTests.DynamicAnalysis
{
    public class TracerTests
    {
        public class MockPlayer
        {
            public string Name = "TestPlayer";
            public int Health = 100;
            public bool IsActive = true;
        }

        public class MockGameLogic
        {
            public void ProcessPlayer(MockPlayer player)
            {
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
        }
    }
}
