using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions;

namespace Example
{
    /// <summary>
    /// 演示交互式重构流程：让用户在运行时决定是否应用某个重构。
    /// </summary>
    public class InteractiveRefactoringExample
    {
        public void Run()
        {
            string source = @"
class Hero {
    void Update() {
        if (Health < 10) {
            RunAway();
            PlaySound(""Scream"");
        }
        Attack();
    }
    int Health = 100;
    void RunAway() { }
    void PlaySound(string name) { }
    void Attack() { }
}";

            Console.WriteLine("=== 源代码 ===");
            Console.WriteLine(source);

            var tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetRoot();

            // 收集所有方法调用
            var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
            var nodesToRemove = new HashSet<SyntaxNode>();

            Console.WriteLine("\n=== 交互式选择 ===");
            foreach (var inv in invocations)
            {
                Console.Write($"发现调用 '{inv}': 是否移除? (y/n) ");
                var key = Console.ReadLine();
                if (key?.ToLower() == "y")
                {
                    nodesToRemove.Add(inv);
                }
            }

            if (nodesToRemove.Count > 0)
            {
                // 使用 ExpressionSimplifier 执行选定的移除
                // 这里我们直接传入一个基于 HashSet 的谓词
                Func<SyntaxNode, bool> shouldRemove = n => nodesToRemove.Contains(n);
                var newRoot = ExpressionProcessor.RemoveParts(root, shouldRemove);

                Console.WriteLine("\n=== 重构结果 ===");
                Console.WriteLine(newRoot?.ToFullString());
            }
            else
            {
                Console.WriteLine("\n未进行任何更改。");
            }
        }
    }
}
