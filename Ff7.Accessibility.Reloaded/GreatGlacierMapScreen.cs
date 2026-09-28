namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// hyoumap (field 669), the Glacier Map. [SWITCH] opens it from any glacier screen once the
/// map has been taken, and the wall map in snmin2 opens it too. The field has no MESSAGE,
/// no BGON or BGOFF and turns control off (UC 1); its init loops on IFKEYON 0x02E0 - OK
/// (0x0220), Cancel (0x0040) or Switch (0x0080) - and then MAPJUMPs back to where the party
/// came from. So a sighted player sees one static picture and nothing else happens.
///
/// <para>The description is of the installed picture itself, rendered from hyoumap's
/// background tiles (a single layer of direct-colour tiles) and read directly, not taken
/// from a walkthrough map. Its handwritten labels are decorative script that cannot be read,
/// and there is no marker for the party, so the description names neither places nor a
/// position.</para>
/// </summary>
public static class GreatGlacierMapScreen
{
    public const int FieldId = 669;

    public const string Description =
        "Glacier Map. A hand-drawn map on old paper. Its small labels are in a decorative script " +
        "that cannot be read, and there is no mark showing where you are. Mountains fill the top left, " +
        "the top right and the right side, with snow across the top. A red tick is drawn near the top " +
        "centre. A stream winds down from the top centre to a large outlined area left of centre, with " +
        "a small peaked hut in its middle. Trails drawn as double lines link a small crossing over the " +
        "stream near the top left, a cluster of small marks at the far left, a bare tree on the left, " +
        "a pine on the right, two rocks right of centre, a cave in the lower right mountains and a small " +
        "gate at the bottom centre. Dark forests cover the lower left, the lower middle and an area right " +
        "of centre. Press OK, Cancel or Switch to close the map.";
}

/// <summary>
/// Speaks <see cref="GreatGlacierMapScreen.Description"/> once each time the map screen is
/// entered. The host passes the loaded field id every frame; a repeat key can speak the
/// description again at any time while <see cref="IsShowing"/>.
/// </summary>
public sealed class GreatGlacierMapScreenReadout
{
    public bool IsShowing { get; private set; }

    public string? Observe(int fieldId)
    {
        var showing = fieldId == GreatGlacierMapScreen.FieldId;
        var opened = showing && !IsShowing;
        IsShowing = showing;
        return opened ? GreatGlacierMapScreen.Description : null;
    }

    public void Reset() => IsShowing = false;
}
