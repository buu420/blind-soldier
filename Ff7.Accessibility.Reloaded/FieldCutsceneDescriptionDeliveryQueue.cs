namespace Ff7.Accessibility.Reloaded;

public enum FieldCutsceneDeliveryOutcome
{
    /// <summary>No cue was waiting for this field.</summary>
    Nothing,

    /// <summary>Held for a native film that has not raised its active flag yet.</summary>
    Waiting,

    /// <summary>Independent narration started; the paragraph was deliberately not spoken.</summary>
    Narrated,

    /// <summary>The ordinary paragraph was accepted by the speaker.</summary>
    Spoken,

    /// <summary>The speaker refused; the cue stays at the head for the next tick.</summary>
    Refused,

    /// <summary>
    /// The cue described a film the game is not playing and nothing has been written
    /// for the one it is. Spent without being spoken, deliberately.
    /// </summary>
    Withheld,

    /// <summary>
    /// The film this cue describes is already being described. Spent deliberately,
    /// unspoken, and without reserving a dialogue window.
    /// </summary>
    AlreadyDescribed,

    /// <summary>
    /// A room this playthrough has already heard. The cue is spent so it cannot come
    /// back, and nothing is said - so no dialogue window is reserved and nothing waits
    /// behind it.
    /// </summary>
    Skipped
}

/// <summary>
/// The x86 delivery step, extracted so the ordering it has to guarantee is testable
/// on its own rather than only inside the monitor loop. The x64 coordinator performs
/// the same sequence; both are exercised by their own adapter tests.
/// </summary>
public sealed class FieldCutsceneDescriptionDelivery(FieldCutsceneDescriptionDeliveryQueue queue)
{
    /// <param name="describeRunningFilm">
    /// What the film that is actually playing shows, when the cue's own paragraph
    /// describes a different one. Returning null means nothing has been written for
    /// it, and the cue is dropped rather than spoken.
    /// </param>
    public FieldCutsceneDeliveryOutcome Deliver(
        int fieldId,
        Func<FieldCutsceneDescriptionCue, FieldMovieNarrationStartResult> beginNarration,
        Func<string, bool> trySpeak,
        Action<FieldCutsceneDescriptionCue> onDelivered,
        out FieldCutsceneDescriptionCue delivered,
        Func<string?>? describeRunningFilm = null,
        Func<FieldCutsceneDescriptionCue, bool>? shouldOffer = null)
    {
        ArgumentNullException.ThrowIfNull(beginNarration);
        ArgumentNullException.ThrowIfNull(trySpeak);
        ArgumentNullException.ThrowIfNull(onDelivered);

        delivered = default;
        if (!queue.TryPeek(fieldId, out var cue))
        {
            return FieldCutsceneDeliveryOutcome.Nothing;
        }

        // Asked before anything is started or reserved. A room the player has already
        // heard in this playthrough leaves the queue without costing a narration track,
        // a speech attempt or a dialogue reservation.
        if (shouldOffer is not null && !shouldOffer(cue))
        {
            queue.CommitDelivered(cue);
            delivered = cue;
            return FieldCutsceneDeliveryOutcome.Skipped;
        }

        var narration = beginNarration(cue);
        if (narration == FieldMovieNarrationStartResult.Started)
        {
            queue.CommitDelivered(cue);
            onDelivered(cue);
            delivered = cue;
            return FieldCutsceneDeliveryOutcome.Narrated;
        }

        // Speaking here would commit and dequeue the cue, and a frame later the
        // native film would be running with nothing left to start the track with.
        if (narration == FieldMovieNarrationStartResult.WaitingForNativeStart)
        {
            return FieldCutsceneDeliveryOutcome.Waiting;
        }

        // The film is already being described, by its recording or by the cue
        // schedule that took over from one. Saying the paragraph as well would
        // describe the scene twice. The cue is spent so it cannot come back, and no
        // dialogue window is reserved because nothing was said.
        if (narration == FieldMovieNarrationStartResult.AlreadyDescribed)
        {
            queue.CommitDelivered(cue);
            delivered = cue;
            return FieldCutsceneDeliveryOutcome.AlreadyDescribed;
        }

        // The paragraph was written for a film that is not the one playing. This is
        // what a disc change does to an anchor: the address is the same and the film
        // is not. Saying it anyway would describe the wrong scene with confidence,
        // which is worse than saying nothing, so the cue is spent either way.
        var text = cue.Text;
        if (narration == FieldMovieNarrationStartResult.DescribesADifferentFilm)
        {
            var substitute = describeRunningFilm?.Invoke();
            if (string.IsNullOrWhiteSpace(substitute))
            {
                queue.CommitDelivered(cue);
                onDelivered(cue);
                delivered = cue;
                return FieldCutsceneDeliveryOutcome.Withheld;
            }

            text = substitute;
        }

        // Only reserve the dialogue window once the speaker has actually taken the
        // text, and leave a refused cue at the head of the queue.
        if (!trySpeak(text))
        {
            return FieldCutsceneDeliveryOutcome.Refused;
        }

        cue = cue with { Text = text };

        queue.CommitDelivered(cue);
        onDelivered(cue);
        delivered = cue;
        return FieldCutsceneDeliveryOutcome.Spoken;
    }
}

/// <summary>
/// Holds queued scene descriptions until an output actually accepts them.
///
/// The ordering rule matters: a cue the speaker refuses has not been delivered, so
/// it must stay at the head. Dequeuing on attempt and re-enqueuing on failure moved
/// a refused cue behind later ones and played the scene out of order, and starting
/// the dialogue-priority window before acceptance let a permanently unavailable
/// speaker renew a fifteen-second reservation on every tick.
/// </summary>
public sealed class FieldCutsceneDescriptionDeliveryQueue
{
    private readonly object sync = new();
    private readonly Queue<FieldCutsceneDescriptionCue> pending = new();

    public int Count
    {
        get
        {
            lock (sync)
            {
                return pending.Count;
            }
        }
    }

    public void Enqueue(FieldCutsceneDescriptionCue cue)
    {
        lock (sync)
        {
            pending.Enqueue(cue);
        }
    }

    /// <summary>
    /// Returns the oldest cue for the current field without removing it. Cues for a
    /// field the player has already left can never be delivered, so they are dropped
    /// rather than blocking everything behind them.
    /// </summary>
    public bool TryPeek(int fieldId, out FieldCutsceneDescriptionCue cue)
    {
        lock (sync)
        {
            while (pending.Count > 0)
            {
                var candidate = pending.Peek();
                if (candidate.FieldId == fieldId)
                {
                    cue = candidate;
                    return true;
                }

                pending.Dequeue();
            }

            cue = default;
            return false;
        }
    }

    /// <summary>Removes the cue only if it is still the head, after real delivery.</summary>
    public void CommitDelivered(FieldCutsceneDescriptionCue delivered)
    {
        lock (sync)
        {
            if (pending.Count > 0 && pending.Peek().Key == delivered.Key)
            {
                pending.Dequeue();
            }
        }
    }

    public bool HasPendingFor(int fieldId)
    {
        lock (sync)
        {
            return pending.Any(cue => cue.FieldId == fieldId);
        }
    }

    public void Clear()
    {
        lock (sync)
        {
            pending.Clear();
        }
    }
}
