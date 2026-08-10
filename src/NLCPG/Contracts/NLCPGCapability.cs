namespace NLCPG.Contracts;

[Flags]
public enum NLCPGCapability
{
    None = 0,
    SyntaxSemantic = 1 << 0,
    MethodModel = 1 << 1,
    CallTargets = 1 << 2,
    Cfg = 1 << 3,
    DataFlow = 1 << 4,
    Dominance = 1 << 5,
    ControlDependence = 1 << 6,
    QueryIndex = 1 << 7,
    InterproceduralDataFlow = 1 << 8,
    SyntaxToken = 1 << 9,
    Reference = 1 << 10,
    TypeRef = 1 << 11,
    Default = SyntaxSemantic |
              MethodModel |
              CallTargets |
              Cfg |
              DataFlow |
              QueryIndex |
              SyntaxToken |
              Reference |
              TypeRef,
    All = Default | Dominance | ControlDependence | InterproceduralDataFlow,
}
