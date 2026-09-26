namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Delivers a field activity line that a newer one makes worthless - today only the Temple
/// clock's "Moving, ten thirty." - so that the player hears the newest reading and never a
/// queue of old ones.
///
/// <para>Three rules, and nothing else:</para>
/// <list type="bullet">
/// <item><description>
/// <b>Newest only.</b> At most one line is owed. When it is finally said it is the readout's
/// current line, not the one that was owed first, and it interrupts whatever older reading is
/// still playing. When nothing is current any more - the room was left, the readout reset or
/// the clock could not be read - nothing is owed.
/// </description></item>
/// <item><description>
/// <b>Other speech first.</b> Anything else that was just spoken - the Time Guardian's
/// "[OK] Stop!", a dialogue line, the repeat key - is not cut off. The owed line waits until
/// the screen reader reports that it has stopped speaking, or, where it cannot say, for about
/// as long as those words take at a slow rate. A screen reader that reports speaking forever
/// is outlasted in the end: the wait never runs past that line's own ceiling, the words at a
/// very slow rate plus a margin (<see cref="CeilingFor"/>), so a long repeat line is heard to
/// its end while a stuck device still gives the clock back. Only the speech is protected, not
/// the window it came from: a window left open says nothing more once its words are done, so
/// it does not silence the clock.
/// </description></item>
/// <item><description>
/// <b>Failure is not delivery.</b> A muted or failing speaker keeps the line owed, retried no
/// more often than <see cref="RetryInterval"/>, for as long as it is still current.
/// </description></item>
/// </list>
/// </summary>
public sealed class FieldActivityLatestLineDelivery
{
    /// <summary>How soon a refused line is offered to the speaker again.</summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>How long other speech is assumed not to have started yet, before the screen reader's own word is taken.</summary>
    public static readonly TimeSpan SpeechStartGrace = TimeSpan.FromMilliseconds(250);

    /// <summary>The shortest estimate of how long another line takes to say.</summary>
    public static readonly TimeSpan MinimumProtection = TimeSpan.FromMilliseconds(800);

    /// <summary>A slow speaking rate, about 180 words a minute, for estimating when the screen reader cannot say.</summary>
    public static readonly TimeSpan PerWord = TimeSpan.FromMilliseconds(330);

    /// <summary>
    /// A very slow rate, 100 words a minute, for the ceiling: how long a screen reader that
    /// keeps reporting that it is speaking is believed.
    /// </summary>
    public static readonly TimeSpan CeilingPerWord = TimeSpan.FromMilliseconds(600);

    /// <summary>Added to every ceiling, for a line that starts late behind other speech.</summary>
    public static readonly TimeSpan CeilingMargin = TimeSpan.FromSeconds(5);

    /// <summary>No ceiling is shorter than this, however few the words.</summary>
    public static readonly TimeSpan MinimumCeiling = TimeSpan.FromSeconds(6);

    /// <summary>
    /// The longest a line of <paramref name="words"/> words holds the clock back while the
    /// screen reader keeps saying it is speaking. The clock's 34-word repeat line is believed for
    /// 25.4 seconds; a stuck device therefore holds the clock for a bounded time, never for ever.
    /// </summary>
    public static TimeSpan CeilingFor(int words) =>
        TimeSpan.FromTicks(Math.Max(MinimumCeiling.Ticks, CeilingPerWord.Ticks * words + CeilingMargin.Ticks));

    // Other speech can be reported from the loader's lifecycle thread. Monitor is reentrant,
    // so the report a delivery itself causes on this thread still reaches the delivering check.
    private readonly object sync = new();
    private string? owed;
    private DateTime nextAttempt = DateTime.MinValue;
    private DateTime otherSpeechAt = DateTime.MinValue;
    private DateTime otherSpeechEstimatedEnd = DateTime.MinValue;
    private DateTime otherSpeechCeiling = DateTime.MinValue;
    private bool delivering;

    /// <summary>Whether a line is still owed.</summary>
    public bool HasPending
    {
        get
        {
            lock (sync)
            {
                return owed is not null;
            }
        }
    }

    /// <summary>
    /// Something other than this line was just spoken. Calls made while this class is itself
    /// delivering are its own line and are ignored.
    /// </summary>
    public void NoteOtherSpeech(string text, DateTime now)
    {
        lock (sync)
        {
            NoteOtherSpeechLocked(text, now);
        }
    }

    private void NoteOtherSpeechLocked(string text, DateTime now)
    {
        if (delivering || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var estimate = TimeSpan.FromTicks(Math.Max(PerWord.Ticks * words, MinimumProtection.Ticks));
        // A line spoken while an earlier one is still protected does not shorten the earlier
        // one's protection: whichever ends later is what is waited for.
        var stillProtected = otherSpeechAt != DateTime.MinValue;
        otherSpeechAt = now;
        otherSpeechEstimatedEnd = stillProtected && otherSpeechEstimatedEnd > now + estimate
            ? otherSpeechEstimatedEnd
            : now + estimate;
        otherSpeechCeiling = stillProtected && otherSpeechCeiling > now + CeilingFor(words)
            ? otherSpeechCeiling
            : now + CeilingFor(words);
    }

    /// <summary>Nothing is owed any more: the room was left, the readout was turned off or the game lost focus.</summary>
    public void Reset()
    {
        lock (sync)
        {
            ResetLocked();
        }
    }

    private void ResetLocked()
    {
        owed = null;
        nextAttempt = DateTime.MinValue;
    }

    /// <summary>
    /// One host tick. <paramref name="newLine"/> is the line the readout wants said now, if
    /// any; <paramref name="current"/> is what it would say for the latest observation, and is
    /// what actually gets delivered. <paramref name="deliver"/> speaks with interruption and
    /// says whether the speaker took it. <paramref name="isSpeaking"/> is the screen reader's
    /// own answer, or null when it cannot give one.
    /// </summary>
    /// <returns>The line delivered this tick, or null.</returns>
    public string? Pump(
        string? newLine,
        string? current,
        DateTime now,
        Func<string, bool> deliver,
        Func<bool?>? isSpeaking = null)
    {
        ArgumentNullException.ThrowIfNull(deliver);
        lock (sync)
        {
            return PumpLocked(newLine, current, now, deliver, isSpeaking);
        }
    }

    private string? PumpLocked(
        string? newLine,
        string? current,
        DateTime now,
        Func<string, bool> deliver,
        Func<bool?>? isSpeaking)
    {
        if (newLine is not null)
        {
            owed = newLine;
            // A new reading is worth an attempt now, whatever happened to the last one.
            nextAttempt = DateTime.MinValue;
        }

        if (owed is null)
        {
            return null;
        }

        if (current is null)
        {
            // Nothing the readout could say now: an older line is not current either.
            ResetLocked();
            return null;
        }

        owed = current;
        if (IsOtherSpeechProtected(now, isSpeaking) || now < nextAttempt)
        {
            return null;
        }

        bool delivered;
        delivering = true;
        try
        {
            delivered = deliver(owed);
        }
        catch (Exception)
        {
            // A speaker that throws has not said it.
            delivered = false;
        }
        finally
        {
            delivering = false;
        }

        if (!delivered)
        {
            nextAttempt = now + RetryInterval;
            return null;
        }

        var spoken = owed;
        owed = null;
        nextAttempt = DateTime.MinValue;
        return spoken;
    }

    private bool IsOtherSpeechProtected(DateTime now, Func<bool?>? isSpeaking)
    {
        if (otherSpeechAt == DateTime.MinValue)
        {
            return false;
        }

        var since = now - otherSpeechAt;
        if (since < SpeechStartGrace)
        {
            return true;
        }

        bool? speaking;
        try
        {
            speaking = isSpeaking?.Invoke();
        }
        catch (Exception)
        {
            speaking = null;
        }

        var protectedNow = now < otherSpeechCeiling && speaking switch
        {
            // The screen reader can say: protected exactly while it is still speaking.
            true => true,
            false => false,
            // It cannot: about as long as the words take.
            null => now < otherSpeechEstimatedEnd
        };
        if (!protectedNow)
        {
            // That speech is over. From here on the screen reader speaking means the clock's
            // own earlier reading, which a newer reading is meant to replace.
            otherSpeechAt = DateTime.MinValue;
        }

        return protectedNow;
    }
}
