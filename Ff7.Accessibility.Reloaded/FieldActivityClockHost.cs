namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Shared delivery for replaceable field readings: clock hands, exchange amounts
/// and timed Ancient Forest plants. Only the current reading can reach the speaker.
/// </summary>
public static class FieldActivityClockHost
{
    /// <summary>
    /// The clock and forest are watched only while the game has focus. Focus loss
    /// forgets observed motion and pending speech; returning starts a fresh sample.
    /// </summary>
    public static bool MayObserve(
        int fieldId,
        bool isForeground,
        FieldActivityReadout readout,
        FieldActivityLatestLineDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(readout);
        ArgumentNullException.ThrowIfNull(delivery);
        if ((fieldId != FieldActivityReadout.ClockRoomFieldId && !AncientForestPuzzleReadout.HasPuzzle(fieldId)) || isForeground)
        {
            return true;
        }

        readout.Reset();
        delivery.Reset();
        return false;
    }

    /// <summary>
    /// After the readout has observed: hands its replaceable reading to the delivery. The reading
    /// is spoken only while speech is on and the game still has focus at the moment it would be
    /// spoken; otherwise it stays owed, as the current reading only, and is not counted as said.
    /// The readout is told when its reading was actually said.
    /// </summary>
    /// <returns>The line spoken this tick, or null.</returns>
    public static string? Deliver(
        FieldActivityCue cue,
        FieldActivityReadout readout,
        FieldActivityLatestLineDelivery delivery,
        DateTime now,
        Func<bool> isForeground,
        Func<bool> isSpeechEnabled,
        Func<string, bool> speak,
        Func<bool?>? isSpeaking)
    {
        ArgumentNullException.ThrowIfNull(readout);
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(isForeground);
        ArgumentNullException.ThrowIfNull(isSpeechEnabled);
        ArgumentNullException.ThrowIfNull(speak);
        var spoken = delivery.Pump(
            cue.ReplacesEarlierLine ? cue.Speech : null,
            readout.CurrentReplaceableLine,
            now,
            text => isSpeechEnabled() && isForeground() && speak(text),
            isSpeaking,
            interruptProtectedSpeech: readout.CurrentReplaceableLineIsUrgent);
        if (spoken is not null)
        {
            // Only now has the line begun to be heard; a line held back or refused has not. Until
            // it has been, the readout asks whether anything else was said over it, and whether the
            // screen reader is still saying it.
            readout.NoteReplaceableLineSpoken(spoken, now, () => delivery.OtherSpeechSinceDelivery, isSpeaking);
        }

        return spoken;
    }
}
