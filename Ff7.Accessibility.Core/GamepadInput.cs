namespace Ff7.Accessibility.Core;

/// <summary>
/// The controller buttons this mod can read, using XInput's own
/// <c>XINPUT_GAMEPAD_*</c> bit values so a snapshot is the wButtons word with a
/// name rather than a second mapping that could drift from it.
/// </summary>
[Flags]
public enum GamepadButton
{
    None = 0,
    DPadUp = 0x0001,
    DPadDown = 0x0002,
    DPadLeft = 0x0004,
    DPadRight = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LeftThumb = 0x0040,

    /// <summary>R3 - the right stick pressed in. Opens and closes the menu.</summary>
    RightThumb = 0x0080,
    LeftShoulder = 0x0100,
    RightShoulder = 0x0200,

    /// <summary>A on Xbox, Cross on PlayStation.</summary>
    A = 0x1000,

    /// <summary>B on Xbox, Circle on PlayStation.</summary>
    B = 0x2000,

    /// <summary>X on Xbox, Square on PlayStation.</summary>
    X = 0x4000,

    /// <summary>Y on Xbox, Triangle on PlayStation.</summary>
    Y = 0x8000,
}

/// <summary>
/// One poll of one controller.
/// </summary>
/// <param name="IsConnected">
/// False means no pad answered on the latched slot. It is not the same as "no
/// buttons": a disconnect has to close the menu and let go of the game's input,
/// whereas an empty button word is just nobody pressing anything.
/// </param>
/// <param name="UserIndex">Which XInput slot this came from, or -1 when none.</param>
/// <param name="PacketNumber">
/// XInput's own change counter. Equal packet numbers mean the pad state has not
/// changed since the last poll, which is how a held button is told from a new one
/// without trusting our own polling rate.
/// </param>
public readonly record struct GamepadSnapshot(
    bool IsConnected,
    int UserIndex,
    uint PacketNumber,
    GamepadButton Buttons)
{
    public static GamepadSnapshot Disconnected => new(false, -1, 0, GamepadButton.None);

    public bool IsDown(GamepadButton button) => (Buttons & button) == button && button != GamepadButton.None;
}

/// <summary>Polls one controller. Implemented over XInput; faked in tests.</summary>
public interface IGamepadReader
{
    /// <summary>
    /// The pad this reader has latched onto, or -1. Latching matters because a
    /// second pad powering on mid-game must not silently become the one the menu
    /// listens to - the player would press a button and nothing would happen.
    /// </summary>
    int ActiveUserIndex { get; }

    GamepadSnapshot Poll();
}

/// <summary>
/// A reader that never reports a pad, for the menu when its snapshots come from an
/// input hook instead. The polling overload is then never the one that runs, and
/// this makes that explicit rather than leaving a live reader nobody calls.
/// </summary>
public sealed class EmptyGamepadReader : IGamepadReader
{
    public static EmptyGamepadReader Instance { get; } = new();

    public int ActiveUserIndex => -1;

    public GamepadSnapshot Poll() => GamepadSnapshot.Disconnected;
}

/// <summary>
/// Turns held button state into commands: one press is one command, a button held
/// across a focus change or a reconnect is not a press at all, and a direction held
/// down repeats at a bounded rate rather than once per frame.
///
/// <para>The rule about holding is the same one
/// <see cref="NavigationKeyPressTracker"/> keeps for the keyboard, and for the same
/// reason. A player who is holding A when the window comes back, or who had a
/// direction down when the pad reconnected, has not asked for anything; firing there
/// puts the party somewhere they did not choose.</para>
/// </summary>
public sealed class GamepadButtonEdgeTracker
{
    /// <summary>How long a direction is held before it starts repeating.</summary>
    public static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(600);

    /// <summary>
    /// How often it repeats after that: about three a second. Deliberately slower than
    /// a key repeat, because each step is a spoken sentence and one that arrives before
    /// the last has finished is a list nobody can follow.
    /// </summary>
    public static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(320);

    private readonly TimeSpan repeatDelay;
    private readonly TimeSpan repeatInterval;
    private readonly Dictionary<GamepadButton, DateTime> nextRepeatUtc = new();

    private GamepadButton held;
    private GamepadButton blockedUntilReleased;
    private bool wasConnected = true;

    public GamepadButtonEdgeTracker(TimeSpan? repeatDelay = null, TimeSpan? repeatInterval = null)
    {
        this.repeatDelay = repeatDelay ?? RepeatDelay;
        this.repeatInterval = repeatInterval ?? RepeatInterval;
    }

    /// <summary>What is physically down as of the last observation.</summary>
    public GamepadButton Held => held;

    /// <summary>
    /// Records the pad state and returns the buttons that count as pressed now.
    ///
    /// <para><paramref name="mayEmit"/> false still records what is held - that is
    /// the whole point, so a button held through the gap cannot fire when the gap
    /// ends - but emits nothing.</para>
    /// </summary>
    /// <param name="repeating">
    /// Buttons that repeat while held, at the bounded rate above. Everything else
    /// fires once per press.
    /// </param>
    public GamepadButton Observe(
        GamepadSnapshot snapshot,
        bool mayEmit,
        DateTime nowUtc,
        GamepadButton repeating = GamepadButton.None) =>
        Observe(snapshot, mayEmit, nowUtc, repeating, out _);

    /// <param name="repeated">
    /// Which of the returned buttons are auto-repeats of something still held rather
    /// than a new press. A caller that must choose one command per poll needs to know:
    /// a deliberate press should never lose to a repeat.
    /// </param>
    public GamepadButton Observe(
        GamepadSnapshot snapshot,
        bool mayEmit,
        DateTime nowUtc,
        GamepadButton repeating,
        out GamepadButton repeated)
    {
        repeated = GamepadButton.None;
        var down = snapshot.IsConnected ? snapshot.Buttons : GamepadButton.None;

        // A pad that has gone away takes its held state with it. Without this, the
        // button that was down when the cable was pulled is still "down" when it
        // comes back, and the first poll after a reconnect sees a release rather
        // than the press the player is making.
        if (!snapshot.IsConnected)
        {
            held = GamepadButton.None;
            blockedUntilReleased = GamepadButton.None;
            nextRepeatUtc.Clear();
            wasConnected = false;
            return GamepadButton.None;
        }

        // The first poll after a reconnect has no previous state to compare against,
        // so everything down looks newly pressed. It is not: the player was holding
        // a button when the pad came back, or the pad reported its resting state. It
        // has to be released before it can mean anything.
        if (!wasConnected)
        {
            wasConnected = true;
            held = down;
            blockedUntilReleased = down;
            nextRepeatUtc.Clear();
            return GamepadButton.None;
        }

        var pressed = down & ~held;
        var released = held & ~down;
        held = down;

        foreach (var button in Buttons)
        {
            if ((released & button) == button)
            {
                nextRepeatUtc.Remove(button);
            }
        }

        blockedUntilReleased &= down;

        if (!mayEmit)
        {
            // Anything down while we are not allowed to act has to be released
            // before it can ever mean anything.
            blockedUntilReleased |= down;
            nextRepeatUtc.Clear();
            return GamepadButton.None;
        }

        var emitted = pressed & ~blockedUntilReleased;

        foreach (var button in Buttons)
        {
            if ((repeating & button) != button || (down & button) != button
                || (blockedUntilReleased & button) == button)
            {
                continue;
            }

            if ((emitted & button) == button)
            {
                nextRepeatUtc[button] = nowUtc + repeatDelay;
                continue;
            }

            if (nextRepeatUtc.TryGetValue(button, out var due) && nowUtc >= due)
            {
                emitted |= button;
                repeated |= button;
                nextRepeatUtc[button] = nowUtc + repeatInterval;
            }
        }

        return emitted;
    }

    /// <summary>
    /// Refuses everything currently held until it is released and pressed again.
    /// Used when something else has just taken or given back ownership, so the
    /// button that did the taking cannot immediately do something else.
    /// </summary>
    public void BlockHeld()
    {
        blockedUntilReleased |= held;
        nextRepeatUtc.Clear();
    }

    /// <summary>
    /// Forgets everything and rearms neutrally: the next poll blocks whatever is down
    /// rather than reading it as a fresh press.
    ///
    /// <para>Clearing held state alone is not enough. After the menu is retired
    /// because the pad stopped answering, the button the player is still holding would
    /// look newly pressed on the very first poll that arrives and reopen the menu they
    /// had just lost.</para>
    /// </summary>
    public void Reset()
    {
        held = GamepadButton.None;
        blockedUntilReleased = GamepadButton.None;
        nextRepeatUtc.Clear();
        wasConnected = false;
    }

    private static readonly GamepadButton[] Buttons = Enum.GetValues<GamepadButton>()
        .Where(button => button != GamepadButton.None)
        .ToArray();
}
