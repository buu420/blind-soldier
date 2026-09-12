using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Input;

/// <summary>
/// Where automatic walking's direction is kept on the Steam 2026 host, instead of being
/// pressed on the player's keyboard.
///
/// <para>The x64 host does not read the keyboard the way the original did. Its
/// <c>IDirectInputDeviceA::GetDeviceState</c> is a native shim that <em>synthesizes</em> a
/// legacy 256-byte DIK array from its own logical actions - action 10..13 become
/// <c>0x48/0x50/0x4B/0x4D</c> through a constant table - so the old keypad codes in the
/// guest control table are that shim's <b>output vocabulary</b>, not keys the host
/// watches. Pressing them with <c>SendInput</c> therefore could not move the party, and
/// did not: every transition was reported inserted while the game's direction stayed
/// <c>None</c>. Worse, numpad 2 is NVDA's "read current character", so the presses were
/// reaching the screen reader instead of the game and the player heard "blank" repeatedly.
/// </para>
///
/// <para>Autowalk emits no Windows key events on this runtime. The desired
/// direction is recorded here and <see cref="Steam2026NativeDirectInputKeyboardHook"/>
/// overlays it onto the buffer the shim has just filled, before the translated caller
/// reads it.</para>
///
/// <para>Refusing rather than pretending: a press is only accepted while the overlay is
/// installed and the game has focus, so a host where the seam is missing reports a
/// failure the player hears once, instead of walking nowhere in silence. Releases are
/// always accepted - the shim rebuilds the whole buffer on its next poll, so forgetting a
/// token <em>is</em> releasing it, and a release that could fail would leave the shared
/// controller owning a key forever.</para>
/// </summary>
internal sealed class Steam2026NativeDirectionalInputSink : IHighwayKeyboardInputSink
{
    /// <summary>
    /// Stops delivery if the autowalk coordinator stops updating. Normal stop and pause
    /// release immediately through the shared controller. A valid driving frame renews
    /// the lease; stale polls suppress output without taking ownership from the controller.
    /// </summary>
    internal static readonly TimeSpan Freshness = TimeSpan.FromMilliseconds(500);

    /// <summary>Four cardinals at most, which is two tokens for a diagonal.</summary>
    internal const int MaxHeldTokens = 4;

    private readonly object sync = new();
    private readonly List<byte> held = new(MaxHeldTokens);
    private readonly Func<DateTime> now;
    private readonly Func<bool> isForeground;
    private Func<bool>? isOverlayInstalled;
    private DateTime renewedUtc;
    private long acceptedPresses;
    private string diagnostic = string.Empty;

    internal Steam2026NativeDirectionalInputSink(
        Func<bool>? isForeground = null,
        Func<DateTime>? now = null)
    {
        this.isForeground = isForeground ?? (static () => true);
        this.now = now ?? (static () => DateTime.UtcNow);
    }

    /// <summary>
    /// Why a press was refused, or empty. The coordinator logs this beside the shared
    /// controller's own failure text, which is phrased for <c>SendInput</c>.
    /// </summary>
    internal string Diagnostic
    {
        get
        {
            lock (sync)
            {
                return diagnostic;
            }
        }
    }

    /// <summary>Directional presses this sink has accepted. Diagnostic only.</summary>
    internal long AcceptedPresses => Interlocked.Read(ref acceptedPresses);

    /// <summary>
    /// Hands the sink the overlay's liveness. Late-bound because the hook is installed
    /// once the exact supported image is validated, which is after the sink exists.
    /// </summary>
    internal void AttachOverlay(Func<bool> overlayIsInstalled)
    {
        ArgumentNullException.ThrowIfNull(overlayIsInstalled);
        lock (sync)
        {
            isOverlayInstalled = overlayIsInstalled;
        }
    }

    /// <summary>
    /// Keeps a commanded direction alive for another <see cref="Freshness"/>. Called once
    /// per frame by whichever coordinator is driving.
    /// </summary>
    internal void Renew(DateTime nowUtc)
    {
        lock (sync)
        {
            if (held.Count > 0)
            {
                renewedUtc = nowUtc;
            }
        }
    }

    /// <summary>Drops every direction at once: stop, pause, menu, arrival, teardown.</summary>
    internal void Clear()
    {
        lock (sync)
        {
            held.Clear();
            renewedUtc = default;
        }
    }

    public HighwayKeyboardSendResult Send(IReadOnlyList<HighwayKeyboardTransition> transitions)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        if (transitions.Count == 0)
        {
            return new HighwayKeyboardSendResult(0, 0);
        }

        // Sampled before the lock: it is a Win32 call, and this lock is also taken on the
        // game's own input thread inside its keyboard poll.
        var foreground = isForeground();
        lock (sync)
        {
            // Counted as a prefix because that is how the shared controller takes
            // ownership: it owns transitions[0..inserted). Releases come first in every
            // batch it builds, so "all releases, then refuse" is always a prefix.
            var accepted = 0;
            foreach (var transition in transitions)
            {
                if (!TryResolveToken(transition, out var token))
                {
                    diagnostic =
                        $"scan code 0x{transition.ScanCode:X2} is not a legacy key code the " +
                        "Steam 2026 keyboard state can carry";
                    break;
                }

                if (!transition.IsKeyDown)
                {
                    _ = held.Remove(token);
                    accepted++;
                    continue;
                }

                if (isOverlayInstalled?.Invoke() != true)
                {
                    diagnostic =
                        "the Steam 2026 native keyboard-state overlay is not installed, so " +
                        "automatic movement has nowhere to go";
                    break;
                }

                if (!foreground)
                {
                    diagnostic = "Final Fantasy VII does not have focus";
                    break;
                }

                if (held.Count >= MaxHeldTokens)
                {
                    diagnostic = "more directional keys than a direction can hold";
                    break;
                }

                if (!held.Contains(token))
                {
                    held.Add(token);
                }

                renewedUtc = now();
                Interlocked.Increment(ref acceptedPresses);
                accepted++;
            }

            if (accepted == transitions.Count)
            {
                diagnostic = string.Empty;
            }

            return new HighwayKeyboardSendResult(accepted, 0);
        }
    }

    /// <summary>
    /// The tokens the overlay should mark as held, or false for none. Called from inside
    /// the host's own <c>GetDeviceState</c>, so it takes the lock briefly and copies.
    /// </summary>
    internal bool TryTakeDesired(DateTime nowUtc, Span<byte> tokens, out int count)
    {
        count = 0;
        var foreground = isForeground();
        lock (sync)
        {
            if (held.Count == 0 || held.Count > tokens.Length)
            {
                return false;
            }

            // A direction nobody is renewing is not delivered - but it is not forgotten
            // either. Dropping it here would take it off the shared controller behind its
            // back: that controller only presses what it does not already own, so it would
            // never re-press a key this had silently discarded. Held and undeliverable is
            // inert and recoverable; discarded is a route that walks nowhere for good.
            if (nowUtc - renewedUtc > Freshness || nowUtc < renewedUtc)
            {
                return false;
            }

            if (!foreground)
            {
                return false;
            }

            for (var index = 0; index < held.Count; index++)
            {
                tokens[index] = held[index];
            }

            count = held.Count;
            return true;
        }
    }

    /// <summary>
    /// Back to the control table's own token. The resolver split it into a scan code and
    /// the extended bit; the DIK array is indexed by the byte they came from.
    /// </summary>
    private static bool TryResolveToken(HighwayKeyboardTransition transition, out byte token)
    {
        token = 0;
        if (transition.ScanCode == 0 || transition.ScanCode > 0x7F)
        {
            return false;
        }

        token = (byte)(transition.ScanCode | (transition.IsExtended ? 0x80 : 0));
        return true;
    }
}
