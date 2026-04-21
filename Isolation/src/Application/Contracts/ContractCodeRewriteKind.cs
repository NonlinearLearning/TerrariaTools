namespace Application.Contracts;

public enum ContractCodeRewriteKind
{
    Unknown = 0,
    DeleteClass = 1,
    DeleteMethod = 2,
    PrivatizeMethod = 3,
    ClearMethodBody = 4,
}
