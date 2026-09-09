using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace RoslynPrototype.ContractTests.RuleCatalog;

public sealed class RuleCatalogGeneratorTests
{
    [Fact]
    public void ValidRules_GenerateFourTypedCatalogsAndDirectFactories()
    {
        var run = Run("""
            namespace Demo;

            [NLISSN.Core.Pipeline.RuleRegistration]
            public sealed class AlphaMark : NLISSN.Core.Marking.RuleDefinitionMark
            {
                public override string RuleId { get; } = "mark.alpha";
            }

            [NLISSN.Core.Pipeline.RuleRegistration]
            public sealed class BetaPropagate : NLISSN.Core.Propagation.RuleDefinitionPropagate
            {
                public override string RuleId { get; } = "propagate.beta";
            }

            [NLISSN.Core.Pipeline.RuleRegistration]
            public sealed class GammaLift : NLISSN.Core.Lifting.RuleDefinitionLift
            {
                public override string RuleId { get; } = "lift.gamma";
            }

            [NLISSN.Core.Pipeline.RuleRegistration]
            public sealed class DeltaPropose : NLISSN.Core.Decision.RuleDefinitionPropose
            {
                public override string RuleId { get; } = "propose.delta";
            }
            """);

        AssertNoErrors(run);
        var catalog = AssertGeneratedCatalog(run);

        Assert.Contains("public static class GeneratedRuleCatalog", catalog, StringComparison.Ordinal);
        Assert.Contains("RuleRegistration<global::NLISSN.Core.Marking.RuleDefinitionMark>", catalog, StringComparison.Ordinal);
        Assert.Contains("RuleRegistration<global::NLISSN.Core.Propagation.RuleDefinitionPropagate>", catalog, StringComparison.Ordinal);
        Assert.Contains("RuleRegistration<global::NLISSN.Core.Lifting.RuleDefinitionLift>", catalog, StringComparison.Ordinal);
        Assert.Contains("RuleRegistration<global::NLISSN.Core.Decision.RuleDefinitionPropose>", catalog, StringComparison.Ordinal);
        Assert.Contains("static () => new global::Demo.AlphaMark()", catalog, StringComparison.Ordinal);
        Assert.Contains("static () => new global::Demo.BetaPropagate()", catalog, StringComparison.Ordinal);
        Assert.Contains("static () => new global::Demo.GammaLift()", catalog, StringComparison.Ordinal);
        Assert.Contains("static () => new global::Demo.DeltaPropose()", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("RuleFeature", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain(".Feature", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingRegistration_ReportsWarningAndExcludesTheRule()
    {
        var run = Run("""
            namespace Demo;

            public sealed class UnregisteredMark : NLISSN.Core.Marking.RuleDefinitionMark
            {
                public override string RuleId { get; } = "mark.unregistered";
            }

            [NLISSN.Core.Pipeline.RuleRegistration]
            public sealed class RegisteredMark : NLISSN.Core.Marking.RuleDefinitionMark
            {
                public override string RuleId { get; } = "mark.registered";
            }

            [NLISSN.Core.Pipeline.RuleRegistration]
            public sealed class RegisteredPropagate : NLISSN.Core.Propagation.RuleDefinitionPropagate
            {
                public override string RuleId { get; } = "propagate.registered";
            }

            [NLISSN.Core.Pipeline.RuleRegistration]
            public sealed class RegisteredLift : NLISSN.Core.Lifting.RuleDefinitionLift
            {
                public override string RuleId { get; } = "lift.registered";
            }

            [NLISSN.Core.Pipeline.RuleRegistration]
            public sealed class RegisteredPropose : NLISSN.Core.Decision.RuleDefinitionPropose
            {
                public override string RuleId { get; } = "propose.registered";
            }
            """);

        Assert.Contains(run.Diagnostics, diagnostic =>
          diagnostic.Id == "NLRCG003" &&
          diagnostic.Severity == DiagnosticSeverity.Warning);
        AssertNoErrors(run);
        var catalog = AssertGeneratedCatalog(run);
        Assert.Contains("RegisteredMark", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("UnregisteredMark", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogIgnore_ExcludesTheRuleWithoutMissingRegistrationWarning()
    {
        var run = Run("""
            namespace Demo;

            [NLISSN.Core.Pipeline.RuleCatalogIgnore]
            public sealed class IgnoredMark : NLISSN.Core.Marking.RuleDefinitionMark
            {
                public override string RuleId { get; } = "mark.ignored";
            }
            """);

        Assert.DoesNotContain(run.Diagnostics, diagnostic => diagnostic.Id == "NLRCG003");
        AssertNoErrors(run);
        var catalog = AssertGeneratedCatalog(run);
        Assert.DoesNotContain("IgnoredMark", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidStage_ReportsErrorAndDoesNotGeneratePartialCatalog()
    {
        var run = Run("""
            namespace Demo;

            [NLISSN.Core.Pipeline.RuleRegistration]
            public sealed class InvalidStage : NLISSN.Core.Pipeline.IRuleDefinition
            {
                public string RuleId => "invalid.stage";
            }
            """);

        Assert.Contains(run.Diagnostics, diagnostic => diagnostic.Id == "NLRCG002");
        AssertNoGeneratedCatalog(run);
    }

    [Fact]
    public void IncompleteCatalog_ReportsErrorAndDoesNotGeneratePartialCatalog()
    {
        var run = Run("""
            namespace Demo;

            [NLISSN.Core.Pipeline.RuleRegistration]
            public sealed class IncompleteMark : NLISSN.Core.Marking.RuleDefinitionMark
            {
                public override string RuleId { get; } = "mark.incomplete";
            }
            """);

        Assert.Contains(run.Diagnostics, diagnostic => diagnostic.Id == "NLRCG011");
        AssertNoGeneratedCatalog(run);
    }

    [Fact]
    public void AbstractAndOpenGenericRules_ReportsErrorsAndDoesNotGeneratePartialCatalog()
    {
        var run = Run("""
            namespace Demo;

            [NLISSN.Core.Pipeline.RuleRegistration]
            public abstract class AbstractMark : NLISSN.Core.Marking.RuleDefinitionMark
            {
                public override string RuleId { get; } = "mark.abstract";
            }

            [NLISSN.Core.Pipeline.RuleRegistration]
            public sealed class GenericMark<T> : NLISSN.Core.Marking.RuleDefinitionMark
            {
                public override string RuleId { get; } = "mark.generic";
            }
            """);

        Assert.Contains(run.Diagnostics, diagnostic => diagnostic.Id == "NLRCG008");
        AssertNoGeneratedCatalog(run);
    }

    [Fact]
    public void MissingAccessibleParameterlessConstructor_ReportsErrorAndDoesNotGenerateCatalog()
    {
        var run = Run("""
            namespace Demo;

            [NLISSN.Core.Pipeline.RuleRegistration]
            public sealed class NoDefaultConstructorMark : NLISSN.Core.Marking.RuleDefinitionMark
            {
                private NoDefaultConstructorMark(string value) { }
                public override string RuleId { get; } = "mark.no-default";
            }
            """);

        Assert.Contains(run.Diagnostics, diagnostic => diagnostic.Id == "NLRCG007");
        AssertNoGeneratedCatalog(run);
    }

    [Fact]
    public void DuplicateRuleId_ReportsErrorAndDoesNotGeneratePartialCatalog()
    {
        var run = Run("""
            namespace Demo;

            [NLISSN.Core.Pipeline.RuleRegistration]
            public sealed class FirstMark : NLISSN.Core.Marking.RuleDefinitionMark
            {
                public override string RuleId { get; } = "mark.duplicate";
            }

            [NLISSN.Core.Pipeline.RuleRegistration]
            public sealed class SecondMark : NLISSN.Core.Marking.RuleDefinitionMark
            {
                public override string RuleId { get; } = "mark.duplicate";
            }
            """);

        Assert.Contains(run.Diagnostics, diagnostic => diagnostic.Id == "NLRCG006");
        AssertNoGeneratedCatalog(run);
    }

    [Fact]
    public void FixedCatalogTypeConflict_ReportsErrorAndDoesNotGenerateCatalog()
    {
        var run = Run("""
            namespace NLISSN.Rules
            {
                public static class GeneratedRuleCatalog { }
            }

            namespace Demo
            {
                [NLISSN.Core.Pipeline.RuleRegistration]
                public sealed class ValidMark : NLISSN.Core.Marking.RuleDefinitionMark
                {
                    public override string RuleId { get; } = "mark.valid";
                }
            }
            """);

        Assert.Contains(run.Diagnostics, diagnostic => diagnostic.Id == "NLRCG009");
        AssertNoGeneratedCatalog(run);
    }

    [Fact]
    public void RuleIdMustBeACompileTimeNonBlankString()
    {
        var run = Run("""
            namespace Demo;

            [NLISSN.Core.Pipeline.RuleRegistration]
            public sealed class DynamicIdMark : NLISSN.Core.Marking.RuleDefinitionMark
            {
                private static string CreateId() => "mark.dynamic";
                public override string RuleId => CreateId();
            }
            """);

        Assert.Contains(run.Diagnostics, diagnostic => diagnostic.Id == "NLRCG005");
        AssertNoGeneratedCatalog(run);
    }

    [Fact]
    public void CatalogOrder_IsStableByTypeNameThenFullyQualifiedName()
    {
        const string source = """
            namespace Zed
            {
                [NLISSN.Core.Pipeline.RuleRegistration]
                public sealed class DuplicateName : NLISSN.Core.Marking.RuleDefinitionMark
                {
                    public override string RuleId { get; } = "mark.zed";
                }
            }

            namespace Alpha
            {
                [NLISSN.Core.Pipeline.RuleRegistration]
                public sealed class DuplicateName : NLISSN.Core.Marking.RuleDefinitionMark
                {
                    public override string RuleId { get; } = "mark.alpha";
                }
            }

            namespace Support
            {
                [NLISSN.Core.Pipeline.RuleRegistration]
                public sealed class OrderPropagate : NLISSN.Core.Propagation.RuleDefinitionPropagate
                {
                    public override string RuleId { get; } = "propagate.order";
                }

                [NLISSN.Core.Pipeline.RuleRegistration]
                public sealed class OrderLift : NLISSN.Core.Lifting.RuleDefinitionLift
                {
                    public override string RuleId { get; } = "lift.order";
                }

                [NLISSN.Core.Pipeline.RuleRegistration]
                public sealed class OrderPropose : NLISSN.Core.Decision.RuleDefinitionPropose
                {
                    public override string RuleId { get; } = "propose.order";
                }
            }
            """;

        var first = Run(source);
        var second = Run(source);

        AssertNoErrors(first);
        var firstCatalog = AssertGeneratedCatalog(first);
        var secondCatalog = AssertGeneratedCatalog(second);
        Assert.Equal(firstCatalog, secondCatalog);
        Assert.True(
          firstCatalog.IndexOf("global::Alpha.DuplicateName", StringComparison.Ordinal) <
          firstCatalog.IndexOf("global::Zed.DuplicateName", StringComparison.Ordinal));
        Assert.Contains("\"Alpha.DuplicateName\"", firstCatalog, StringComparison.Ordinal);
        Assert.Contains("\"Zed.DuplicateName\"", firstCatalog, StringComparison.Ordinal);
    }

    private static TestRun Run(string userSource)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
          assemblyName: "RuleCatalogGeneratorTests",
          syntaxTrees: new[]
          {
              CSharpSyntaxTree.ParseText(ContractSource, parseOptions),
              CSharpSyntaxTree.ParseText(userSource, parseOptions)
          },
          references: TrustedPlatformReferences,
          options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var generator = new NLISSN.Rule.Generator.RuleCatalogGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
          generators: new[] { generator.AsSourceGenerator() },
          parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
          compilation,
          out var outputCompilation,
          out var updateDiagnostics);
        var result = driver.GetRunResult();
        return new TestRun(
          outputCompilation,
          result,
          result.Diagnostics.Concat(updateDiagnostics).Concat(outputCompilation.GetDiagnostics()).ToArray());
    }

    private static string AssertGeneratedCatalog(TestRun run)
    {
        var generated = run.Result.Results
          .SelectMany(result => result.GeneratedSources)
          .Where(source => source.HintName == "NLISSN.Rules.GeneratedRuleCatalog.g.cs")
          .ToArray();

        var source = Assert.Single(generated);
        return source.SourceText.ToString();
    }

    private static void AssertNoGeneratedCatalog(TestRun run)
    {
        Assert.DoesNotContain(
          run.Result.Results.SelectMany(result => result.GeneratedSources),
          source => source.HintName == "NLISSN.Rules.GeneratedRuleCatalog.g.cs");
    }

    private static void AssertNoErrors(TestRun run)
    {
        Assert.DoesNotContain(run.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private sealed record TestRun(
      Compilation OutputCompilation,
      GeneratorDriverRunResult Result,
      IReadOnlyList<Diagnostic> Diagnostics);

    private static readonly IReadOnlyList<MetadataReference> TrustedPlatformReferences =
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
          .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Where(path => !Path.GetFileNameWithoutExtension(path).StartsWith("NL", StringComparison.Ordinal))
        .Select(path => MetadataReference.CreateFromFile(path))
        .ToArray();

    private const string ContractSource = """
        using System;
        using System.Collections.Generic;

        namespace NLISSN.Core.Pipeline
        {
            public interface IRuleDefinition
            {
                string RuleId { get; }
            }

            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class RuleRegistrationAttribute : Attribute { }

            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class RuleCatalogIgnoreAttribute : Attribute { }

            public sealed class RuleRegistration<TStage>
                where TStage : class, IRuleDefinition
            {
                public RuleRegistration(
                    string ruleId,
                    string typeName,
                    string fullyQualifiedName,
                    Func<TStage> factory) { }
            }
        }

        namespace NLISSN.Core.Marking
        {
            public abstract class RuleDefinitionMark : NLISSN.Core.Pipeline.IRuleDefinition
            {
                public abstract string RuleId { get; }
            }
        }

        namespace NLISSN.Core.Propagation
        {
            public abstract class RuleDefinitionPropagate : NLISSN.Core.Pipeline.IRuleDefinition
            {
                public abstract string RuleId { get; }
            }
        }

        namespace NLISSN.Core.Lifting
        {
            public abstract class RuleDefinitionLift : NLISSN.Core.Pipeline.IRuleDefinition
            {
                public abstract string RuleId { get; }
            }
        }

        namespace NLISSN.Core.Decision
        {
            public abstract class RuleDefinitionPropose : NLISSN.Core.Pipeline.IRuleDefinition
            {
                public abstract string RuleId { get; }
            }
        }
        """;
}
