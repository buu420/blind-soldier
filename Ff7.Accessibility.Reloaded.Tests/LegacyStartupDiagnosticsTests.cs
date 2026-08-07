using System.Text;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Reloaded.Runtime;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class LegacyStartupDiagnosticsTests
{
    private const string BinaryFormatterSwitch =
        "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization";

    internal static void Run()
    {
        ClassifiesStockSeventhHeavenLateAttach();
        ClassifiesDirectReloadedStartup();
        ClassifiesIncompleteEvidenceAsUnexpected();
        RejectsUnrecognizedReloadedPrefixes();
        RecognizesExactReloadedEvidenceForCapture();
        RequiresExecutableDirectoryForAppLoaderEvidence();
        DoesNotRetainTheObsoleteManagedStartupContract();
    }

    private static void ClassifiesStockSeventhHeavenLateAttach()
    {
        var snapshot = new LegacyStartupSnapshot(
            Is64Bit: false,
            NativeModules:
            [
                "dinput.dll | 4.5.2.0 | C:\\FF7\\dinput.dll",
                "FFNx.dll | 1.24.3.0 | C:\\FF7\\FFNx.dll",
                "coreclr.dll | 9.0.0.0 | C:\\FF7\\coreclr.dll",
                "hostfxr.dll | 9.0.0.0 | C:\\FF7\\hostfxr.dll"
            ],
            ManagedAssemblies:
            [
                "AppProxy | 4.5.2.0 | C:\\FF7\\AppProxy.dll",
                "AppWrapper | 4.5.2.0 | C:\\FF7\\AppWrapper.dll",
                "Reloaded.Mod.Interfaces | 2.5.0.0 | C:\\FF7\\Reloaded.Mod.Interfaces.dll"
            ]);

        Equal(
            "stock-7h-ffnx-late-attach",
            LegacyStartupDiagnostics.Classify(snapshot),
            "stock 7th Heaven startup classification");
    }

    private static void ClassifiesDirectReloadedStartup()
    {
        var snapshot = new LegacyStartupSnapshot(
            Is64Bit: false,
            NativeModules:
            [
                "coreclr.dll | 8.0.0.0 | C:\\FF7\\coreclr.dll",
                "hostfxr.dll | 8.0.0.0 | C:\\FF7\\hostfxr.dll"
            ],
            ManagedAssemblies:
            [
                "Reloaded.Mod.Interfaces | 2.5.0.0 | C:\\FF7\\Reloaded.Mod.Interfaces.dll"
            ]);

        Equal(
            "direct-reloaded",
            LegacyStartupDiagnostics.Classify(snapshot),
            "direct Reloaded startup classification");
    }

    private static void ClassifiesIncompleteEvidenceAsUnexpected()
    {
        var snapshot = new LegacyStartupSnapshot(
            Is64Bit: false,
            NativeModules:
            [
                "dinput.dll | 4.5.2.0 | C:\\FF7\\dinput.dll",
                "coreclr.dll | 9.0.0.0 | C:\\FF7\\coreclr.dll",
                "hostfxr.dll | 9.0.0.0 | C:\\FF7\\hostfxr.dll"
            ],
            ManagedAssemblies:
            [
                "AppProxy | 4.5.2.0 | C:\\FF7\\AppProxy.dll",
                "Reloaded.Mod.Interfaces | 2.5.0.0 | C:\\FF7\\Reloaded.Mod.Interfaces.dll"
            ]);

        Equal(
            "partial-unexpected",
            LegacyStartupDiagnostics.Classify(snapshot),
            "incomplete startup evidence classification");
    }

    private static void RejectsUnrecognizedReloadedPrefixes()
    {
        var snapshot = new LegacyStartupSnapshot(
            Is64Bit: false,
            NativeModules:
            [
                "coreclr.dll | 8.0.0.0 | C:\\FF7\\coreclr.dll",
                "hostfxr.dll | 8.0.0.0 | C:\\FF7\\hostfxr.dll"
            ],
            ManagedAssemblies:
            [
                "Reloaded.Unrelated | 1.0.0.0 | C:\\FF7\\Reloaded.Unrelated.dll"
            ]);

        Equal(
            "partial-unexpected",
            LegacyStartupDiagnostics.Classify(snapshot),
            "unrecognized Reloaded managed prefix classification");
        Equal(
            false,
            LegacyStartupDiagnostics.IsRecognizedReloadedNativeModuleName("Reloaded.Unrelated.dll"),
            "unrecognized Reloaded native prefix");
    }

    private static void RecognizesExactReloadedEvidenceForCapture()
    {
        Equal(
            true,
            LegacyStartupDiagnostics.IsRecognizedReloadedManagedAssemblyName("Reloaded.Mod.Loader"),
            "Reloaded mod loader managed evidence");
        Equal(
            true,
            LegacyStartupDiagnostics.IsRecognizedReloadedManagedAssemblyName("Reloaded.Mod.Interfaces"),
            "Reloaded interfaces managed evidence");
        Equal(
            true,
            LegacyStartupDiagnostics.IsRecognizedReloadedNativeModuleName("Reloaded.Mod.Loader.dll"),
            "Reloaded mod loader native evidence");
        Equal(
            false,
            LegacyStartupDiagnostics.IsRecognizedReloadedManagedAssemblyName("Reloaded.Unrelated"),
            "unrecognized Reloaded managed prefix");
    }

    private static void RequiresExecutableDirectoryForAppLoaderEvidence()
    {
        Equal(
            true,
            LegacyStartupDiagnostics.IsRelevantNativeEvidence(
                "dinput.dll",
                "C:\\FF7\\dinput.dll",
                productName: null,
                executableDirectory: "C:\\FF7"),
            "local dinput evidence");
        Equal(
            true,
            LegacyStartupDiagnostics.IsRelevantNativeEvidence(
                "AppLoader.dll",
                "C:\\FF7\\AppLoader.dll",
                productName: null,
                executableDirectory: "C:\\FF7"),
            "local AppLoader evidence");
        Equal(
            false,
            LegacyStartupDiagnostics.IsRelevantNativeEvidence(
                "dinput.dll",
                "C:\\Other\\dinput.dll",
                productName: null,
                executableDirectory: "C:\\FF7"),
            "non-local dinput evidence");
        Equal(
            false,
            LegacyStartupDiagnostics.IsRelevantNativeEvidence(
                "AppLoader.dll",
                "C:\\Other\\AppLoader.dll",
                productName: null,
                executableDirectory: "C:\\FF7"),
            "non-local AppLoader evidence");
    }

    private static void DoesNotRetainTheObsoleteManagedStartupContract()
    {
        AppContext.SetSwitch(BinaryFormatterSwitch, false);
        _ = new Mod();
        Equal(
            false,
            AppContext.TryGetSwitch(BinaryFormatterSwitch, out var enabled) && enabled,
            "constructing the mod does not enable BinaryFormatter");

        var repositoryRoot = FindRepositoryRoot();
        var productionSource = Path.Combine(repositoryRoot, "Ff7.Accessibility.Reloaded");
        var sourceText = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(productionSource, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));
        DoesNotContain(BinaryFormatterSwitch, sourceText, "production source BinaryFormatter switch");
        DoesNotContain("BlindSoldier.ManagedReady", sourceText, "production source managed-ready event");

        var artifact = File.ReadAllBytes(typeof(Mod).Assembly.Location);
        DoesNotContain(
            "BlindSoldier.ManagedReady",
            Encoding.UTF8.GetString(artifact),
            "managed artifact managed-ready event");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Ff7.Accessibility.Reloaded")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the repository root for startup source checks.");
    }

    private static void DoesNotContain(string unexpected, string actual, string label)
    {
        if (actual.Contains(unexpected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{label}: found obsolete '{unexpected}'.");
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
