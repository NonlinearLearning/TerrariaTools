using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Reflection;

namespace TerrariaTools.Dome.Core.Cpg;

public sealed class RoslynCSharpFrontend
{
    public DomeCpg CreateCpg(RoslynFrontendConfig config)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(config.SourceCode, path: config.FileName);
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "Dome.Core.Cpg.Frontend",
            syntaxTrees: [syntaxTree],
            references: CreateMetadataReferences());
        SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);

        DomeCpg cpg = new();
        CpgContext context = new(cpg, BuiltinSchema.Create());
        RoslynFrontendContext frontendContext = new(config, syntaxTree, compilation, semanticModel);
        cpg.AttachFrontendContext(frontendContext);

        new MetaDataPass(context, frontendContext).CreateAndApply();
        new NamespaceBlockPass(context, frontendContext).CreateAndApply();
        new TypeDeclPass(context, frontendContext).CreateAndApply();
        new MemberPass(context, frontendContext).CreateAndApply();
        new MethodPass(context, frontendContext).CreateAndApply();
        new ParameterPass(context, frontendContext).CreateAndApply();
        new LocalPass(context, frontendContext).CreateAndApply();
        new AstCreationPass(context, frontendContext).CreateAndApply();
        new TypeNodePass(context, frontendContext).CreateAndApply();

        return cpg;
    }

    private static ImmutableArray<MetadataReference> CreateMetadataReferences()
    {
        Assembly[] assemblies =
        [
            typeof(object).Assembly,
            typeof(Console).Assembly,
            typeof(Enumerable).Assembly,
            typeof(System.Runtime.GCSettings).Assembly,
        ];

        return assemblies
            .Select(assembly => assembly.Location)
            .Where(location => !string.IsNullOrWhiteSpace(location))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(location => (MetadataReference)MetadataReference.CreateFromFile(location))
            .ToImmutableArray();
    }
}
