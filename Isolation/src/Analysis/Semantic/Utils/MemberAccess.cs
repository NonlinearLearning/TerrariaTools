namespace Analysis.Semantic.Utils;

/// <summary>
/// 判断 Joern 操作符是否属于成员访问。
///
/// 对应 Joern `semanticcpg/utils/MemberAccess.scala`。
/// </summary>
public static class MemberAccess
{
    private static readonly HashSet<string> GenericMemberAccessNames = new(StringComparer.Ordinal)
    {
        "<operator>.memberAccess",
        "<operator>.indirectComputedMemberAccess",
        "<operator>.indirectMemberAccess",
        "<operator>.computedMemberAccess",
        "<operator>.indirection",
        "<operator>.addressOf",
        "<operator>.fieldAccess",
        "<operator>.indirectFieldAccess",
        "<operator>.indexAccess",
        "<operator>.indirectIndexAccess",
        "<operator>.pointerShift",
        "<operator>.getElementPtr",
    };

    private static readonly HashSet<string> FieldAccessNames = new(GenericMemberAccessNames, StringComparer.Ordinal)
    {
        "<operator>.sizeOf",
    };

    /// <summary>
    /// 判断名称是否是泛化成员访问。
    /// </summary>
    public static bool IsGenericMemberAccessName(string name)
    {
        return GenericMemberAccessNames.Contains(name);
    }

    /// <summary>
    /// 判断名称是否是字段/成员访问。
    /// </summary>
    public static bool IsFieldAccess(string name)
    {
        return FieldAccessNames.Contains(name);
    }
}
