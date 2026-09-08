using System.Buffers.Binary;

namespace Ff7.Accessibility.LegacyLayout;

public readonly record struct HighwayActorSnapshot(
    int Slot,
    int State,
    int SecondaryState,
    int LateralFixed,
    int LongitudinalFixed,
    int HitPoints,
    int Type,
    int AttackTimer)
{
    public double LateralUnits => LateralFixed / 256d;

    public double LongitudinalUnits => LongitudinalFixed / 256d;

    public bool IsActive =>
        Slot is >= HighwayStateReader.FirstEnemySlot and <= HighwayStateReader.LastEnemySlot &&
        State is 0 or 1 &&
        HitPoints > 0;
}

public readonly record struct HighwayPartyHealthSnapshot(
    int Slot,
    string Name,
    ushort CurrentHp,
    ushort MaximumHp);

/// <summary>
/// The word the arcade HUD paints across the middle of the screen. FUN_00659379 puts
/// up one of its own three textures - guaa, huaa and iuaa, which read READY, GO! and
/// GOAL - and nothing at all for any other state, so this says exactly what is on
/// screen at the moment it is read and nothing about what is coming.
/// </summary>
public enum HighwayBanner
{
    /// <summary>
    /// The HUD object could not be reached this frame, so what it is showing is not
    /// known. Distinct from <see cref="None"/>, which is a HUD that was read and is
    /// showing nothing: a transient failure must not read as the banner going away
    /// and coming back.
    /// </summary>
    Unknown = -1,

    None = 0,
    Ready = 1,
    Goal = 2,
    Go = 3
}

public sealed record HighwayStateSnapshot(
    byte Module,
    HighwayActorSnapshot Cloud,
    HighwayActorSnapshot Truck,
    IReadOnlyList<HighwayActorSnapshot> Enemies,
    IReadOnlyList<HighwayPartyHealthSnapshot> PartyHealth,
    int Score,
    bool IsStoryChase,
    int HighScore = 0,
    HighwayBanner Banner = HighwayBanner.None);

/// <summary>
/// Publishes a bounded, module-bookended snapshot of FFVII's original highway
/// minigame address layout. Failed guest reads are never reinterpreted as zero.
/// </summary>
public sealed class HighwayStateReader
{
    public const int AddressCurrentModule = 0x00CBF9DC;
    public const int AddressPartyHealth = 0x00D858A8;
    public const int AddressStoryMode = 0x00D8596C;
    public const int AddressScore = 0x00D85990;

    /// <summary>
    /// The HI-SCORE the arcade HUD draws. FUN_0065950C dispatches on
    /// <see cref="AddressStoryMode"/> == 1 to the arcade HUD FUN_00659379, which
    /// draws this at 0x00659413 immediately before the running score at 0x0065949F.
    /// It is displayed data in arcade mode, and is not drawn at all in the story
    /// chase, whose HUD is the party display FUN_00658BFA.
    /// </summary>
    public const int AddressHighScore = 0x00D85994;
    public const int AddressActorTable = 0x00D8B320;

    /// <summary>
    /// The HUD object itself. FUN_0065076D stores whatever the constructor
    /// FUN_0065878E returned here, and stores zero when the allocation failed, so a
    /// null here is a HUD that does not exist rather than one showing nothing.
    /// FUN_0065113E loads this same global to reach the renderer.
    /// </summary>
    public const int AddressHudPointer = 0x00D8D444;

    /// <summary>
    /// The banner state, the HUD object's own first field. The constructor clears it
    /// and FUN_00659379 draws a texture only for 1, 3 and 2.
    /// </summary>
    public const int HudBannerStateOffset = 0x00;

    public const byte HighwayModule = 6;
    public const int ActorStride = 0x1A8;
    public const int ActorCount = 5;
    public const int FirstEnemySlot = 2;
    public const int LastEnemySlot = 4;
    public const int ActorStateOffset = 0x68;
    public const int ActorSecondaryStateOffset = 0x6C;
    public const int ActorLateralOffset = 0x80;
    public const int ActorLongitudinalOffset = 0x88;
    public const int ActorHitPointsOffset = 0xF0;
    public const int ActorTypeOffset = 0xF8;
    public const int ActorAttackTimerOffset = 0x16C;

    public const int PartySlotCount = 5;
    public const int PartyHealthStride = 10;
    public const int PartyMaximumHpOffset = 0;
    public const int PartyCurrentHpOffset = 2;

    private static readonly string[] PartyNames =
        ["Cloud", "Barret", "Tifa", "Aeris", "Red XIII"];

    private readonly ILegacyAddressSpace addressSpace;

    public HighwayStateReader(ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
    }

    public string LastDiagnostic { get; private set; } = "not read";

    public bool TryRead(out HighwayStateSnapshot snapshot)
    {
        snapshot = default!;
        if (!TryReadFrame(out var frame, out var diagnostic, out var bannerDiagnostic))
        {
            LastDiagnostic = diagnostic;
            return false;
        }

        if (!TryParse(frame, out snapshot, out diagnostic))
        {
            LastDiagnostic = diagnostic;
            return false;
        }

        LastDiagnostic =
            $"module={snapshot.Module}, enemies={snapshot.Enemies.Count}, " +
            $"active={snapshot.Enemies.Count(enemy => enemy.IsActive)}, " +
            $"party={snapshot.PartyHealth.Count}, score={snapshot.Score}, " +
            $"mode={(snapshot.IsStoryChase ? "story" : "gold-saucer")}, " +
            $"banner={snapshot.Banner}";

        // Said out loud in the log rather than swallowed. A banner nobody hears is
        // the failure this whole path exists to prevent, so a HUD that stopped
        // reading has to be visible somewhere.
        if (bannerDiagnostic.Length != 0)
        {
            LastDiagnostic += $" ({bannerDiagnostic})";
        }

        return true;
    }

    private bool TryReadFrame(
        out HighwayFrame frame, out string diagnostic, out string bannerDiagnostics)
    {
        frame = default!;
        bannerDiagnostics = string.Empty;
        diagnostic = "highway primitive read failed";
        if (!addressSpace.TryReadByte((uint)AddressCurrentModule, out var moduleBefore))
        {
            return false;
        }

        if (moduleBefore != HighwayModule)
        {
            diagnostic = $"module={moduleBefore}, not highway";
            return false;
        }

        var actorBytes = new byte[ActorCount * ActorStride];
        var partyBytes = new byte[PartySlotCount * PartyHealthStride];
        if (!addressSpace.TryRead((uint)AddressActorTable, actorBytes) ||
            !addressSpace.TryReadInt32((uint)AddressStoryMode, out var storyMode) ||
            !addressSpace.TryReadInt32((uint)AddressScore, out var score) ||
            !addressSpace.TryReadInt32((uint)AddressHighScore, out var highScore))
        {
            return false;
        }

        // Party health is only drawn by the story chase's own HUD. Reading it in the
        // arcade mode would let a stale or unreadable block silence a perfectly good
        // arcade snapshot, and there is nothing on screen for it to describe.
        if (storyMode == 0)
        {
            if (!addressSpace.TryRead((uint)AddressPartyHealth, partyBytes))
            {
                return false;
            }
        }
        else
        {
            Array.Fill(partyBytes, (byte)0xFF);
        }

        // The banner belongs to the arcade HUD alone. FUN_0065950C sends the story
        // chase to a different HUD object entirely, which draws no banner and whose
        // own first field means something else.
        //
        // A HUD that cannot be reached is reported as unknown rather than failing the
        // whole frame. Everything else here - the bikers, the truck and the score -
        // is what the player is steering by, and losing all of it because one pointer
        // did not read would be far worse than not naming a banner.
        var banner = HighwayBanner.None;
        if (storyMode == 1 && !TryReadBanner(out banner, out var bannerDiagnostic))
        {
            banner = HighwayBanner.Unknown;
            bannerDiagnostics = bannerDiagnostic;
        }

        if (!addressSpace.TryReadByte((uint)AddressCurrentModule, out var moduleAfter))
        {
            return false;
        }

        if (moduleAfter != moduleBefore)
        {
            diagnostic = $"highway module changed during read: {moduleBefore}->{moduleAfter}";
            return false;
        }

        frame = new HighwayFrame(
            moduleBefore, actorBytes, partyBytes, storyMode, score, highScore, banner);
        diagnostic = string.Empty;
        return true;
    }

    /// <summary>
    /// The word the arcade HUD is showing right now, through the HUD object the
    /// renderer itself reaches. A HUD that has not been built yet is not a HUD
    /// showing nothing, so a null pointer is a failed read rather than a blank
    /// screen, and a state the renderer draws no texture for is simply no banner.
    /// </summary>
    private bool TryReadBanner(out HighwayBanner banner, out string diagnostic)
    {
        banner = HighwayBanner.None;
        diagnostic = string.Empty;
        if (!addressSpace.TryReadUInt32((uint)AddressHudPointer, out var hud))
        {
            diagnostic = "highway HUD pointer unreadable";
            return false;
        }

        if (hud is < 0x00010000u or > 0x7FFF0000u)
        {
            diagnostic = $"highway HUD pointer 0x{hud:X8} is not an object";
            return false;
        }

        if (!addressSpace.TryReadInt32(hud + HudBannerStateOffset, out var state))
        {
            diagnostic = "highway HUD banner state unreadable";
            return false;
        }

        // Who owns the word that was just read, and which HUD was being drawn while
        // it was read. FUN_0065076D replaces the pointer at 0x00D8D444 when the HUD
        // is rebuilt, and FUN_0065950C picks the arcade HUD or the story chase's
        // party display from 0x00D8596C - so a state word sampled across either
        // change belongs to a HUD that is no longer on screen, and the word at the
        // same offset in the other HUD is not a banner at all.
        //
        // Neither check can be replaced by the module test above: both of these move
        // while the module stays at 6.
        if (!addressSpace.TryReadUInt32((uint)AddressHudPointer, out var hudAfter) ||
            hudAfter != hud)
        {
            diagnostic = $"highway HUD owner changed while its banner was being read: 0x{hud:X8}";
            return false;
        }

        if (!addressSpace.TryReadInt32((uint)AddressStoryMode, out var modeAfter) || modeAfter != 1)
        {
            diagnostic = "highway left arcade mode while its banner was being read";
            return false;
        }

        banner = state switch
        {
            1 => HighwayBanner.Ready,
            2 => HighwayBanner.Goal,
            3 => HighwayBanner.Go,
            _ => HighwayBanner.None
        };
        return true;
    }

    private static bool TryParse(
        HighwayFrame frame,
        out HighwayStateSnapshot snapshot,
        out string diagnostic)
    {
        snapshot = default!;
        diagnostic = string.Empty;
        if (frame.StoryMode is not 0 and not 1)
        {
            diagnostic = $"invalid highway story mode {frame.StoryMode}";
            return false;
        }

        if (frame.Score is < 0 or > 10_000_000)
        {
            diagnostic = $"invalid highway score {frame.Score}";
            return false;
        }

        var actors = new HighwayActorSnapshot[ActorCount];
        for (var slot = 0; slot < actors.Length; slot++)
        {
            var actor = ParseActor(frame.ActorBytes, slot);
            if (slot is >= FirstEnemySlot and <= LastEnemySlot &&
                actor.HitPoints is < -100_000 or > 100_000)
            {
                diagnostic = $"invalid highway actor HP in slot {slot}: {actor.HitPoints}";
                return false;
            }

            if (actor.IsActive && actor.Type is < 10 or > 12)
            {
                diagnostic = $"invalid active highway enemy type in slot {slot}: {actor.Type}";
                return false;
            }

            actors[slot] = actor;
        }

        var partyHealth = new List<HighwayPartyHealthSnapshot>(PartySlotCount);
        for (var slot = 0; slot < PartySlotCount; slot++)
        {
            var offset = slot * PartyHealthStride;
            var maximum = ReadUInt16(frame.PartyBytes, offset + PartyMaximumHpOffset);
            var current = ReadUInt16(frame.PartyBytes, offset + PartyCurrentHpOffset);
            if (maximum == ushort.MaxValue || current == ushort.MaxValue)
            {
                continue;
            }

            if ((maximum == 0 && current != 0) ||
                (maximum != ushort.MaxValue && current > maximum))
            {
                diagnostic = $"invalid highway party HP in slot {slot}: {current}/{maximum}";
                return false;
            }

            if (maximum is 0 or ushort.MaxValue)
            {
                continue;
            }

            partyHealth.Add(
                new HighwayPartyHealthSnapshot(slot, PartyNames[slot], current, maximum));
        }

        snapshot = new HighwayStateSnapshot(
            frame.Module,
            actors[0],
            actors[1],
            Array.AsReadOnly(actors[FirstEnemySlot..(LastEnemySlot + 1)]),
            partyHealth.AsReadOnly(),
            frame.Score,
            frame.StoryMode == 0,
            frame.HighScore,
            frame.Banner);
        return true;
    }

    private static HighwayActorSnapshot ParseActor(byte[] actorBytes, int slot)
    {
        var offset = slot * ActorStride;
        return new HighwayActorSnapshot(
            slot,
            ReadInt32(actorBytes, offset + ActorStateOffset),
            ReadInt32(actorBytes, offset + ActorSecondaryStateOffset),
            ReadInt32(actorBytes, offset + ActorLateralOffset),
            ReadInt32(actorBytes, offset + ActorLongitudinalOffset),
            ReadInt32(actorBytes, offset + ActorHitPointsOffset),
            ReadInt32(actorBytes, offset + ActorTypeOffset),
            ReadInt32(actorBytes, offset + ActorAttackTimerOffset));
    }

    private static int ReadInt32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)));

    private static ushort ReadUInt16(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, sizeof(ushort)));

    private sealed record HighwayFrame(
        byte Module,
        byte[] ActorBytes,
        byte[] PartyBytes,
        int StoryMode,
        int Score,
        int HighScore,
        HighwayBanner Banner);
}
