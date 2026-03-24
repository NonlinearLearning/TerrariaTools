namespace TerrariaTools.Dome.Core.Cpg;

public sealed class MethodDecoratorPass(CpgContext context) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        foreach (MethodNode method in Context.Cpg.Nodes.OfType<MethodNode>())
        {
            MethodParameterInNode[] parameterIns = Context.Cpg.Nodes
                .OfType<MethodParameterInNode>()
                .Where(
                    parameter =>
                        string.Equals(parameter.MethodName, method.Name, StringComparison.Ordinal) &&
                        string.Equals(parameter.ContainingTypeName, method.ContainingTypeName, StringComparison.Ordinal))
                .OrderBy(parameter => parameter.Order)
                .ToArray();
            foreach (MethodParameterInNode parameterIn in parameterIns)
            {
                diff.AddNode(
                    new MethodParameterOutNode(
                        NodeIdFactory.MethodParameterOut(
                            parameterIn.ContainingTypeName,
                            parameterIn.MethodName,
                            parameterIn.Name,
                            parameterIn.Order),
                        parameterIn.MethodName,
                        parameterIn.Name,
                        parameterIn.Order,
                        parameterIn.TypeFullName,
                        parameterIn.ContainingTypeName));
            }

            diff.AddNode(
                new MethodReturnNode(
                    NodeIdFactory.MethodReturn(method.ContainingTypeName, method.Name),
                    method.Name,
                    method.ReturnTypeName,
                    method.ContainingTypeName));
        }
    }
}
