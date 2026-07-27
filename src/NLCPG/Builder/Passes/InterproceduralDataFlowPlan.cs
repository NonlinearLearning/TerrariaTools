using NLCPG.Contracts;
using NLCPG.Model;

namespace NLCPG.Builder.Passes;

/// 本地数据流稳定后产出的跨过程边计划。
/// 该记录只保存已排序、可确定的边界连接候选，供后续统一提交。
/// 怎么全是plan之类的命名
internal sealed record InterproceduralDataFlowPlan(
  NLCPGNode CallSiteNode,
  NLCPGNode TargetMethodNode,
  NLCPGNode SourceNode,
  NLCPGNode TargetNode,
  NLCPGInterproceduralBridgeKind BridgeKind,
  int ArgumentOrdinal = -1);
