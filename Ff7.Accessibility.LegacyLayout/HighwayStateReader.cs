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

public sealed record HighwayStateSnapshot(
    byte Module,
    HighwayActorSnapshot Cloud,
    HighwayActorSnapshot Truck,
    IReadOnlyList<HighwayActorSnapshot> Enemies,
    IReadOnlyList<HighwayPartyHealthSnapshot> PartyHealth,
    int Score,
    bool IsStoryChase);

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
    public const int AddressActorTable = 0x00D8B320;

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
        if (!TryReadFrame(out var frame, out var diagnostic))
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
            $"mode={(snapshot.IsStoryChase ? "story" : "gold-saucer")}";
        return true;
    }

    private bool TryReadFrame(out HighwayFrame frame, out string diagnostic)
    {
        frame = default!;
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
            !addressSpace.TryRead((uint)AddressPartyHealth, partyBytes) ||
            !addressSpace.TryReadInt32((uint)AddressStoryMode, out var storyMode) ||
            !addressSpace.TryReadInt32((uint)AddressScore, out var score) ||
            !addressSpace.TryReadByte((uint)AddressCurrentModule, out var moduleAfter))
        {
            return false;
        }

        if (moduleAfter != moduleBefore)
        {
            diagnostic = $"highway module changed during read: {moduleBefore}->{moduleAfter}";
            return false;
        }

        frame = new HighwayFrame(moduleBefore, actorBytes, partyBytes, storyMode, score);
        diagnostic = string.Empty;
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
            frame.StoryMode == 0);
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
        int Score);
}
