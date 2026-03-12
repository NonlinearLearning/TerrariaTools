using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Rules.Dome;
using TerrariaTools.Rules.Dome.Mark.StaticRules;
using TerrariaTools.Dome.Tests.Infrastructure;
using Xunit;

namespace TerrariaTools.Dome.Tests.Rules
{
    public class AnalysisRulesTests : RoslynTestBase
    {
        [Fact]
        public async Task ClassMarkingRule_ShouldMarkUnusedClasses()
        {
            var codeA = @"
public class UsedClass { }
public class BaseClass { }
public class UnusedClass { }
public class DerivedClass : BaseClass { }
";
            var codeB = @"
class Client {
    void Use() {
        var u = new UsedClass();
    }
}
";
            var (workspace, solution, project) = await CreateSolutionAsync(
                ("CodeA.cs", codeA),
                ("CodeB.cs", codeB)).ConfigureAwait(false);

            var docA = project.Documents.First(d => d.Name == "CodeA.cs");
            var model = await docA.GetSemanticModelAsync().ConfigureAwait(false);
            var root = await docA.GetSyntaxRootAsync().ConfigureAwait(false);

            var rule = new ClassMarkingRule();
            var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToList();

            // UsedClass -> Has references
            var usedClass = classes.First(c => c.Identifier.Text == "UsedClass");
            var markedUsed = await rule.MarkClassAsync(usedClass, model, solution).ConfigureAwait(false);
            Assert.False(markedUsed.HasAnnotations(RuleConstants.RewriteAnnotationKind));

            // BaseClass -> Has derived class
            var baseClass = classes.First(c => c.Identifier.Text == "BaseClass");
            var markedBase = await rule.MarkClassAsync(baseClass, model, solution).ConfigureAwait(false);
            Assert.False(markedBase.HasAnnotations(RuleConstants.RewriteAnnotationKind));

            // UnusedClass -> No refs, no derived
            var unusedClass = classes.First(c => c.Identifier.Text == "UnusedClass");
            var markedUnused = await rule.MarkClassAsync(unusedClass, model, solution).ConfigureAwait(false);
            Assert.True(markedUnused.HasAnnotations(RuleConstants.RewriteAnnotationKind));
            Assert.Contains(markedUnused.GetAnnotations(RuleConstants.RewriteAnnotationKind), a => a.Data == "Action=Delete");
        }

        [Fact]
        public async Task FunctionMarkingRule_ShouldMarkUnusedFunctions()
        {
            var codeA = @"
public class MyClass {
    public void UnusedMethod() { }
    public void UsedMethod() { }
    public int UnusedReturnMethod() { return 1; }
    public int UsedReturnMethod() { return 1; }
    public virtual void VirtualMethod() { }
}
";
            var codeB = @"
class Client {
    void Use() {
        var c = new MyClass();
        c.UsedMethod();
        var x = c.UsedReturnMethod();
    }
}
";
            var (workspace, solution, project) = await CreateSolutionAsync(
                ("CodeA.cs", codeA),
                ("CodeB.cs", codeB)).ConfigureAwait(false);

            var docA = project.Documents.First(d => d.Name == "CodeA.cs");
            var model = await docA.GetSemanticModelAsync().ConfigureAwait(false);
            var root = await docA.GetSyntaxRootAsync().ConfigureAwait(false);

            var rule = new FunctionMarkingRule();
            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();

            // UnusedMethod -> Delete
            var unusedMethod = methods.First(m => m.Identifier.Text == "UnusedMethod");
            var markedUnused = await rule.MarkMethodAsync(unusedMethod, model, solution).ConfigureAwait(false);
            Assert.True(markedUnused.HasAnnotations(RuleConstants.RewriteAnnotationKind));
            Assert.Contains(markedUnused.GetAnnotations(RuleConstants.RewriteAnnotationKind), a => a.Data == "Action=Delete");

            // UsedMethod -> Keep
            var usedMethod = methods.First(m => m.Identifier.Text == "UsedMethod");
            var markedUsed = await rule.MarkMethodAsync(usedMethod, model, solution).ConfigureAwait(false);
            Assert.False(markedUsed.HasAnnotations(RuleConstants.RewriteAnnotationKind));

            // VirtualMethod -> Keep (Inheritance Chain)
            // Even if unused, virtual methods are protected by inheritance check in rule
            var virtualMethod = methods.First(m => m.Identifier.Text == "VirtualMethod");
            var markedVirtual = await rule.MarkMethodAsync(virtualMethod, model, solution).ConfigureAwait(false);
            Assert.False(markedVirtual.HasAnnotations(RuleConstants.RewriteAnnotationKind));
        }
    }
}
