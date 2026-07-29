namespace NLISSN.Core.Pipeline;

public interface IRuleOptions
{
    bool TryGetOption(string key, out string value);
}
