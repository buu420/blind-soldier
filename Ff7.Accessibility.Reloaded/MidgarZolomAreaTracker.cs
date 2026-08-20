using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Announces entering and leaving the Midgar Zolom's marsh.
/// </summary>
/// <remarks>
/// On the world map the marsh between Kalm and the Mythril Mine is drawn as its
/// own terrain, and the Zolom itself is a serpent visibly moving across it. A
/// sighted player therefore knows two things at a glance: that they have walked
/// onto the marsh, and whether the Zolom is still on it. Without that, the first
/// notice a player gets is the battle transition.
///
/// This is deliberately separate from <see cref="MidgarZolomCrossingTracker"/>,
/// which is a timing cue for the dash across - it fires at the shore when the
/// Zolom is at the far anchor. This one is an area cue and says nothing about
/// when to move.
/// </remarks>
public sealed class MidgarZolomAreaTracker
{
    /// <summary>Native ground type 7 is the Zolom swamp.</summary>
    public const int MarshTerrainId = 7;

    public const string EnteredWithZolomText = "Midgar Zolom marsh. The Zolom is here.";
    public const string EnteredText = "Midgar Zolom marsh.";
    public const string LeftText = "Clear of the Zolom marsh.";

    private bool wasOnMarsh;

    /// <param name="isOnMarshTerrain">
    /// Whether the player's own world triangle is the marsh, not merely beside it.
    /// </param>
    /// <param name="crossingCueSpoken">
    /// True when <see cref="MidgarZolomCrossingTracker"/> has already spoken this
    /// tick. Both cues trigger within a step or two of each other at the shore,
    /// and the crossing cue is the more urgent of the two, so the area cue stands
    /// down and simply records that the player is now on the marsh.
    /// </param>
    public string? Observe(
        WorldMapStateSnapshot player,
        MidgarZolomStateReadResult zolom,
        bool isOnMarshTerrain,
        bool crossingCueSpoken)
    {
        var onMarsh =
            isOnMarshTerrain &&
            player.CurrentModule == WorldMapStateReader.WorldModule &&
            player.IsOverworld;
        if (!onMarsh)
        {
            var wasThere = wasOnMarsh;
            wasOnMarsh = false;
            return wasThere ? LeftText : null;
        }

        var entered = !wasOnMarsh;
        wasOnMarsh = true;
        if (!entered || crossingCueSpoken)
        {
            return null;
        }

        // The Zolom only threatens a party on foot; model ids 0, 1 and 2 are the
        // three characters who can lead the world map, and anything else is a
        // vehicle whose rider can see perfectly well that they are mounted.
        var onFoot = player.PlayerModelId is 0 or 1 or 2;
        var zolomPresent = zolom.IsUsable && zolom.State.IsActive;
        return onFoot && zolomPresent ? EnteredWithZolomText : EnteredText;
    }

    public void Reset() => wasOnMarsh = false;
}
