using System.Runtime.CompilerServices;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using VerifyTests;

namespace TerrariaTools.Testing.GoldenOutputs;

public sealed class VerifyCompatibilitySettingsFixture
{
    private static int _initialized;

    public VerifyCompatibilitySettingsFixture()
    {
        EnsureInitialized();
    }

    public static SettingsTask VerifyJson(object target, [CallerFilePath] string sourceFilePath = "")
    {
        EnsureInitialized();
        return Verifier.Verify(target, CreateSettings(sourceFilePath));
    }

    public static SettingsTask VerifyText(string text, [CallerFilePath] string sourceFilePath = "")
    {
        EnsureInitialized();
        return Verifier.Verify(text, CreateSettings(sourceFilePath));
    }

    [ModuleInitializer]
    public static void Initialize()
    {
        EnsureInitialized();
    }

    private static void EnsureInitialized()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        VerifierSettings.SortPropertiesAlphabetically();
        VerifierSettings.IgnoreMember<ModelExecution.RunReport>(report => report.Message);
        VerifierSettings.IgnoreMember<ApplicationAbstractions.WorkspaceLoadDiagnostic>(diagnostic => diagnostic.Message);
        VerifierSettings.AddScrubber(builder =>
        {
            builder.Replace("\\\\", "/");
            builder.Replace("\\", "/");
        });
    }

    private static VerifySettings CreateSettings(string sourceFilePath)
    {
        var settings = new VerifySettings();
        settings.UseDirectory(Path.GetDirectoryName(sourceFilePath)!);
        return settings;
    }
}




