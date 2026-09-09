using Microsoft.CodeAnalysis;
using NLCPG.Analysis.FlowSummaries;

namespace NLISSN.Application;

/// Creates the default call-flow resolver for one Roslyn compilation.
public static class FlowSummaryResolverFactory
{
    public static ICallFlowResolver Create(Compilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        return new CallFlowResolver(new NLCPGFlowSummaryRegistry(
            projectSummaries: null,
            userSummaries: null,
            frameworkSummaries: NLCPGDefaultFlowSummaries.CreateForCompilation(compilation)));
    }
}
