namespace Ff7.Accessibility.Reloaded;

/// <summary>Module3 FUN_0074EA48: turn in place, then Confirm for forward thrust.</summary>
internal static class WorldMapSubmarineSteering
{
    internal const int PlayerModelId = 13;
    internal const int TravelDepth = -3000;
    internal const int EmeraldDepth = -4250;
    internal const int EmeraldEncounterDistance = 1215;

    internal static bool TryResolve(WorldMapStateSnapshot state, WorldMapRouteWaypoint aim,
        out FieldNavigationInput direction, out bool thrust, int desiredDepth = TravelDepth, int turnTolerance = 64)
    {
        direction = FieldNavigationInput.None;
        thrust = false;
        if (state.PlayerModelId != PlayerModelId || !state.HasNativeControlMode ||
            state.NativeCameraMode is not (2 or 3) || state.NativeFrameMultiplier is < 1 or > 4)
            return false;
        if (state.WorldMapType == 2)
        {
            if (state.Y < desiredDepth - (desiredDepth < TravelDepth ? 120 : 0))
            { direction = FieldNavigationInput.Down; return true; }
            if (desiredDepth < TravelDepth && state.Y > desiredDepth + 120)
            { direction = FieldNavigationInput.Up; return true; }
        }
        var dx = WorldMapTargetCatalog.WrappedDelta(state.X, aim.X, 0x48000);
        var dz = WorldMapTargetCatalog.WrappedDelta(state.Z, aim.Z, 0x38000);
        if (Math.Abs(dx) + Math.Abs(dz) < 24) return false;
        var heading = Math.Atan2(dx, -dz) * 4096d / (Math.PI * 2d);
        var turn = ((heading - state.CameraFront + 6144d) % 4096d) - 2048d;
        // A native frame turns by8*multiplier underwater, or16*multiplier above.
        // The host observes multiple frames; a small deadband prevents alternating yaw.
        if (Math.Abs(turn) > turnTolerance)
        { direction = turn > 0d ? FieldNavigationInput.Right : FieldNavigationInput.Left; return true; }
        thrust = true;
        return true;
    }
}
