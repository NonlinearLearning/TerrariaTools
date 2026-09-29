using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Rule.Checker;

[Generator]
public sealed class RuleCatalogGenerator : IIncrementalGenerator
{
    private const string RuleDefinitionMetadataName = "NLISSN.Core.Pipeline.IRuleDefinition";
    private const string MarkMetadataName = "NLISSN.Core.Marking.RuleDefinitionMark";
    private const string PropagateMetadataName = "NLISSN.Core.Propagation.RuleDefinitionPropagate";
    private const string LiftMetadataName = "NLISSN.Core.Lifting.RuleDefinitionLift";
    private const string ProposeMetadataName = "NLISSN.Core.Decision.RuleDefinitionPropose";
    private const string RegistrationMetadataName = "NLISSN.Core.Pipeline.RuleRegistrationAttribute";
    private const string IgnoreMetadataName = "NLISSN.Core.Pipeline.RuleCatalogIgnoreAttribute";
    private const string FeatureMetadataName = "NLISSN.Core.Pipeline.RuleFeature";
    private const string GeneratedCatalogMetadataName = "NLISSN.Rules.GeneratedRuleCatalog";

    private static readonly ImmutableHashSet<int> KnownFeatures =
      ImmutableHashSet.Create(0, 1, 2, 3, 4);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var declaredTypes = context.SyntaxProvider
          .CreateSyntaxProvider(
            static (node, _) => node is TypeDeclarationSyntax,
            static (syntaxContext, _) =>
              syntaxContext.SemanticModel.GetDeclaredSymbol(syntaxContext.Node) as INamedTypeSymbol)
          .Where(static symbol => symbol is not null)
          .Select(static (symbol, _) => symbol!)
          .Collect();

        context.RegisterSourceOutput(
          context.CompilationProvider.Combine(declaredTypes),
          static (sourceProductionContext, input) => Execute(
            sourceProductionContext,
            input.Left,
            input.Right));
    }

    private static void Execute(
      SourceProductionContext context,
      Compilation compilation,
      ImmutableArray<INamedTypeSymbol> declaredTypes)
    {
        var contracts = ResolveContracts(compilation, context);
        if (contracts is null)
        {
            return;
        }

        if (compilation.GetTypeByMetadataName(GeneratedCatalogMetadataName) is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(RuleCatalogDiagnostics.CatalogConflict, Location.None));
            return;
        }

        var entries = new List<RuleEntry>();
        var hasError = false;
        var sourceTypes = declaredTypes
          .Where(type => type.Locations.Any(location => location.IsInSource))
          .GroupBy(
            type => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            StringComparer.Ordinal)
          .Select(group => group.First())
          .OrderBy(type => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
          .ToArray();

        foreach (var type in sourceTypes)
        {
            if (type.TypeKind != TypeKind.Class)
            {
                continue;
            }

            var registrationAttributes = GetAttributes(type, RegistrationMetadataName);
            var ignoreAttributes = GetAttributes(type, IgnoreMetadataName);
            var stage = FindStage(type, contracts);
            var implementsRuleDefinition = Implements(type, contracts.RuleDefinition);

            if (stage is null)
            {
                if (implementsRuleDefinition && (IsConcrete(type) || registrationAttributes.Length > 0 || ignoreAttributes.Length > 0))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                      RuleCatalogDiagnostics.InvalidStage,
                      GetLocation(type),
                      GetQualifiedName(type)));
                    hasError = true;
                }

                continue;
            }

            if (registrationAttributes.Length > 1 || (registrationAttributes.Length > 0 && ignoreAttributes.Length > 0))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                  RuleCatalogDiagnostics.InvalidMetadata,
                  GetLocation(type),
                  GetQualifiedName(type)));
                hasError = true;
                continue;
            }

            if (!IsConcrete(type))
            {
                if (registrationAttributes.Length > 0 || ignoreAttributes.Length > 0)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                      RuleCatalogDiagnostics.InvalidRuleType,
                      GetLocation(type),
                      GetQualifiedName(type)));
                    hasError = true;
                }

                continue;
            }

            if (ignoreAttributes.Length > 0)
            {
                continue;
            }

            if (registrationAttributes.Length == 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                  RuleCatalogDiagnostics.MissingRegistration,
                  GetLocation(type),
                  GetQualifiedName(type)));
                continue;
            }

            if (!TryGetFeature(registrationAttributes[0], contracts.Feature is not null, out var feature))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                  RuleCatalogDiagnostics.InvalidFeature,
                  GetLocation(type),
                  GetQualifiedName(type)));
                hasError = true;
                continue;
            }

            if (!TryGetRuleId(compilation, type, out var ruleId))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                  RuleCatalogDiagnostics.InvalidRuleId,
                  GetLocation(type),
                  GetQualifiedName(type)));
                hasError = true;
                continue;
            }

            if (!HasAccessibleType(type) || !HasAccessibleParameterlessConstructor(type))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                  RuleCatalogDiagnostics.InaccessibleRule,
                  GetLocation(type),
                  GetQualifiedName(type)));
                hasError = true;
                continue;
            }

            entries.Add(new RuleEntry(
              type,
              stage.Value,
              ruleId,
              feature));
        }

        foreach (var duplicate in entries
          .GroupBy(entry => entry.RuleId, StringComparer.Ordinal)
          .Where(group => group.Count() > 1))
        {
            foreach (var entry in duplicate)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                  RuleCatalogDiagnostics.DuplicateRuleId,
                  GetLocation(entry.Type),
                  duplicate.Key));
            }

            hasError = true;
        }

        if (contracts.Feature is not null)
        {
            foreach (var featureEntries in entries
              .GroupBy(entry => entry.Feature)
              .OrderBy(group => group.Key))
            {
                foreach (var stage in new[]
                {
                    StageKind.Mark,
                    StageKind.Propagate,
                    StageKind.Lift,
                    StageKind.Propose
                })
                {
                    if (featureEntries.Any(entry => entry.Stage == stage))
                    {
                        continue;
                    }

                    context.ReportDiagnostic(Diagnostic.Create(
                      RuleCatalogDiagnostics.FeatureMissingStage,
                      GetLocation(featureEntries.First().Type),
                      GetFeatureName(featureEntries.Key),
                      stage));
                    hasError = true;
                }
            }
        }

        if (hasError)
        {
            return;
        }

        context.AddSource(
          "NLISSN.Rules.GeneratedRuleCatalog.g.cs",
          EmitCatalog(entries, contracts.Feature is not null));
    }

    private static ContractSymbols? ResolveContracts(
      Compilation compilation,
      SourceProductionContext context)
    {
        var ruleDefinition = compilation.GetTypeByMetadataName(RuleDefinitionMetadataName);
        var mark = compilation.GetTypeByMetadataName(MarkMetadataName);
        var propagate = compilation.GetTypeByMetadataName(PropagateMetadataName);
        var lift = compilation.GetTypeByMetadataName(LiftMetadataName);
        var propose = compilation.GetTypeByMetadataName(ProposeMetadataName);
        var registration = compilation.GetTypeByMetadataName(RegistrationMetadataName);
        var ignore = compilation.GetTypeByMetadataName(IgnoreMetadataName);
        var feature = compilation.GetTypeByMetadataName(FeatureMetadataName);

        var resolved = new[]
        {
            (RuleDefinitionMetadataName, ruleDefinition),
            (MarkMetadataName, mark),
            (PropagateMetadataName, propagate),
            (LiftMetadataName, lift),
            (ProposeMetadataName, propose),
            (RegistrationMetadataName, registration),
            (IgnoreMetadataName, ignore),
        };
        var missing = resolved.Where(pair => pair.Item2 is null).ToArray();
        if (missing.Length > 0)
        {
            foreach (var item in missing)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                  RuleCatalogDiagnostics.MissingContract,
                  Location.None,
                  item.Item1));
            }

            return null;
        }

        return new ContractSymbols(
          ruleDefinition!,
          mark!,
          propagate!,
          lift!,
          propose!,
          registration!,
          ignore!,
          feature);
    }

    private static ImmutableArray<AttributeData> GetAttributes(
      INamedTypeSymbol type,
      string metadataName)
    {
        return type.GetAttributes()
          .Where(attribute => string.Equals(
            attribute.AttributeClass?.ToDisplayString(),
            metadataName,
            StringComparison.Ordinal))
          .ToImmutableArray();
    }

    private static StageKind? FindStage(INamedTypeSymbol type, ContractSymbols contracts)
    {
        var matches = new List<StageKind>();
        if (DerivesFrom(type, contracts.Mark))
        {
            matches.Add(StageKind.Mark);
        }

        if (DerivesFrom(type, contracts.Propagate))
        {
            matches.Add(StageKind.Propagate);
        }

        if (DerivesFrom(type, contracts.Lift))
        {
            matches.Add(StageKind.Lift);
        }

        if (DerivesFrom(type, contracts.Propose))
        {
            matches.Add(StageKind.Propose);
        }

        return matches.Count == 1 ? matches[0] : null;
    }

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Implements(INamedTypeSymbol type, INamedTypeSymbol interfaceType)
    {
        return SymbolEqualityComparer.Default.Equals(type, interfaceType) ||
          type.AllInterfaces.Any(@interface =>
            SymbolEqualityComparer.Default.Equals(@interface, interfaceType));
    }

    private static bool IsConcrete(INamedTypeSymbol type)
    {
        return !type.IsAbstract && type.TypeParameters.Length == 0;
    }

    private static bool TryGetFeature(
      AttributeData attribute,
      bool featureContractAvailable,
      out int feature)
    {
        feature = 0;
        if (attribute.ConstructorArguments.Length == 0)
        {
            return true;
        }

        if (!featureContractAvailable || attribute.ConstructorArguments.Length != 1)
        {
            return false;
        }

        var argument = attribute.ConstructorArguments[0];
        if (argument.Value is null)
        {
            return false;
        }

        try
        {
            feature = Convert.ToInt32(argument.Value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return false;
        }

        return KnownFeatures.Contains(feature);
    }

    private static bool TryGetRuleId(
      Compilation compilation,
      INamedTypeSymbol type,
      out string ruleId)
    {
        ruleId = string.Empty;
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var property in current.GetMembers("RuleId").OfType<IPropertySymbol>())
            {
                if (property.Type.SpecialType != SpecialType.System_String)
                {
                    return false;
                }

                foreach (var syntaxReference in property.DeclaringSyntaxReferences)
                {
                    if (syntaxReference.GetSyntax() is not PropertyDeclarationSyntax propertySyntax)
                    {
                        continue;
                    }

                    var expression = propertySyntax.Initializer?.Value ?? propertySyntax.ExpressionBody?.Expression;
                    if (expression is null)
                    {
                        continue;
                    }

                    var constant = compilation.GetSemanticModel(expression.SyntaxTree).GetConstantValue(expression);
                    if (constant.HasValue && constant.Value is string value && !string.IsNullOrWhiteSpace(value))
                    {
                        ruleId = value;
                        return true;
                    }
                }

                return false;
            }
        }

        return false;
    }

    private static bool HasAccessibleType(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasAccessibleParameterlessConstructor(INamedTypeSymbol type)
    {
        var constructor = type.GetMembers()
          .OfType<IMethodSymbol>()
          .Where(member => member.MethodKind == MethodKind.Constructor && member.Parameters.Length == 0)
          .OrderBy(member => member.IsImplicitlyDeclared ? 0 : 1)
          .FirstOrDefault();
        return constructor is not null &&
          constructor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal;
    }

    private static Location GetLocation(INamedTypeSymbol type)
    {
        return type.Locations.FirstOrDefault(location => location.IsInSource) ?? Location.None;
    }

    private static string GetQualifiedName(INamedTypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
    }

    private static string EmitCatalog(
      IReadOnlyList<RuleEntry> entries,
      bool includeFeatures)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("namespace NLISSN.Rules;");
        builder.AppendLine();
        builder.AppendLine("public static class GeneratedRuleCatalog");
        builder.AppendLine("{");
        EmitStage(builder, entries, StageKind.Mark, "Markers", "NLISSN.Core.Marking.RuleDefinitionMark", includeFeatures);
        EmitStage(builder, entries, StageKind.Propagate, "Propagators", "NLISSN.Core.Propagation.RuleDefinitionPropagate", includeFeatures);
        EmitStage(builder, entries, StageKind.Lift, "Lifters", "NLISSN.Core.Lifting.RuleDefinitionLift", includeFeatures);
        EmitStage(builder, entries, StageKind.Propose, "Proposers", "NLISSN.Core.Decision.RuleDefinitionPropose", includeFeatures);
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void EmitStage(
      StringBuilder builder,
      IReadOnlyList<RuleEntry> entries,
      StageKind stage,
      string propertyName,
      string stageTypeName,
      bool includeFeatures)
    {
        var stageEntries = entries
          .Where(entry => entry.Stage == stage)
          .OrderBy(entry => entry.Type.Name, StringComparer.Ordinal)
          .ThenBy(entry => GetQualifiedName(entry.Type), StringComparer.Ordinal)
          .ToArray();
        var registrationType = $"global::NLISSN.Core.Pipeline.RuleRegistration<global::{stageTypeName}>";

        builder.AppendLine($"    public static global::System.Collections.Generic.IReadOnlyList<{registrationType}> {propertyName} {{ get; }} =");
        builder.AppendLine($"        global::System.Array.AsReadOnly(new {registrationType}[]");
        builder.AppendLine("        {");
        foreach (var entry in stageEntries)
        {
            var typeName = GetQualifiedName(entry.Type);
            builder.AppendLine($"            new {registrationType}(");
            builder.AppendLine($"                {Literal(entry.RuleId)},");
            builder.AppendLine($"                {Literal(entry.Type.Name)},");
            builder.AppendLine($"                {Literal(typeName)},");
            if (includeFeatures)
            {
                builder.AppendLine($"                global::NLISSN.Core.Pipeline.RuleFeature.{GetFeatureName(entry.Feature)},");
            }
            builder.AppendLine($"                static () => new global::{typeName}()),");
        }

        builder.AppendLine("        });");
        builder.AppendLine();
    }

    private static string GetFeatureName(int feature)
    {
        return feature switch
        {
            0 => "Core",
            1 => "UnreachableMethodDeletion",
            2 => "UnreferencedMethodDeletion",
            3 => "UnusedInterfaceImplementationCleanup",
            4 => "InternalOnlyPublicMethodPrivatization",
            _ => throw new InvalidOperationException($"Unknown RuleFeature value {feature}.")
        };
    }

    private static string Literal(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    private sealed class ContractSymbols
    {
        public ContractSymbols(
          INamedTypeSymbol ruleDefinition,
          INamedTypeSymbol mark,
          INamedTypeSymbol propagate,
          INamedTypeSymbol lift,
          INamedTypeSymbol propose,
          INamedTypeSymbol registration,
          INamedTypeSymbol ignore,
          INamedTypeSymbol? feature)
        {
            RuleDefinition = ruleDefinition;
            Mark = mark;
            Propagate = propagate;
            Lift = lift;
            Propose = propose;
            Registration = registration;
            Ignore = ignore;
            Feature = feature;
        }

        public INamedTypeSymbol RuleDefinition { get; }
        public INamedTypeSymbol Mark { get; }
        public INamedTypeSymbol Propagate { get; }
        public INamedTypeSymbol Lift { get; }
        public INamedTypeSymbol Propose { get; }
        public INamedTypeSymbol Registration { get; }
        public INamedTypeSymbol Ignore { get; }
        public INamedTypeSymbol? Feature { get; }
    }

    private sealed class RuleEntry
    {
        public RuleEntry(INamedTypeSymbol type, StageKind stage, string ruleId, int feature)
        {
            Type = type;
            Stage = stage;
            RuleId = ruleId;
            Feature = feature;
        }

        public INamedTypeSymbol Type { get; }
        public StageKind Stage { get; }
        public string RuleId { get; }
        public int Feature { get; }
    }

    private enum StageKind
    {
        Mark,
        Propagate,
        Lift,
        Propose,
    }
}
