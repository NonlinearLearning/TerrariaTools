namespace NLCPG.Model;


// 将重复字符串映射为图内整数标识，并支持按标识还原文本。
public sealed class StringInterner
{
    private readonly object _gate = new();
    private readonly Dictionary<string, uint> _idsByText = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, string> _textsById = new();
    private uint _nextId = 1;

    // 为字符串分配图内整数标识；null 使用保留的零值，重复文本复用已分配编号。
    public uint Intern(string? text)
    {
        if (text is null)
        {
            return 0;
        }

        lock (_gate)
        {
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
    }

    // 尝试把整数标识还原为原始字符串；零值不对应任何字符串。
    public bool TryResolve(uint id, out string? text)
    {
        if (id == 0)
        {
            text = null;
            return false;
        }

        lock (_gate)
        {
            return _textsById.TryGetValue(id, out text!);
        }
    }
}
