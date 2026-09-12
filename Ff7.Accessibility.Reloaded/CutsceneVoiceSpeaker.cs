namespace Ff7.Accessibility.Reloaded;

/// <summary>What happened when a scene description was handed to the two voices.</summary>
/// <param name="Spoken">
/// Whether anybody actually said it. False keeps the cue at the head of its queue,
/// because a description nobody said has not happened.
/// </param>
/// <param name="ClipDuration">
/// The recording's own length when a recording said it, and null when the screen
/// reader did. The dialogue window is held for this rather than for a word-count
/// estimate, because a recording's length is known and an estimate is not.
/// </param>
public readonly record struct CutsceneVoiceDelivery(bool Spoken, TimeSpan? ClipDuration)
{
    /// <summary>Nobody said it, and it must stay queued.</summary>
    public static CutsceneVoiceDelivery NotSpoken => new(false, null);
}

/// <summary>
/// The one decision both runtimes make about a scene description: play the recording
/// of it, speak it, or leave it alone for now.
///
/// <para>This lives here rather than being written out twice because the middle case
/// is easy to get wrong in exactly the way that loses content. A recording that
/// exists but cannot start <em>yet</em> is not a missing recording: falling back to
/// the screen reader there would put a second voice on top of the first and spend the
/// cue in the wrong voice, permanently, for a condition that clears in a few hundred
/// milliseconds. Only reasons the recording can never be the answer - nothing
/// recorded says these words, the file is not installed, the feature is off, the
/// device will not open or refuses to start - fall back to speech, and those must,
/// because a scene left undescribed is the worst outcome there is.</para>
/// </summary>
public static class CutsceneVoiceSpeaker
{
    /// <param name="player">The recorded voice, or null where it is not configured.</param>
    /// <param name="speak">The screen reader, used only when no recording can answer.</param>
    /// <param name="onRecorded">Optional diagnostics for a description said by a clip.</param>
    public static CutsceneVoiceDelivery Deliver(
        CutsceneVoicePlayer? player,
        string text,
        CutsceneVoiceOwner owner,
        Func<string, bool> speak,
        Action<string, TimeSpan>? onRecorded = null)
    {
        ArgumentNullException.ThrowIfNull(speak);

        var duration = TimeSpan.Zero;
        var result = player is null
            ? CutsceneVoiceResult.Unavailable
            : player.TrySpeak(text, owner, out duration);

        switch (result)
        {
            case CutsceneVoiceResult.Played:
                onRecorded?.Invoke(text, duration);
                return new CutsceneVoiceDelivery(true, duration);

            // Busy is not unavailable. The cue stays where it is and is offered again
            // a tick later, in the voice it was meant to have.
            case CutsceneVoiceResult.Busy:
                return CutsceneVoiceDelivery.NotSpoken;

            default:
                return new CutsceneVoiceDelivery(speak(text), null);
        }
    }
}
