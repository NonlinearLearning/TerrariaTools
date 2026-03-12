using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using TerrariaTools.Analysis.Dome;
using TerrariaTools.Rules.Dome;

namespace TerrariaTools.Rules.Dome.Mark.StaticRules
{
    /// <summary>
    /// 函数标记规则。
    /// 根据引用情况、继承关系和函数体状态，为函数打上“删除”或“添加返回值”的标记。
    /// 复用了 InheritanceAnalyzer (继承分析) 和 ReferenceAnalyzer (引用分析)。
    /// </summary>
    public class FunctionMarkingRule
    {
        public string Name => "函数标记规则";

        /// <summary>
        /// 异步分析并标记函数声明（自动进行引用分析）。
        /// </summary>
        public async Task<MethodDeclarationSyntax> MarkMethodAsync(MethodDeclarationSyntax methodDeclaration, SemanticModel model, Solution solution)
        {
            var symbol = model.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;
            if (symbol == null) return methodDeclaration;

            // 自动分析引用情况 (复用 ReferenceAnalyzer)
            bool hasReferences = await ReferenceAnalyzer.HasReferencesAsync(symbol, solution);

            return MarkMethod(methodDeclaration, model, hasReferences);
        }

        /// <summary>
        /// 同步分析并标记函数声明（需手动传入引用状态）。
        /// </summary>
        public MethodDeclarationSyntax MarkMethod(MethodDeclarationSyntax methodDeclaration, SemanticModel model, bool hasReferences)
        {
            var symbol = model.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;
            if (symbol == null) return methodDeclaration;

            // 1. 无引用的情况
            if (!hasReferences)
            {
                // 复用 InheritanceAnalyzer 检查继承链
                // 排除 Override, Virtual, Abstract, 或实现接口的函数
                if (!InheritanceAnalyzer.IsInInheritanceChain(symbol))
                {
                    // 标记为删除
                    var deleteAnnotation = new SyntaxAnnotation(RuleConstants.RewriteAnnotationKind, RuleConstants.ActionDelete);
                    return methodDeclaration.WithAdditionalAnnotations(deleteAnnotation);
                }
            }
            // 2. 有引用的情况
            else
            {
                // 判断是否为空函数体
                bool isConcreteMethodWithEmptyBody = methodDeclaration.Body != null && !methodDeclaration.Body.Statements.Any();

                if (isConcreteMethodWithEmptyBody)
                {
                    if (!symbol.ReturnsVoid)
                    {
                        // 非 void 类型且为空体，标记为“添加返回值”
                        string returnTypeName = symbol.ReturnType.ToDisplayString();
                        string defaultValue = GetDefaultValueForType(symbol.ReturnType);

                        var addReturnAnnotation = new SyntaxAnnotation(RuleConstants.RewriteAnnotationKind,
                            $"Action=AddReturn|ReturnType={returnTypeName}|DefaultValue={defaultValue}");

                        return methodDeclaration.WithAdditionalAnnotations(addReturnAnnotation);
                    }
                }
            }

            return methodDeclaration;
        }

        /// <summary>
        /// 根据类型获取默认返回值的字符串表示。
        /// </summary>
        private string GetDefaultValueForType(ITypeSymbol type)
        {
            if (type.IsReferenceType) return "null";
            if (type.NullableAnnotation == NullableAnnotation.Annotated) return "null";

            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean: return "false";
                case SpecialType.System_SByte:
                case SpecialType.System_Byte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Decimal:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                    return "0";
                case SpecialType.System_Char:
                    return "'\\0'";
                case SpecialType.System_DateTime:
                    return "DateTime.MinValue";
            }

            if (type.IsValueType) return "default";
            return "null";
        }
    }
}
