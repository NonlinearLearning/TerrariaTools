namespace NLCPG.Model;


//字符串映射数字还原
public sealed class StringInterner
{
    private readonly Dictionary<string, uint> _idsByText = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, string> _textsById = new();
    private uint _nextId = 1;

    // 为字符串分配稳定整数标识；重复文本复用已分配编号。
    public uint Intern(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_idsByText.TryGetValue(text, out var existing))
        {
            return existing;
        }

        var id = _nextId;
        _nextId += 1;
        _idsByText[text] = id;
        _textsById[id] = text;
        return id;
    }

    // 尝试把整数标识还原为原始字符串。
    public bool TryResolve(uint id, out string text)
    {
        return _textsById.TryGetValue(id, out text!);
    }
}
