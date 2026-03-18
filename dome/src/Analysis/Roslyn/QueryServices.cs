namespace TerrariaTools.Dome.Analysis.Roslyn;

using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;

/// <summary>
/// Model-native inheritance query service.
/// </summary>
internal sealed partial class InheritanceQueryService : ModelAnalysis.IInheritanceQueryService
{
    private readonly HashSet<string> _overrideMembers;
    private readonly HashSet<string> _interfaceMembers;
    private readonly HashSet<string> _inheritanceTypes;

    public InheritanceQueryService(
        IEnumerable<string> overrideMembers,
        IEnumerable<string> interfaceMembers,
        IEnumerable<string> inheritanceTypes)
    {
        _overrideMembers = new HashSet<string>(overrideMembers, StringComparer.Ordinal);
        _interfaceMembers = new HashSet<string>(interfaceMembers, StringComparer.Ordinal);
        _inheritanceTypes = new HashSet<string>(inheritanceTypes, StringComparer.Ordinal);
    }

    public bool IsOverrideMember(string memberId) => _overrideMembers.Contains(memberId);

    public bool ImplementsInterfaceMember(string memberId) => _interfaceMembers.Contains(memberId);

    public bool IsInInheritanceChain(string typeId) => _inheritanceTypes.Contains(typeId);
}

/// <summary>
/// Model-native reference query service.
/// </summary>
internal sealed partial class ReferenceQueryService : ModelAnalysis.IReferenceQueryService
{
    private readonly Dictionary<string, HashSet<ModelPrimitives.MemberId>> _memberToFunctions;
    private readonly Dictionary<string, HashSet<string>> _memberToTypes;
    private readonly Dictionary<string, HashSet<ModelPrimitives.MemberId>> _typeToFunctions;
    private readonly Dictionary<string, HashSet<string>> _typeToTypes;

    public ReferenceQueryService(
        Dictionary<string, HashSet<ModelPrimitives.MemberId>> memberToFunctions,
        Dictionary<string, HashSet<string>> memberToTypes,
        Dictionary<string, HashSet<ModelPrimitives.MemberId>> typeToFunctions,
        Dictionary<string, HashSet<string>> typeToTypes)
    {
        _memberToFunctions = memberToFunctions;
        _memberToTypes = memberToTypes;
        _typeToFunctions = typeToFunctions;
        _typeToTypes = typeToTypes;
    }

    public bool HasReferences(string symbolOrMemberId)
    {
        return _memberToFunctions.ContainsKey(symbolOrMemberId) ||
               _memberToTypes.ContainsKey(symbolOrMemberId) ||
               _typeToFunctions.ContainsKey(symbolOrMemberId) ||
               _typeToTypes.ContainsKey(symbolOrMemberId);
    }

    public IReadOnlyList<ModelPrimitives.MemberId> GetReferencingFunctions(string symbolOrMemberId)
    {
        var results = new HashSet<ModelPrimitives.MemberId>();
        if (_memberToFunctions.TryGetValue(symbolOrMemberId, out var memberFunctions))
        {
            results.UnionWith(memberFunctions);
        }

        if (_typeToFunctions.TryGetValue(symbolOrMemberId, out var typeFunctions))
        {
            results.UnionWith(typeFunctions);
        }

        return results.Count == 0
            ? Array.Empty<ModelPrimitives.MemberId>()
            : results.OrderBy(item => item.Value, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<string> GetReferencingTypes(string symbolOrMemberId)
    {
        var results = new HashSet<string>(StringComparer.Ordinal);
        if (_memberToTypes.TryGetValue(symbolOrMemberId, out var memberTypes))
        {
            results.UnionWith(memberTypes);
        }

        if (_typeToTypes.TryGetValue(symbolOrMemberId, out var typeTypes))
        {
            results.UnionWith(typeTypes);
        }

        return results.Count == 0
            ? Array.Empty<string>()
            : results.OrderBy(item => item, StringComparer.Ordinal).ToArray();
    }
}
