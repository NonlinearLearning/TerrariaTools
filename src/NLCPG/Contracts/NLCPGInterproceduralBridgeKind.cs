namespace NLCPG.Contracts;

/// 标识跨过程流边表示的已解析边界关系。
public enum NLCPGInterproceduralBridgeKind
{
    ArgumentToParameter,
    ReturnToMethodReturn,
    MethodReturnToCallResult,
}
