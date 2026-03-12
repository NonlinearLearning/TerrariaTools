namespace TerrariaTools.Dome.Analysis.Roslyn;

using TerrariaTools.Dome.Core;

/// <summary>
/// 继承查询服务，提供成员重写、接口实现和继承链查询。
/// </summary>
internal sealed class InheritanceQueryService : IInheritanceQueryService
{
    private readonly HashSet<string> _overrideMembers;
    private readonly HashSet<string> _interfaceMembers;
    private readonly HashSet<string> _inheritanceTypes;

    /// <summary>
    /// 初始化继承查询服务的新实例。
    /// </summary>
    /// <param name="overrideMembers">重写成员集合。</param>
    /// <param name="interfaceMembers">接口实现成员集合。</param>
    /// <param name="inheritanceTypes">继承类型集合。</param>
    public InheritanceQueryService(
        IEnumerable<string> overrideMembers,
        IEnumerable<string> interfaceMembers,
        IEnumerable<string> inheritanceTypes)
    {
        _overrideMembers = new HashSet<string>(overrideMembers, StringComparer.Ordinal);
        _interfaceMembers = new HashSet<string>(interfaceMembers, StringComparer.Ordinal);
        _inheritanceTypes = new HashSet<string>(inheritanceTypes, StringComparer.Ordinal);
    }

    /// <summary>
    /// 判断成员是否为重写成员。
    /// </summary>
    /// <param name="memberId">成员ID。</param>
    /// <returns>如果是重写成员则返回 true，否则返回 false。</returns>
    public bool IsOverrideMember(string memberId) => _overrideMembers.Contains(memberId);

    /// <summary>
    /// 判断成员是否实现了接口成员。
    /// </summary>
    /// <param name="memberId">成员ID。</param>
    /// <returns>如果是接口实现成员则返回 true，否则返回 false。</returns>
    public bool ImplementsInterfaceMember(string memberId) => _interfaceMembers.Contains(memberId);

    /// <summary>
    /// 判断类型是否在继承链中。
    /// </summary>
    /// <param name="typeId">类型ID。</param>
    /// <returns>如果类型在继承链中则返回 true，否则返回 false。</returns>
    public bool IsInInheritanceChain(string typeId) => _inheritanceTypes.Contains(typeId);
}

/// <summary>
/// 引用查询服务，提供符号引用关系的查询。
/// </summary>
internal sealed class ReferenceQueryService : IReferenceQueryService
{
    private readonly Dictionary<string, HashSet<MemberId>> _memberToFunctions;
    private readonly Dictionary<string, HashSet<string>> _memberToTypes;
    private readonly Dictionary<string, HashSet<MemberId>> _typeToFunctions;
    private readonly Dictionary<string, HashSet<string>> _typeToTypes;

    /// <summary>
    /// 初始化引用查询服务的新实例。
    /// </summary>
    /// <param name="memberToFunctions">成员到函数的引用映射。</param>
    /// <param name="memberToTypes">成员到类型的引用映射。</param>
    /// <param name="typeToFunctions">类型到函数的引用映射。</param>
    /// <param name="typeToTypes">类型到类型的引用映射。</param>
    public ReferenceQueryService(
        Dictionary<string, HashSet<MemberId>> memberToFunctions,
        Dictionary<string, HashSet<string>> memberToTypes,
        Dictionary<string, HashSet<MemberId>> typeToFunctions,
        Dictionary<string, HashSet<string>> typeToTypes)
    {
        _memberToFunctions = memberToFunctions;
        _memberToTypes = memberToTypes;
        _typeToFunctions = typeToFunctions;
        _typeToTypes = typeToTypes;
    }

    /// <summary>
    /// 检查指定的符号或成员ID是否有引用。
    /// </summary>
    /// <param name="symbolOrMemberId">符号或成员ID。</param>
    /// <returns>如果有引用则返回 true，否则返回 false。</returns>
    public bool HasReferences(string symbolOrMemberId)
    {
        return _memberToFunctions.ContainsKey(symbolOrMemberId) ||
               _memberToTypes.ContainsKey(symbolOrMemberId) ||
               _typeToFunctions.ContainsKey(symbolOrMemberId) ||
               _typeToTypes.ContainsKey(symbolOrMemberId);
    }

    /// <summary>
    /// 获取引用了指定符号或成员ID的函数列表。
    /// </summary>
    /// <param name="symbolOrMemberId">符号或成员ID。</param>
    /// <returns>引用函数列表。</returns>
    public IReadOnlyList<MemberId> GetReferencingFunctions(string symbolOrMemberId)
    {
        if (_memberToFunctions.TryGetValue(symbolOrMemberId, out var memberFunctions))
        {
            return memberFunctions.OrderBy(item => item.Value, StringComparer.Ordinal).ToArray();
        }

        if (_typeToFunctions.TryGetValue(symbolOrMemberId, out var typeFunctions))
        {
            return typeFunctions.OrderBy(item => item.Value, StringComparer.Ordinal).ToArray();
        }

        return Array.Empty<MemberId>();
    }

    /// <summary>
    /// 获取引用了指定符号或成员ID的类型列表。
    /// </summary>
    /// <param name="symbolOrMemberId">符号或成员ID。</param>
    /// <returns>引用类型列表。</returns>
    public IReadOnlyList<string> GetReferencingTypes(string symbolOrMemberId)
    {
        if (_memberToTypes.TryGetValue(symbolOrMemberId, out var memberTypes))
        {
            return memberTypes.OrderBy(item => item, StringComparer.Ordinal).ToArray();
        }

        if (_typeToTypes.TryGetValue(symbolOrMemberId, out var typeTypes))
        {
            return typeTypes.OrderBy(item => item, StringComparer.Ordinal).ToArray();
        }

        return Array.Empty<string>();
    }
}
