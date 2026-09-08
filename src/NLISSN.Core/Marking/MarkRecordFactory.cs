using Microsoft.CodeAnalysis;
using NLISSN.Core.Pipeline;

namespace NLISSN.Core.Marking;

public static class MarkRecordFactory
{
    // 用最小必需字段构造一条种子标记记录，延后图绑定和注解填充。
    public static MarkRecord Create(
      string ruleId,
      SyntaxNode syntaxNode,
      string reason,
      RuleOutputKind? outputKind = null,
      RuleSemanticTag? semanticTag = null,
      RuleEvidenceOrigin origins = RuleEvidenceOrigin.None,
      RuleFactKind? factKind = null)
    {
        return new MarkRecord(
          ruleId,
          syntaxNode,
          null,
          null,
          reason,
          OutputKind: outputKind,
          SemanticTag: semanticTag,
          Origins: origins,
          FactKind: factKind);
    }
}
