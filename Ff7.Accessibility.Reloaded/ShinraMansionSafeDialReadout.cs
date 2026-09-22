using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public readonly record struct ShinraMansionSafeDialCue(string? Speech, bool IsDialOnScreen);

/// <summary>
/// Reads sinin2_1's visible safe dial, without reading the combination or checking input.
/// Entity 1 (disn), script 3 opens window 0 with WSPCL type 2 and WNUMB digits 2.
/// The party leader rewrites that window on each held Left/Right frame and clears its
/// display type on exit. Window 3 is the separate clock; window 1 carries the result.
/// </summary>
public sealed class ShinraMansionSafeDialReadout
{
    public const int FieldId = 299;
    public const int DialWindowId = 0;
    public const int LowestDialNumber = 0;
    public const int HighestDialNumber = 99;
    public static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(120);
    public static readonly TimeSpan MovingSpeechInterval = TimeSpan.FromMilliseconds(250);

    private bool isOnScreen;
    private bool hasCurrentReading;
    private int currentNumber = -1;
    private int lastSpokenNumber = -1;
    private DateTime lastChangedAt;
    private DateTime lastSpeechAt;

    public int? CurrentNumber => isOnScreen && hasCurrentReading ? currentNumber : null;
    public bool SuppressesCountdownSpeech => isOnScreen;
    public bool OwnsWindow(int windowId) => isOnScreen && windowId == DialWindowId;
    public string? Describe() => CurrentNumber is int number ? $"Safe dial {number}." : null;

    /// <summary>An explicit repeat must not fall back to an old number during a failed read.</summary>
    public string? DescribeForRepeat() => isOnScreen
        ? Describe() ?? "Safe dial reading unavailable."
        : null;

    /// <summary>Retried speech must still describe a fresh visible value.</summary>
    public bool IsCurrentSpeech(string speech) => CurrentNumber is int number &&
        (speech == $"{number}." || speech == $"Safe dial {number}.");

    /// <summary>Both hosts use this rule for failed or deferred delivery.</summary>
    public string? RetainSpeech(string? pending, ShinraMansionSafeDialCue cue)
    {
        var candidate = cue.Speech ?? pending;
        return cue.IsDialOnScreen && candidate is not null && IsCurrentSpeech(candidate)
            ? candidate
            : null;
    }

    /// <summary>
    /// A torn read provides no number, but is not evidence that the safe closed.
    /// Retain ownership so the countdown cannot interrupt while the next sample settles.
    /// </summary>
    public ShinraMansionSafeDialCue ObserveUnavailable()
    {
        hasCurrentReading = false;
        return new(null, isOnScreen);
    }

    /// <summary>Drop competing timer speech instead of deferring it past the result.</summary>
    public bool TrySuppressCountdown(FieldCountdownSpeechCoordinator countdown)
    {
        ArgumentNullException.ThrowIfNull(countdown);
        if (!SuppressesCountdownSpeech || !countdown.TryGetPending(out var pending)) return false;
        countdown.Acknowledge(pending);
        return true;
    }

    public ShinraMansionSafeDialCue Observe(
        int fieldId, FieldActivityNumericWindow window, DateTime now)
    {
        if (fieldId != FieldId || !window.IsUsable || window.DigitLimit != 2 ||
            window.Value < LowestDialNumber || window.Value > HighestDialNumber)
        {
            Reset();
            return default;
        }

        var number = window.Value;
        hasCurrentReading = true;
        if (!isOnScreen)
        {
            isOnScreen = true;
            currentNumber = lastSpokenNumber = number;
            lastChangedAt = lastSpeechAt = now;
            return new($"Safe dial {number}.", true);
        }

        if (number != currentNumber)
        {
            currentNumber = number;
            lastChangedAt = now;
        }

        if (number == lastSpokenNumber) return new(null, true);

        // Continuous movement needs audible progress so the player knows when to stop.
        // Take only the latest sample on each interval; never queue intermediate values.
        // A stopped dial gets its final number promptly even between moving updates.
        var elapsed = now - lastSpeechAt;
        var settled = now - lastChangedAt >= SettleDelay;
        if (elapsed < MovingSpeechInterval && (!settled || elapsed < SettleDelay))
            return new(null, true);

        lastSpokenNumber = number;
        lastSpeechAt = now;
        return new($"{number}.", true);
    }

    public void Reset()
    {
        isOnScreen = hasCurrentReading = false;
        currentNumber = lastSpokenNumber = -1;
        lastChangedAt = lastSpeechAt = default;
    }
}
