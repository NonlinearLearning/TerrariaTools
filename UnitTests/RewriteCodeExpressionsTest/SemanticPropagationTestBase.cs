/**
 * 功能描述：语义传播单元测试基类，提供模型构建、节点查找和验证逻辑。
 */
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using TerrariaTools.RewriteCodeExpressions;
using TerrariaTools.RewriteCodeExpressions.Pipeline;
using Xunit;

namespace TerrariaTools.UnitTests
{
    public abstract class SemanticPropagationTestBase
    {
        /// <summary>
        /// 根据源码字符串构建语义模型和语法树根节点。
        /// </summary>
        /// <param name="Source">C# 源码字符串</param>
        /// <returns>语义模型和语法树根节点的元组</returns>
        protected (SemanticModel Model, SyntaxNode Root) GetModelAndRoot(string Source)
        {
            var Tree = CSharpSyntaxTree.ParseText(Source);
            var Compilation = CSharpCompilation.Create("Test")
                .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddSyntaxTrees(Tree);
            return (Compilation.GetSemanticModel(Tree), Tree.GetRoot());
        }

        /// <summary>
        /// 在语法树中查找并返回名称匹配的待标记声明节点。
        /// </summary>
        /// <param name="Root">语法树根节点</param>
        /// <param name="TargetName">目标声明的标识符名称</param>
        /// <returns>找到的语法节点集合</returns>
        protected HashSet<SyntaxNode> GetNodesToMark(SyntaxNode Root, string TargetName)
        {
            var Nodes = new HashSet<SyntaxNode>();
            foreach (var Node in Root.DescendantNodesAndSelf())
            {
                if (Node is VariableDeclaratorSyntax V && V.Identifier.ValueText == TargetName)
                    Nodes.Add(V);
                else if (Node is ParameterSyntax P && P.Identifier.ValueText == TargetName)
                    Nodes.Add(P);
                else if (Node is PropertyDeclarationSyntax Pr && Pr.Identifier.ValueText == TargetName)
                    Nodes.Add(Pr);
                else if (Node is MethodDeclarationSyntax M && M.Identifier.ValueText == TargetName)
                    Nodes.Add(M);
                else if (Node is ClassDeclarationSyntax C && C.Identifier.ValueText == TargetName)
                    Nodes.Add(C);
                else if (Node is TypeParameterSyntax Tp && Tp.Identifier.ValueText == TargetName)
                    Nodes.Add(Tp);
                else if (Node is FieldDeclarationSyntax F && F.Declaration.Variables.Any(Vv => Vv.Identifier.ValueText == TargetName))
                    Nodes.Add(F.Declaration.Variables.First(Vv => Vv.Identifier.ValueText == TargetName));
                else if (Node is EnumDeclarationSyntax E && E.Identifier.ValueText == TargetName)
                    Nodes.Add(E);
                else if (Node is InterfaceDeclarationSyntax I && I.Identifier.ValueText == TargetName)
                    Nodes.Add(I);
                else if (Node is UsingDirectiveSyntax U && U.Alias?.Name.Identifier.ValueText == TargetName)
                    Nodes.Add(U);
                else if (Node is SingleVariableDesignationSyntax S && S.Identifier.ValueText == TargetName)
                    Nodes.Add(S);
                else if (Node is CatchDeclarationSyntax Cd && Cd.Identifier.Text == TargetName)
                    Nodes.Add(Cd);
                else if (Node is IndexerDeclarationSyntax Ind && TargetName == "this")
                    Nodes.Add(Ind);
                else if (Node is ForEachStatementSyntax Fe && Fe.Identifier.Text == TargetName)
                    Nodes.Add(Fe);
                else if (Node is IdentifierNameSyntax Id && Id.Identifier.Text == TargetName && Node.Parent is not MemberAccessExpressionSyntax)
                    Nodes.Add(Id);
                else if (Node is GenericNameSyntax G && G.Identifier.Text == TargetName)
                    Nodes.Add(G);
            }
            return Nodes;
        }

        /// <summary>
        /// 执行特定的传播器逻辑。
        /// </summary>
        /// <param name="Model">语义模型</param>
        /// <param name="Root">语法树根节点</param>
        /// <param name="NodesToMark">待标记节点集合</param>
        protected abstract void RunPropagator(SemanticModel Model, SyntaxNode Root, HashSet<SyntaxNode> NodesToMark);

        /// <summary>
        /// 验证传播逻辑是否正确标记了所有预期的引用节点。
        /// </summary>
        /// <param name="Source">C# 源码字符串</param>
        /// <param name="TargetName">初始标记的目标名称</param>
        /// <param name="ExpectedUsageSubstrings">预期被标记的引用节点所包含的字符串</param>
        protected void VerifyPropagation(string Source, string TargetName, string[] ExpectedUsageSubstrings)
        {
            var (Model, Root) = GetModelAndRoot(Source);
            var NodesToMark = GetNodesToMark(Root, TargetName);
            Assert.NotEmpty(NodesToMark);

            int LastCount;
            do
            {
                LastCount = NodesToMark.Count;
                RunPropagator(Model, Root, NodesToMark);

                var UpwardCollector = new UpwardMarkCollector(NodesToMark);
                UpwardCollector.Visit(Root);

            } while (NodesToMark.Count > LastCount);

            foreach (var Substring in ExpectedUsageSubstrings)
            {
                bool Found = NodesToMark.Any(N => N.ToString().Contains(Substring));
                Assert.True(Found, $"Expected to find marked node containing '{Substring}' in source: {Source}");
            }
        }
    }
}
