namespace Logic.Analysis.Engine.X2Cpg.DataStructures;

/// <summary>
/// 保存预扫描或类型桩得到的程序摘要。
///
/// 对应 Joern `ProgramSummary.scala` 的核心能力：按命名空间保存类型，并支持
/// 类型、方法、字段解析。
/// </summary>
public sealed class ProgramSummary
{
    private readonly Dictionary<string, List<TypeSummary>> namespaceToType = new(StringComparer.Ordinal);
    private readonly HashSet<TypeSummary> typesInScope = new();
    private readonly HashSet<IMemberSummary> membersInScope = new();
    private readonly Dictionary<string, string> aliasedTypes = new(StringComparer.Ordinal);

    /// <summary>
    /// 添加类型到命名空间。重名类型会被合并。
    /// </summary>
    public void AddType(string namespaceName, TypeSummary type)
    {
        ArgumentNullException.ThrowIfNull(type);
        namespaceName ??= string.Empty;

        if (!namespaceToType.TryGetValue(namespaceName, out List<TypeSummary>? types))
        {
            types = new List<TypeSummary>();
            namespaceToType[namespaceName] = types;
        }

        TypeSummary? existing = types.FirstOrDefault(candidate => candidate.Name == type.Name);
        if (existing is null)
        {
            types.Add(type);
        }
        else
        {
            existing.Merge(type);
        }
    }

    /// <summary>
    /// 获取命名空间下的类型。
    /// </summary>
    public IReadOnlyList<TypeSummary> TypesUnderNamespace(string namespaceName)
    {
        return namespaceToType.TryGetValue(namespaceName ?? string.Empty, out List<TypeSummary>? types)
            ? types
            : Array.Empty<TypeSummary>();
    }

    /// <summary>
    /// 查找类型所属命名空间。
    /// </summary>
    public string? NamespaceFor(TypeSummary type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return namespaceToType.FirstOrDefault(pair => pair.Value.Contains(type)).Key;
    }

    /// <summary>
    /// 按短名、部分限定名或完整名匹配类型。
    /// </summary>
    public IReadOnlyList<TypeSummary> MatchingTypes(string typeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        string[] suffix = typeName.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return namespaceToType.Values.SelectMany(types => types)
            .Where(type => EndsWith(type.Name.Split('.', StringSplitOptions.RemoveEmptyEntries), suffix))
            .ToArray();
    }

    /// <summary>
    /// 吸收另一个摘要。
    /// </summary>
    public void Absorb(ProgramSummary other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach ((string namespaceName, List<TypeSummary> types) in other.namespaceToType)
        {
            foreach (TypeSummary type in types)
            {
                AddType(namespaceName, type);
            }
        }
    }

    /// <summary>
    /// 将命名空间下所有类型加入当前可见集合。
    /// </summary>
    public void AddImportedNamespace(string namespaceName)
    {
        foreach (TypeSummary type in TypesUnderNamespace(namespaceName))
        {
            typesInScope.Add(type);
        }
    }

    /// <summary>
    /// 将匹配类型加入当前可见集合。
    /// </summary>
    public void AddImportedTypeOrModule(string typeOrModule)
    {
        foreach (TypeSummary type in MatchingTypes(typeOrModule))
        {
            typesInScope.Add(type);
        }
    }

    /// <summary>
    /// 添加类型别名。
    /// </summary>
    public void AddTypeAlias(string alias, string fullName)
    {
        aliasedTypes[alias] = fullName;
    }

    /// <summary>
    /// 将成员加入当前可见集合。
    /// </summary>
    public void AddImportedMember(string typeOrModule, params string[] memberNames)
    {
        HashSet<string> requested = memberNames.ToHashSet(StringComparer.Ordinal);
        foreach (TypeSummary type in MatchingTypes(typeOrModule))
        {
            foreach (IMemberSummary member in type.Fields.Cast<IMemberSummary>().Concat(type.Methods))
            {
                if (requested.Count == 0 || requested.Contains(member.Name))
                {
                    membersInScope.Add(member);
                }
            }
        }
    }

    /// <summary>
    /// 解析类型引用。
    /// </summary>
    public TypeSummary? TryResolveTypeReference(string typeName)
    {
        string actualName = aliasedTypes.TryGetValue(typeName, out string? aliasTarget) ? aliasTarget : typeName;
        return typesInScope.FirstOrDefault(type => EndsWith(type.Name.Split('.'), actualName.Split('.'))) ??
               MatchingTypes(actualName).FirstOrDefault();
    }

    /// <summary>
    /// 解析方法调用。
    /// </summary>
    public MethodSummary? TryResolveMethodInvocation(string callName, string? typeFullName = null)
    {
        if (typeFullName is null)
        {
            return membersInScope.OfType<MethodSummary>().FirstOrDefault(method => method.Name == callName) ??
                   typesInScope.SelectMany(type => type.Methods).FirstOrDefault(method => method.Name == callName);
        }

        return TryResolveTypeReference(typeFullName)?.Methods.FirstOrDefault(method => method.Name == callName);
    }

    /// <summary>
    /// 解析字段访问。
    /// </summary>
    public FieldSummary? TryResolveFieldAccess(string fieldName, string? typeFullName = null)
    {
        if (typeFullName is null)
        {
            return membersInScope.OfType<FieldSummary>().FirstOrDefault(field => field.Name == fieldName);
        }

        return TryResolveTypeReference(typeFullName)?.Fields.FirstOrDefault(field => field.Name == fieldName);
    }

    private static bool EndsWith(IReadOnlyList<string> value, IReadOnlyList<string> suffix)
    {
        if (suffix.Count > value.Count)
        {
            return false;
        }

        for (int index = 1; index <= suffix.Count; index++)
        {
            if (!string.Equals(value[^index], suffix[^index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// 表示可解析成员摘要。
/// </summary>
public interface IMemberSummary
{
    string Name { get; }
}

/// <summary>
/// 表示类型摘要。
/// </summary>
public sealed class TypeSummary
{
    public TypeSummary(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public List<MethodSummary> Methods { get; } = new();

    public List<FieldSummary> Fields { get; } = new();

    public void Merge(TypeSummary other)
    {
        foreach (MethodSummary method in other.Methods.Where(method => Methods.All(existing => existing.Name != method.Name || existing.Signature != method.Signature)))
        {
            Methods.Add(method);
        }

        foreach (FieldSummary field in other.Fields.Where(field => Fields.All(existing => existing.Name != field.Name)))
        {
            Fields.Add(field);
        }
    }
}

/// <summary>
/// 表示方法摘要。
/// </summary>
public sealed record MethodSummary(string Name, string Signature) : IMemberSummary;

/// <summary>
/// 表示字段摘要。
/// </summary>
public sealed record FieldSummary(string Name, string TypeFullName) : IMemberSummary;
