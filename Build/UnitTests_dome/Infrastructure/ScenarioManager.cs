using System;
using System.Reflection;
using TerrariaTools.Dome.Tests.Scenarios;

namespace TerrariaTools.Dome.Tests.Infrastructure
{
    public static class ScenarioManager
    {
        public static string? GetScenario(string path)
        {
            // Path format: "MemberName" or "NestedClass.MemberName"
            // e.g. "IfElse" or "IfElseVariants.SimpleIf"

            var parts = path.Split('.');
            Type currentType = typeof(SharedScenarios);
            object? currentValue = null;

            // Handle nested classes
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var nestedType = currentType.GetNestedType(parts[i]);
                if (nestedType != null)
                {
                    currentType = nestedType;
                }
                else
                {
                    // Maybe it's a property/field on the current type that returns an object?
                    // For now assuming static classes structure from SharedScenarios
                    return null;
                }
            }

            var memberName = parts[parts.Length - 1];
            var field = currentType.GetField(memberName, BindingFlags.Public | BindingFlags.Static);
            if (field != null)
            {
                return field.GetValue(null) as string;
            }

            var prop = currentType.GetProperty(memberName, BindingFlags.Public | BindingFlags.Static);
            if (prop != null)
            {
                return prop.GetValue(null) as string;
            }

            return null;
        }
    }
}
