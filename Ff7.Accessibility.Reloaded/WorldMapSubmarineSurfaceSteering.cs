namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// The surfaced submarine (model 13, map 0) under an ordinary camera-relative command, as
/// FUN_0074EA48 moves it. Confirm is never part of the command.
/// <list type="bullet">
/// <item>Each update moves it 0x1E times the engine multiplier [0xDFC480], the default
/// speed for every model but 3, 5, 6 and the Chocobos.</item>
/// <item>Left and Right give X = -/+ delta, Up and Down Z = -/+ delta, and a diagonal is
/// (component * 3) >> 2 on both axes.</item>
/// <item>Camera modes 0 and 1 apply that delta in world axes. No camera rotation is
/// applied, whatever [0xDFC484] holds.</item>
/// <item>Camera mode 2 first turns [0xDFC484] by 8 * multiplier * (Up or Down held ? 1 : 2)
/// for each turn key, then rotates the delta by RotMatrixXYZ(0, -front, 0) (006628DE) and
/// RotTrans (00662ECC). Left and Right therefore also move the boat sideways at full speed,
/// and with Down held the keys that turn left and right swap.</item>
/// </list>
/// A command is safe only when every update of the native input lease stays on submarine
/// water. Each step must clear the shared segment test, and the surface FUN_0074CC07 would
/// select there (nearest the boat's height on map 0) must be submarine terrain and not
/// another place's entrance.
/// <para>When the caller passes the destination's arrival triangles, a step that meets the
/// shore after the forecast has reached one of them ends the forecast instead of refusing.
/// Native refusal can deflect the boat along the coast on submarine water. The host stops
/// on observed arrival; a stalled host may slide along the shore. Another place's entrance
/// is still refused before contact. Installed submarine-water faces contain no native
/// field entrances. Without this rule, a coastal destination under 900 units from the
/// shore beyond it could never be approached.</para>
/// </summary>
internal static class WorldMapSubmarineSurfaceSteering
{
    internal const int PlayerModelId = 13;

    /// <summary>The host's key lease: keys stay held this long without a fresh command.</summary>
    internal const int LeaseMilliseconds = 500;

    private const int PadUp = 0x1000;
    private const int PadRight = 0x2000;
    private const int PadDown = 0x4000;
    private const int PadLeft = 0x8000;

    private static readonly IReadOnlySet<int> NoExemptions = new HashSet<int>();

    internal static bool IsCommandSafe(
        WorldMapStateSnapshot state,
        FieldNavigationInput input,
        WorldMapData map,
        WorldMapRoutePlanner planner,
        IReadOnlySet<int> exemptEntrances) =>
        IsCommandSafe(state, input, map, planner, exemptEntrances, out _);

    internal static bool IsCommandSafe(
        WorldMapStateSnapshot state,
        FieldNavigationInput input,
        WorldMapData map,
        WorldMapRoutePlanner planner,
        IReadOnlySet<int> exemptEntrances,
        IReadOnlySet<int>? arrivalTriangles) =>
        IsCommandSafe(state, input, map, planner, exemptEntrances, out _, arrivalTriangles);

    internal static bool IsCommandSafe(
        WorldMapStateSnapshot state,
        FieldNavigationInput input,
        WorldMapData map,
        WorldMapRoutePlanner planner,
        IReadOnlySet<int> exemptEntrances,
        out string diagnostic,
        IReadOnlySet<int>? arrivalTriangles = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(planner);
        if (!IsCoherent(state, map, out diagnostic))
        {
            return false;
        }

        if (!TryGetMask(input, out var mask))
        {
            diagnostic = $"{input} is not a surface submarine command";
            return false;
        }

        if (mask == 0)
        {
            diagnostic = "no movement";
            return true;
        }

        if (!planner.TryResolvePlayerTriangle(state, out var startTriangle))
        {
            diagnostic = $"surface submarine position unresolved: {planner.LastDiagnostic}";
            return false;
        }

        var exempt = ResolveEscapeExemptions(map, planner, startTriangle, exemptEntrances ?? NoExemptions);
        if (!TryFindNativeSurface(map, state.X, state.Z, state.Y, out var startSurface, out var startHeight))
        {
            diagnostic = "no native surface under the submarine";
            return false;
        }

        // FUN_0074CC07 compares the boat's own height with the raw surface height. The
        // height above the selected surface is carried along the forecast unchanged.
        var heightAboveSurface = state.Y - (int)Math.Round(startHeight);
        var current = state with
        {
            TerrainId = startSurface.TerrainId,
            TerrainScriptId = startSurface.TerrainScriptId,
            RegionId = startSurface.RegionId & 0x1F
        };
        var updates = LeaseUpdates(state.NativeFrameMultiplier);
        var arrived = arrivalTriangles?.Contains(startTriangle) == true;
        for (var update = 1; update <= updates; update++)
        {
            var frame = Step(state.NativeCameraMode, state.NativeFrameMultiplier, current.CameraFront, mask);
            current = current with { CameraFront = frame.CameraFront };
            if (frame.DeltaX == 0 && frame.DeltaZ == 0)
            {
                continue;
            }

            var nextX = Wrap(current.X + frame.DeltaX, map.WrapWidth);
            var nextZ = Wrap(current.Z + frame.DeltaZ, map.WrapHeight);
            var hasSurface = TryFindNativeSurface(map, nextX, nextZ, current.Y, out var surface, out var height);
            if (hasSurface && planner.IsUnwantedEntrance(surface.Id, exempt, startTriangle))
            {
                diagnostic = $"update {update} of {input} reaches another place's entrance (triangle {surface.Id})";
                return false;
            }

            var leavesWater = !planner.CanTraverseSegment(current, new WorldMapRouteWaypoint(nextX, current.Y, nextZ), null, exempt);
            var landsOffWater = !hasSurface || !WorldMapTerrainPassability.CanTraverse(PlayerModelId, 0, surface.TerrainId);
            if (leavesWater || landsOffWater)
            {
                if (arrived)
                {
                    diagnostic = $"{input} reaches the destination, then native movement may deflect along the shore at update {update}";
                    return true;
                }

                diagnostic = leavesWater
                    ? $"update {update} of {input} leaves submarine water at {nextX},{nextZ}"
                    : $"update {update} of {input} selects non-submarine terrain at {nextX},{nextZ}";
                return false;
            }

            current = current with
            {
                X = nextX,
                Y = (int)Math.Round(height) + heightAboveSurface,
                Z = nextZ,
                TerrainId = surface.TerrainId,
                TerrainScriptId = surface.TerrainScriptId,
                RegionId = surface.RegionId & 0x1F
            };
            arrived |= arrivalTriangles?.Contains(surface.Id) == true;
        }

        diagnostic = $"{input} clear for {updates} native updates";
        return true;
    }

    /// <summary>
    /// Native updates in the lease. The multiplier is the number of 60 Hz ticks one update
    /// stands for (2 at the PC's 30 updates a second, so 15 updates). The forecast distance
    /// is therefore the same at any multiplier.
    /// </summary>
    internal static int LeaseUpdates(int multiplier) =>
        (LeaseMilliseconds * 60 / 1000 + multiplier - 1) / multiplier;

    /// <summary>One FUN_0074EA48 update of the surfaced submarine, without Confirm.</summary>
    internal static WorldMapSubmarineSurfaceFrame Step(int cameraMode, int multiplier, int cameraFront, int mask)
    {
        var delta = multiplier * 0x1E;
        var deltaX = 0;
        var deltaZ = 0;
        var turned = false;
        if ((mask & PadLeft) != 0)
        {
            deltaX = -delta;
            turned = true;
        }

        if ((mask & PadRight) != 0)
        {
            deltaX = delta;
            turned = true;
        }

        if ((mask & PadUp) != 0)
        {
            if (!turned)
            {
                deltaZ = -delta;
            }
            else
            {
                deltaX = (deltaX * 3) >> 2;
                deltaZ = (delta * -3) >> 2;
            }
        }

        if ((mask & PadDown) != 0)
        {
            if (!turned && (mask & PadUp) == 0)
            {
                deltaZ = delta;
            }
            else
            {
                deltaX = (deltaX * 3) >> 2;
                deltaZ = (delta * 3) >> 2;
            }
        }

        if (cameraMode != 2)
        {
            return new(deltaX, deltaZ, cameraFront);
        }

        // Map 0, camera mode 2: with Down held the left-turn count reads the Right key and
        // the right-turn count the Left key. There is no underwater halving here.
        var down = (mask & PadDown) != 0;
        var rate = (multiplier << 3) * ((mask & (PadUp | PadDown)) != 0 ? 1 : 2);
        if ((mask & (down ? PadRight : PadLeft)) != 0)
        {
            cameraFront -= rate;
        }

        if ((mask & (down ? PadLeft : PadRight)) != 0)
        {
            cameraFront += rate;
        }

        if (cameraFront < 0)
        {
            cameraFront += 0x1000;
        }
        else if (cameraFront >= 0x1000)
        {
            cameraFront -= 0x1000;
        }

        var angle = cameraFront * Math.PI * 2d / 4096d;
        var cos = (int)Math.Round(Math.Cos(angle) * 4096d);
        var sin = (int)Math.Round(Math.Sin(angle) * 4096d);
        var x = (short)deltaX;
        var z = (short)deltaZ;
        return new((cos * x - sin * z) >> 12, (sin * x + cos * z) >> 12, cameraFront);
    }

    internal static bool TryGetMask(FieldNavigationInput input, out int mask)
    {
        mask = input switch
        {
            FieldNavigationInput.None => 0,
            FieldNavigationInput.Up => PadUp,
            FieldNavigationInput.UpRight => PadUp | PadRight,
            FieldNavigationInput.Right => PadRight,
            FieldNavigationInput.DownRight => PadDown | PadRight,
            FieldNavigationInput.Down => PadDown,
            FieldNavigationInput.DownLeft => PadDown | PadLeft,
            FieldNavigationInput.Left => PadLeft,
            FieldNavigationInput.UpLeft => PadUp | PadLeft,
            _ => -1
        };
        return mask >= 0;
    }

    private static bool IsCoherent(WorldMapStateSnapshot state, WorldMapData map, out string diagnostic)
    {
        diagnostic = string.Empty;
        if (state.CurrentModule != WorldMapStateReader.WorldModule ||
            state.WorldMapType != 0 || map.WorldMapType != 0)
        {
            diagnostic = $"module/map {state.CurrentModule}/{state.WorldMapType} is not the surface world map";
        }
        else if (state.PlayerModelId != PlayerModelId)
        {
            diagnostic = $"player model {state.PlayerModelId} is not the submarine";
        }
        else if (!state.HasNativeControlMode || state.NativeCameraMode is not (0 or 1 or 2))
        {
            diagnostic = state.HasNativeControlMode
                ? $"camera mode {state.NativeCameraMode} has no checked surface submarine controls"
                : "native camera mode unreadable";
        }
        else if (state.NativeFrameMultiplier is < 1 or > 4)
        {
            diagnostic = $"native frame multiplier {state.NativeFrameMultiplier} is outside 1-4";
        }
        else if (state.CameraFront is < 0 or >= 0x1000)
        {
            diagnostic = $"camera front {state.CameraFront} is outside 0-4095";
        }

        return diagnostic.Length == 0;
    }

    /// <summary>The planner's own escape rule: a trigger already stood in can be left through.</summary>
    private static IReadOnlySet<int> ResolveEscapeExemptions(
        WorldMapData map,
        WorldMapRoutePlanner planner,
        int startTriangle,
        IReadOnlySet<int> exempt)
    {
        if (!planner.EntranceTriangleIds.Contains(startTriangle))
        {
            return exempt;
        }

        var escape = new HashSet<int>(exempt) { startTriangle };
        var pending = new Queue<int>();
        pending.Enqueue(startTriangle);
        while (pending.TryDequeue(out var current))
        {
            foreach (var neighbor in map.Triangles[current].Neighbors)
            {
                if (planner.EntranceTriangleIds.Contains(neighbor) && escape.Add(neighbor))
                {
                    pending.Enqueue(neighbor);
                }
            }
        }

        return escape;
    }

    private static bool TryFindNativeSurface(
        WorldMapData map,
        int x,
        int z,
        int referenceHeight,
        out WorldMapTriangle surface,
        out double height)
    {
        height = 0d;
        if (!WorldMapBroncoLanding.TryFindSurfaceNear(map, x, z, referenceHeight, out surface))
        {
            return false;
        }

        height = HeightAt(surface, Wrap(x, map.WrapWidth), Wrap(z, map.WrapHeight));
        return true;
    }

    private static double HeightAt(WorldMapTriangle triangle, int x, int z)
    {
        var a = triangle.Vertex0;
        var b = triangle.Vertex1;
        var c = triangle.Vertex2;
        var denominator = (b.Z - c.Z) * (double)(a.X - c.X) + (c.X - b.X) * (double)(a.Z - c.Z);
        if (denominator == 0d)
        {
            return triangle.Centroid.Y;
        }

        var weightA = ((b.Z - c.Z) * (double)(x - c.X) + (c.X - b.X) * (double)(z - c.Z)) / denominator;
        var weightB = ((c.Z - a.Z) * (double)(x - c.X) + (a.X - c.X) * (double)(z - c.Z)) / denominator;
        return weightA * a.Y + weightB * b.Y + (1d - weightA - weightB) * c.Y;
    }

    private static int Wrap(int value, int extent)
    {
        var wrapped = value % extent;
        return wrapped < 0 ? wrapped + extent : wrapped;
    }
}

/// <summary>One native update: the world delta and the camera front it leaves.</summary>
internal readonly record struct WorldMapSubmarineSurfaceFrame(int DeltaX, int DeltaZ, int CameraFront);
