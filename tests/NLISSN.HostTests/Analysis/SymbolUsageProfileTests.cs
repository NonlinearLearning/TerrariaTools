using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Analysis;
using NLISSN.Core.Pipeline;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class SymbolUsageProfileTests
{
  [Fact]
  public void GetOrCreateEpochCompilationCache_ConcurrentCalls_BuildsOnceAndNextEpochRebuilds()
  {
    var compilation = CreateCompilation(("cache.cs", "class C { }"));
    var runtime = AnalysisRuntime.CreateDefault();
    var builds = 0;
    var profiles = new SymbolUsageProfile[16];

    Parallel.For(0, profiles.Length, index =>
    {
      profiles[index] = runtime.GetOrCreateEpochCompilationCache(
        compilation,
        currentCompilation =>
        {
          Interlocked.Increment(ref builds);
          return new SymbolUsageProfile(currentCompilation);
        });
    });
    var nextEpoch = runtime.NextEpoch();
    var nextProfile = nextEpoch.GetOrCreateEpochCompilationCache(
      compilation,
      currentCompilation =>
      {
        Interlocked.Increment(ref builds);
        return new SymbolUsageProfile(currentCompilation);
      });

    Assert.Equal(2, builds);
    Assert.All(profiles, profile => Assert.Same(profiles[0], profile));
    Assert.NotSame(profiles[0], nextProfile);
  }

  [Fact]
  public void GetParameterCallsites_CrossFileCalls_ReturnsStableFileAndSpanOrder()
  {
    var compilation = CreateCompilation(
      ("z.cs", "class C { void Call() => M(2); }") ,
      ("a.cs", "partial class C { void M(int value) { } void Call2() => M(1); }"));
    var parameter = GetMethod(compilation, "M").Parameters.Single();

    var result = new SymbolUsageProfile(compilation).GetParameterCallsites(parameter);

    Assert.Equal(UsageProfileStatus.Complete, result.Status);
    Assert.Equal(new[] { "a.cs", "z.cs" }, result.Facts.Select(fact => fact.FilePath).ToArray());
  }

  [Fact]
  public void GetParameterCallsites_WhenBudgetExceeded_ReturnsTruncatedFacts()
  {
    var compilation = CreateCompilation(("budget.cs", "class C { void M(int value) { } void Call() { M(1); M(2); } }"));
    var parameter = GetMethod(compilation, "M").Parameters.Single();

    var result = new SymbolUsageProfile(compilation).GetParameterCallsites(parameter, new UsageProfileQuery(MaxCallsites: 1));

    Assert.Equal(UsageProfileStatus.Truncated, result.Status);
    Assert.Single(result.Facts);
  }

  [Fact]
  public void GetParameterCallsites_WhenDynamicInvocationExists_ReturnsUncertain()
  {
    var compilation = CreateCompilation(("dynamic.cs", "class C { void M(int value) { } void Call(dynamic d) { d.M(1); M(2); } }"));
    var parameter = GetMethod(compilation, "M").Parameters.Single();

    var result = new SymbolUsageProfile(compilation).GetParameterCallsites(parameter);

    Assert.Equal(UsageProfileStatus.Uncertain, result.Status);
    Assert.Empty(result.Facts);
  }

  [Fact]
  public void GetTypeRelations_BaseAndInterface_ReturnsClassifiedFacts()
  {
    var compilation = CreateCompilation(("relations.cs", "interface I { } class Base { } class C : Base, I { }"));
    var type = compilation.GlobalNamespace.GetTypeMembers("C").Single();

    var result = new SymbolUsageProfile(compilation).GetTypeRelations(type);

    Assert.Equal(UsageProfileStatus.Complete, result.Status);
    Assert.Contains(result.Facts, fact => fact.Kind == TypeRelationKind.BaseType && fact.RelatedSymbol.Name == "Base");
    Assert.Contains(result.Facts, fact => fact.Kind == TypeRelationKind.Interface && fact.RelatedSymbol.Name == "I");
  }

  [Fact]
  public void GetSymbolIdentity_PartialTypeDeclarations_ReturnsSameProjectLocalIdentity()
  {
    var compilation = CreateCompilation(
      ("first.cs", "partial class C { }"),
      ("second.cs", "partial class C { }"));
    var type = compilation.GlobalNamespace.GetTypeMembers("C").Single();
    var profile = new SymbolUsageProfile(compilation);

    var declarations = profile.GetDeclarations(type);

    Assert.Equal(2, declarations.Facts.Count);
    Assert.All(declarations.Facts, fact => Assert.Equal(profile.GetSymbolIdentity(type), fact.SymbolIdentity));
  }

  [Fact]
  public void GetTypeRelations_TransitiveInterface_ReturnsInterfaceFact()
  {
    var compilation = CreateCompilation(("transitive-interface.cs", "interface IRoot { } interface IChild : IRoot { } class C : IChild { }"));
    var type = compilation.GlobalNamespace.GetTypeMembers("C").Single();

    var result = new SymbolUsageProfile(compilation).GetTypeRelations(type);

    Assert.Contains(result.Facts, fact => fact.Kind == TypeRelationKind.Interface && fact.RelatedSymbol.Name == "IRoot");
  }

  [Fact]
  public void GetReferences_SymbolMentionedAcrossFiles_ReturnsStableFacts()
  {
    var compilation = CreateCompilation(
      ("z.cs", "partial class C { void Z() { M(); } }"),
      ("a.cs", "partial class C { void M() { } void A() { M(); } }"));
    var method = GetMethod(compilation, "M");

    var result = new SymbolUsageProfile(compilation).GetReferences(method);

    Assert.Equal(UsageProfileStatus.Complete, result.Status);
    Assert.Equal(new[] { "a.cs", "z.cs" }, result.Facts.Select(fact => fact.FilePath).ToArray());
  }

  [Fact]
  public void GetDelegateBindings_MethodGroupAndLambda_ReturnsClassifiedFacts()
  {
    var compilation = CreateCompilation(("delegate.cs", "using System; class C { delegate void D(int x); void M(int x) { } void Run() { D a = M; D b = x => { }; } }"));
    var delegateType = compilation.GlobalNamespace.GetTypeMembers("C").Single().GetTypeMembers("D").Single();

    var result = new SymbolUsageProfile(compilation).GetDelegateBindings(delegateType);

    Assert.Equal(UsageProfileStatus.Complete, result.Status);
    Assert.Contains(result.Facts, fact => fact.Kind == DelegateBindingKind.MethodGroup);
    Assert.Contains(result.Facts, fact => fact.Kind == DelegateBindingKind.Lambda);
  }

  [Fact]
  public void GetDelegateBindings_WhenDelegateFlowsThroughAnotherDelegate_ReturnsUncertain()
  {
    var compilation = CreateCompilation(("delegate-chain.cs", "using System; class C { delegate void D(int x); void M(int x) { } void Run() { D first = M; D second = first; } }"));
    var delegateType = compilation.GlobalNamespace.GetTypeMembers("C").Single().GetTypeMembers("D").Single();

    var result = new SymbolUsageProfile(compilation).GetDelegateBindings(delegateType);

    Assert.Equal(UsageProfileStatus.Uncertain, result.Status);
    Assert.Contains(result.Facts, fact => fact.Kind == DelegateBindingKind.PassThrough && fact.Target is ILocalSymbol);
  }

  [Fact]
  public void GetDelegateBindings_WhenExpressionTreeBindingExists_ReturnsUncertain()
  {
    var compilation = CreateCompilation(("expression-tree.cs", "using System; using System.Linq.Expressions; class C { delegate void D(int x); void Run() { Expression<D> expression = x => { }; } }"));
    var delegateType = compilation.GlobalNamespace.GetTypeMembers("C").Single().GetTypeMembers("D").Single();

    var result = new SymbolUsageProfile(compilation).GetDelegateBindings(delegateType);

    Assert.Equal(UsageProfileStatus.Uncertain, result.Status);
    Assert.Contains(result.Facts, fact => fact.Kind == DelegateBindingKind.Unsupported);
  }

  [Fact]
  public void GetMethodCallsites_WhenReflectionInvocationExists_ReturnsUncertain()
  {
    var compilation = CreateCompilation(("reflection.cs", "class C { static void M(int value) { } void Run() { typeof(C).GetMethod(\"M\")!.Invoke(null, new object?[] { 1 }); } }"));
    var method = GetMethod(compilation, "M");

    var result = new SymbolUsageProfile(compilation).GetMethodCallsites(method);

    Assert.Equal(UsageProfileStatus.Uncertain, result.Status);
  }

  [Fact]
  public void GetMethodCallsites_WhenCompilationHasUnboundCode_ReturnsUncertain()
  {
    var compilation = CreateCompilation(("unbound.cs", "class C { void M(int value) { } void Run() { Missing(); M(1); } }"));
    var method = GetMethod(compilation, "M");

    var result = new SymbolUsageProfile(compilation).GetMethodCallsites(method);

    Assert.Equal(UsageProfileStatus.Uncertain, result.Status);
  }

  private static IMethodSymbol GetMethod(CSharpCompilation compilation, string name)
  {
    return compilation.GlobalNamespace.GetTypeMembers("C").Single().GetMembers(name).OfType<IMethodSymbol>().Single();
  }

  private static CSharpCompilation CreateCompilation(params (string Path, string Source)[] sources)
  {
    var trees = sources.Select(source => CSharpSyntaxTree.ParseText(source.Source, path: source.Path));
    return CSharpCompilation.Create(
      "SymbolUsageProfileTests",
      trees,
      new[]
      {
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(System.Linq.Expressions.Expression<>).Assembly.Location),
      },
      new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
  }
}
