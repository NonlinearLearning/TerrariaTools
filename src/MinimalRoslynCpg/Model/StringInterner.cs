namespace MinimalRoslynCpg.Model;

public sealed class StringInterner
{
  private readonly Dictionary<string, uint> _idsByText = new(StringComparer.Ordinal);
  private readonly Dictionary<uint, string> _textsById = new();
  private uint _nextId = 1;

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

  public bool TryResolve(uint id, out string text)
  {
    return _textsById.TryGetValue(id, out text!);
  }
}
