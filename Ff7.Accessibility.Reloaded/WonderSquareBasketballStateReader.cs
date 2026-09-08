using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// The visible state of the Wonder Square Basketball Game's wind-up, taken from
/// Cloud's own model rather than from a counter.
/// </summary>
/// <param name="IsShotRunning">
/// Cloud is executing the shot script. This is the controller identity, not a
/// guess from bank values that every script in the room shares.
/// </param>
/// <param name="IsRising">The rise segment is playing and has not reached its end.</param>
/// <param name="HasSettled">The rise segment has reached its last frame and is held.</param>
/// <param name="HasThrown">The throw segment is playing.</param>
/// <param name="Score">Bank[6][1], the game's running GP.</param>
public readonly record struct WonderSquareBasketballState(
    bool IsShotRunning,
    bool IsRising,
    bool HasSettled,
    bool HasThrown,
    int Score);

/// <summary>
/// Reads what the Basketball Game is actually showing.
///
/// Evidence, installed flevel field 506 games_1:
///  - bsl(16) script 4 "Go" shows dialogue 21, "Basketball Game 1 game 200 gil ...
///    Hold down [OK] to gain strength. Release [OK] to shoot.", takes the gil and at
///    byte 93 requests cloud(1) script 10, which is the shot itself. cloud(1)
///    script 10 is therefore the controller, and this reader will not report a shot
///    unless entity 1 is running it.
///  - Inside that script the visible wind-up is one <c>CANM!2</c> of animation 12,
///    frames 8..16, at byte 75, started in the same frame as ball(13) script 5's
///    single <c>OFST</c> that raises the ball by 112 at speed 8. Bytes 56 and 88 are
///    the other two segments of the same animation: frames 0..8 before the button,
///    and frames 16..49 for the throw.
///
/// The three segments share one animation id and are told apart by their end frame,
/// which the native <c>CANM!2</c> handler FUN_00614E3E writes to model + 0x6A while
/// the current frame at + 0x68 counts up in frame &lt;&lt; 4 units. So "the ball has
/// finished rising" is read directly off the displayed model: no assumption about
/// how often a helper script's loop runs, and no comparison against the script's own
/// hidden success value, which is never read here at all.
/// </summary>
public sealed class WonderSquareBasketballStateReader
{
    public const int BasketballFieldId = 506;
    public const int CloudEntityId = 1;
    public const int ShotScriptId = 10;

    /// <summary>The shared animation; the segment is identified by its end frame.</summary>
    public const int ShotAnimationId = 12;

    /// <summary>cloud(1) script 10 byte 75: CANM!2 animation 12, frames 8..16.</summary>
    public const int RiseEndFrame = 16;

    /// <summary>cloud(1) script 10 byte 88: CANM!1 animation 12, frames 16..49.</summary>
    public const int ThrowEndFrame = 49;

    /// <summary>Bank[6][1], the game's running GP, as a word in the temp block.</summary>
    public const int ScoreOffset = 1;

    private readonly ILegacyAddressSpace memory;
    private readonly FieldScriptControllerReader controller;

    public WonderSquareBasketballStateReader(ILegacyAddressSpace memory)
        : this(memory, new FieldScriptControllerReader(memory))
    {
    }

    public WonderSquareBasketballStateReader(
        ILegacyAddressSpace memory,
        FieldScriptControllerReader controller)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public string LastDiagnostic { get; private set; } = string.Empty;

    public void Reset() => LastDiagnostic = string.Empty;

    public bool TryRead(out WonderSquareBasketballState state)
    {
        state = default;
        if (!memory.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module) ||
            module != FieldPositionReader.FieldModule)
        {
            LastDiagnostic = "not the field module";
            return false;
        }

        if (!memory.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var fieldId) ||
            fieldId != BasketballFieldId)
        {
            LastDiagnostic = $"field {fieldId} is not Wonder Square building one";
            return false;
        }

        if (!controller.TryRead(BasketballFieldId, CloudEntityId, ShotScriptId, out var cloud))
        {
            LastDiagnostic = "basketball controller state unreadable";
            return false;
        }

        if (!cloud.IsControllerActive || !cloud.HasModel)
        {
            state = new WonderSquareBasketballState(false, false, false, false, 0);
            LastDiagnostic = "no shot script is running on Cloud";
            return true;
        }

        // Banks 5 and 6 are the same temp block: the odd bank addresses bytes and the
        // even one addresses words at the same offsets, so Bank[6][1] is the 16-bit
        // slot at offset 1. Only read once the shot script owns the room.
        if (!memory.TryReadUInt16(
                (uint)(JunonMinigameStateReader.AddressTemporaryFieldBank + ScoreOffset), out var score))
        {
            LastDiagnostic = "basketball score unreadable";
            return false;
        }

        var rising = cloud.IsPlayingSegment(ShotAnimationId, RiseEndFrame);
        var settled = cloud.HasSegmentSettled(ShotAnimationId, RiseEndFrame);
        state = new WonderSquareBasketballState(
            IsShotRunning: true,
            IsRising: rising && !settled,
            HasSettled: settled,
            HasThrown: cloud.IsPlayingSegment(ShotAnimationId, ThrowEndFrame),
            Score: score);
        LastDiagnostic =
            $"basketball anim={cloud.AnimationId}, frame={cloud.CurrentFrameFixed}>>4 of {cloud.EndFrameIndex}, " +
            $"runState={cloud.AnimationRunState}, score={score}";
        return true;
    }
}
