using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Analysis
{
    /// <summary>
    /// 分析 MessageBuffer.cs 以提取 GetData 方法中引用的 Player 字段。
    /// 升级版：接入 Roslyn 语义模型，实现精准的跨文件符号分析。
    /// </summary>
    public class PlayerFieldExtractor : IPlayerFieldExtractor
    {
        private readonly Solution _solution;

        public class AnalysisResult
        {
            public HashSet<ISymbol> ReferencedSymbols { get; set; } = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            public HashSet<string> ReferencedFieldNames { get; set; } = new HashSet<string>();
            public List<string> UnresolvedReferences { get; set; } = new List<string>();
        }

        public PlayerFieldExtractor(Solution solution)
        {
            _solution = solution ?? throw new ArgumentNullException(nameof(solution));
        }

        /// <summary>
        /// 执行深度语义分析，提取指定方法中对特定类型成员的所有引用。
        /// </summary>
        /// <param name="targetTypeName">目标类型全名（如 "Terraria.Player"）</param>
        /// <param name="containerTypeName">包含目标方法的类名（如 "Terraria.MessageBuffer"）</param>
        /// <param name="methodName">目标方法名（如 "GetData"）</param>
        public async Task<AnalysisResult> AnalyzeAsync(string targetTypeName = "Terraria.Player", string containerTypeName = "Terraria.MessageBuffer", string methodName = "GetData")
        {
            var result = new AnalysisResult();

            foreach (var project in _solution.Projects)
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                // 寻找目标类型
                var targetTypeSymbol = compilation.GetTypeByMetadataName(targetTypeName);

                // 寻找包含方法的类
                var containerTypeSymbol = compilation.GetTypeByMetadataName(containerTypeName);
                if (containerTypeSymbol == null) continue;

                // 在当前项目中寻找对应的语法树
                foreach (var tree in compilation.SyntaxTrees)
                {
                    var root = await tree.GetRootAsync();
                    var model = compilation.GetSemanticModel(tree);

                    // 寻找目标方法声明
                    var methodDecls = root.DescendantNodes()
                        .OfType<MethodDeclarationSyntax>()
                        .Where(m => m.Identifier.Text == methodName);

                    foreach (var methodDecl in methodDecls)
                    {
                        var methodSymbol = model.GetDeclaredSymbol(methodDecl);
                        if (methodSymbol == null || !SymbolEqualityComparer.Default.Equals(methodSymbol.ContainingType, containerTypeSymbol))
                            continue;

                        // 分析方法体中的所有成员访问
                        var memberAccesses = methodDecl.DescendantNodes().OfType<MemberAccessExpressionSyntax>();
                        foreach (var access in memberAccesses)
                        {
                            var symbolInfo = model.GetSymbolInfo(access.Name);
                            var symbol = symbolInfo.Symbol;

                            if (symbol != null)
                            {
                                // 检查该成员是否属于目标类型（或其基类）
                                if (IsMemberOfTargetType(symbol, targetTypeSymbol))
                                {
                                    result.ReferencedSymbols.Add(symbol);
                                    result.ReferencedFieldNames.Add(symbol.Name);
                                }
                            }
                            else
                            {
                                // 记录无法解析的引用（可能是由于符号丢失或编译错误）
                                result.UnresolvedReferences.Add(access.ToString());
                            }
                        }
                    }
                }
            }

            return result;
        }

        private bool IsMemberOfTargetType(ISymbol memberSymbol, INamedTypeSymbol? targetType)
        {
            if (targetType == null) return false;

            var containingType = memberSymbol.ContainingType;
            if (containingType == null) return false;

            // 检查是否是目标类型本身或其基类
            var current = targetType;
            while (current != null)
            {
                if (SymbolEqualityComparer.Default.Equals(containingType, current))
                    return true;
                current = current.BaseType;
            }

            return false;
        }
    }
}
