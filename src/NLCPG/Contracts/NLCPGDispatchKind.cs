namespace NLCPG.Contracts;

//
public enum NLCPGDispatchCategory
{
    Method,
    DecisionAction,
}

[Flags]
public enum NLCPGDispatchFlags
{
    None = 0,
    Internal = 1 << 0,
    External = 1 << 1,
    Static = 1 << 2,
    Instance = 1 << 3,
    Extension = 1 << 4,
    Interface = 1 << 5,
    InterfaceImplementation = 1 << 6,
    Override = 1 << 7,
    Virtual = 1 << 8,
    Abstract = 1 << 9,
    Definition = 1 << 10,
    Dispatch = 1 << 11,
    PropertyGet = 1 << 12,
    PropertySet = 1 << 13,
    PropertyAccessor = 1 << 14,
    Indexer = 1 << 15,
    Exact = 1 << 16,
    ReceiverExact = 1 << 17,
    Hierarchy = 1 << 18,
    Fallback = 1 << 19,
    ExternalFallback = 1 << 20,
}
//决策种类
public enum NLCPGDecisionActionKind
{
    Skip = 0,
    Delete = 1,
    Replace = 2,
}

public readonly record struct NLCPGDispatchKind(NLCPGDispatchCategory Category, NLCPGDispatchFlags Flags = NLCPGDispatchFlags.None, NLCPGDecisionActionKind? Action = null)
{
    // 为决策动作构造不带方法分派标志的调度描述。
    public static NLCPGDispatchKind ForDecisionAction(NLCPGDecisionActionKind action)
    {
        return new NLCPGDispatchKind(NLCPGDispatchCategory.DecisionAction, NLCPGDispatchFlags.None, action);
    }

    // 将当前调度信息格式化成稳定的字符串标签。
    public override string ToString()
    {
        return Category switch
        {
            NLCPGDispatchCategory.Method => FormatMethodDispatch(),
            NLCPGDispatchCategory.DecisionAction => Action?.ToString() ?? string.Empty,
            _ => string.Empty,
        };
    }

    private string FormatMethodDispatch()
    {
        var segments = new List<string>();
        if ((Flags & NLCPGDispatchFlags.Internal) != 0)
        {
            segments.Add("internal");
        }
        else if ((Flags & NLCPGDispatchFlags.External) != 0)
        {
            segments.Add("external");
        }

        if ((Flags & NLCPGDispatchFlags.Extension) != 0)
        {
            segments.Add((Flags & NLCPGDispatchFlags.Instance) != 0
              ? "extension-instance"
              : "extension-static");
        }
        else if ((Flags & NLCPGDispatchFlags.InterfaceImplementation) != 0)
        {
            segments.Add("interface-implementation");
        }
        else if ((Flags & NLCPGDispatchFlags.Interface) != 0)
        {
            segments.Add((Flags & NLCPGDispatchFlags.Definition) != 0
              ? "interface-definition"
              : "interface-dispatch");
        }
        else if ((Flags & NLCPGDispatchFlags.Override) != 0)
        {
            segments.Add((Flags & NLCPGDispatchFlags.Definition) != 0
              ? "override-definition"
              : "override-dispatch");
        }
        else if ((Flags & NLCPGDispatchFlags.Abstract) != 0)
        {
            segments.Add("abstract-definition");
        }
        else if ((Flags & NLCPGDispatchFlags.Virtual) != 0)
        {
            segments.Add((Flags & NLCPGDispatchFlags.Definition) != 0
              ? "virtual-definition"
              : "virtual-dispatch");
        }
        else if ((Flags & NLCPGDispatchFlags.Definition) != 0)
        {
            segments.Add((Flags & NLCPGDispatchFlags.Static) != 0
              ? "static-definition"
              : "instance-definition");
        }
        else
        {
            segments.Add("static");
        }

        if ((Flags & NLCPGDispatchFlags.PropertyGet) != 0)
        {
            segments.Add((Flags & NLCPGDispatchFlags.Indexer) != 0
              ? "indexer-get"
              : "property-get");
        }
        else if ((Flags & NLCPGDispatchFlags.PropertySet) != 0)
        {
            segments.Add((Flags & NLCPGDispatchFlags.Indexer) != 0
              ? "indexer-set"
              : "property-set");
        }
        else if ((Flags & NLCPGDispatchFlags.PropertyAccessor) != 0)
        {
            segments.Add((Flags & NLCPGDispatchFlags.Indexer) != 0
              ? "indexer-accessor"
              : "property-accessor");
        }

        if ((Flags & NLCPGDispatchFlags.ExternalFallback) != 0)
        {
            segments.Add("external-fallback");
        }
        else if ((Flags & NLCPGDispatchFlags.Exact) != 0)
        {
            segments.Add("exact");
        }
        else if ((Flags & NLCPGDispatchFlags.ReceiverExact) != 0)
        {
            segments.Add("receiver-exact");
        }
        else if ((Flags & NLCPGDispatchFlags.Hierarchy) != 0)
        {
            segments.Add("hierarchy");
        }
        else if ((Flags & NLCPGDispatchFlags.Fallback) != 0)
        {
            segments.Add("fallback");
        }

        return string.Join("-", segments);
    }
}
