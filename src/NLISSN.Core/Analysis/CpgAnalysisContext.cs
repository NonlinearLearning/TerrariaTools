using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NLCPG.Analysis;
using NLCPG.Analysis.FlowSummaries;
using NLCPG.Contracts;
using NLCPG.Model;

namespace NLISSN.Core.Analysis;
//这个也是
/// 结构分析阶段共享的只读上下文。
public sealed record CpgAnalysisContext(
  /// 当前源码对应的主 CPG 图。
  NLCPGGraph Graph,
  /// 当前源码的 Roslyn 语义模型。
  SemanticModel SemanticModel,
  /// 当前编译单元的语法树根节点。
  SyntaxNode CompilationRoot,
  /// 当前图明确构建并可供规则使用的能力集合。
  NLCPGCapability AvailableCapabilities = NLCPGCapability.All,
  /// 规则可注入的受限 CPG 关系查询服务。
  ICpgRelationQueryService? RelationQueryService = null,
  ICallFlowResolver? CallFlowResolver = null);
