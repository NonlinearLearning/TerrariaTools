using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.Json;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Application;
using NLISSN.Core.Rewrite;
using RoslynPrototype.Tests.TestCodeSet.Propagation;
using NLISSN.Rules;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class PropagationRuleExpansionTests
{
    [Fact]
    public void Analyze_ConditionalAccessInvoke_PropagatesToConditionalAccess()
    {
        var result = Analyze(PropagationSources.ConditionalAccessInvokeSource, "conditional-access-invoke.cs", "Invoke");

        AssertContainsEffective(result, SyntaxKind.ConditionalAccessExpression, "handler?.Invoke()");
    }

    [Fact]
    public void Analyze_ObjectCreationWithInitializer_PropagatesThroughInitializerAndCreation()
    {
        var result = Analyze(PropagationSources.ObjectCreationWithInitializerSource, "object-creation-initializer.cs", "Seed");

        AssertContainsEffectiveText(result, SyntaxKind.ObjectCreationExpression, "new Holder");
    }

    [Fact]
    public void Analyze_ConditionalExpression_PropagatesToTernaryHost()
    {
        var result = Analyze(PropagationSources.ConditionalExpressionSource, "conditional-expression.cs", "s");

        AssertContainsPropagated(result, SyntaxKind.ConditionalExpression, "ready ? s.Seed : 0");
    }

    [Fact]
    public void Analyze_TransparentWrappers_PropagateOneWrapperAtATime()
    {
        var result = Analyze(PropagationSources.TransparentWrappersSource, "transparent-wrappers.cs", "s");

        AssertContainsPropagated(result, SyntaxKind.ParenthesizedExpression, "(s.Seed)");
        AssertContainsPropagated(result, SyntaxKind.CastExpression, "(int)(s.Seed)");
        AssertContainsPropagated(result, SyntaxKind.CheckedExpression, "checked((int)(s.Seed))");
    }

    [Fact]
    public void Analyze_InterpolatedString_PropagatesThroughInterpolation()
    {
        var result = Analyze(PropagationSources.InterpolatedStringSource, "interpolated-string.cs", "s");

        AssertContainsPropagated(result, SyntaxKind.Interpolation, "{s.Seed}");
        AssertContainsPropagated(result, SyntaxKind.InterpolatedStringExpression, "$\"value {s.Seed}\"");
    }

    [Fact]
    public void Analyze_YieldThrowAndArrow_PropagateToStatementOrClauseHosts()
    {
        var result = Analyze(PropagationSources.YieldThrowAndArrowSource, "yield-throw-arrow.cs", "s");

        AssertContainsPropagatedText(result, SyntaxKind.YieldReturnStatement, "yield return s.Seed;");
        AssertContainsPropagatedText(result, SyntaxKind.ThrowStatement, "throw new InvalidOperationException(s.Text);");
        AssertContainsPropagated(result, SyntaxKind.ArrowExpressionClause, "=> s.Seed");
    }

    [Fact]
    public void Analyze_ResourceAndLoopHeaders_StopAtUnaryAddressOfExpression()
    {
        var result = AnalyzeWithSeeds(
          PropagationSources.ResourceAndLoopHeadersSource,
          "resource-loop-headers.cs",
          (SyntaxKind.SimpleMemberAccessExpression, "s.Sync"),
          (SyntaxKind.InvocationExpression, "s.Open()"),
          (SyntaxKind.SimpleMemberAccessExpression, "s.Seed"),
          (SyntaxKind.SimpleMemberAccessExpression, "s.Values"));

        AssertContainsPropagatedText(result, SyntaxKind.LockStatement, "lock (s.Sync)");
        AssertContainsPropagatedText(result, SyntaxKind.UsingStatement, "using (s.Open())");
        AssertContainsPropagatedText(result, SyntaxKind.ForEachStatement, "foreach (var value in s.Values)");
        Assert.Contains(result.PropagatedMarks, mark =>
          mark.Mark.SyntaxNode.IsKind(SyntaxKind.AddressOfExpression) &&
          string.Equals(mark.Mark.SyntaxNode.ToString(), "&s.Seed", StringComparison.Ordinal) &&
          mark.Mark.SemanticTag == RuleFactPorts.FlowUnaryExpression);
        Assert.DoesNotContain(result.PropagatedMarks, mark =>
          mark.Mark.SyntaxNode.IsKind(SyntaxKind.FixedStatement));
    }

    [Fact]
    public void Analyze_DeleteClassTargetInsideUsingBody_DoesNotDeleteUsingStatement()
    {
        const string source = """
            using System;

            public static class PlayerInput
            {
                public static bool UsingGamepadUI => true;
            }

            public sealed class Sample
            {
                public void Check(IDisposable resource)
                {
                    using (resource)
                    {
                        if (PlayerInput.UsingGamepadUI)
                        {
                            Keep();
                        }
                    }
                }

                private static void Keep()
                {
                }
            }
            """;

        var result = AnalyzeDeleteClass(source, "using-body-target.cs");

        Assert.DoesNotContain(result.Decisions, decision =>
          decision.Action == DecisionActionKind.Delete &&
          decision.FinalNode.IsKind(SyntaxKind.UsingStatement));
    }

    [Fact]
    public void Analyze_SwitchExpression_PropagatesThroughArmAndExpression()
    {
        var result = Analyze(PropagationSources.SwitchExpressionSource, "switch-expression.cs", "s");

        AssertContainsPropagatedText(result, SyntaxKind.SwitchExpressionArm, "0 => s.Seed");
        AssertContainsPropagatedText(result, SyntaxKind.SwitchExpression, "value switch");
    }

    [Fact]
    public void Analyze_ArgumentShell_PropagatesThroughArgumentListAndInvocation()
    {
        var result = AnalyzeWithSeed(
          PropagationSources.ArgumentShellSource,
          "argument-shell.cs",
          SyntaxKind.SimpleMemberAccessExpression,
          "s.Seed");

        AssertContainsPropagated(result, SyntaxKind.Argument, "s.Seed");
        AssertContainsPropagated(result, SyntaxKind.ArgumentList, "(s.Seed, 1)");
        AssertContainsPropagated(result, SyntaxKind.InvocationExpression, "Combine(s.Seed, 1)");
    }

    [Fact]
    public void Analyze_ChainedMemberAccess_PropagatesToOutermostAccessAndInvocation()
    {
        var result = Analyze(PropagationSources.ChainedMemberAccessSource, "chained-member-access.cs", "s");

        AssertContainsEffective(result, SyntaxKind.InvocationExpression, "s.Inner.Next.Value()");
        AssertContainsPropagatedText(result, SyntaxKind.ReturnStatement, "return s.Inner.Next.Value();");
    }

    [Fact]
    public void Analyze_SymbolReference_PropagatesFromMarkedDefinitionToLaterReferences()
    {
        var result = Analyze(PropagationSources.SymbolReferenceSource, "symbol-reference.cs", "s");

        AssertContainsPropagated(result, SyntaxKind.VariableDeclarator, "value = s.Seed");
        AssertContainsPropagated(result, SyntaxKind.IdentifierName, "value");
    }

    [Fact]
    public void Analyze_LogicalAndOperand_PropagatesToLogicalAndHost()
    {
        const string source = """
            class Sample
            {
                void Check(bool removable, bool survivor)
                {
                    if (removable && survivor)
                    {
                    }
                }
            }
            """;

        var result = AnalyzeWithSeed(
          source,
          "logical-and.cs",
          SyntaxKind.IdentifierName,
          "removable");

        Assert.Contains(result.PropagatedMarks, mark =>
          mark.Mark.SyntaxNode.IsKind(SyntaxKind.LogicalAndExpression) &&
          mark.Mark.SemanticTag == RuleFactPorts.FlowLogicalExpression);
        Assert.Contains(result.PropagatedMarks, mark =>
          string.Equals(mark.Mark.SyntaxNode.ToString(), "survivor", StringComparison.Ordinal) &&
          mark.Mark.SemanticTag == RuleFactPorts.TargetExpression);
        Assert.DoesNotContain(result.PropagatedMarks, mark =>
          mark.Mark.SyntaxNode.IsKind(SyntaxKind.IfStatement));
    }

    [Fact]
    public void Analyze_LogicalOrOperand_PropagatesToLogicalOrHost()
    {
        const string source = """
            class Sample
            {
                void Check(bool removable, bool survivor)
                {
                    if (removable || survivor)
                    {
                    }
                }
            }
            """;

        var result = AnalyzeWithSeed(
          source,
          "logical-or.cs",
          SyntaxKind.IdentifierName,
          "removable");

        var propagated = Assert.Single(result.PropagatedMarks, mark =>
          mark.RuleId == "DEL-SOBJ-PROP-LOGICAL-OPERAND-001");
        Assert.Equal(SyntaxKind.LogicalOrExpression, propagated.Mark.SyntaxNode.Kind());
        Assert.Equal("removable || survivor", propagated.Mark.SyntaxNode.ToString());
    }

    [Fact]
    public void Analyze_MultipleLogicalOperands_DeduplicatesHostAndRetainsSource()
    {
        const string source = """
            class Sample
            {
                void Check(bool first, bool second, bool survivor)
                {
                    if (first && second && survivor)
                    {
                    }
                }
            }
            """;

        var result = AnalyzeWithSeeds(
          source,
          "logical-and-deduplication.cs",
          (SyntaxKind.IdentifierName, "first"),
          (SyntaxKind.IdentifierName, "second"));

        Assert.Contains(result.PropagatedMarks, mark =>
          string.Equals(mark.Mark.SyntaxNode.ToString(), "first && second && survivor", StringComparison.Ordinal) &&
          mark.Mark.SemanticTag == RuleFactPorts.FlowLogicalExpression);
        Assert.Contains(result.PropagatedMarks, mark =>
          string.Equals(mark.Mark.SyntaxNode.ToString(), "survivor", StringComparison.Ordinal) &&
          mark.Mark.SemanticTag == RuleFactPorts.TargetExpression);
    }

    [Fact]
    public void Analyze_MixedLogicalChain_DoesNotPropagateAcrossOperatorKinds()
    {
        const string source = """
            class Sample
            {
                void Check(bool removable, bool second, bool survivor)
                {
                    if (removable && second || survivor)
                    {
                    }
                }
            }
            """;

        var result = AnalyzeWithSeed(
          source,
          "mixed-logical-chain.cs",
          SyntaxKind.IdentifierName,
          "removable");

        Assert.Contains(result.PropagatedMarks, mark =>
          mark.Mark.SyntaxNode.IsKind(SyntaxKind.LogicalAndExpression) &&
          string.Equals(mark.Mark.SyntaxNode.ToString(), "removable && second", StringComparison.Ordinal) &&
          mark.Mark.SemanticTag == RuleFactPorts.FlowLogicalExpression);
        Assert.DoesNotContain(result.PropagatedMarks, mark =>
          string.Equals(mark.Mark.SyntaxNode.ToString(), "survivor", StringComparison.Ordinal) &&
          mark.Mark.SemanticTag == RuleFactPorts.TargetExpression);
    }

    [Fact]
    public void Analyze_LogicalAndRightTarget_PropagatesTargetFactToLeftInvocationAndDeletesIf()
    {
        var result = AnalyzeDeleteClass(
          CreateCommandConditionSource("&&"),
          "logical-and-right-target.cs");

        Assert.Contains(result.PropagatedMarks, mark =>
          mark.Mark.SyntaxNode.IsKind(SyntaxKind.InvocationExpression) &&
          string.Equals(
            mark.Mark.SyntaxNode.ToString(),
            "CanExecuteCommand()",
            StringComparison.Ordinal) &&
          mark.Mark.SemanticTag == RuleFactPorts.TargetExpression);
        Assert.Contains(result.Decisions, decision =>
          decision.Action == DecisionActionKind.Delete &&
          decision.FinalNode.IsKind(SyntaxKind.IfStatement));
    }

    [Fact]
    public void Analyze_LogicalAndRightTarget_PropagatesTargetFactToParenthesizedLeftExpression()
    {
        const string source = """
            public static class PlayerInput
            {
                public static bool UsingGamepad => true;
            }

            public sealed class Sample
            {
                public void Check()
                {
                    if ((CanExecuteFirst() && CanExecuteSecond()) && PlayerInput.UsingGamepad)
                    {
                    }
                }

                private static bool CanExecuteFirst() => true;

                private static bool CanExecuteSecond() => true;
            }
            """;
        var result = AnalyzeDeleteClass(source, "logical-and-parenthesized-left.cs");

        Assert.Contains(result.PropagatedMarks, mark =>
          mark.Mark.SyntaxNode.IsKind(SyntaxKind.ParenthesizedExpression) &&
          string.Equals(
            mark.Mark.SyntaxNode.ToString(),
            "(CanExecuteFirst() && CanExecuteSecond())",
            StringComparison.Ordinal) &&
          mark.Mark.SemanticTag == RuleFactPorts.TargetExpression);
        Assert.Contains(result.Decisions, decision =>
          decision.Action == DecisionActionKind.Delete &&
          decision.FinalNode.IsKind(SyntaxKind.IfStatement));
    }

    [Fact]
    public void Analyze_LogicalAndChainRightTarget_PropagatesTargetFactToLeftLogicalSubtree()
    {
        const string source = """
            public static class PlayerInput
            {
                public static bool UsingGamepad => true;
            }

            public sealed class Sample
            {
                public void Check()
                {
                    if (CanExecuteFirst() && CanExecuteSecond() && PlayerInput.UsingGamepad)
                    {
                    }
                }

                private static bool CanExecuteFirst() => true;

                private static bool CanExecuteSecond() => true;
            }
            """;
        var result = AnalyzeDeleteClass(source, "logical-and-chain-right-target.cs");

        Assert.Contains(result.PropagatedMarks, mark =>
          mark.Mark.SyntaxNode.IsKind(SyntaxKind.LogicalAndExpression) &&
          string.Equals(
            mark.Mark.SyntaxNode.ToString(),
            "CanExecuteFirst() && CanExecuteSecond()",
            StringComparison.Ordinal) &&
          mark.Mark.SemanticTag == RuleFactPorts.TargetExpression);
        Assert.Contains(result.Decisions, decision =>
          decision.Action == DecisionActionKind.Delete &&
          decision.FinalNode.IsKind(SyntaxKind.IfStatement));
    }

    [Fact]
    public void Analyze_LogicalAndLeftTarget_PropagatesAcrossRightOperandChain()
    {
        const string source = """
            public static class PlayerInput
            {
                public static bool UsingGamepadUI => true;
            }

            public static class ItemID
            {
                public static class Sets
                {
                    public static bool OpenableBag(int type) => type > 0;
                }
            }

            public sealed class Sample
            {
                public void Check(int context, int type)
                {
                    if (PlayerInput.UsingGamepadUI &&
                        context == 0 &&
                        ItemID.Sets.OpenableBag(type))
                    {
                    }
                }
            }
            """;
        var result = AnalyzeDeleteClass(source, "logical-and-left-target.cs");

        Assert.Contains(result.PropagatedMarks, mark =>
          mark.Mark.SyntaxNode.IsKind(SyntaxKind.InvocationExpression) &&
          string.Equals(
            mark.Mark.SyntaxNode.ToString(),
            "ItemID.Sets.OpenableBag(type)",
            StringComparison.Ordinal) &&
          mark.Mark.SemanticTag == RuleFactPorts.TargetExpression);
        Assert.Contains(result.PropagatedMarks, mark =>
          mark.Mark.SyntaxNode.IsKind(SyntaxKind.EqualsExpression) &&
          string.Equals(mark.Mark.SyntaxNode.ToString(), "context == 0", StringComparison.Ordinal) &&
          mark.Mark.SemanticTag == RuleFactPorts.TargetExpression);
        Assert.Contains(result.PropagatedMarks, mark =>
          mark.Mark.SyntaxNode.IsKind(SyntaxKind.LogicalAndExpression) &&
          mark.Mark.SyntaxNode.ToString().Contains(
            "PlayerInput.UsingGamepadUI",
            StringComparison.Ordinal) &&
          mark.Mark.SemanticTag == RuleFactPorts.FlowLogicalExpression);
        Assert.Contains(result.Decisions, decision =>
          decision.Action == DecisionActionKind.Delete &&
          decision.FinalNode.IsKind(SyntaxKind.IfStatement));
    }

    [Fact]
    public void Analyze_LogicalOrRightTarget_DoesNotPropagateTargetFactToLeftInvocation()
    {
        var result = AnalyzeDeleteClass(
          CreateCommandConditionSource("||"),
          "logical-or-right-target.cs");

        Assert.DoesNotContain(result.PropagatedMarks, mark =>
          mark.Mark.SyntaxNode.IsKind(SyntaxKind.InvocationExpression) &&
          string.Equals(
            mark.Mark.SyntaxNode.ToString(),
            "CanExecuteCommand()",
            StringComparison.Ordinal) &&
          mark.Mark.SemanticTag == RuleFactPorts.TargetExpression);
        Assert.DoesNotContain(result.Decisions, decision =>
          decision.Action == DecisionActionKind.Delete &&
          decision.FinalNode.IsKind(SyntaxKind.IfStatement));
    }

    [Fact]
    public void Analyze_LogicalOrInitializerTarget_PreservesNonTargetDefinitionAndItsMousePath()
    {
        const string source = """
            public static class PlayerInput
            {
                public static bool UsingGamepad => true;
            }

            public sealed class Sample
            {
                public void Check(bool selectionChanged, bool mouseClick, int context)
                {
                    bool flag = selectionChanged || (PlayerInput.UsingGamepad && context == 41);
                    if (flag && mouseClick)
                    {
                        selectionChanged = true;
                    }
                }
            }
            """;

        var result = AnalyzeDeleteClass(source, "logical-or-initializer-target.cs");

        Assert.DoesNotContain(result.Decisions, decision =>
          decision.Action == DecisionActionKind.Delete &&
          decision.FinalNode is VariableDeclaratorSyntax declarator &&
          declarator.Identifier.ValueText == "flag");
        Assert.DoesNotContain(result.Decisions, decision =>
          decision.Action == DecisionActionKind.Delete &&
          decision.FinalNode is IfStatementSyntax ifStatement &&
          ifStatement.Condition.ToString().Contains("mouseClick", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_TargetedLocalWithSurvivingReads_PreservesDeclarationAndBindings()
    {
        const string source = """
            public static class PlayerInput
            {
                public static bool UsingGamepad => true;
            }

            public readonly struct Vector2
            {
                public Vector2(float x, float y) { }
                public static Vector2 operator +(Vector2 left, Vector2 right) => left;
                public Vector2 Floor() => this;
            }

            public sealed class Sample
            {
                private static Vector2 Read() => new(1, 1);
                private static void Use(Vector2 value) { }

                public void Draw()
                {
                    Vector2 vector = Read() + new Vector2(PlayerInput.UsingGamepad ? 1 : 0, 0);
                    Vector2 vec = vector + Read();
                    vector += Read();
                    vec = vec.Floor();
                    Use(vector);
                    Use(vec);
                }
            }
            """;

        var result = AnalyzeDeleteClass(source, "targeted-local-surviving-reads.cs");

        Assert.Contains("Vector2 vector =", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Contains("Vector2 vec =", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Contains("vector += Read();", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Contains("vec = vec.Floor();", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Contains("Use(vector);", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Contains("Use(vec);", result.RewrittenSource, StringComparison.Ordinal);
        AssertBindingValid(result.RewrittenSource! + "\npublic static class PlayerInput { public static bool UsingGamepad => true; }");
    }

    [Fact]
    public void Analyze_LogicalOrTarget_DoesNotDeleteLocalsUsedBySurvivingReturn()
    {
        const string source = """
            public static class PlayerInput
            {
                public static bool UsingGamepad => true;
            }

            public sealed class Sample
            {
                public int Read(bool up, bool right, bool down, bool left)
                {
                    bool flag = up || (PlayerInput.UsingGamepad && right);
                    int value = flag ? 1 : 2;
                    int value2 = right ? 3 : 4;
                    int value3 = down ? 5 : 6;
                    int value4 = left ? 7 : 8;
                    return value + value2 + value3 + value4;
                }
            }
            """;

        var result = AnalyzeDeleteClass(source, "logical-or-surviving-locals.cs");

        Assert.Contains("bool flag =", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Contains("int value =", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Contains("int value2 =", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Contains("int value3 =", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Contains("int value4 =", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Contains("return value + value2 + value3 + value4;", result.RewrittenSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_LocalReferenceInLogicalOr_DoesNotDeleteDerivedLocalUsedByReturn()
    {
        const string source = """
            public static class PlayerInput
            {
                public static bool UsingGamepad => true;
            }

            public sealed class Sample
            {
                public bool Read(bool right, bool menuRight)
                {
                    bool flag = PlayerInput.UsingGamepad;
                    bool value = right || (flag && menuRight);
                    return value;
                }
            }
            """;

        var result = AnalyzeDeleteClass(source, "local-reference-logical-or.cs");

        Assert.Contains("bool flag = PlayerInput.UsingGamepad;", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Contains("bool value = right;", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Contains("return value;", result.RewrittenSource, StringComparison.Ordinal);
        AssertBindingValid(result.RewrittenSource! + "\npublic static class PlayerInput { public static bool UsingGamepad => true; }");
    }

    [Fact]
    public void Analyze_TargetedLocalReferencedBySurvivingElse_PreservesDeclaration()
    {
        const string source = """
            public static class PlayerInput
            {
                public static int Value => 1;
                public static bool UsingGamepad => true;
            }

            public sealed class Sample
            {
                private static void Use(int value) { }

                public void Draw(bool fallback)
                {
                    int value = PlayerInput.Value;
                    if (fallback)
                    {
                        Use(PlayerInput.UsingGamepad ? 1 : 0);
                    }
                    else
                    {
                        Use(value);
                    }
                }
            }
            """;

        var result = AnalyzeDeleteClass(source, "targeted-local-surviving-else.cs");

        Assert.Contains("int value = PlayerInput.Value;", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Contains("Use(value);", result.RewrittenSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_ParenthesizedLogicalOrTarget_PropagatesAcrossContainingLogicalAndOnly()
    {
        const string source = """
            public static class PlayerInput
            {
                public static bool UsingGamepad => true;
            }

            public sealed class Sample
            {
                public void Check(bool left, bool fallback)
                {
                    if (left && (PlayerInput.UsingGamepad || fallback))
                    {
                    }
                }
            }
            """;

        var result = AnalyzeDeleteClass(source, "parenthesized-logical-or-target.cs");

        Assert.Contains(result.PropagatedMarks, mark =>
            mark.RuleId == "DEL-SOBJ-PROP-LOGICAL-OPERAND-001" &&
            string.Equals(mark.Mark.SyntaxNode.ToString(), "left", StringComparison.Ordinal) &&
            mark.Mark.SemanticTag == RuleFactPorts.TargetExpression);
        Assert.DoesNotContain(result.PropagatedMarks, mark =>
            mark.RuleId == "DEL-SOBJ-PROP-LOGICAL-OPERAND-001" &&
            string.Equals(mark.Mark.SyntaxNode.ToString(), "fallback", StringComparison.Ordinal) &&
            mark.Mark.SemanticTag == RuleFactPorts.TargetExpression);
    }

    [Fact]
    public void Analyze_LogicalAndComparisonOperand_PropagatesToUnarySibling()
    {
        const string source = """
            class Sample
            {
                void Check(bool removable, string text)
                {
                    if (!removable && text.Length > 0)
                    {
                    }
                }
            }
            """;

        var result = AnalyzeWithSeed(
          source,
          "logical-and-comparison-operand.cs",
          SyntaxKind.SimpleMemberAccessExpression,
          "text.Length");

        Assert.Contains(result.PropagatedMarks, mark =>
          mark.RuleId == "DEL-SOBJ-PROP-LOGICAL-OPERAND-001" &&
          mark.Mark.SyntaxNode.IsKind(SyntaxKind.LogicalNotExpression) &&
          string.Equals(mark.Mark.SyntaxNode.ToString(), "!removable", StringComparison.Ordinal) &&
          mark.Mark.SemanticTag == RuleFactPorts.TargetExpression);
    }

    [Fact]
    public void Analyze_LogicalAndComparisonOperand_DeletesIfAfterBothOperandsPropagate()
    {
        const string source = """
            public static class PlayerInput
            {
                public static bool GamepadDisableInstructionsDisplay => true;
            }

            class Sample
            {
                void Check(string text)
                {
                    if (!PlayerInput.GamepadDisableInstructionsDisplay && text.Length > 0)
                    {
                    }
                }
            }
            """;

        var result = Analyze(source, "logical-and-comparison-rewrite.cs", "text");

        Assert.DoesNotContain("if (", result.RewrittenSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_LogicalPrecedence_UsesParenthesizedSubtreeAsAndOperand()
    {
        const string source = """
            class Sample
            {
                void Check(bool removable, string text, bool i, bool q)
                {
                    if (removable && (text.Length > 0 || i) || q)
                    {
                    }
                }
            }
            """;

        var result = AnalyzeWithSeed(
          source,
          "logical-precedence.cs",
          SyntaxKind.IdentifierName,
          "removable");

        Assert.Contains(result.PropagatedMarks, mark =>
          mark.Mark.SyntaxNode.IsKind(SyntaxKind.ParenthesizedExpression) &&
          string.Equals(mark.Mark.SyntaxNode.ToString(), "(text.Length > 0 || i)", StringComparison.Ordinal) &&
          mark.Mark.SemanticTag == RuleFactPorts.TargetExpression);
        Assert.DoesNotContain(result.PropagatedMarks, mark =>
          mark.Mark.SemanticTag == RuleFactPorts.TargetExpression &&
          (string.Equals(mark.Mark.SyntaxNode.ToString(), "i", StringComparison.Ordinal) ||
           string.Equals(mark.Mark.SyntaxNode.ToString(), "q", StringComparison.Ordinal)));
    }

    [Fact]
    public void Analyze_LogicalOrOperand_UsesWholeOrGroupAsContainingAndOperand()
    {
        const string source = """
            class Sample
            {
                void Check(bool removable, string text, bool i, bool q)
                {
                    if (removable && (text.Length > 0 || i) || q)
                    {
                    }
                }
            }
            """;

        var result = AnalyzeWithSeed(
          source,
          "logical-or-precedence-boundary.cs",
          SyntaxKind.SimpleMemberAccessExpression,
          "text.Length");

        Assert.Contains(result.PropagatedMarks, mark =>
          mark.RuleId == "DEL-SOBJ-PROP-LOGICAL-OPERAND-001" &&
          mark.Mark.SyntaxNode.IsKind(SyntaxKind.LogicalOrExpression) &&
          string.Equals(mark.Mark.SyntaxNode.ToString(), "text.Length > 0 || i", StringComparison.Ordinal) &&
          mark.Mark.SemanticTag == RuleFactPorts.FlowLogicalExpression);
        Assert.Contains(result.PropagatedMarks, mark =>
          string.Equals(mark.Mark.SyntaxNode.ToString(), "removable", StringComparison.Ordinal) &&
          mark.Mark.SemanticTag == RuleFactPorts.TargetExpression);
        Assert.DoesNotContain(result.PropagatedMarks, mark =>
          mark.Mark.SemanticTag == RuleFactPorts.TargetExpression &&
          (string.Equals(mark.Mark.SyntaxNode.ToString(), "i", StringComparison.Ordinal) ||
           string.Equals(mark.Mark.SyntaxNode.ToString(), "q", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("&", "left & right", SyntaxKind.BitwiseAndExpression)]
    [InlineData("|", "left | right", SyntaxKind.BitwiseOrExpression)]
    [InlineData("^", "left ^ right", SyntaxKind.ExclusiveOrExpression)]
    public void Analyze_BitwiseBooleanOperand_PropagatesOnlyTheAggregate(
      string binaryOperator,
      string expectedOperand,
      SyntaxKind expectedKind)
    {
        var source = $$"""
            class Sample
            {
                void Check(bool removable, bool left, bool right)
                {
                    if (removable && left {{binaryOperator}} right)
                    {
                    }
                }
            }
            """;

        var result = AnalyzeWithSeed(
          source,
          "bitwise-boolean-operand.cs",
          SyntaxKind.IdentifierName,
          "left");

        Assert.Contains(result.PropagatedMarks, mark =>
          mark.Mark.SyntaxNode.IsKind(expectedKind) &&
          string.Equals(mark.Mark.SyntaxNode.ToString(), expectedOperand, StringComparison.Ordinal) &&
          mark.Mark.SemanticTag == RuleFactPorts.TargetExpression);
        Assert.DoesNotContain(result.PropagatedMarks, mark =>
          string.Equals(mark.Mark.SyntaxNode.ToString(), "right", StringComparison.Ordinal) &&
          mark.Mark.SemanticTag == RuleFactPorts.TargetExpression);
    }

    [Fact]
    public void Analyze_UnaryOperand_PropagatesToUnaryExpressionOnly()
    {
        const string source = """
            class Sample
            {
                void Check(bool removable, bool survivor)
                {
                    if (!removable && survivor)
                    {
                    }
                }
            }
            """;

        var result = AnalyzeWithSeed(
          source,
          "unary-operand.cs",
          SyntaxKind.IdentifierName,
          "removable");

        Assert.Contains(result.PropagatedMarks, mark =>
          mark.Mark.SyntaxNode.IsKind(SyntaxKind.LogicalNotExpression) &&
          string.Equals(mark.Mark.SyntaxNode.ToString(), "!removable", StringComparison.Ordinal) &&
          string.Equals(
            mark.Mark.SemanticTag?.Value,
            "Flow.UnaryExpression",
            StringComparison.Ordinal));
        Assert.DoesNotContain(result.PropagatedMarks, mark =>
          mark.Mark.SemanticTag == RuleFactPorts.TargetExpression &&
          string.Equals(mark.Mark.SyntaxNode.ToString(), "survivor", StringComparison.Ordinal));
        Assert.DoesNotContain(result.LiftedMarks, mark =>
          mark.Mark.SyntaxNode.IsKind(SyntaxKind.LogicalAndExpression));
    }

    [Fact]
    public void Analyze_UnaryTerminalInitializer_DoesNotPropagateThroughLocalReference()
    {
        const string source = """
            public static class PlayerInput
            {
                public static bool UsingGamepad => true;
            }

            public sealed class Sample
            {
                public bool Check(bool enabled, bool smart)
                {
                    bool flag = enabled && !PlayerInput.UsingGamepad;
                    return smart && flag;
                }
            }
            """;

        var result = AnalyzeDeleteClass(source, "unary-terminal-local-reference.cs");

        Assert.DoesNotContain(result.PropagatedMarks, mark =>
          mark.Mark.SyntaxNode is VariableDeclaratorSyntax declarator &&
          string.Equals(declarator.Identifier.ValueText, "flag", StringComparison.Ordinal) &&
          mark.Mark.SemanticTag == RuleFactPorts.FlowLocalDefinition);
        Assert.DoesNotContain(result.PropagatedMarks, mark =>
          mark.Mark.SyntaxNode is IdentifierNameSyntax identifier &&
          string.Equals(identifier.Identifier.ValueText, "flag", StringComparison.Ordinal) &&
          mark.Mark.SemanticTag == RuleFactPorts.FlowSymbolReference);
        Assert.DoesNotContain(result.PropagatedMarks, mark =>
          mark.Mark.SyntaxNode.IsKind(SyntaxKind.LogicalAndExpression) &&
          string.Equals(mark.Mark.SyntaxNode.ToString(), "smart && flag", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_ConditionalOperand_ProducesWholeExpressionHostWithoutBranchOrOuterPropagation()
    {
        const string source = """
            class Sample
            {
                void Check(bool removable, bool first, bool second, bool survivor)
                {
                    if ((removable ? first : second) && survivor)
                    {
                    }
                }
            }
            """;

        var result = AnalyzeWithSeed(
          source,
          "conditional-operand.cs",
          SyntaxKind.IdentifierName,
          "removable");

        Assert.Contains(result.PropagatedMarks, mark =>
          mark.Mark.SyntaxNode.IsKind(SyntaxKind.ConditionalExpression) &&
          string.Equals(mark.Mark.SyntaxNode.ToString(), "removable ? first : second", StringComparison.Ordinal) &&
          string.Equals(
            mark.Mark.SemanticTag?.Value,
            "Flow.ConditionalExpression",
            StringComparison.Ordinal));
        Assert.DoesNotContain(result.PropagatedMarks, mark =>
          mark.Mark.SemanticTag == RuleFactPorts.TargetExpression &&
          (string.Equals(mark.Mark.SyntaxNode.ToString(), "first", StringComparison.Ordinal) ||
           string.Equals(mark.Mark.SyntaxNode.ToString(), "second", StringComparison.Ordinal) ||
           string.Equals(mark.Mark.SyntaxNode.ToString(), "survivor", StringComparison.Ordinal)));
        Assert.Contains(result.LiftedMarks, mark =>
          mark.Mark.SyntaxNode.IsKind(SyntaxKind.ConditionalExpression) &&
          string.Equals(mark.Mark.SyntaxNode.ToString(), "removable ? first : second", StringComparison.Ordinal) &&
          mark.Mark.SemanticTag == RuleFactPorts.LiftExpressionHost);
        Assert.DoesNotContain(result.LiftedMarks, mark =>
          mark.Mark.SyntaxNode.IsKind(SyntaxKind.LogicalAndExpression));
    }

    [Fact]
    public void Analyze_FlagReferenceChain_ReachesFixedPointWithStableDopResults()
    {
        const string source = """
            static class Options
            {
                public static bool DisableLeftShiftTrashCan => true;
                public static bool DisableQuickTrash => false;
            }

            static class PlayerInput
            {
                public static bool UsingGamepad => false;
            }

            class Sample
            {
                public bool Check()
                {
                    bool flag = Options.DisableLeftShiftTrashCan && PlayerInput.UsingGamepad;
                    return !Options.DisableQuickTrash && flag;
                }
            }
            """;
        var serial = AnalyzeFlagChain(source, 1);
        var twoWorkers = AnalyzeFlagChain(source, 2);
        var parallel = AnalyzeFlagChain(source, 16);

        var serialDeclarator = Assert.Single(
          serial.PropagatedMarks,
          mark => mark.Mark.SyntaxNode is VariableDeclaratorSyntax declarator &&
            string.Equals(declarator.Identifier.ValueText, "flag", StringComparison.Ordinal) &&
            mark.Mark.SemanticTag == RuleFactPorts.FlowLocalDefinition);
        Assert.Equal(1, serialDeclarator.Depth);
        var serialReference = Assert.Single(
          serial.PropagatedMarks,
          mark => mark.Mark.SyntaxNode is IdentifierNameSyntax identifier &&
            string.Equals(identifier.Identifier.ValueText, "flag", StringComparison.Ordinal) &&
            mark.Mark.SemanticTag == RuleFactPorts.FlowSymbolReference);
        Assert.Equal(2, serialReference.Depth);
        Assert.Contains(serial.PropagatedMarks, mark =>
          mark.Mark.SyntaxNode.IsKind(SyntaxKind.LogicalAndExpression) &&
          string.Equals(mark.Mark.SyntaxNode.ToString(), "!Options.DisableQuickTrash && flag", StringComparison.Ordinal) &&
          mark.Mark.SemanticTag == RuleFactPorts.FlowLogicalExpression);

        Assert.Equal(BuildFactSignature(serial), BuildFactSignature(parallel));
        Assert.Equal(BuildFactSignature(serial), BuildFactSignature(twoWorkers));
        Assert.Equal(
          JsonSerializer.Serialize(serial.Evidence),
          JsonSerializer.Serialize(parallel.Evidence));
        Assert.Equal(
          JsonSerializer.Serialize(serial.Evidence),
          JsonSerializer.Serialize(twoWorkers.Evidence));
        Assert.Equal(
          serial.Decisions.Select(BuildDecisionSignature),
          parallel.Decisions.Select(BuildDecisionSignature));
        Assert.Equal(
          serial.Decisions.Select(BuildDecisionSignature),
          twoWorkers.Decisions.Select(BuildDecisionSignature));
        Assert.Equal(serial.RewrittenSource, parallel.RewrittenSource);
        Assert.Equal(serial.RewrittenSource, twoWorkers.RewrittenSource);
        Assert.Equal(serial.Diff.ToString(), parallel.Diff.ToString());
        Assert.Equal(serial.Diff.ToString(), twoWorkers.Diff.ToString());
    }

    [Fact]
    public void Analyze_FlagSymbolReference_PropagatesToLogicalHostAndSiblingOperand()
    {
        const string source = """
            static class PlayerInput
            {
                public static bool InBuildingMode => false;
            }

            class Sample
            {
                public bool Check(bool smart, bool available)
                {
                    bool flag = available && PlayerInput.InBuildingMode;
                    return smart && flag;
                }
            }
            """;

        var result = AnalyzeDeleteClass(source, "flag-symbol-logical-host.cs");

        Assert.Contains(result.PropagatedMarks, mark =>
            mark.RuleId == "DEL-SOBJ-PROP-LOGICAL-OPERAND-001" &&
            mark.Mark.SyntaxNode.IsKind(SyntaxKind.LogicalAndExpression) &&
            string.Equals(mark.Mark.SyntaxNode.ToString(), "smart && flag", StringComparison.Ordinal) &&
            mark.Mark.SemanticTag == RuleFactPorts.FlowLogicalExpression);
        Assert.Contains(result.PropagatedMarks, mark =>
            mark.RuleId == "DEL-SOBJ-PROP-LOGICAL-OPERAND-001" &&
            string.Equals(mark.Mark.SyntaxNode.ToString(), "smart", StringComparison.Ordinal) &&
            mark.Mark.SemanticTag == RuleFactPorts.TargetExpression);
    }

    private static PrototypeAnalysisResult AnalyzeFlagChain(string source, int maxDegreeOfParallelism)
    {
        var application = new ApplicationService(RuleRegistry.CreateDefaultRules());
        return application.Analyze(
          source,
          "flag-chain.cs",
          new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
          {
            ["delete-class"] = "PlayerInput",
            ["max-degree-of-parallelism"] = maxDegreeOfParallelism.ToString(),
            ["cpg-max-degree-of-parallelism"] = maxDegreeOfParallelism.ToString(),
            ["enable-group-parallelism"] = "true"
          });
    }

    private static void AssertBindingValid(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
          "rewritten-source",
          new[] { tree },
          new[]
          {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
          },
          new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assert.DoesNotContain(
          compilation.GetDiagnostics(),
          diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static string BuildFactSignature(PrototypeAnalysisResult result)
    {
        return string.Join(
          "|",
          result.PropagatedMarks
            .Select(mark => string.Join(
              ":",
              mark.RuleId,
              mark.Mark.SyntaxNode.SpanStart,
              mark.Mark.SyntaxNode.Span.Length,
              mark.Mark.SyntaxNode.RawKind,
              mark.Mark.SemanticTag?.Value,
              mark.Depth)));
    }

    private static string BuildDecisionSignature(RuleDecision decision)
    {
        return string.Join(
          ":",
          decision.Action,
          decision.FinalNode.SpanStart,
          decision.FinalNode.Span.Length,
          decision.Reason);
    }

    private static PrototypeAnalysisResult Analyze(string source, string filePath, string targetName)
    {
        var application = new  ApplicationService(RuleRegistry.CreateDefaultRules());
        return application.Analyze(
          source,
          filePath,
          new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
          {
            ["target-name"] = targetName
          });
    }

    private static PrototypeAnalysisResult AnalyzeDeleteClass(string source, string filePath)
    {
        var application = new ApplicationService(RuleRegistry.CreateDefaultRules());
        return application.Analyze(
          source,
          filePath,
          new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
          {
            ["delete-class"] = "PlayerInput"
          });
    }

    private static string CreateCommandConditionSource(string logicalOperator)
    {
        return $$"""
            public static class PlayerInput
            {
                public static bool UsingGamepad => true;
            }

            public sealed class Sample
            {
                public void Check()
                {
                    if (CanExecuteCommand() {{logicalOperator}} PlayerInput.UsingGamepad)
                    {
                    }
                }

                private static bool CanExecuteCommand()
                {
                    return true;
                }
            }
            """;
    }

    private static PrototypeAnalysisResult AnalyzeWithSeed(string source, string filePath, SyntaxKind seedKind, string seedText)
    {
        return AnalyzeWithSeeds(source, filePath, (seedKind, seedText));
    }

    private static PrototypeAnalysisResult AnalyzeWithSeeds(string source, string filePath, params (SyntaxKind Kind, string Text)[] seeds)
    {
        var application = new  ApplicationService(
          new RuleDefinitionMark[] { new ExactSyntaxSeedRule(seeds) },
          RuleRegistry.CreateDefaultRules().Propagators
            .OfType<ExpressionFlowPropagationRuleBase>()
            .ToList(),
          new RuleDefinitionLift[]
          {
            new ExpressionHostLiftingRule(),
            new IfStructureLiftingRule(),
            new SwitchStructureLiftingRule()
          },
          Array.Empty<RuleDefinitionPropose>());
        return application.Analyze(
          source,
          filePath,
          new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static void AssertContainsPropagated(PrototypeAnalysisResult result, SyntaxKind kind, string expectedText)
    {
        Assert.Contains(EnumerateEffectiveNodes(result), node =>
          node.RawKind == (int)kind &&
          string.Equals(node.ToString(), expectedText, StringComparison.Ordinal));
    }

    private static void AssertContainsPropagatedText(PrototypeAnalysisResult result, SyntaxKind kind, string expectedText)
    {
        Assert.Contains(EnumerateEffectiveNodes(result), node =>
          node.RawKind == (int)kind &&
          node.ToString().Contains(expectedText, StringComparison.Ordinal));
    }

    private static void AssertContainsEffective(PrototypeAnalysisResult result, SyntaxKind kind, string expectedText)
    {
        Assert.Contains(EnumerateEffectiveNodes(result), node =>
          node.RawKind == (int)kind &&
          string.Equals(node.ToString(), expectedText, StringComparison.Ordinal));
    }

    private static void AssertContainsEffectiveText(PrototypeAnalysisResult result, SyntaxKind kind, string expectedText)
    {
        Assert.Contains(EnumerateEffectiveNodes(result), node =>
          node.RawKind == (int)kind &&
          node.ToString().Contains(expectedText, StringComparison.Ordinal));
    }

    private static IEnumerable<SyntaxNode> EnumerateEffectiveNodes(PrototypeAnalysisResult result)
    {
        return result.SeedMarks
          .Select(mark => mark.SyntaxNode)
          .Concat(result.PropagatedMarks.Select(mark => mark.Mark.SyntaxNode))
          .Concat(result.LiftedMarks.Select(mark => mark.Mark.SyntaxNode));
    }

    private sealed class ExactSyntaxSeedRule : RuleDefinitionMark
    {
        private static readonly RuleSemanticTag AtomicTargetSemanticTag = RuleFactPorts.TargetExpression;
        private readonly IReadOnlyList<(SyntaxKind Kind, string Text)> _seeds;

        public ExactSyntaxSeedRule(IReadOnlyList<(SyntaxKind Kind, string Text)> seeds)
        {
            _seeds = seeds;
        }

    public override string RuleId { get; } = "DEL-SOBJ-MARK-MEMBER-001";


        public override string Name { get; } = "Exact syntax seed for propagation tests";

        public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds =>
          _seeds.Select(seed => seed.Kind).Distinct().ToList();

        public override RuleProducesContract Produces { get; } = new(new[]
        {
          new RuleProducedSyntax(ExpressionFlowPropagationRuleBase.TargetExpressionInputNodeKinds, AtomicTargetSemanticTag)
        });

        public override IEnumerable<MarkRecord> Mark(IMarkRuleContext context, SyntaxNode root)
        {
            foreach (var seed in _seeds)
            {
                foreach (var node in root.DescendantNodes()
                           .Where(node =>
                             node.RawKind == (int)seed.Kind &&
                             string.Equals(node.ToString(), seed.Text, StringComparison.Ordinal)))
                {
                    yield return new MarkRecord(
                      RuleId,
                      node,
                      null,
                      null,
                      $"Test seed '{seed.Text}'.",
                      SemanticTag: AtomicTargetSemanticTag,
                      Origins: RuleEvidenceOrigin.AtomicExpression);
                }
            }
        }
    }
}
