using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Moves the Fort Condor battle cursor to a point, on either runtime.
/// </summary>
/// <remarks>
/// <para>Shared rather than written once per runtime on purpose. The first version
/// of the jump existed on x86 only and the x64 runtime was left announcing that it
/// could not move - which is exactly the split this mod is supposed to refuse. One
/// implementation, handed a writer by whichever host it is running under, cannot
/// drift apart the way two would.</para>
///
/// <para>Cursor X and Y are adjacent 16-bit values at
/// <see cref="CondorBattleStateReader.AddressCursor"/>, so both are replaced by a
/// single 32-bit write and the battle never observes a cursor that has moved in one
/// axis only. Guest address 0x00CBCCC0 has page offset 0xCC0, four-byte aligned,
/// with all four bytes inside the one page.</para>
///
/// <para>Every coordinate this is asked for came from the game's own reading of a
/// unit, so the bounds check is here to catch a corrupt read rather than to
/// second-guess the battle. The real guarantee is the read-back the writer
/// performs: the caller is told whether the cursor actually moved, and says so out
/// loud when it did not.</para>
/// </remarks>
public sealed class CondorCursorMover
{
    /// <summary>
    /// Well past any legitimate battlefield coordinate, and comfortably inside the
    /// 16 bits each axis is stored in. A value outside this is a corrupt reading,
    /// not a place.
    /// </summary>
    private const int CoordinateLimit = 4095;

    private readonly ILegacyMemoryWriter? writer;
    private readonly Action<string> log;

    /// <param name="writer">
    /// The host's write capability, or null on a host that has none - in which case
    /// every move is refused rather than silently doing nothing.
    /// </param>
    public CondorCursorMover(ILegacyMemoryWriter? writer, Action<string>? log = null)
    {
        this.writer = writer;
        this.log = log ?? (_ => { });
    }

    /// <summary>Whether this host can move the cursor at all.</summary>
    public bool IsAvailable => writer is not null;

    /// <summary>
    /// Refuses every move, deliberately, and says why.
    /// </summary>
    /// <remarks>
    /// <para><b>Writing this global alone is wrong and this method must not do it.</b>
    /// The battle's cursor is camera-relative: <c>FUN_005FE91B</c> derives it as
    /// <c>cursor - camera</c>, clamps the <em>relative</em> value, writes back
    /// <c>relative + camera</c>, and advances the camera origin
    /// (<c>0x00C60B00/04</c>) and the scroll accumulators (<c>0x00C74C38/3C</c>) in
    /// lockstep. Nothing clamps the world global. A value stored here is carried
    /// straight through and never brought back, so the cursor lands outside the
    /// visible window while the view stays put - a state no sighted player can
    /// produce or see, which is exactly what this mod exists not to do.</para>
    ///
    /// <para>Worse than cosmetic: the cursor <em>is</em> the hire position
    /// (<c>FUN_00604009</c> to <c>FUN_00607123</c> copies it into the new unit's
    /// +0x48/+0x4A), so a teleport followed by a purchase spends real gil on a unit
    /// placed off the field, announced only as "Placed."</para>
    ///
    /// <para>The right way to move the cursor is to press the game's own direction
    /// keys through the shared SendInput boundary and let
    /// <c>FUN_005FE771 → FUN_005FE8CF → FUN_005FE91B</c> move camera, accumulators
    /// and cursor together - correct by construction because the game does it.
    /// Until that lands, this refuses, and the navigator says so out loud rather
    /// than reporting a move that did not happen.</para>
    ///
    /// <para>The write plumbing beneath this - <see cref="ILegacyMemoryWriter"/>,
    /// the vetted atomic exchange, and the translated page-table resolution - is
    /// sound and stays. What was wrong was never <em>how</em> it wrote, but
    /// <em>what</em>.</para>
    /// </remarks>
    public bool TryMoveTo(int x, int y)
    {
        log(
            $"Fort Condor cursor: refused a direct move to {x}, {y}. The cursor is " +
            "camera-relative and writing it alone would scroll the view off the " +
            "player's position; steering through the game's own input is required.");
        return false;
    }
}
