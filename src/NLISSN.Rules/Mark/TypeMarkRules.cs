using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLISSN.Core.Marking;

namespace NLISSN.Rules;

/// 删除类规则的 seed mark 入口：分别定位声明、表达式和 TypeSyntax，后续阶段再决定删除范围。
public sealed class ClassDeclarationMarkRule : RuleDefinitionMark
{
    public override string CapabilityId { get; } = "mark.type.declaration";

    public override string RuleId { get; } = "DEL-CLASS-MARK-DECL-001";

    public override RuleFactDomain FactDomain => RuleFactDomain.Class;


    public override string Name { get; } = "Match class declarations by delete-class option";

    public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
      new[] { SyntaxKind.ClassDeclaration };

    // 仅对名称直接匹配 delete-class 目标的类声明生成 seed mark。
    public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
    {
        return DeleteClassMarkRuleHelpers.BuildDeclarationMarks(context, root, RuleId);
    }
}

/// 标记绑定到目标类的表达式，避免把整个宿主语句作为初始删除单位。
public sealed class ClassExpressionMarkRule : RuleDefinitionMark
{
    private static readonly IReadOnlyList<SyntaxKind> SupportedKinds =
      new[]
      {
        SyntaxKind.IdentifierName,
        SyntaxKind.SimpleMemberAccessExpression,
        SyntaxKind.MemberBindingExpression,
        SyntaxKind.InvocationExpression,
        SyntaxKind.ElementAccessExpression,
        SyntaxKind.ConditionalAccessExpression,
        SyntaxKind.ObjectCreationExpression,
        SyntaxKind.ImplicitObjectCreationExpression
      };

    public override string CapabilityId { get; } = "mark.type.expression";

    public override string RuleId { get; } = "DEL-CLASS-MARK-EXPR-001";

    public override RuleFactDomain FactDomain => RuleFactDomain.Class;


    public override string Name { get; } = "Match expressions that reference the delete-class target";

    public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds => SupportedKinds;

    // 标记语义上引用目标类的表达式，并过滤掉会被更大宿主覆盖的重复命中。
    public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
    {
        return DeleteClassMarkRuleHelpers.BuildExpressionMarks(
          context,
          root,
          RuleId,
          SupportedKinds);
    }
}

/// 标记目标类的类型语法，供声明宿主传播规则收束为可改写声明。
public sealed class ClassTypeSyntaxMarkRule : RuleDefinitionMark
{
    private static readonly IReadOnlyList<SyntaxKind> SupportedKinds =
      new[]
      {
        SyntaxKind.IdentifierName,
        SyntaxKind.QualifiedName,
        SyntaxKind.AliasQualifiedName,
        SyntaxKind.GenericName
      };

    public override string CapabilityId { get; } = "mark.type.type-syntax";

    public override string RuleId { get; } = "DEL-CLASS-MARK-TYPE-001";

    public override RuleFactDomain FactDomain => RuleFactDomain.Class;


    public override string Name { get; } = "Match type syntax that references the delete-class target";

    public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds => SupportedKinds;

    // 标记处在声明位置上的目标类 TypeSyntax，供声明宿主传播规则继续收束。
    public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
    {
        return DeleteClassMarkRuleHelpers.BuildTypeSyntaxMarks(
          context,
          root,
          RuleId,
          SupportedKinds);
    }
}
