namespace Logic.Marking;

/// <summary>
/// 定义变更候选生成器。
/// </summary>
public interface IChangeCandidateMarker
{
    RuleExecutionResult Execute(RuleExecutionInput input);
}
