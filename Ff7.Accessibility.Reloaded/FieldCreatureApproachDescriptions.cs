namespace Ff7.Accessibility.Reloaded;

/// <summary>Only the owned native draw observation may offer these OFSTW cues.</summary>
public static class FieldCreatureApproachDescriptions
{
    public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateAll() =>
    [
        new(767,12,3,12,"The huge creature glides closer.",0xC4),
        new(767,12,4,22,"The creature closes in.",0xC4)
    ];
}
