namespace TerrariaTools.Dome.Adapters.Analysis.Roslyn.CpgProjection;

using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;
using CoreCpg = TerrariaTools.Dome.Core.Cpg;

public sealed record CpgAnalysisProjection(
    CoreCpg.DomeCpg CodePropertyGraph,
    CoreAnalysis.FunctionDependencyGraph FunctionGraph);
