namespace Application.Contracts;

public sealed record ContractExternalCallerPresenceDto(IReadOnlyCollection<string> Callers)
{
    public bool Exists => Callers.Count > 0;
}
