using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Rules;

/// 方法声明及其删除参数后的替换语法对。
public sealed record MethodRewrite(
  MethodDeclarationSyntax Method,
  MethodDeclarationSyntax ReplacementMethod);
