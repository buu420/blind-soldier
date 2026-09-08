using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public enum JunonMinigameKind
{
    None,
    Cpr,
    WelcomeParade,
    SendOff
}

public enum JunonSendOffCommand
{
    None,
    Ok,
    Menu,
    Switch,
    Cancel,
    LeftFace,
    RightFace
}

public readonly record struct JunonCprState(
    bool IsActive,
    bool IsBreathing,
    bool IsStopped,
    byte Gauge,
    byte TotalDelivered,
    byte OverfillCount);

public readonly record struct JunonWelcomeParadeState(
    bool IsActive,
    bool JoinedFormation,
    byte Rating,
    ushort FinalRating,
    bool HasFormationTarget,
    int PlayerX,
    int PlayerY,
    int FormationTargetX,
    int FormationTargetY,
    FieldNavigationControlTransform ControlTransform,
    uint HeldInput,
    byte UserControl,
    byte ActiveMessageCount,
    ushort MovieActive)
{
    public FieldPositionSnapshot NavigationPosition { get; init; }
    public bool IsNowPromptVisible { get; init; }
    public byte NowPromptSequence { get; init; }
}

public readonly record struct JunonSendOffState(
    bool IsActive,
    ushort Sequence,
    JunonSendOffCommand Command,
    byte MoodPercent,
    bool IsSpecialPose);

public readonly record struct JunonMinigameSnapshot(
    JunonMinigameKind Kind,
    JunonCprState Cpr,
    JunonWelcomeParadeState Parade,
    JunonSendOffState SendOff)
{
    public static JunonMinigameSnapshot Inactive { get; } =
        new(JunonMinigameKind.None, default, default, default);

    public static JunonMinigameSnapshot FromCpr(JunonCprState state) =>
        new(JunonMinigameKind.Cpr, state, default, default);

    public static JunonMinigameSnapshot FromWelcomeParade(JunonWelcomeParadeState state) =>
        new(JunonMinigameKind.WelcomeParade, default, state, default);

    public static JunonMinigameSnapshot FromSendOff(JunonSendOffState state) =>
        new(JunonMinigameKind.SendOff, default, default, state);
}

/// <summary>
/// Reads the native temporary-bank state that drives Junon's three sighted-only
/// minigames. Field-script bank 5 and bank 6 both resolve to the byte array at
/// <see cref="AddressTemporaryFieldBank"/> in the x86 interpreter; the x64
/// runtime reaches the same guest addresses through <see cref="ILegacyAddressSpace"/>.
/// </summary>
public sealed class JunonMinigameStateReader
{
    public const byte FieldModule = 1;
    public const ushort CprFieldId = 434;
    public const ushort WelcomeParadeFieldId = 363;
    public const ushort SendOffFieldId = 382;

    public const int AddressCurrentModule = FieldPositionReader.AddressCurrentModule;
    public const int AddressCurrentFieldId = FieldPositionReader.AddressFieldId;
    public const int AddressTemporaryFieldBank = 0x00CC14D0;
    public const int AddressCurrentEntityScriptId = FieldScriptContextReader.AddressCurrentEntityScriptId;
    public const int AddressCurrentEntityScriptPriority = FieldScriptContextReader.AddressCurrentEntityScriptPriority;
    public const int ScriptSlotsPerEntity = FieldScriptContextReader.ScriptSlotsPerEntity;
    public const int TemporaryBankCaptureLength = 64;
    public const int SendOffCaptainEntityId = 20;
    public const byte SendOffPromptPriority = 6;

    private const int CprPhaseOffset = 20;
    private const int CprOverfillOffset = 21;
    private const int CprFlagsOffset = 22;
    private const int CprGaugeOffset = 23;
    private const int CprTotalOffset = 24;
    private const byte CprBreathingMask = 0x01;
    private const byte CprStoppedMask = 0x02;

    private const int ParadeActiveOffset = 28;
    // border1/Go increments this byte and substitutes it into the visible Now window.
    private const int ParadeNowSequenceOffset = 25;
    private const int ParadeRatingOffset = 29;
    private const int ParadeJoinedOffset = 35;
    private const int ParadeFinalRatingOffset = 36;
    // The native window owner bytes are entity ids, not animation states.
    // Window 0 belongs to border1 (12), window 2 to count2 (10), and the
    // remaining two windows are free (255). Only border1/Go opens Now.
    private const uint ParadeNowWindowOwners = 0xFF0AFF0C;
    private const int ParadeFormationAnchorEntityId = 27;
    private const int ParadeFormationTopY = -427;
    private const int ParadeFormationBottomY = -815;
    // hei7/5 and hei8/5 occupy the upper and middle ranks. The native
    // hei9/6 demonstration approaches the lower opening via Y=-759.
    private const int ParadeFormationEntryY = -759;
    private const int ParadeFormationBottomXOffset = -32;

    private const int SendOffMoodOffset = 29;
    private const int SendOffSpecialOffset = 31;
    private const int SendOffSequenceOffset = 27;
    private const ushort SendOffFirstSequence = 16;
    private const ushort SendOffSpecialSequence = 48;

    private readonly ILegacyAddressSpace memory;
    private readonly FieldPositionReader positionReader;
    private readonly FieldNavigationControlReader controlReader;

    public JunonMinigameStateReader(ILegacyAddressSpace memory)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        positionReader = new FieldPositionReader(memory);
        controlReader = new FieldNavigationControlReader(memory);
    }

    public bool TryRead(out JunonMinigameSnapshot snapshot)
    {
        snapshot = default;
        if (!TryCapture(out var before))
        {
            return false;
        }

        FormationGuidance formation = default;
        if (before.Module == FieldModule &&
            before.FieldId == WelcomeParadeFieldId &&
            before.Temporary[ParadeActiveOffset] != 0)
        {
            _ = TryReadFormationGuidance(out formation);
        }

        if (!TryCapture(out var after) || !before.Matches(after))
        {
            return false;
        }

        if (before.Module != FieldModule)
        {
            snapshot = JunonMinigameSnapshot.Inactive;
            return true;
        }

        snapshot = before.FieldId switch
        {
            CprFieldId => ReadCpr(before.Temporary),
            WelcomeParadeFieldId => ReadWelcomeParade(before, formation),
            SendOffFieldId => ReadSendOff(
                before.Temporary,
                ResolveSendOffCommand(before.SendOffPriority, before.SendOffScriptId)),
            _ => JunonMinigameSnapshot.Inactive
        };
        return true;
    }

    private bool TryCapture(out NativeCapture capture)
    {
        capture = default;
        if (!memory.TryReadByte((uint)AddressCurrentModule, out var module) ||
            !memory.TryReadUInt16((uint)AddressCurrentFieldId, out var fieldId))
        {
            return false;
        }

        var temporary = new byte[TemporaryBankCaptureLength];
        if (module == FieldModule &&
            fieldId is CprFieldId or WelcomeParadeFieldId or SendOffFieldId &&
            !memory.TryRead((uint)AddressTemporaryFieldBank, temporary))
        {
            return false;
        }

        byte sendOffPriority = 0;
        byte sendOffScriptId = 0;
        if (module == FieldModule && fieldId == SendOffFieldId)
        {
            if (!memory.TryReadByte(
                    (uint)(AddressCurrentEntityScriptPriority + SendOffCaptainEntityId),
                    out sendOffPriority) ||
                sendOffPriority >= ScriptSlotsPerEntity ||
                !memory.TryReadByte(
                    (uint)(AddressCurrentEntityScriptId +
                        SendOffCaptainEntityId * ScriptSlotsPerEntity +
                        sendOffPriority),
                    out sendOffScriptId))
            {
                return false;
            }
        }

        uint heldInput = 0;
        byte userControl = 0;
        byte activeMessageCount = 0;
        uint windowOwners = uint.MaxValue;
        ushort movieActive = 0;
        if (module == FieldModule &&
            fieldId == WelcomeParadeFieldId &&
            temporary[ParadeActiveOffset] != 0 &&
            (!memory.TryReadUInt32(
                    (uint)FieldNavigationInputReader.AddressCurrentKeyInput,
                    out heldInput) ||
             !memory.TryReadByte(
                    (uint)FieldAudibleCueStateReader.AddressUserControl,
                    out userControl) ||
             !memory.TryReadByte(
                    (uint)FieldAudibleCueStateReader.AddressActiveFieldMessageCount,
                    out activeMessageCount) ||
             !memory.TryReadUInt16(
                    (uint)FieldAudibleCueStateReader.AddressFieldMovieActive,
                    out movieActive)))
        {
            return false;
        }

        if (module == FieldModule && fieldId == WelcomeParadeFieldId &&
            temporary[ParadeActiveOffset] != 0 && activeMessageCount > 1 &&
            !memory.TryReadUInt32((uint)FieldMessageReader.AddressFieldWindowStates, out windowOwners))
        {
            return false;
        }

        capture = new NativeCapture(
            module,
            fieldId,
            temporary,
            sendOffPriority,
            sendOffScriptId,
            heldInput,
            userControl,
            activeMessageCount,
            windowOwners,
            movieActive);
        return true;
    }

    private static JunonMinigameSnapshot ReadCpr(byte[] temporary)
    {
        var active = temporary[CprPhaseOffset] != 0;
        if (!active)
        {
            return JunonMinigameSnapshot.Inactive;
        }

        var flags = temporary[CprFlagsOffset];
        return JunonMinigameSnapshot.FromCpr(new JunonCprState(
            IsActive: true,
            IsBreathing: (flags & CprBreathingMask) != 0,
            IsStopped: (flags & CprStoppedMask) != 0,
            Gauge: temporary[CprGaugeOffset],
            TotalDelivered: temporary[CprTotalOffset],
            OverfillCount: temporary[CprOverfillOffset]));
    }

    private static JunonMinigameSnapshot ReadWelcomeParade(
        NativeCapture capture,
        FormationGuidance formation)
    {
        var temporary = capture.Temporary;
        var active = temporary[ParadeActiveOffset] != 0;
        if (!active)
        {
            return JunonMinigameSnapshot.Inactive;
        }

        return JunonMinigameSnapshot.FromWelcomeParade(new JunonWelcomeParadeState(
            IsActive: true,
            JoinedFormation: temporary[ParadeJoinedOffset] != 0,
            Rating: temporary[ParadeRatingOffset],
            FinalRating: BinaryPrimitives.ReadUInt16LittleEndian(
                temporary.AsSpan(ParadeFinalRatingOffset, sizeof(ushort))),
            HasFormationTarget: formation.IsUsable,
            PlayerX: formation.PlayerX,
            PlayerY: formation.PlayerY,
            FormationTargetX: formation.TargetX,
            FormationTargetY: formation.TargetY,
            ControlTransform: formation.ControlTransform,
            HeldInput: capture.HeldInput,
            UserControl: capture.UserControl,
            ActiveMessageCount: capture.ActiveMessageCount,
            MovieActive: capture.MovieActive)
        {
            NavigationPosition = formation.Position,
            NowPromptSequence = temporary[ParadeNowSequenceOffset],
            IsNowPromptVisible = temporary[ParadeJoinedOffset] != 0 &&
                capture.ActiveMessageCount == 2 && capture.WindowOwners == ParadeNowWindowOwners
        });
    }

    private static JunonMinigameSnapshot ReadSendOff(
        byte[] temporary,
        JunonSendOffCommand command)
    {
        var sequence = BinaryPrimitives.ReadUInt16LittleEndian(
            temporary.AsSpan(SendOffSequenceOffset, sizeof(ushort)));
        var special = temporary[SendOffSpecialOffset] != 0;
        var active = sequence is >= SendOffFirstSequence and < SendOffSpecialSequence || special;
        if (!active)
        {
            return JunonMinigameSnapshot.Inactive;
        }

        return JunonMinigameSnapshot.FromSendOff(new JunonSendOffState(
            IsActive: true,
            Sequence: sequence,
            Command: command,
            MoodPercent: temporary[SendOffMoodOffset],
            IsSpecialPose: special));
    }

    private static JunonSendOffCommand ResolveSendOffCommand(byte priority, byte scriptId)
    {
        if (priority != SendOffPromptPriority)
        {
            return JunonSendOffCommand.None;
        }

        return scriptId switch
        {
            6 => JunonSendOffCommand.Ok,
            7 => JunonSendOffCommand.Switch,
            8 => JunonSendOffCommand.Menu,
            9 => JunonSendOffCommand.Cancel,
            10 => JunonSendOffCommand.LeftFace,
            11 => JunonSendOffCommand.RightFace,
            _ => JunonSendOffCommand.None
        };
    }

    private bool TryReadFormationGuidance(out FormationGuidance guidance)
    {
        guidance = default;
        var positionResult = positionReader.ReadNavigation();
        if (!positionResult.IsUsable ||
            positionResult.Position.FieldId != WelcomeParadeFieldId)
        {
            return false;
        }

        var control = controlReader.Read(positionResult.Position);
        if (!control.IsUsable ||
            !TryReadEntityPosition(ParadeFormationAnchorEntityId, out var firstAnchor) ||
            !TryReadEntityPosition(ParadeFormationAnchorEntityId, out var secondAnchor) ||
            firstAnchor != secondAnchor)
        {
            return false;
        }

        var bottomX = ApplyNativeClampedByteSubtract(
            firstAnchor.X, -ParadeFormationBottomXOffset);
        var entryAmount = (ParadeFormationEntryY - ParadeFormationTopY) /
            (double)(ParadeFormationBottomY - ParadeFormationTopY);
        // FUN_00637879 rejects points whose projection is past an endpoint.
        // Keep the target in the open lower interior even when Cloud is below
        // the line, rather than choosing its nearest endpoint.
        var target = new Point(
            (int)Math.Round(firstAnchor.X + entryAmount * (bottomX - firstAnchor.X),
                MidpointRounding.AwayFromZero),
            ParadeFormationEntryY);
        guidance = new FormationGuidance(
            true,
            positionResult.Position.X,
            positionResult.Position.Y,
            target.X,
            target.Y,
            control.Transform,
            positionResult.Position);
        return true;
    }

    private bool TryReadEntityPosition(int entityId, out Point position)
    {
        position = default;
        if (!memory.TryReadByte((uint)FieldPositionReader.AddressFieldNumModels, out var modelCount) ||
            !memory.TryReadByte(
                (uint)(FieldNavigationObjectReader.AddressFieldModelIdArray + entityId),
                out var modelId) ||
            !memory.TryReadUInt32(
                (uint)FieldNavigationObjectReader.AddressFieldEventDataPtr,
                out var eventTable) ||
            eventTable == 0 ||
            modelId == byte.MaxValue ||
            modelId >= modelCount)
        {
            return false;
        }

        uint eventAddress;
        try
        {
            eventAddress = checked(eventTable + (uint)(modelId * FieldNavigationObjectReader.FieldEventDataStride));
        }
        catch (OverflowException)
        {
            return false;
        }

        if (!memory.TryReadInt32(
                eventAddress + FieldNavigationObjectReader.PositionXOffset,
                out var x) ||
            !memory.TryReadInt32(
                eventAddress + FieldNavigationObjectReader.PositionYOffset,
                out var y))
        {
            return false;
        }

        position = new Point(x >> 12, y >> 12);
        return true;
    }

    private static int ApplyNativeClampedByteSubtract(int value, int amount)
    {
        // junonr4 uses field opcode 0x78 (MINUS!), not the 16-bit subtraction
        // opcode. FFVII reads and writes only the low byte, clamps that byte at
        // zero, and leaves the high byte intact before SLINE consumes the word.
        var raw = unchecked((ushort)(short)value);
        var low = Math.Max((raw & 0xFF) - amount, 0);
        return unchecked((short)((raw & 0xFF00) | low));
    }

    private readonly record struct Point(int X, int Y);

    private readonly record struct FormationGuidance(
        bool IsUsable,
        int PlayerX,
        int PlayerY,
        int TargetX,
        int TargetY,
        FieldNavigationControlTransform ControlTransform,
        FieldPositionSnapshot Position);

    private readonly record struct NativeCapture(
        byte Module,
        ushort FieldId,
        byte[] Temporary,
        byte SendOffPriority,
        byte SendOffScriptId,
        uint HeldInput,
        byte UserControl,
        byte ActiveMessageCount,
        uint WindowOwners,
        ushort MovieActive)
    {
        public bool Matches(NativeCapture other) =>
            Module == other.Module &&
            FieldId == other.FieldId &&
            SendOffPriority == other.SendOffPriority &&
            SendOffScriptId == other.SendOffScriptId &&
            HeldInput == other.HeldInput &&
            UserControl == other.UserControl &&
            ActiveMessageCount == other.ActiveMessageCount &&
            WindowOwners == other.WindowOwners &&
            MovieActive == other.MovieActive &&
            Temporary.AsSpan().SequenceEqual(other.Temporary);
    }
}

public readonly record struct JunonMinigameSpeechCue(string Text, bool Interrupt = true);

public sealed class JunonMinigameSpeechTracker
{
    public const int RatingAnnouncementThreshold = 3;
    private static readonly IReadOnlyList<JunonMinigameSpeechCue> NoCues =
        Array.Empty<JunonMinigameSpeechCue>();

    private JunonMinigameKind currentKind;
    private JunonMinigameSnapshot previous;
    private bool hasPrevious;
    private int lastSpokenParadeRating;
    private string lastFormationGuidance = string.Empty;
    private bool lastParadeAlignmentAssistActive;

    public IReadOnlyList<JunonMinigameSpeechCue> Observe(
        JunonMinigameSnapshot snapshot,
        bool paradeAlignmentAssistActive = false)
    {
        if (snapshot.Kind == JunonMinigameKind.None)
        {
            Reset();
            return NoCues;
        }

        var entered = !hasPrevious || currentKind != snapshot.Kind;
        if (entered)
        {
            Reset();
            currentKind = snapshot.Kind;
        }

        var text = snapshot.Kind switch
        {
            JunonMinigameKind.Cpr => ObserveCpr(snapshot.Cpr, entered),
            JunonMinigameKind.WelcomeParade => ObserveWelcomeParade(
                snapshot.Parade,
                entered,
                paradeAlignmentAssistActive),
            JunonMinigameKind.SendOff => ObserveSendOff(snapshot.SendOff, entered),
            _ => null
        };

        previous = snapshot;
        currentKind = snapshot.Kind;
        hasPrevious = true;
        lastParadeAlignmentAssistActive =
            snapshot.Kind == JunonMinigameKind.WelcomeParade &&
            paradeAlignmentAssistActive;
        return string.IsNullOrWhiteSpace(text)
            ? NoCues
            : [new JunonMinigameSpeechCue(text, ShouldInterrupt(snapshot.Kind, entered, text))];
    }

    public void Reset()
    {
        currentKind = JunonMinigameKind.None;
        previous = default;
        hasPrevious = false;
        lastSpokenParadeRating = 0;
        lastFormationGuidance = string.Empty;
        lastParadeAlignmentAssistActive = false;
    }

    private string? ObserveCpr(JunonCprState current, bool entered)
    {
        var parts = new List<string>();
        if (entered)
        {
            parts.Add(current.IsBreathing
                ? $"Breathing. Gauge {Math.Min(current.Gauge, (byte)10)} of 10."
                : "CPR gauge ready. Press Switch to start breathing.");
            return string.Join(' ', parts);
        }

        var prior = previous.Cpr;
        if (!prior.IsBreathing && current.IsBreathing)
        {
            parts.Add("Breathing.");
        }

        if (prior.Gauge < 10 && current.Gauge >= 10)
        {
            parts.Add("Full.");
        }

        if (current.OverfillCount > prior.OverfillCount)
        {
            parts.Clear();
            parts.Add("Overfilled. Start again.");
        }

        if (current.TotalDelivered > prior.TotalDelivered)
        {
            parts.Clear();
            parts.Add(current.TotalDelivered > 40
                ? $"CPR complete. Total {current.TotalDelivered} of 41."
                : $"Breath delivered. Total {current.TotalDelivered} of 41.");
        }

        return parts.Count == 0 ? null : string.Join(' ', parts);
    }

    private string? ObserveWelcomeParade(
        JunonWelcomeParadeState current,
        bool entered,
        bool alignmentAssistActive)
    {
        var parts = new List<string>();
        var guidance = DescribeFormationGuidance(current);
        var assistModeChanged =
            hasPrevious &&
            lastParadeAlignmentAssistActive != alignmentAssistActive;
        if (entered)
        {
            if (alignmentAssistActive)
            {
                parts.Add(
                    "Welcome parade. Alignment assist on. " +
                    "Hold Run while moving into position. " +
                    "Press OK when you hear Now or the timing tone.");
            }
            else if (current.JoinedFormation)
            {
                parts.Add("Welcome parade. In formation. Match the soldiers and press OK when you hear Now or the timing tone.");
            }
            else if (!string.IsNullOrWhiteSpace(guidance))
            {
                parts.Add($"Welcome parade. Move into the open place {guidance}.");
            }
            else
            {
                parts.Add("Welcome parade. Move into the open place in the formation.");
            }

            parts.Add($"Live TV rating, {current.Rating} percent.");
            lastSpokenParadeRating = current.Rating;
            lastFormationGuidance = guidance;
            return string.Join(' ', parts);
        }

        var prior = previous.Parade;
        if (!prior.JoinedFormation && current.JoinedFormation)
        {
            parts.Add(alignmentAssistActive
                ? "In formation. Keep pressing OK when you hear Now or the timing tone."
                : "In formation. Match the soldiers and press OK when you hear Now or the timing tone.");
        }
        else if (!alignmentAssistActive &&
                 !current.JoinedFormation &&
                 !string.IsNullOrWhiteSpace(guidance) &&
                 (assistModeChanged ||
                  !string.Equals(guidance, lastFormationGuidance, StringComparison.Ordinal)))
        {
            parts.Add($"Move into the open place {guidance}.");
            lastFormationGuidance = guidance;
        }

        if (current.FinalRating != 0 && current.FinalRating != prior.FinalRating)
        {
            parts.Add($"Final live TV rating, {current.FinalRating} percent.");
            lastSpokenParadeRating = current.FinalRating;
        }
        else if (Math.Abs(current.Rating - lastSpokenParadeRating) >= RatingAnnouncementThreshold)
        {
            parts.Add($"Live TV rating, {current.Rating} percent.");
            lastSpokenParadeRating = current.Rating;
        }

        return parts.Count == 0 ? null : string.Join(' ', parts);
    }

    private string? ObserveSendOff(JunonSendOffState current, bool entered)
    {
        var parts = new List<string>();
        if (entered)
        {
            parts.Add("Junon send-off. Match each command.");
            parts.Add($"President's mood, {current.MoodPercent} percent.");
            return string.Join(' ', parts);
        }

        var prior = previous.SendOff;
        if (current.IsSpecialPose && !prior.IsSpecialPose)
        {
            parts.Add("Special pose.");
        }

        if (current.MoodPercent != prior.MoodPercent)
        {
            parts.Add($"President's mood, {current.MoodPercent} percent.");
        }

        return parts.Count == 0 ? null : string.Join(' ', parts);
    }

    // FFVII displays the parade's Now command and every send-off command in
    // ordinary field-message windows. The existing message reader owns those
    // time-critical prompts. Numeric gauges remain useful, but must queue
    // behind the prompt instead of interrupting it.
    private static bool ShouldInterrupt(
        JunonMinigameKind kind,
        bool entered,
        string text) =>
        entered || kind switch
        {
            JunonMinigameKind.WelcomeParade =>
                !text.StartsWith("Live TV rating,", StringComparison.Ordinal) &&
                !text.StartsWith("Final live TV rating,", StringComparison.Ordinal),
            JunonMinigameKind.SendOff =>
                !text.StartsWith("President's mood,", StringComparison.Ordinal),
            _ => true
        };

    private static string DescribeFormationGuidance(JunonWelcomeParadeState state) =>
        state.HasFormationTarget
            ? FieldNavigationSpokenCueFormatter.Format(
                state.FormationTargetX - state.PlayerX,
                state.FormationTargetY - state.PlayerY,
                state.ControlTransform)
            : string.Empty;
}

public sealed class JunonMinigameCueCoordinator
{
    private static readonly IReadOnlyList<JunonMinigameSpeechCue> NoCues =
        Array.Empty<JunonMinigameSpeechCue>();

    private readonly JunonMinigameStateReader reader;
    private readonly JunonMinigameSpeechTracker tracker;

    public JunonMinigameCueCoordinator(
        JunonMinigameStateReader reader,
        JunonMinigameSpeechTracker? tracker = null)
    {
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
        this.tracker = tracker ?? new JunonMinigameSpeechTracker();
    }

    public IReadOnlyList<JunonMinigameSpeechCue> Observe() =>
        reader.TryRead(out var snapshot)
            ? tracker.Observe(snapshot)
            : NoCues;

    public bool TryRead(out JunonMinigameSnapshot snapshot) => reader.TryRead(out snapshot);

    public IReadOnlyList<JunonMinigameSpeechCue> ObserveSnapshot(
        JunonMinigameSnapshot snapshot,
        bool paradeAlignmentAssistActive) =>
        tracker.Observe(snapshot, paradeAlignmentAssistActive);

    public void Reset() => tracker.Reset();
}
