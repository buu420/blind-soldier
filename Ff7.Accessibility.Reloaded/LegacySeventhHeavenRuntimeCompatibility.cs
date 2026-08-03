namespace Ff7.Accessibility.Reloaded;

internal static class LegacySeventhHeavenRuntimeCompatibility
{
    private const string BinaryFormatterSwitch =
        "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization";

    internal static void Enable() => AppContext.SetSwitch(BinaryFormatterSwitch, true);
}
