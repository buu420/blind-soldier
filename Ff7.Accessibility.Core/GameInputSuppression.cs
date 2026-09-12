namespace Ff7.Accessibility.Core;

/// <summary>
/// Keeps the game from seeing the buttons the navigation menu is using.
///
/// <para>This is the part that decides whether the feature is honest. Reading the
/// pad is easy; the problem is that the game is reading the same pad. Without
/// suppression, choosing a destination with A also confirms whatever dialogue is on
/// screen, cycling targets with the D-pad also walks the party across the room, and
/// closing with B also opens the menu the player did not ask for. A navigation menu
/// that does that is worse than no navigation menu.</para>
///
/// <para>The implementation belongs to the runtime, because what has to be
/// suppressed and where is a fact about the executable in front of us, not about
/// this policy. Until one exists, <see cref="Unavailable"/> reports
/// <see cref="IsAvailable"/> false and the menu refuses to open rather than
/// opening on top of the game's own controls.</para>
/// </summary>
public interface IGameInputSuppressor
{
    /// <summary>
    /// Whether this host can actually stop the game reading these buttons. False
    /// means the menu must not open: polling the pad without this would give the
    /// player a menu whose every keystroke also does something in the game.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Asks for <paramref name="buttons"/> to stop reaching the game. Called every
    /// tick the menu owns them, so it must be idempotent and cheap.
    /// </summary>
    /// <returns>
    /// False if suppression could not be established or has been lost. The menu
    /// closes on false: continuing to read a pad the game can also see is the exact
    /// failure this interface exists to prevent.
    /// </returns>
    bool TryHold(GamepadButton buttons, out string diagnostic);

    /// <summary>
    /// Gives every button back. Called once the menu has closed <em>and</em> every
    /// button it was holding has been physically released - not at the moment of
    /// closing, because the press that closed the menu is still down and the game
    /// would receive it.
    /// </summary>
    void ReleaseAll();
}

/// <summary>
/// The stand-in for a host that cannot suppress game input. It refuses, which keeps
/// the menu shut.
/// </summary>
public sealed class UnavailableGameInputSuppressor : IGameInputSuppressor
{
    private readonly string reason;

    public UnavailableGameInputSuppressor(string reason = "input suppression is not available on this host")
        => this.reason = reason;

    public static UnavailableGameInputSuppressor Instance { get; } = new();

    public bool IsAvailable => false;

    public bool TryHold(GamepadButton buttons, out string diagnostic)
    {
        diagnostic = reason;
        return false;
    }

    public void ReleaseAll()
    {
    }
}
