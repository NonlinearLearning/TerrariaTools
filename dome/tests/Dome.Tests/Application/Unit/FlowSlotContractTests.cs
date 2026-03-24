using System.Reflection;
using System.Runtime.CompilerServices;
using TerrariaTools.Dome.Application.Ports;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class FlowSlotContractTests
{
    [Fact]
    public void FlowSlotContracts_ExistInApplicationPortsNamespace()
    {
        var exportedTypes = new[]
        {
            typeof(IFlowExecutionContext),
            typeof(IFlowSlot<,>),
            typeof(ILoadSlot),
            typeof(IAnalyzeSlot),
            typeof(IRuleSlot),
            typeof(IDecisionSlot),
            typeof(IResultSlot),
            typeof(LoadInput),
            typeof(LoadOutput),
            typeof(AnalyzeInput),
            typeof(AnalyzeOutput),
            typeof(RuleInput),
            typeof(RuleOutput),
            typeof(DecisionInput),
            typeof(DecisionOutput),
            typeof(ResultInput)
        };

        Assert.All(
            exportedTypes,
            static type => Assert.Equal("TerrariaTools.Dome.Application.Ports", type.Namespace));
    }

    [Fact]
    public void DomeFlowSlotContracts_UseImmutableTypedInputsAndOutputs()
    {
        AssertSlotContract<ILoadSlot, LoadInput, LoadOutput>();
        AssertSlotContract<IAnalyzeSlot, AnalyzeInput, AnalyzeOutput>();
        AssertSlotContract<IRuleSlot, RuleInput, RuleOutput>();
        AssertSlotContract<IDecisionSlot, DecisionInput, DecisionOutput>();
        AssertSlotContract<IResultSlot, ResultInput, RunResult>();

        AssertImplementsFlowSlot<ILoadSlot, LoadInput, LoadOutput>();
        AssertImplementsFlowSlot<IAnalyzeSlot, AnalyzeInput, AnalyzeOutput>();
        AssertImplementsFlowSlot<IRuleSlot, RuleInput, RuleOutput>();
        AssertImplementsFlowSlot<IDecisionSlot, DecisionInput, DecisionOutput>();
        AssertImplementsFlowSlot<IResultSlot, ResultInput, RunResult>();

        AssertImmutableRecordLike(typeof(LoadInput));
        AssertImmutableRecordLike(typeof(LoadOutput));
        AssertImmutableRecordLike(typeof(AnalyzeInput));
        AssertImmutableRecordLike(typeof(AnalyzeOutput));
        AssertImmutableRecordLike(typeof(RuleInput));
        AssertImmutableRecordLike(typeof(RuleOutput));
        AssertImmutableRecordLike(typeof(DecisionInput));
        AssertImmutableRecordLike(typeof(DecisionOutput));
        AssertImmutableRecordLike(typeof(ResultInput));
    }

    private static void AssertSlotContract<TSlot, TInput, TOutput>()
    {
        var method = typeof(IFlowSlot<TInput, TOutput>).GetMethod("ExecuteAsync");
        Assert.NotNull(method);
        Assert.Collection(
            method!.GetParameters(),
            parameter => Assert.Equal(typeof(TInput), parameter.ParameterType),
            parameter => Assert.Equal(typeof(IFlowExecutionContext), parameter.ParameterType),
            parameter => Assert.Equal(typeof(CancellationToken), parameter.ParameterType));
        Assert.Equal(typeof(Task<TOutput>), method.ReturnType);
    }

    private static void AssertImplementsFlowSlot<TSlot, TInput, TOutput>()
    {
        Assert.Contains(
            typeof(IFlowSlot<TInput, TOutput>),
            typeof(TSlot).GetInterfaces());
    }

    private static void AssertImmutableRecordLike(Type type)
    {
        Assert.True(type.IsSealed, $"{type.Name} must be sealed.");

        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        Assert.NotEmpty(properties);
        Assert.All(
            properties,
            property =>
            {
                var setter = property.SetMethod;
                if (setter is null)
                {
                    return;
                }

                Assert.True(
                    IsInitOnly(setter),
                    $"{type.Name}.{property.Name} must be init-only.");
            });
    }

    private static bool IsInitOnly(MethodInfo setMethod) =>
        setMethod.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));
}
