using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Field;

/// <summary>
/// The two foreground decisions the host loop makes about scene descriptions.
///
/// <para>They live here rather than inline in the loop because both were wrong in a
/// way that is invisible from the coordinator: the coordinator did the right thing
/// when asked, and the host did not ask. Written out here they can be driven by a
/// test with a real coordinator, which the loop itself cannot be.</para>
/// </summary>
internal static class Steam2026FieldCutsceneHostTick
{
    /// <summary>
    /// The per-frame film lifecycle when the game is in front of the player, and
    /// taking the audio back when it is not.
    ///
    /// <para>The suspend is unconditional. It used to be guarded by
    /// <c>IsNativeFilmNarrationPlaying</c>, which reports only the film's own track:
    /// a scene description plays on a separate device that the property cannot see,
    /// so an action description carried on talking after the player alt-tabbed. The
    /// call is idempotent and silent when nothing is playing, so asking every frame
    /// costs nothing.</para>
    /// </summary>
    internal static void ObserveOrSuspend(
        Steam2026FieldCutsceneDescriptionCoordinator descriptions,
        bool isHostForeground,
        DateTime nowUtc,
        Func<bool>? hasReadableActiveMessage)
    {
        ArgumentNullException.ThrowIfNull(descriptions);
        if (isHostForeground)
        {
            descriptions.ObserveNativeFilm(nowUtc, hasReadableActiveMessage);
            return;
        }

        descriptions.SuspendNativeFilmNarration(FieldMovieNarrationStopReason.Suspended);
    }

    /// <summary>
    /// A film's deferred cue, but only while the game has the foreground.
    ///
    /// <para>This dispatch was reached unconditionally, so the tick that had just
    /// suspended everything could immediately start a new clip on the same device.
    /// The ordinary description path was already given
    /// <paramref name="isHostForeground"/> and refuses on its own; this one had no
    /// such gate. Refusing here does not lose the cue: the schedule only advances
    /// when the words are actually taken, so it is offered again when the window
    /// comes back and the film is still running.</para>
    /// </summary>
    internal static bool TryDeliverDeferredFilmCue(
        Steam2026FieldCutsceneDescriptionCoordinator descriptions,
        bool isHostForeground,
        DateTime nowUtc,
        Func<bool>? hasReadableActiveMessage,
        Func<string, bool> speak,
        out string delivered,
        out int deliveredFieldId)
    {
        ArgumentNullException.ThrowIfNull(descriptions);
        delivered = string.Empty;
        deliveredFieldId = -1;
        return isHostForeground
            && descriptions.TryDeliverDeferredFilmCue(
                nowUtc, hasReadableActiveMessage, speak, out delivered, out deliveredFieldId);
    }
}
