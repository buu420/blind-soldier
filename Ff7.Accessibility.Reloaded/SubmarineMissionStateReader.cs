using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>What the mission's own result word says has happened.</summary>
public enum SubmarineMissionOutcome
{
    Active = 0,
    Success = 1,
    Destroyed = 2,
    TimedOut = 3,
    Quit = 5,
    Unknown = -1
}

/// <summary>
/// Which hull and colour 79FB20 draws for an enemy, from the model pointer 78E882 stores
/// at record +0x54 (<c>0x989CF8 + model * 16</c>). The colour words are read as
/// <c>0xFF:b0:b1:b2</c> ARGB, so model 0 is orange-yellow, 3 dark red on the same hull,
/// and 2 dark red on a hull of its own: the leader. Model 1 is the player's blue hull and
/// no enemy table uses it.
/// </summary>
public enum SubmarineTargetModel
{
    Unknown = 0,
    Yellow,
    Red,
    RedLeader
}

/// <summary>
/// One enemy the game is drawing, placed where it actually is on screen: a marked record
/// the marker square is drawn for, or a sinking hull 78E3FB still draws. A record that
/// will not project in front of the current camera, or that lands outside the current
/// viewport rectangle, is not one of these: the game is not drawing it and a player
/// cannot see it.
///
/// <para>The position is where this sighting was, in map units, for steering only. It is
/// never spoken: the game prints no coordinates or distances.</para>
/// </summary>
public readonly record struct SubmarineVisibleTarget(
    int Slot,
    bool IsLocked,
    int ScreenX,
    int ScreenY)
{
    public SubmarineTargetModel Model { get; init; }

    public int X { get; init; }

    /// <summary>Depth, growing downward, like the player's.</summary>
    public int Y { get; init; }

    public int Z { get; init; }

    /// <summary>Hit and going down: 78E3FB draws the hull, 78E9CE no longer draws a marker.</summary>
    public bool IsSinking { get; init; }
}

public readonly record struct SubmarineMissionSnapshot(
    bool IsActive,
    bool IsArcade,
    bool IsPaused,
    bool HasResult,
    SubmarineMissionOutcome Outcome,
    bool IsQuitPromptOpen,
    bool QuitPromptYesSelected,
    int RemainingSeconds,
    int HealthPercent,
    int Depth,
    int Speed,
    int Pitch,
    int Yaw,
    int ReadyTorpedoes,
    int ReloadingTorpedoes,
    int Warnings,
    bool HasLockedTarget,
    bool CanPlaceTargets,
    IReadOnlyList<SubmarineVisibleTarget> VisibleTargets,
    int ViewportOriginX,
    int ViewportOriginY,
    int ViewportWidth,
    int ViewportHeight)
{
    public static SubmarineMissionSnapshot Inactive { get; } = new(
        false, false, false, false, SubmarineMissionOutcome.Active, false, false,
        0, 0, 0, 0, 0, 0, 0, 0, 0, false, false, Array.Empty<SubmarineVisibleTarget>(),
        0, 0, 0, 0);

    /// <summary>
    /// The player's own position in map units (Y is depth, growing downward), from the same
    /// coherent view sample as the targets. Only meaningful when <see cref="CanPlaceTargets"/>.
    /// Steering internals: never spoken.
    /// </summary>
    public int PlayerX { get; init; }

    public int PlayerY { get; init; }

    public int PlayerZ { get; init; }

    /// <summary>
    /// 9873D8, the throttle step 798580 moves: 9 is stopped, 17 full ahead, 18 the boost
    /// that lasts only while Menu is held at 17, below 9 astern. Only meaningful when
    /// <see cref="CanPlaceTargets"/>.
    /// </summary>
    public int SpeedIndex { get; init; }

    /// <summary>9873A8: non-zero while PageDown's overview camera (view 4) is up.</summary>
    public int ViewMode { get; init; }

    /// <summary>
    /// The overview's sonar reaches 4096 but sweeps too slowly for 78D092 to raise any
    /// marker to 0x400, so 78E2F8 can never lock from it.
    /// </summary>
    public bool IsOverview => ViewMode != 0;

    // 7929B1 draws only these. Nothing else in the word is on screen.
    public bool TerrainClose => (Warnings & 0x1) != 0;
    public bool HullContact => (Warnings & 0x2) != 0;
    public bool MineNearby => (Warnings & 0x4) != 0;
    public bool EnemyDetected => (Warnings & 0x400) != 0;
    public bool TorpedoInTheWater => (Warnings & 0x800) != 0;
}

/// <summary>
/// The submarine mission's own instruments, exactly the ones it draws.
///
/// <para>The mission is its own module - 63C17F's MINIGAME type 4 selects module 10,
/// whose 77D030 init, 77DF72 frame and 77DAED teardown own the screen - so nothing here
/// is a field or world reading. Everything reported is something the game puts on the
/// screen: the clock, the damage bar, the depth and speed numbers, the compass and pitch
/// gauges, the four torpedo indicators, the warning lamps, the coloured squares it draws
/// over enemies it has detected, and the hulls of enemies going down.</para>
///
/// <para>The squares are the delicate part. An enemy record carrying the sonar-range bit
/// is not an enemy on screen: 78D092 sets that bit from the travelling sonar pulse, and
/// the game still only draws a square where 78E9CE can project the record into the
/// current camera with a positive W. This reader runs that same transform and then clips
/// against the viewport rectangle the renderer context carries. A record that fails
/// either test is not reported at all, and nothing about it - position, hull or lock -
/// leaves the reader. Hidden health, AI headings, the exact distance and the reload
/// countdown are never read.</para>
///
/// <para>The whole view - camera, renderer context, viewport, projection, the player's
/// position, throttle and view mode, and every enemy record - is sampled twice and
/// compared, because a stable module number is not evidence that those bytes came from
/// one frame. Identity is sampled before and after everything else. E73F18 is part of it
/// on purpose: 77DF72 clears it as each frame's update starts and 78C9E1 sets it when the
/// frame is done, so a change in it means a frame boundary fell inside the capture. A
/// torn capture is not used at all; the whole capture is taken again, a bounded number of
/// times, and only one whose own samples agree is reported. A view still torn after that
/// leaves the instruments intact and marks the view unavailable - an unknown view is not
/// an empty sea - and the diagnostic names what would not hold still.</para>
/// </summary>
public sealed class SubmarineMissionStateReader
{
    public const byte MinigameModule = 10;

    public const uint AddressCurrentModule = 0x00CBF9DC;
    public const uint AddressActiveRun = 0x00980DAC;
    public const uint AddressInnerCompletion = 0x00E73F18;
    public const uint AddressArcadeFlag = 0x00E74760;
    public const uint AddressResult = 0x00E74768;
    public const uint AddressSessionFlags = 0x00E7476C;
    public const uint AddressRemainingFrames = 0x00980DCC;
    public const uint AddressHealth = 0x00987348;
    public const uint AddressDepth = 0x0098733C;
    public const uint AddressSpeedNumerator = 0x009873DC;
    public const uint AddressSpeedDenominator = 0x00988540;
    public const uint AddressPitch = 0x0098734C;
    public const uint AddressYaw = 0x0098734E;
    public const uint AddressTorpedoSlots = 0x009873E4;
    public const uint AddressWarnings = 0x009873D4;
    public const uint AddressEnemyRecords = 0x0098A1A8;
    public const uint AddressCamera = 0x00E996F8;
    public const uint AddressProjectionContextPointer = 0x00DB2BB8;
    public const uint AddressPlayerX = 0x00987338;
    public const uint AddressPlayerY = AddressDepth;
    public const uint AddressPlayerZ = 0x00987340;
    public const uint AddressSpeedIndex = 0x009873D8;
    public const uint AddressViewMode = 0x009873A8;

    /// <summary>79FB20's model table; an enemy's +0x54 is <c>this + model * 16</c>.</summary>
    public const uint AddressModelTable = 0x00989CF8;
    public const int ModelTableStride = 16;

    public const int EnemyRecordCount = 12;
    public const int EnemyRecordStride = 0x70;
    public const int TorpedoSlotCount = 4;

    /// <summary>
    /// Whole identity, instrument and view captures tried per read. Each one keeps both
    /// double samples; only a capture whose own samples agree is used.
    /// </summary>
    public const int MaximumCaptureAttempts = 4;

    public const int ProjectionMatrixOffset = 0x8D0;
    public const int ViewportOriginXOffset = 0x848;
    public const int ViewportOriginYOffset = 0x84C;
    public const int ViewportWidthOffset = 0x850;
    public const int ViewportHeightOffset = 0x854;

    // 7B7A50 and 7B7A54: the fixed-point divisor 6617E9 applies to the camera's nine
    // rotation shorts, and the 1.0 it writes into the matrix's last element.
    private const float CameraRotationScale = 4096f;

    // The damage bar is 127 pixels wide and normalised by this.
    private const int HealthFullScale = 16384;

    // 9873DC * 200 over 988540 * 2 is the number the speed readout prints.
    private const int SpeedNumeratorScale = 200;

    private const int SessionPausedBit = 0x1;
    private const int SessionResultBit = 0x2;
    private const int SessionQuitPromptBit = 0x4;
    private const int SessionQuitYesBit = 0x8;

    private const int EnemyActiveMask = 0x3;
    private const int EnemySinkingBit = 0x20;
    private const int EnemyMarkerLocked = 0x800;
    private const int EnemyMarkerMask = 0xF00;

    private const int EnemyPositionXOffset = 0x00;
    private const int EnemyPositionYOffset = 0x04;
    private const int EnemyPositionZOffset = 0x08;
    private const int EnemyFlagsOffset = 0x34;
    private const int EnemyMarkerOffset = 0x38;
    private const int EnemyModelOffset = 0x54;

    private const int TorpedoReadyValue = 0x20000;
    private const int TorpedoReloadingHighWord = 0x10000;

    private const int MaximumViewportExtent = 8192;
    private const int MaximumDepth = 1024;

    // The renderer context is followed for a fixed span of offsets; anything that could
    // not carry those offsets without wrapping is not a context this reader will use.
    private const uint MaximumContextAddress = 0xFFFF0000;

    // Position X, Y and Z, flags, marker and hull pointer.
    private const int EnemyFieldsPerRecord = 6;

    // A tear naming every enemy field would be a paragraph; the first few say enough.
    private const int MaximumNamedTears = 3;

    private readonly ILegacyAddressSpace memory;

    public SubmarineMissionStateReader(ILegacyAddressSpace memory)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    public string LastDiagnostic { get; private set; } = "not read";

    /// <summary>Captures abandoned because identity changed between its two samples.</summary>
    public int TornIdentityCaptures { get; private set; }

    /// <summary>Captures abandoned because the view's two samples disagreed.</summary>
    public int TornViewCaptures { get; private set; }

    /// <summary>What tore most recently, even when a later capture recovered.</summary>
    public string? LastTear { get; private set; }

    /// <summary>
    /// Reads the mission if it owns the screen. Returns false when the reading cannot be
    /// trusted - a failed read, an identity that would not hold still for any of the
    /// bounded captures, or an instrument the game could not have drawn - so a caller never
    /// mistakes a failure for a mission that has ended.
    /// </summary>
    public bool TryRead(out SubmarineMissionSnapshot snapshot)
    {
        snapshot = SubmarineMissionSnapshot.Inactive;
        SubmarineMissionSnapshot? coherentInstruments = null;
        string? identityTear = null;
        string? viewTear = null;

        for (var attempt = 1; attempt <= MaximumCaptureAttempts; attempt++)
        {
            // The frame deliberately clears its render-availability flag after drawing and
            // sets it again, so requiring it at an arbitrary polling moment would drop every
            // other reading. Identity is sampled again after everything that depends on it.
            if (!TryReadIdentity(out var before))
            {
                LastDiagnostic = "identity unreadable";
                return false;
            }

            if (before.Module != MinigameModule || before.ActiveRun == 0)
            {
                LastDiagnostic = $"not in the mission: module={before.Module}, run={before.ActiveRun}";
                snapshot = SubmarineMissionSnapshot.Inactive;
                return true;
            }

            if (!TryReadInstruments(before, out var reading, out var tornView, out var instrumentDiagnostic))
            {
                LastDiagnostic = instrumentDiagnostic;
                return false;
            }

            if (!TryReadIdentity(out var after))
            {
                LastDiagnostic = "identity unreadable";
                return false;
            }

            if (before != after)
            {
                identityTear = DescribeIdentityTear(before, after);
                TornIdentityCaptures++;
                LastTear = $"identity: {identityTear}";
                continue;
            }

            if (tornView is not null)
            {
                // The instruments of this capture are coherent and kept in case no capture
                // gets a view to agree with itself; the view is tried again.
                viewTear = tornView;
                TornViewCaptures++;
                LastTear = $"view: {tornView}";
                coherentInstruments = reading;
                continue;
            }

            snapshot = reading;
            LastDiagnostic = DescribeReading(reading);
            return true;
        }

        if (coherentInstruments is { } instruments)
        {
            snapshot = instruments;
            LastDiagnostic =
                $"{DescribeReading(instruments)}, torn view after {MaximumCaptureAttempts} attempts: {viewTear}";
            return true;
        }

        LastDiagnostic = $"torn identity after {MaximumCaptureAttempts} attempts: {identityTear}";
        return false;
    }

    /// <summary>
    /// The model pointer's hull. Only a pointer into the table at one of the three enemy
    /// entries names anything; model 1 is the player's own hull.
    /// </summary>
    internal static SubmarineTargetModel DecodeModel(uint pointer) => pointer switch
    {
        YellowHull => SubmarineTargetModel.Yellow,
        RedLeaderHull => SubmarineTargetModel.RedLeader,
        RedHull => SubmarineTargetModel.Red,
        _ => SubmarineTargetModel.Unknown
    };

    // Models 0, 2 and 3 of the 16-byte table.
    private const uint YellowHull = AddressModelTable;
    private const uint RedLeaderHull = AddressModelTable + 0x20;
    private const uint RedHull = AddressModelTable + 0x30;

    private static string DescribeReading(SubmarineMissionSnapshot reading) =>
        $"mission active: outcome={reading.Outcome}, paused={reading.IsPaused}, " +
        $"targets={reading.VisibleTargets.Count}, placeable={reading.CanPlaceTargets}";

    private readonly record struct Identity(
        byte Module,
        int ActiveRun,
        int InnerCompletion,
        int SessionFlags,
        int Result,
        byte Arcade);

    private bool TryReadIdentity(out Identity identity)
    {
        identity = default;
        if (!memory.TryReadByte(AddressCurrentModule, out var module) ||
            !memory.TryReadInt32(AddressActiveRun, out var activeRun) ||
            !memory.TryReadInt32(AddressInnerCompletion, out var completion) ||
            !memory.TryReadInt32(AddressSessionFlags, out var flags) ||
            !memory.TryReadInt32(AddressResult, out var result) ||
            !memory.TryReadByte(AddressArcadeFlag, out var arcade))
        {
            return false;
        }

        identity = new Identity(module, activeRun, completion, flags, result, arcade);
        return true;
    }

    private static string DescribeIdentityTear(Identity before, Identity after)
    {
        var torn = new List<string>(6);
        if (before.Module != after.Module)
        {
            torn.Add($"module {before.Module}->{after.Module}");
        }

        if (before.ActiveRun != after.ActiveRun)
        {
            torn.Add($"run flag {before.ActiveRun}->{after.ActiveRun}");
        }

        if (before.InnerCompletion != after.InnerCompletion)
        {
            torn.Add($"frame flag {before.InnerCompletion}->{after.InnerCompletion}");
        }

        if (before.SessionFlags != after.SessionFlags)
        {
            torn.Add($"session flags 0x{before.SessionFlags:X}->0x{after.SessionFlags:X}");
        }

        if (before.Result != after.Result)
        {
            torn.Add($"result {before.Result}->{after.Result}");
        }

        if (before.Arcade != after.Arcade)
        {
            torn.Add($"arcade flag {before.Arcade}->{after.Arcade}");
        }

        return string.Join(", ", torn);
    }

    private bool TryReadInstruments(
        Identity identity,
        out SubmarineMissionSnapshot snapshot,
        out string? tornView,
        out string diagnostic)
    {
        snapshot = SubmarineMissionSnapshot.Inactive;
        tornView = null;
        diagnostic = "instruments unreadable";
        if (!memory.TryReadInt32(AddressRemainingFrames, out var remainingFrames) ||
            !memory.TryReadInt32(AddressHealth, out var health) ||
            !memory.TryReadInt32(AddressDepth, out var depth) ||
            !memory.TryReadInt32(AddressSpeedNumerator, out var speedNumerator) ||
            !memory.TryReadInt32(AddressSpeedDenominator, out var speedDenominator) ||
            !memory.TryReadInt16(AddressPitch, out var pitch) ||
            !memory.TryReadInt16(AddressYaw, out var yaw) ||
            !memory.TryReadInt32(AddressWarnings, out var warnings))
        {
            return false;
        }

        // The native readout divides by this, so a mission that has it at zero has not
        // finished initialising. Reporting "stopped" from it would invent a reading the
        // game itself could not have drawn, which is worse than saying nothing yet.
        if (speedDenominator <= 0)
        {
            diagnostic = $"speed denominator not initialised: {speedDenominator}";
            return false;
        }

        var ready = 0;
        var reloading = 0;
        for (var slot = 0; slot < TorpedoSlotCount; slot++)
        {
            if (!memory.TryReadInt32(
                    AddressTorpedoSlots + (uint)(slot * sizeof(int)),
                    out var indicator))
            {
                return false;
            }

            // 79F114 will only fire a slot holding exactly this, so it is the one the
            // indicator shows as loaded. The low word of a reloading slot is a countdown
            // the game does not print, and is deliberately not read out.
            if (indicator == TorpedoReadyValue)
            {
                ready++;
            }
            else if ((indicator & unchecked((int)0xFFFF0000)) == TorpedoReloadingHighWord)
            {
                reloading++;
            }
        }

        var paused = (identity.SessionFlags & SessionPausedBit) != 0;
        var hasResult = (identity.SessionFlags & SessionResultBit) != 0 || identity.Result != 0;
        var outcome = identity.Result switch
        {
            0 => SubmarineMissionOutcome.Active,
            1 => SubmarineMissionOutcome.Success,
            2 => SubmarineMissionOutcome.Destroyed,
            3 => SubmarineMissionOutcome.TimedOut,
            5 => SubmarineMissionOutcome.Quit,
            _ => SubmarineMissionOutcome.Unknown
        };

        // A stopped mission is not steering anything, and the squares it was drawing are
        // gone with the frame that declared the result. Reusing them would put enemies on
        // a screen that is showing a banner.
        var placeable = false;
        IReadOnlyList<SubmarineVisibleTarget> targets = Array.Empty<SubmarineVisibleTarget>();
        var viewport = default(Viewport);
        var pose = default(ViewPose);
        if (!paused && !hasResult)
        {
            placeable = TryReadVisibleTargets(out targets, out viewport, out pose, out tornView);
        }

        snapshot = new SubmarineMissionSnapshot(
            IsActive: true,
            IsArcade: identity.Arcade != 0,
            IsPaused: paused,
            HasResult: hasResult,
            Outcome: outcome,
            IsQuitPromptOpen: (identity.SessionFlags & SessionQuitPromptBit) != 0,
            QuitPromptYesSelected: (identity.SessionFlags & SessionQuitYesBit) != 0,
            // 792216 clamps a negative frame count to zero before drawing it, so nothing
            // left is what the screen says and what is said here.
            RemainingSeconds: Math.Max(0, remainingFrames) / 60,
            HealthPercent: ToHealthPercent(health),
            Depth: Math.Clamp(depth >> 12, 0, MaximumDepth),
            Speed: ToSpeed(speedNumerator, speedDenominator),
            Pitch: pitch,
            Yaw: yaw,
            ReadyTorpedoes: ready,
            ReloadingTorpedoes: reloading,
            Warnings: warnings,
            HasLockedTarget: targets.Any(target => target.IsLocked),
            CanPlaceTargets: placeable,
            VisibleTargets: targets,
            ViewportOriginX: viewport.OriginX,
            ViewportOriginY: viewport.OriginY,
            ViewportWidth: viewport.Width,
            ViewportHeight: viewport.Height)
        {
            PlayerX = pose.X,
            PlayerY = pose.Y,
            PlayerZ = pose.Z,
            SpeedIndex = pose.SpeedIndex,
            ViewMode = pose.ViewMode
        };
        diagnostic = "instruments read";
        return true;
    }

    /// <summary>792216 clamps a negative health word to zero before drawing the bar.</summary>
    private static int ToHealthPercent(int health) =>
        Math.Clamp((int)(health * 100L / HealthFullScale), 0, 100);

    private static int ToSpeed(int numerator, int denominator) =>
        (int)(numerator * (long)SpeedNumeratorScale / (denominator * 2L));

    private readonly record struct Viewport(int OriginX, int OriginY, int Width, int Height)
    {
        public bool Contains(float x, float y) =>
            x >= OriginX && x < OriginX + Width &&
            y >= OriginY && y < OriginY + Height;
    }

    /// <summary>The player's side of a coherent view: map position, throttle and view mode.</summary>
    private readonly record struct ViewPose(int X, int Y, int Z, int SpeedIndex, int ViewMode);

    /// <summary>
    /// Everything the marker squares and the steering depend on, in one sample: the camera,
    /// the renderer context it is projected through, that context's viewport and
    /// projection, the player's position, throttle and view mode, and every enemy record.
    /// Two of these are compared before anything is placed, because a marker drawn from
    /// one frame's camera and the next frame's position is in the wrong place and nothing
    /// about the module number would say so.
    /// </summary>
    private sealed class ViewCapture
    {
        internal short[] Rotation { get; } = new short[9];
        internal int[] Translation { get; } = new int[3];
        internal uint Context { get; set; }
        internal int[] ViewportBounds { get; } = new int[4];
        internal float[] Projection { get; } = new float[16];
        internal int[] Player { get; } = new int[3];
        internal int SpeedIndex { get; set; }
        internal int ViewMode { get; set; }
        internal int[] Records { get; } = new int[EnemyRecordCount * EnemyFieldsPerRecord];

        internal List<string> Differences(ViewCapture other)
        {
            var differences = new List<string>();
            if (!Rotation.AsSpan().SequenceEqual(other.Rotation))
            {
                differences.Add("camera rotation");
            }

            if (!Translation.AsSpan().SequenceEqual(other.Translation))
            {
                differences.Add("camera translation");
            }

            if (Context != other.Context)
            {
                differences.Add("renderer context");
            }

            if (!ViewportBounds.AsSpan().SequenceEqual(other.ViewportBounds))
            {
                differences.Add("viewport");
            }

            if (!Projection.AsSpan().SequenceEqual(other.Projection))
            {
                differences.Add("projection");
            }

            if (!Player.AsSpan().SequenceEqual(other.Player))
            {
                differences.Add("player position");
            }

            if (SpeedIndex != other.SpeedIndex)
            {
                differences.Add("throttle");
            }

            if (ViewMode != other.ViewMode)
            {
                differences.Add("view mode");
            }

            for (var slot = 0; slot < EnemyRecordCount; slot++)
            {
                var index = slot * EnemyFieldsPerRecord;
                if (!Records.AsSpan(index, 3).SequenceEqual(other.Records.AsSpan(index, 3)))
                {
                    differences.Add($"enemy {slot} position");
                }

                if (Records[index + 3] != other.Records[index + 3])
                {
                    differences.Add($"enemy {slot} flags");
                }

                if (Records[index + 4] != other.Records[index + 4])
                {
                    differences.Add($"enemy {slot} marker");
                }

                if (Records[index + 5] != other.Records[index + 5])
                {
                    differences.Add($"enemy {slot} hull");
                }
            }

            return differences;
        }
    }

    private bool TryReadVisibleTargets(
        out IReadOnlyList<SubmarineVisibleTarget> targets,
        out Viewport viewport,
        out ViewPose pose,
        out string? tear)
    {
        targets = Array.Empty<SubmarineVisibleTarget>();
        viewport = default;
        pose = default;
        tear = null;

        if (!TryCaptureView(out var first) || !TryCaptureView(out var second))
        {
            return false;
        }

        var differences = first.Differences(second);
        if (differences.Count != 0)
        {
            tear = differences.Count <= MaximumNamedTears
                ? string.Join(", ", differences)
                : $"{string.Join(", ", differences.Take(MaximumNamedTears))} and {differences.Count - MaximumNamedTears} more";
            return false;
        }

        if (!TryBuildCameraMatrix(first, out var camera) ||
            !TryResolveViewport(first, out viewport) ||
            !IsUsableProjection(first.Projection))
        {
            return false;
        }

        var found = new List<SubmarineVisibleTarget>(EnemyRecordCount);
        for (var slot = 0; slot < EnemyRecordCount; slot++)
        {
            var index = slot * EnemyFieldsPerRecord;
            var flags = first.Records[index + 3];
            var markerState = first.Records[index + 4] & EnemyMarkerMask;

            // 78E9CE draws a square over a live record carrying a marker; 78E3FB keeps
            // drawing a dead one's hull while 78D092 sinks it. Nothing else is on screen.
            var marked = (flags & EnemyActiveMask) != 0 && markerState != 0;
            var sinking = (flags & EnemySinkingBit) != 0;
            if (!marked && !sinking)
            {
                continue;
            }

            var x = first.Records[index] >> 12;
            var y = first.Records[index + 1] >> 12;
            var z = first.Records[index + 2] >> 12;
            if (!TryProject(
                    camera,
                    first.Projection,
                    (short)x,
                    (short)y,
                    (short)z,
                    out var screenX,
                    out var screenY) ||
                !viewport.Contains(screenX, screenY))
            {
                continue;
            }

            found.Add(new SubmarineVisibleTarget(
                slot,
                marked && (markerState & EnemyMarkerLocked) != 0,
                (int)MathF.Round(screenX),
                (int)MathF.Round(screenY))
            {
                Model = DecodeModel(unchecked((uint)first.Records[index + 5])),
                X = x,
                Y = y,
                Z = z,
                IsSinking = sinking && !marked
            });
        }

        targets = found;
        pose = new ViewPose(
            first.Player[0] >> 12,
            first.Player[1] >> 12,
            first.Player[2] >> 12,
            first.SpeedIndex,
            first.ViewMode);
        return true;
    }

    private bool TryCaptureView(out ViewCapture capture)
    {
        capture = new ViewCapture();
        for (var index = 0; index < capture.Rotation.Length; index++)
        {
            if (!memory.TryReadInt16(AddressCamera + (uint)(index * sizeof(short)), out var value))
            {
                return false;
            }

            capture.Rotation[index] = value;
        }

        // The three translations are unaligned int32s at +0x12, +0x16 and +0x1A.
        for (var index = 0; index < capture.Translation.Length; index++)
        {
            if (!memory.TryReadInt32(AddressCamera + 0x12 + (uint)(index * 4), out var value))
            {
                return false;
            }

            capture.Translation[index] = value;
        }

        if (!memory.TryReadUInt32(AddressProjectionContextPointer, out var context))
        {
            return false;
        }

        capture.Context = context;
        if (context != 0 && context <= MaximumContextAddress)
        {
            uint[] viewportOffsets =
            [
                ViewportOriginXOffset,
                ViewportOriginYOffset,
                ViewportWidthOffset,
                ViewportHeightOffset
            ];
            for (var index = 0; index < viewportOffsets.Length; index++)
            {
                if (!memory.TryReadInt32(context + viewportOffsets[index], out var value))
                {
                    return false;
                }

                capture.ViewportBounds[index] = value;
            }

            for (var index = 0; index < capture.Projection.Length; index++)
            {
                if (!memory.TryReadSingle(
                        context + ProjectionMatrixOffset + (uint)(index * sizeof(float)),
                        out var value))
                {
                    return false;
                }

                capture.Projection[index] = value;
            }
        }

        // 987338/98733C/987340 is where 7991C6 moves the player; 9873D8 is 798580's throttle
        // step and 9873A8 its overview toggle.
        if (!memory.TryReadInt32(AddressPlayerX, out capture.Player[0]) ||
            !memory.TryReadInt32(AddressPlayerY, out capture.Player[1]) ||
            !memory.TryReadInt32(AddressPlayerZ, out capture.Player[2]) ||
            !memory.TryReadInt32(AddressSpeedIndex, out var speedIndex) ||
            !memory.TryReadInt32(AddressViewMode, out var viewMode))
        {
            return false;
        }

        capture.SpeedIndex = speedIndex;
        capture.ViewMode = viewMode;

        for (var slot = 0; slot < EnemyRecordCount; slot++)
        {
            var record = AddressEnemyRecords + (uint)(slot * EnemyRecordStride);
            var index = slot * EnemyFieldsPerRecord;
            if (!memory.TryReadInt32(record + EnemyPositionXOffset, out capture.Records[index]) ||
                !memory.TryReadInt32(record + EnemyPositionYOffset, out capture.Records[index + 1]) ||
                !memory.TryReadInt32(record + EnemyPositionZOffset, out capture.Records[index + 2]) ||
                !memory.TryReadInt32(record + EnemyFlagsOffset, out capture.Records[index + 3]) ||
                !memory.TryReadInt32(record + EnemyMarkerOffset, out capture.Records[index + 4]) ||
                !memory.TryReadInt32(record + EnemyModelOffset, out capture.Records[index + 5]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryResolveViewport(ViewCapture capture, out Viewport viewport)
    {
        viewport = default;
        if (capture.Context == 0 || capture.Context > MaximumContextAddress)
        {
            return false;
        }

        var originX = capture.ViewportBounds[0];
        var originY = capture.ViewportBounds[1];
        var width = capture.ViewportBounds[2];
        var height = capture.ViewportBounds[3];

        // Bounded without negating anything: int.MinValue has no positive counterpart,
        // and asking for its magnitude is an exception rather than a rejection.
        if (width <= 0 || height <= 0 ||
            width > MaximumViewportExtent || height > MaximumViewportExtent ||
            originX < -MaximumViewportExtent || originX > MaximumViewportExtent ||
            originY < -MaximumViewportExtent || originY > MaximumViewportExtent)
        {
            return false;
        }

        viewport = new Viewport(originX, originY, width, height);
        return true;
    }

    private static bool IsUsableProjection(float[] projection) =>
        projection.All(float.IsFinite) && projection.Any(value => value != 0f);

    /// <summary>
    /// 6617E9's conversion of the canonical camera at E996F8: nine rotation shorts over
    /// 4096 laid out in rows, then the three unaligned translations, then 1. A rotation
    /// that is entirely zero is not a camera the game has finished setting up, and a view
    /// built from it would place every marker at the same point.
    /// </summary>
    private static bool TryBuildCameraMatrix(ViewCapture capture, out float[] matrix)
    {
        matrix = [];
        if (capture.Rotation.All(value => value == 0))
        {
            return false;
        }

        var rotation = capture.Rotation;
        matrix = new float[16];
        matrix[0] = rotation[0] / CameraRotationScale;
        matrix[1] = rotation[3] / CameraRotationScale;
        matrix[2] = rotation[6] / CameraRotationScale;
        matrix[4] = rotation[1] / CameraRotationScale;
        matrix[5] = rotation[4] / CameraRotationScale;
        matrix[6] = rotation[7] / CameraRotationScale;
        matrix[8] = rotation[2] / CameraRotationScale;
        matrix[9] = rotation[5] / CameraRotationScale;
        matrix[10] = rotation[8] / CameraRotationScale;
        matrix[12] = capture.Translation[0];
        matrix[13] = capture.Translation[1];
        matrix[14] = capture.Translation[2];
        matrix[15] = 1f;
        return true;
    }

    /// <summary>
    /// The camera then the projection, each applied the way the native code applies it.
    ///
    /// <para>These two are not applied the same way, and that is the whole of it. 66C6CD
    /// builds its product as <c>C[4r+c] = sum(A[4r+k] * B[4c+k])</c> - each of the
    /// camera's rows dotted with each of the stored projection's rows - and 66CE40 then
    /// reads that product back by columns, <c>out[j] = C[j]X + C[4+j]Y + C[8+j]Z +
    /// C[12+j]</c>. Composing those two gives the camera read by columns and the
    /// projection read by contiguous rows, because 67C2C0 already transposed the
    /// projection into the context at +0x8D0 when 67D1FB stored it. Reading the
    /// projection by columns as well puts every marker somewhere the game is not drawing
    /// one.</para>
    /// </summary>
    internal static bool TryProject(
        float[] camera,
        float[] projection,
        float x,
        float y,
        float z,
        out float screenX,
        out float screenY)
    {
        screenX = 0f;
        screenY = 0f;
        TransformByColumns(camera, x, y, z, out var view);
        TransformByRows(projection, view, out var clip);
        if (!float.IsFinite(clip[0]) || !float.IsFinite(clip[1]) || !float.IsFinite(clip[3]) ||
            clip[3] <= 0f)
        {
            return false;
        }

        screenX = clip[0] / clip[3];
        screenY = clip[1] / clip[3];
        return float.IsFinite(screenX) && float.IsFinite(screenY);
    }

    private static void TransformByColumns(
        float[] matrix,
        float x,
        float y,
        float z,
        out float[] result)
    {
        result = new float[4];
        for (var j = 0; j < 4; j++)
        {
            result[j] = matrix[j] * x + matrix[4 + j] * y + matrix[8 + j] * z + matrix[12 + j];
        }
    }

    private static void TransformByRows(float[] matrix, float[] vector, out float[] result)
    {
        result = new float[4];
        for (var j = 0; j < 4; j++)
        {
            result[j] =
                matrix[4 * j] * vector[0] +
                matrix[4 * j + 1] * vector[1] +
                matrix[4 * j + 2] * vector[2] +
                matrix[4 * j + 3] * vector[3];
        }
    }
}
