using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Analysis
{
    /// <summary>
    /// 成员级切片重写器。
    /// 继承自 CSharpSyntaxRewriter，根据给定的“必要符号集”移除未使用的类成员。
    /// 实现了“重写思路.txt”中的死代码消除和精细化提取。
    /// </summary>
    public class MemberSlicingRewriter : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _semanticModel;
        private readonly HashSet<ISymbol> _necessarySymbols;

        public MemberSlicingRewriter(SemanticModel semanticModel, IEnumerable<ISymbol> necessarySymbols)
        {
            _semanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
            _necessarySymbols = new HashSet<ISymbol>(necessarySymbols, SymbolEqualityComparer.Default);
        }

        /// <summary>
        /// 访问方法声明。如果该方法不在必要符号集中，则将其移除。
        /// </summary>
        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var symbol = _semanticModel.GetDeclaredSymbol(node);
            if (symbol != null && !_necessarySymbols.Contains(symbol))
            {
                return null; // 移除未引用的方法
            }
            return base.VisitMethodDeclaration(node);
        }

        /// <summary>
        /// 访问字段声明。仅保留在必要符号集中的变量。
        /// </summary>
        public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            // 一个字段声明可能包含多个变量（如 int a, b;）
            var variablesToKeep = node.Declaration.Variables.Where(v =>
            {
                var symbol = _semanticModel.GetDeclaredSymbol(v);
                return symbol != null && _necessarySymbols.Contains(symbol);
            }).ToList();

            if (variablesToKeep.Count == 0)
            {
                return null; // 整个声明都不需要
            }

            if (variablesToKeep.Count == node.Declaration.Variables.Count)
            {
                return base.VisitFieldDeclaration(node); // 全部保留
            }

            // 部分保留
            return node.WithDeclaration(node.Declaration.WithVariables(SyntaxFactory.SeparatedList(variablesToKeep)));
        }

        /// <summary>
        /// 访问属性声明。
        /// </summary>
        public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            var symbol = _semanticModel.GetDeclaredSymbol(node);
            if (symbol != null && !_necessarySymbols.Contains(symbol))
            {
                return null;
            }
            return base.VisitPropertyDeclaration(node);
        }

        /// <summary>
        /// 访问类声明。如果类本身不在必要符号集中，但其成员被引用，类仍需保留。
        /// 这里的逻辑是：如果一个类没有任何成员被保留，且类本身也没被直接引用（作为类型），则可能需要移除。
        /// 但通常类名会在成员访问时被引用，所以这里主要处理成员过滤。
        /// </summary>
        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var newNode = (ClassDeclarationSyntax?)base.VisitClassDeclaration(node);
            if (newNode == null) return null;

            // 如果类已经变空（没有任何成员），我们可能仍然需要保留它（作为占位符），
            // 或者根据更高级别的逻辑移除。
            return newNode;
        }
    }
}
