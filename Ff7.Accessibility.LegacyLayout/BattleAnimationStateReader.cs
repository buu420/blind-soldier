using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// One reading of the battle animation queue: whether a battle is running, and if the
/// engine is playing an action animation right now, the queued row it is playing.
/// </summary>
/// <param name="InBattle">The battle module owns the game.</param>
/// <param name="IsPlaying">The engine has started the current queue row and its actor
/// is still animating it.</param>
/// <param name="EventIndex">The queue row being played.</param>
/// <param name="Attacker">The actor performing it: 0-2 party, 4-9 enemies.</param>
/// <param name="Command">The row's command after the W-command remap: 3 summon, 0x14
/// limit, 0x20 enemy attack.</param>
/// <param name="Effect">The row's effect, the index the command's effect dispatcher
/// switches on.</param>
/// <param name="Action">The row's action: a relative index for menu commands, an
/// absolute kernel attack for sub-actions.</param>
/// <remarks>
/// <see cref="HasRow"/> stays true after the performer has gone idle, for as long as the
/// engine keeps that row current while its effects finish; that is the animation's whole
/// visual phase. <see cref="Tick"/> is the wrapping battle tick and <see cref="Paused"/>
/// the battle pause flag, the clock staged descriptions are timed against.
/// </remarks>
public readonly record struct BattleAnimationObservation(
    bool InBattle,
    bool IsPlaying,
    byte EventIndex,
    byte Attacker,
    byte Command,
    byte Effect,
    ushort Action)
{
    public static BattleAnimationObservation NotInBattle { get; } = new(false, false, 0, 0, 0, 0, 0);

    public static BattleAnimationObservation Idle { get; } = new(true, false, 0, 0, 0, 0, 0);

    /// <summary>A row the engine has started and whose performer is still animating.</summary>
    public static BattleAnimationObservation Playing(
        byte eventIndex, byte attacker, byte command, byte effect, ushort action) =>
        new(true, true, eventIndex, attacker, command, effect, action) { HasRow = true };

    /// <summary>
    /// The current action row when its performer is not animating it: queued and not yet
    /// started, or started with the performer idle while its effects run on.
    /// </summary>
    public static BattleAnimationObservation Current(
        byte eventIndex, byte attacker, byte command, byte effect, ushort action) =>
        new(true, false, eventIndex, attacker, command, effect, action) { HasRow = true };

    /// <summary>The queue sits on an action row (kind 1) whose identity is filled in.</summary>
    public bool HasRow { get; init; }

    /// <summary>
    /// The battle tick byte <c>0x00BFD0E4</c>, which <c>FUN_0042D808</c> increments once
    /// per animation-queue update. It wraps at 256 and keeps counting while paused.
    /// </summary>
    public byte Tick { get; init; }

    /// <summary>
    /// The battle pause flag <c>0x00DC0E6C</c>. While it is set <c>FUN_0041BAB3</c> clears
    /// <c>0x009AD1AC</c>, and no effect (<c>FUN_005BF01F</c>) or actor script
    /// (<c>FUN_0041FBA4</c>) advances.
    /// </summary>
    public bool Paused { get; init; }

    /// <summary>
    /// For a summon row: the summon dispatcher <c>FUN_005C0E4B</c> is registered in the
    /// effect table and has not yet begun the summon's own sequence. Its stage 1 counts
    /// down, loads the summon and calls its main, then releases the slot; the release is
    /// the summon sequence's start, the anchor staged summon cues are timed from.
    /// </summary>
    public bool SummonEffectWaiting { get; init; }

    /// <summary>
    /// For a summon row: the attack-name banner is on screen. The banner effect
    /// <c>FUN_0042782A</c> sits in the effect table with its delay
    /// (<c>0x00BFB71E + slot*0x20</c>) spent and its lifetime (<c>0x00BFB71C</c>) not.
    /// </summary>
    public bool BannerVisible { get; init; }

    /// <summary>
    /// The name the banner shows, read from the game's own loaded text as its renderer
    /// reads it; null when no banner is showing or that text is not in the native blob
    /// (the legacy process under FFNx keeps it elsewhere; see <see cref="BannerAction"/>).
    /// </summary>
    public string? BannerText { get; init; }

    /// <summary>
    /// While the banner shows: the command it is drawn for, the active actor's
    /// <c>0x00BE119B + a*0x1AEC</c> that <c>FUN_0042782A</c> hands <c>FUN_006D71FA</c>.
    /// </summary>
    public byte BannerCommand { get; init; }

    /// <summary>While the banner shows: the action it names, <c>0x00BF23FE + a*0x74</c>.</summary>
    public ushort BannerAction { get; init; }
}

/// <summary>
/// Reads which queued action the battle engine is actually animating.
///
/// <para>The action executor writes a whole queue of 12-byte rows at
/// <c>0x009AAD70</c> before anything is shown, and the menu writes nothing there at all,
/// so a row existing proves nothing. What proves a row is playing is the engine having
/// started it. <c>FUN_0042CBF9</c> case 1 starts row <c>[0x00BF2A38]</c> only while the
/// all-idle flag <c>0x00BF2128</c> is set; starting it clears the flag, stores the
/// attacker in <c>0x00BE1170</c>, marks that actor busy (<c>0x00BE119E + a*0x1AEC</c> =
/// 0) and calls <c>FUN_0042D227</c>, which copies the row's action to
/// <c>0x00BF23FE + a*0x74</c>. The actor stays busy until its script's end opcode marks
/// it idle, and <c>FUN_0042D808</c> sets the flag again - and case 1 moves the index on -
/// only once every actor is idle. So a row that is queued but not started always sits
/// behind idle actors, and the flag itself adds nothing the actor's own busy byte does
/// not already say; it is not read.</para>
///
/// <para>The identity is taken from the queued row, not from the actor: animation
/// script opcode 0xDA (<c>FUN_0041FBA4</c> case 0x4C) rewrites the actor's effect and
/// command in the middle of an animation, and the script index is swapped for the idle
/// script when it ends. The action is written only by <c>FUN_0042D227</c>.</para>
///
/// <para>Everything is read twice and both passes must agree; a read that fails or
/// changes underneath is not an observation at all, never an idle battle. The x64
/// translation (7FF7017FF520, 7FF7018008A0, 7FF701801E60) uses the same guest
/// addresses, so both runtimes read through this class.</para>
/// </summary>
public sealed class BattleAnimationStateReader
{
    public const uint AddressCurrentModule = FieldPositionReader.AddressCurrentModule;
    public const byte BattleModule = 2;
    public const uint AddressQueue = 0x009AAD70;
    public const uint AddressQueueIndex = 0x00BF2A38;
    public const int QueueRowSize = 12;
    public const int QueueRowCount = 64;
    public const uint AddressActiveActor = 0x00BE1170;
    public const uint AddressActorIdle = 0x00BE119E;
    public const int ActorStateSize = 0x1AEC;
    public const uint AddressActorAction = 0x00BF23FE;
    public const int ActorActionStateSize = 0x74;
    public const byte ActionRowKind = 1;
    public const int ActorCount = 10;
    public const uint AddressBattleTick = 0x00BFD0E4;
    public const uint AddressBattlePaused = 0x00DC0E6C;

    /// <summary>
    /// The 100 function pointers of the battle effect runner <c>FUN_005BF01F</c>, filled by
    /// <c>FUN_005BEC50</c>. Both runtimes store guest addresses here: the x64 translation
    /// of <c>FUN_005C0E39</c> (7FF702084D30) pushes the guest constant 0x5C0E4B.
    /// </summary>
    public const uint AddressEffectFunctions = 0x00BF2858;
    public const int EffectFunctionCount = 100;
    public const uint SummonDispatcher = 0x005C0E4B;
    public const byte SummonCommand = 3;

    /// <summary>
    /// The attack-name banner. Actor script opcodes 0x0A and 0x5C (<c>FUN_0041FBA4</c>)
    /// register it; each frame its delay has run out and its lifetime has not, it hands
    /// <c>FUN_006D71FA</c> the active actor's command (<c>0x00BE119B + a*0x1AEC</c>) and
    /// action (<c>0x00BF23FE + a*0x74</c>), which <c>FUN_006D1CC0</c> case 0x16 draws. The
    /// effect runners of both runtimes (<c>FUN_005BF01F</c>, 7FF702079EF0) compare the
    /// slot against this guest address.
    /// </summary>
    public const uint BannerEffect = 0x0042782A;
    public const uint AddressEffectLifetime = 0x00BFB71C;
    public const uint AddressEffectDelay = 0x00BFB71E;
    public const int EffectStateSize = 0x20;
    public const uint AddressActorCommand = 0x00BE119B;

    /// <summary>
    /// The loaded kernel text: <c>FUN_00419379</c> appends each section to the blob at
    /// <c>0x009A13C8</c>, records its offset in the table at <c>0x009A7FC8</c> and counts
    /// sections in <c>0x009A8124</c>; <c>FUN_00419457</c> (x64 7FF701767090) looks a string
    /// up. Section 17 is the summon attack names, section 9 the magic names.
    /// </summary>
    public const uint AddressKernelText = 0x009A13C8;
    public const uint AddressKernelSectionOffsets = 0x009A7FC8;
    public const uint AddressKernelSectionCount = 0x009A8124;
    public const int SummonAttackNameSection = 0x11;
    public const int MagicNameSection = 9;
    public const int SummonAttackNameCount = 0x10;
    private const int MaximumNameLength = 64;

    private readonly ILegacyAddressSpace addressSpace;

    public BattleAnimationStateReader(ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
    }

    /// <summary>
    /// Reads the queue coherently. False means unknown: the caller must keep whatever it
    /// believed before rather than treat the battle as idle.
    /// </summary>
    public bool TryRead(out BattleAnimationObservation observation)
    {
        observation = default;
        if (!TryReadOnce(out var first) || !TryReadOnce(out var second) || first != second)
        {
            return false;
        }

        observation = second;
        return true;
    }

    private bool TryReadOnce(out BattleAnimationObservation observation)
    {
        observation = default;
        if (!addressSpace.TryReadByte(AddressCurrentModule, out var module))
        {
            return false;
        }

        if (module != BattleModule)
        {
            observation = BattleAnimationObservation.NotInBattle;
            return true;
        }

        if (!addressSpace.TryReadByte(AddressQueueIndex, out var index) || index >= QueueRowCount ||
            !addressSpace.TryReadByte(AddressBattleTick, out var tick) ||
            !addressSpace.TryReadByte(AddressBattlePaused, out var paused))
        {
            return false;
        }

        Span<byte> row = stackalloc byte[8];
        if (!addressSpace.TryRead(AddressQueue + ((uint)index * QueueRowSize), row))
        {
            return false;
        }

        var attacker = row[0];
        var kind = row[1];
        if (attacker >= ActorCount || kind != ActionRowKind)
        {
            // The terminator (0xFF), or a text, camera or reaction row.
            observation = BattleAnimationObservation.Idle with { Tick = tick, Paused = paused != 0 };
            return true;
        }

        var action = (ushort)(row[6] | (row[7] << 8));
        if (!addressSpace.TryReadByte(AddressActiveActor, out var activeActor) ||
            !addressSpace.TryReadByte(AddressActorIdle + ((uint)attacker * ActorStateSize), out var actorIdle) ||
            !addressSpace.TryReadUInt16(
                AddressActorAction + ((uint)attacker * ActorActionStateSize),
                out var actorAction))
        {
            return false;
        }

        var started = activeActor == attacker && actorIdle == 0 && actorAction == action;
        var summonWaiting = false;
        var bannerVisible = false;
        string? bannerText = null;
        byte bannerCommand = 0;
        ushort bannerAction = 0;
        if (row[3] == SummonCommand)
        {
            Span<byte> effects = stackalloc byte[EffectFunctionCount * 4];
            if (!addressSpace.TryRead(AddressEffectFunctions, effects))
            {
                return false;
            }

            for (var slot = 0; slot < EffectFunctionCount; slot++)
            {
                var function = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
                    effects.Slice(slot * 4, 4));
                summonWaiting |= function == SummonDispatcher;
                if (function != BannerEffect || bannerVisible)
                {
                    continue;
                }

                // Signed: the native banner counts its delay down to 0, but in the legacy
                // process 0x42782A is a detour whose live banner keeps the delay word at -1
                // (0xFFFF) while its lifetime counts down (capture 24500). Spent means <= 0.
                var state = AddressEffectLifetime + ((uint)slot * EffectStateSize);
                if (!addressSpace.TryReadInt16(state, out var lifetime) ||
                    !addressSpace.TryReadInt16(AddressEffectDelay + ((uint)slot * EffectStateSize), out var delay))
                {
                    return false;
                }

                bannerVisible = delay <= 0 && lifetime > 0;
            }

            if (bannerVisible &&
                !TryReadBannerText(activeActor, out bannerCommand, out bannerAction, out bannerText))
            {
                return false;
            }
        }

        observation = (started
            ? BattleAnimationObservation.Playing(index, attacker, row[3], row[2], action)
            : BattleAnimationObservation.Current(index, attacker, row[3], row[2], action))
            with
            {
                Tick = tick,
                Paused = paused != 0,
                SummonEffectWaiting = summonWaiting,
                BannerVisible = bannerVisible,
                BannerText = bannerText,
                BannerCommand = bannerCommand,
                BannerAction = bannerAction,
            };
        return true;
    }

    /// <summary>
    /// The banner's text, resolved the way <c>FUN_006D1CC0</c> case 0x16 does for the
    /// active actor's command 3: <c>FUN_0041963C</c> category 6 reads the summon attack
    /// name for an action below 0x10 and the magic name at the raw action otherwise. Any
    /// other command draws something this reader does not name. False only when memory
    /// could not be read; text that does not decode sensibly is null, never guessed.
    /// </summary>
    private bool TryReadBannerText(byte activeActor, out byte command, out ushort action, out string? text)
    {
        text = null;
        command = 0;
        action = 0;
        if (activeActor >= ActorCount)
        {
            return true;
        }

        if (!addressSpace.TryReadByte(AddressActorCommand + ((uint)activeActor * ActorStateSize), out command) ||
            !addressSpace.TryReadUInt16(AddressActorAction + ((uint)activeActor * ActorActionStateSize), out action))
        {
            return false;
        }

        if (command != SummonCommand)
        {
            return true;
        }

        return action < SummonAttackNameCount
            ? TryReadKernelText(SummonAttackNameSection, action, out text)
            : TryReadKernelText(MagicNameSection, action, out text);
    }

    private bool TryReadKernelText(int section, int entry, out string? text)
    {
        text = null;
        if (!addressSpace.TryReadUInt16(AddressKernelSectionCount, out var sections) ||
            !addressSpace.TryReadUInt16(AddressKernelSectionOffsets + ((uint)section * 2), out var sectionOffset))
        {
            return false;
        }

        const uint blobLength = AddressKernelSectionOffsets - AddressKernelText;
        if (section >= sections || sectionOffset >= blobLength)
        {
            return true;
        }

        var sectionStart = AddressKernelText + sectionOffset;
        if (!addressSpace.TryReadUInt16(sectionStart, out var firstEntry) ||
            !addressSpace.TryReadUInt16(sectionStart + ((uint)entry * 2), out var entryOffset))
        {
            return false;
        }

        // The first entry's offset is the size of the section's offset table.
        if (entry * 2 >= firstEntry || sectionOffset + entryOffset >= blobLength)
        {
            return true;
        }

        var start = sectionStart + entryOffset;
        var length = (int)Math.Min(MaximumNameLength, AddressKernelSectionOffsets - start);
        Span<byte> encoded = stackalloc byte[length];
        if (!addressSpace.TryRead(start, encoded))
        {
            return false;
        }

        var decoded = Ff7EncodedTextDecoder.DecodeKernelTerminated(encoded).Trim();
        text = decoded.Length == 0 || decoded.Contains('\uFFFD') ? null : decoded;
        return true;
    }
}
