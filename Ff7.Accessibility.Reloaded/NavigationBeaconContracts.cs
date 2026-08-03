namespace Ff7.Accessibility.Reloaded;

public enum NavigationBeaconMovementState
{
    Correcting,
    OnCourse
}

public readonly record struct NavigationBeaconCue(
    string TargetLabel,
    string Direction,
    float StickX,
    float StickY,
    float SteamAudioX,
    float SteamAudioY,
    float SteamAudioZ,
    NavigationBeaconMovementState MovementState,
    int DurationMs,
    double DistanceUnits)
{
    public float Pan => StickX;
}
