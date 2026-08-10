using System.Text.Json;
using Workspace.Library;

namespace Workspace.App;
public sealed class AppEntry
{
#if WORKSPACE_DEBUG
    public const string Configuration = "Debug";
#else
    public const string Configuration = "Release";
#endif
    public static string Describe()
    {
        return $"{Configuration}:{LibraryEntry.Value}";
    }
}
