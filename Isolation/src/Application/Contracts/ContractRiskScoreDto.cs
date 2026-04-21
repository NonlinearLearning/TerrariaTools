namespace Application.Contracts;

public sealed record ContractRiskScoreDto(int Score, string Reason)
{
    public bool IsHighRisk => Score >= 80;
}
