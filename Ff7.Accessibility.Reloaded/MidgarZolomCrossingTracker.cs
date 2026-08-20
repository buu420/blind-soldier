namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Announces the visible Zolom crossing window without predicting collision or
/// changing gameplay. The anchors and activation bounds are stock native data.
/// </summary>
public sealed class MidgarZolomCrossingTracker
{
    public const string CueText = "Midgar Zolom is at the far side. Run now.";
    public const int ApproachSideZThreshold = 0x23A98;
    public const int FarmApproachFarX = 0x36008;
    public const int FarmApproachFarZ = 0x26338;
    public const int MineApproachFarX = 0x35D4C;
    public const int MineApproachFarZ = 0x211F8;
    public const int FarSideRadius = 0x1000;
    public const int ActivationMinX = 0x30000;
    public const int ActivationMaxX = 0x3FFFF;
    public const int ActivationMinZ = 0x1C000;
    public const int ActivationMaxZ = 0x2BFFF;

    private bool wasInFarSideWindow;

    public bool Observe(
        WorldMapStateSnapshot player,
        MidgarZolomStateReadResult zolom,
        bool isAtMarshShore)
    {
        if (!zolom.IsUsable ||
            !zolom.State.IsActive ||
            player.CurrentModule != WorldMapStateReader.WorldModule ||
            !player.IsOverworld ||
            player.PlayerModelId is not (0 or 1 or 2) ||
            player.X is < ActivationMinX or > ActivationMaxX ||
            player.Z is < ActivationMinZ or > ActivationMaxZ ||
            !isAtMarshShore)
        {
            Reset();
            return false;
        }

        var farmApproach = player.Z < ApproachSideZThreshold;
        var anchorX = farmApproach ? FarmApproachFarX : MineApproachFarX;
        var anchorZ = farmApproach ? FarmApproachFarZ : MineApproachFarZ;
        var distance =
            Math.Abs(zolom.State.X - (long)anchorX) +
            Math.Abs(zolom.State.Z - (long)anchorZ);
        var inFarSideWindow = distance <= FarSideRadius;
        var shouldAnnounce = inFarSideWindow && !wasInFarSideWindow;
        wasInFarSideWindow = inFarSideWindow;
        return shouldAnnounce;
    }

    public void Reset() => wasInFarSideWindow = false;
}
