namespace Ff7.Accessibility.Core;

public static class AccessibilityConfigMigration
{
    public const int CombinedLadderCueIntervalMs = 700;
    public const int TraversalLadderCueIntervalMs = 1600;
    public const int MountLadderCueIntervalMs = 700;
    public const string CombinedLadderCueSoundPath = @"Assets\navigation\ladder_approach_214.wav";
    public const string TraversalLadderCueSoundPath = @"Assets\navigation\ladder_061.wav";
    public const string MountLadderCueSoundPath = @"Assets\navigation\ladder_approach_214.wav";

    public static bool ApplySeparatedLadderCueDefaults(AccessibilityConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var configuredPath = config.FieldLadderCueSoundPath?.Trim().Replace('/', '\\');
        if (config.FieldLadderCueIntervalMs == CombinedLadderCueIntervalMs &&
            string.Equals(
                configuredPath,
                CombinedLadderCueSoundPath,
                StringComparison.OrdinalIgnoreCase))
        {
            config.FieldLadderCueIntervalMs = TraversalLadderCueIntervalMs;
            config.FieldLadderCueSoundPath = TraversalLadderCueSoundPath;
            config.FieldLadderMountCueIntervalMs = MountLadderCueIntervalMs;
            config.FieldLadderMountCueSoundPath = MountLadderCueSoundPath;
            return true;
        }

        return false;
    }
}
