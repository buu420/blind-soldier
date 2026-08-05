namespace Ff7.Accessibility.Core;

public static class AccessibilityConfigMigration
{
    public const int LegacyLadderCueIntervalMs = 1600;
    public const int CurrentLadderCueIntervalMs = 700;
    public const string LegacyLadderCueSoundPath = @"Assets\navigation\ladder_061.wav";
    public const string CurrentLadderCueSoundPath = @"Assets\navigation\ladder_approach_214.wav";

    public static bool ApplyLegacyLadderCueDefaults(AccessibilityConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var changed = false;
        if (config.FieldLadderCueIntervalMs == LegacyLadderCueIntervalMs)
        {
            config.FieldLadderCueIntervalMs = CurrentLadderCueIntervalMs;
            changed = true;
        }

        var configuredPath = config.FieldLadderCueSoundPath?.Trim().Replace('/', '\\');
        if (string.Equals(
                configuredPath,
                LegacyLadderCueSoundPath,
                StringComparison.OrdinalIgnoreCase))
        {
            config.FieldLadderCueSoundPath = CurrentLadderCueSoundPath;
            changed = true;
        }

        return changed;
    }
}
