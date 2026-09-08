using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>Which way the locked arms are leaning, as a cue selector.</summary>
public enum WonderSquareArmWrestlingPose
{
    None,
    Level,
    PushingAhead,
    BeingPushedBack,
    TheirArmDown,
    YourArmDown
}

/// <summary>
/// The visible state of the Arm Wrestling / Mega Sumo contest: whether a bout is
/// actually running, and which way the locked arms are leaning.
/// </summary>
public readonly record struct WonderSquareArmWrestlingState(
    bool IsActive,
    bool IsContesting,
    int Lean);

/// <summary>
/// Reads the Arm Wrestling contest's own state in games_1.
///
/// Evidence, installed flevel field 506 games_1, udel(11) [OK]:
///  - byte 14 shows dialogue 0, '"Arm Wrestling / Mega Sumo" 1 game 100 gil', then
///    the ordinary Try it / Not interested ASK and the gil check.
///  - byte 104 picks the opponent difficulty into Bank[5][19], byte 140 shows
///    "Push [OK] consecutively." and byte 156 shows "READY……".
///  - byte 162 spins until Bank[5][12] leaves zero: that is the go signal. Byte 182
///    clears it again and byte 186 seeds Bank[5][22] to 3, the level starting lean.
///  - The contest proper is the loop at bytes 190..298. Bank[5][20] counts presses
///    inside each twenty-frame window; passing Bank[5][19] increments Bank[5][22] at
///    byte 228 and failing decrements it at byte 237, and bytes 244..275 then run
///    Cloud's and the opponent's arm animations. So Bank[5][22] drives the displayed
///    lean. Bytes 278 and 288 set the finished flag Bank[5][21] at 6 and at 0, and
///    byte 302 branches those to the native "YOU WIN!!" and "YOU LOSE!" windows.
///
/// Bank ranges alone are not evidence of a bout. An idle room where the whole
/// temporary block reads as zeros satisfies "go cleared, not finished, lean within
/// travel", and the previous reader announced "Arms locked. Your arm is down." on
/// walking in. The contest is therefore gated on udel actually executing that [OK]
/// script, which is the only thing that seeds the lean or runs the animations.
/// </summary>
public sealed class WonderSquareArmWrestlingStateReader
{
    public const int ArmWrestlingFieldId = 506;
    public const int UdelEntityId = 11;

    /// <summary>udel's [OK] script, which owns the whole contest.</summary>
    public const int ContestScriptId = 1;

    public const int GoSignalOffset = 12;
    public const int LeanOffset = 22;
    public const int FinishedOffset = 21;

    /// <summary>The seed at byte 186: arms locked and level.</summary>
    public const int LevelLean = 3;

    /// <summary>Bytes 278 and 288: the two ends of the visible travel.</summary>
    public const int WinningLean = 6;
    public const int LosingLean = 0;

    private readonly ILegacyAddressSpace memory;
    private readonly FieldScriptControllerReader controller;

    public WonderSquareArmWrestlingStateReader(ILegacyAddressSpace memory)
        : this(memory, new FieldScriptControllerReader(memory))
    {
    }

    public WonderSquareArmWrestlingStateReader(
        ILegacyAddressSpace memory,
        FieldScriptControllerReader controller)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public string LastDiagnostic { get; private set; } = string.Empty;

    public void Reset() => LastDiagnostic = string.Empty;

    public bool TryRead(out WonderSquareArmWrestlingState state)
    {
        state = default;
        if (!memory.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module) ||
            module != FieldPositionReader.FieldModule)
        {
            LastDiagnostic = "not the field module";
            return false;
        }

        if (!memory.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var fieldId) ||
            fieldId != ArmWrestlingFieldId)
        {
            LastDiagnostic = $"field {fieldId} is not Wonder Square building one";
            return false;
        }

        if (!controller.TryRead(ArmWrestlingFieldId, UdelEntityId, ContestScriptId, out var udel))
        {
            LastDiagnostic = "arm wrestling controller state unreadable";
            return false;
        }

        if (!udel.IsControllerActive)
        {
            state = new WonderSquareArmWrestlingState(false, false, LevelLean);
            LastDiagnostic = "the arm wrestling machine is not running its contest";
            return true;
        }

        var bank = (uint)JunonMinigameStateReader.AddressTemporaryFieldBank;
        if (!memory.TryReadByte(bank + GoSignalOffset, out var go) ||
            !memory.TryReadByte(bank + LeanOffset, out var lean) ||
            !memory.TryReadByte(bank + FinishedOffset, out var finished))
        {
            LastDiagnostic = "arm wrestling temp bank unreadable";
            return false;
        }

        // Even with the contest script running, the lean is only meaningful after
        // byte 186 has seeded it; before that the script is still showing its price,
        // instruction and READY windows.
        var isContesting = go == 0 &&
                           finished == 0 &&
                           lean is >= LosingLean and <= WinningLean;
        state = new WonderSquareArmWrestlingState(
            IsActive: true,
            IsContesting: isContesting,
            Lean: lean);
        LastDiagnostic = $"arm wrestling go={go}, lean={lean}, finished={finished}";
        return true;
    }
}

/// <summary>
/// Describes the visible arm position during a bout. A sighted player watches the
/// locked arms lean one way or the other; this says which way, and nothing more. It
/// never presses the button, never reports a numeric meter, and leaves the native
/// "READY……", "YOU WIN!!" and "YOU LOSE!" windows to the ordinary message path.
///
/// The contest's own instruction is to press [OK] continuously, and every press
/// interrupts screen-reader speech, so the spoken phrase alone cannot be relied on
/// while playing. The pose is therefore also reported as a <see
/// cref="WonderSquareArmWrestlingPose"/> so the runtimes can sound a short tone on
/// their own device, which button input cannot cut off. The tone fires once per
/// revealed pose change, not on every poll of a held key.
/// </summary>
public sealed class WonderSquareArmWrestlingReadout
{
    /// <summary>
    /// Said once when the arms lock, so the tones are meaningful before they start.
    /// It describes what the tones mean, not an on-screen gauge - there is none.
    /// </summary>
    public const string ToneExplanation =
        "A higher tone means you are pushing their arm down, a lower tone means you are being pushed back, " +
        "and the middle tone means level.";

    private bool wasContesting;
    private WonderSquareArmWrestlingPose lastPose;

    public WonderSquareArmWrestlingPose LastPose => lastPose;

    public string? Observe(WonderSquareArmWrestlingState state) => Observe(state, out _);

    public string? Observe(WonderSquareArmWrestlingState state, out WonderSquareArmWrestlingPose pose)
    {
        pose = WonderSquareArmWrestlingPose.None;
        if (!state.IsContesting)
        {
            wasContesting = false;
            lastPose = WonderSquareArmWrestlingPose.None;
            // The native win and loss windows already say the outcome, so ending the
            // bout is silent rather than a second announcement.
            return null;
        }

        var current = Classify(state.Lean);
        if (!wasContesting)
        {
            wasContesting = true;
            lastPose = current;
            pose = current;
            return $"Arms locked. {Describe(current)} {ToneExplanation}";
        }

        if (current == lastPose)
        {
            return null;
        }

        lastPose = current;
        pose = current;
        return Describe(current);
    }

    public void Reset()
    {
        wasContesting = false;
        lastPose = WonderSquareArmWrestlingPose.None;
    }

    private static WonderSquareArmWrestlingPose Classify(int lean) => lean switch
    {
        <= WonderSquareArmWrestlingStateReader.LosingLean => WonderSquareArmWrestlingPose.YourArmDown,
        >= WonderSquareArmWrestlingStateReader.WinningLean => WonderSquareArmWrestlingPose.TheirArmDown,
        < WonderSquareArmWrestlingStateReader.LevelLean => WonderSquareArmWrestlingPose.BeingPushedBack,
        > WonderSquareArmWrestlingStateReader.LevelLean => WonderSquareArmWrestlingPose.PushingAhead,
        _ => WonderSquareArmWrestlingPose.Level
    };

    private static string Describe(WonderSquareArmWrestlingPose pose) => pose switch
    {
        WonderSquareArmWrestlingPose.YourArmDown => "Your arm is down.",
        WonderSquareArmWrestlingPose.TheirArmDown => "Their arm is down.",
        WonderSquareArmWrestlingPose.BeingPushedBack => "You are being pushed back.",
        WonderSquareArmWrestlingPose.PushingAhead => "You are pushing them back.",
        _ => "Holding level."
    };
}
