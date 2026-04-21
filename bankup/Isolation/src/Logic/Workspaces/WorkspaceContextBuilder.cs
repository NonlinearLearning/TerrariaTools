using Domain.Workspaces;
using Domain.Rules;
using Logic.Rules;

namespace Logic.Workspaces;

/// <summary>
/// 工作区上下文构造器。
/// </summary>
public sealed class WorkspaceContextBuilder : IWorkspaceContextBuilder
{
    private static readonly RuleExecutionPolicy DefaultRuleSetPolicy = new(
        RuleParticipationMode.Candidate,
        RuleConflictMode.PreferHigherPriority,
        RuleFailureMode.Warn,
        RuleSafetyLevel.Balanced,
        RuleEvidenceMode.AttachReason);

    private readonly IRulePresetProvider rulePresetProvider;
    private readonly IWorkspaceRuleDefaultsBuilder workspaceRuleDefaultsBuilder;

    public WorkspaceContextBuilder(
        IRulePresetProvider rulePresetProvider,
        IWorkspaceRuleDefaultsBuilder workspaceRuleDefaultsBuilder)
    {
        this.rulePresetProvider = rulePresetProvider;
        this.workspaceRuleDefaultsBuilder = workspaceRuleDefaultsBuilder;
    }


    public WorkspaceContext Build(WorkspaceContextBuildInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        WorkspaceContext workspaceContext = WorkspaceContext.Create(
            input.SolutionPath,
            input.LanguageVersion,
            input.RunMode,
            input.InputDescriptor ?? BuildInputDescriptor(input),
            BuildRuleSet(input));

        foreach (ProjectDescriptor project in input.Projects)
        {
            workspaceContext.RegisterProject(project);
        }

        foreach (string document in input.Documents)
        {
            workspaceContext.RegisterDocument(DocumentPath.Create(document));
        }

        foreach (ReferenceDescriptor reference in input.References)
        {
            workspaceContext.RegisterReference(reference);
        }

        return workspaceContext;
    }

    private RuleSet BuildRuleSet(WorkspaceContextBuildInput input)
    {
        RuleSet ruleSet = input.InputDescriptor?.RuleSet ?? RuleSet.Create("default", DefaultRuleSetPolicy);
        IReadOnlyCollection<EnabledRule> enabledRules = workspaceRuleDefaultsBuilder.Build(MergeRuleInputs(input));
        foreach (EnabledRule enabledRule in enabledRules)
        {
            if (!ruleSet.ContainsRule(enabledRule.RuleCode))
            {
                ruleSet.AddRule(enabledRule);
            }
        }

        return ruleSet;
    }

    private IReadOnlyCollection<WorkspaceEnabledRuleInput> MergeRuleInputs(WorkspaceContextBuildInput input)
    {
        Dictionary<string, WorkspaceEnabledRuleInput> mergedInputs = new(StringComparer.OrdinalIgnoreCase);

        AddInputs(rulePresetProvider.GetWorkspaceDefaults(), mergedInputs);
        AddInputs(input.RuleInputs, mergedInputs);

        return mergedInputs.Values.ToArray();
    }

    private static void AddInputs(
        IReadOnlyCollection<WorkspaceEnabledRuleInput> inputs,
        IDictionary<string, WorkspaceEnabledRuleInput> mergedInputs)
    {
        foreach (WorkspaceEnabledRuleInput input in inputs)
        {
            ArgumentNullException.ThrowIfNull(input);
            mergedInputs[input.RuleCode] = new WorkspaceEnabledRuleInput
            {
                RuleCode = input.RuleCode,
                DisplayName = input.DisplayName,
            };
        }
    }

    private InputDescriptor BuildInputDescriptor(WorkspaceContextBuildInput input)
    {
        string sourcePath = input.Projects.FirstOrDefault()?.Path
            ?? input.Documents.FirstOrDefault()
            ?? input.SolutionPath;
        InputOrigin origin = input.Projects.Count > 0
            ? InputOrigin.Project
            : input.Documents.Count > 0
                ? InputOrigin.SourceFile
                : InputOrigin.Solution;

        return InputDescriptor.Create(origin, sourcePath, input.RunMode, BuildRuleSet(input));
    }
}
