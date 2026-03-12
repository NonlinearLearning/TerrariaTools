using Microsoft.CodeAnalysis;
using TerrariaTools.Analysis;

namespace TerrariaTools.Analysis.Dome
{
    public interface IFunctionBuildGraphFactory
    {
        FunctionBuildGraph Create(Solution solution);
    }

    public class FunctionBuildGraphFactory : IFunctionBuildGraphFactory
    {
        public FunctionBuildGraph Create(Solution solution)
        {
            return new FunctionBuildGraph(solution);
        }
    }
}
