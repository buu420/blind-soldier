namespace Ff7.Accessibility.Core;

public interface IAccessibilityOutput
{
    void Speak(string text, bool interrupt);

    void PlayCue(AccessibilityCue cue);

    void StopCue(AccessibilityCueKind kind);
}

public sealed record AccessibilityCue(
    AccessibilityCueKind Kind,
    string AssetPath,
    float Volume,
    float Pan,
    bool Loop);

public enum AccessibilityCueKind
{
    Footstep,
    Navigation,
    Exit,
    Ladder,
    Item,
    Materia,
    Chest,
    SavePoint,
    MovieNarration
}
