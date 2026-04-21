namespace Application.Contracts;

public enum ContractCandidateKind
{
    Unknown = 0,
    Type = 1,
    Method = 2,
    Member = 3,
    Caller = 4,
    ClosureRoot = 5,
}
