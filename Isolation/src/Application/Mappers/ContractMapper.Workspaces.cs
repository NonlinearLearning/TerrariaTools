using Application.Contracts.Workspaces;
using Application.Contracts;
using Domain.Rules;
using Domain.Workspaces;

namespace Application.Mappers;

public static partial class ContractMapper
{
    public static WorkspaceContextDto Map(WorkspaceContext workspaceContext)
    {
        return new WorkspaceContextDto
        {
            Id = workspaceContext.Id,
            SolutionPath = workspaceContext.SolutionPath,
            LanguageVersion = workspaceContext.LanguageVersion,
            Projects = workspaceContext.Projects
                .Select(item => new ProjectItemDto { Name = item.Name, Path = item.Path })
                .ToArray(),
            Documents = workspaceContext.Documents.Select(item => item.Value).ToArray(),
            References = workspaceContext.References
                .Select(item => new ReferenceItemDto { Name = item.Name, Version = item.Version })
                .ToArray(),
            RunMode = Map(workspaceContext.RunMode),
            InputDescriptor = new InputDescriptorDto
            {
                Origin = Map(workspaceContext.InputDescriptor.Origin),
                SourcePath = workspaceContext.InputDescriptor.SourcePath,
                RunMode = Map(workspaceContext.InputDescriptor.RunMode),
            },
            RuleSet = new RuleSetDto
            {
                Name = workspaceContext.RuleSet.Name,
                EnabledRules = workspaceContext.RuleSet.EnabledRules
                    .Select(static rule => new EnabledRuleDto
                    {
                        RuleCode = rule.RuleCode.Value,
                        DisplayName = rule.DisplayName,
                    })
                    .ToArray(),
            },
        };
    }
}
