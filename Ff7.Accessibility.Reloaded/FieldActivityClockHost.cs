namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// The host's side of the Temple clock, shared by both runtimes so that neither can drift
/// from the other: when the clock may be watched at all, and when its newest reading may
/// actually reach the speaker. It touches no other field activity.
/// </summary>
public static class FieldActivityClockHost
{
    /// <summary>
    /// Whether the readout may watch this field now. The clock is watched only while the game
    /// has focus. When it has lost focus, what was seen of the hands and any owed reading are
    /// forgotten: coming back, the hands are watched afresh rather than trusted from before.
    /// Every other field is left to the host exactly as it was.
    /// </summary>
    public static bool MayObserve(
        int fieldId,
        bool isForeground,
        FieldActivityReadout readout,
        FieldActivityLatestLineDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(readout);
        ArgumentNullException.ThrowIfNull(delivery);
        if (fieldId != FieldActivityReadout.ClockRoomFieldId || isForeground)
        {
            return true;
        }

        readout.Reset();
        delivery.Reset();
        return false;
    }

    /// <summary>
    /// After the readout has observed: hands the clock's reading to the delivery. The reading
    /// is spoken only while speech is on and the game still has focus at the moment it would be
    /// spoken; otherwise it stays owed, as the current reading only, and is not counted as said.
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
        return delivery.Pump(
            cue.ReplacesEarlierLine ? cue.Speech : null,
            readout.CurrentReplaceableLine,
            now,
            text => isSpeechEnabled() && isForeground() && speak(text),
            isSpeaking);
    }
}
