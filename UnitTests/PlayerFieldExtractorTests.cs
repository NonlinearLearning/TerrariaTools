using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Xunit;
using TerrariaTools.Analysis;

namespace TerrariaTools.UnitTests
{
    public class PlayerFieldExtractorTests
    {
        [Fact]
        public void Analyze_RealFiles_ShouldExtractFields()
        {
            // Arrange
            string baseDir = @"D:\lodes\TR\Backup\New1.27\5\Terraria";
            string playerPath = Path.Combine(baseDir, "Player.cs");
            string entityPath = Path.Combine(baseDir, "Entity.cs");
            string messageBufferPath = Path.Combine(baseDir, "MessageBuffer.cs");

            if (!File.Exists(playerPath) || !File.Exists(messageBufferPath))
            {
                return;
            }

            var extractor = new PlayerFieldExtractor();
            var sourcePaths = new List<string> { playerPath, entityPath };

            // Act
            var result = extractor.Analyze(sourcePaths, messageBufferPath);

            // Assert
            Assert.NotEmpty(result.AllPlayerFields);
            Assert.NotEmpty(result.ReferencedFields);

            // 验证一些已知的字段
            Assert.Contains("whoAmI", result.ReferencedFields); // 现在应该在 Entity 中找到
            Assert.Contains("name", result.ReferencedFields);
            Assert.Contains("difficulty", result.ReferencedFields);

            // 打印结果以供查看
            Console.WriteLine($"从 Player 及其基类中加载了 {result.AllPlayerFields.Count} 个成员。");
            Console.WriteLine($"在 MessageBuffer.GetData 的 switch 结构中找到了 {result.ReferencedFields.Count} 个引用的 Player 字段。");

            Console.WriteLine("\n引用的字段列表:");
            foreach (var field in result.ReferencedFields.OrderBy(f => f))
            {
                Console.WriteLine($"- {field}");
            }

            if (result.MissingFields.Any())
            {
                Console.WriteLine("\n未在源代码中找到但被引用的成员 (可能是其他基类或解析限制):");
                foreach (var field in result.MissingFields.Distinct().OrderBy(f => f))
                {
                    Console.WriteLine($"- {field}");
                }
            }
        }
    }
}
