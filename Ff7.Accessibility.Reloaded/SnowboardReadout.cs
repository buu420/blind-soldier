namespace Ff7.Accessibility.Reloaded;

public sealed class SnowboardReadout
{
    private SnowboardState previous;
    private SnowboardTrackShape? spokenShape;
    private readonly HashSet<int> announcedForks = [];
    private DateTime lastSpeech;

    public string? Observe(SnowboardState state, DateTime now)
    {
        if (!state.IsActive) { Reset(); return null; }
        var old = previous;
        previous = state;
        string? Say(string text) { lastSpeech = now; return text; }
        if (state.Phase == SnowboardPhase.Finishing)
            return !old.IsActive || old.Phase != state.Phase ? Say("Run complete.") : null;
        if (state.IsPaused)
            return !old.IsActive || !old.IsPaused ? Say("Snowboarding paused.") : null;
        if (old.IsActive && old.IsPaused) return Say("Snowboarding resumed.");
        if (state.Phase == SnowboardPhase.Starting)
        {
            // A new run in the same module: its forks and bends are new too.
            if (!old.IsActive || old.Phase != SnowboardPhase.Starting)
            {
                announcedForks.Clear();
                spokenShape = null;
            }

            return !old.IsActive ? Say("Snowboarding.") : null;
        }

        // A second fork visible on entry belongs in this same utterance. A separate
        // interrupting call on the next worker tick would erase the route confirmation.
        if (old.IsActive && old.Segment != state.Segment && old.LeftChild != old.RightChild)
        {
            spokenShape = null;
            var route = old.LeftChild == state.Segment ? "Left route." :
                old.RightChild == state.Segment ? "Right route." : null;
            if (route is not null)
            {
                if (state.Ahead == SnowboardTrackShape.Fork && announcedForks.Add(state.Segment))
                    route += " Fork ahead.";
                return Say(route);
            }
        }
        if (state.Ahead == SnowboardTrackShape.Fork && announcedForks.Add(state.Segment))
            return Say("Fork ahead. Left or right.");
        if (state.Ahead == SnowboardTrackShape.Fork) return null;
        var shape = state.AheadStrip >= 0 ? state.Ahead : state.Shape;
        // Riding in a bend (the nearest non-straight strip is the current one) and now the
        // nearest is a later one in the same direction: a second bend, not the same one.
        if (old.IsActive && old.Segment == state.Segment && shape != SnowboardTrackShape.Straight &&
            old.Ahead == shape && old.AheadStrip >= 0 && old.AheadStrip <= old.Strip &&
            state.AheadStrip > state.Strip)
            spokenShape = null;
        if (spokenShape == shape || (old.IsActive && now - lastSpeech < TimeSpan.FromMilliseconds(1400)))
            return null;
        spokenShape = shape;
        return Say(shape switch
        {
            SnowboardTrackShape.Left => "Left bend.",
            SnowboardTrackShape.Right => "Right bend.",
            _ => "Straight."
        });
    }

    public void Reset()
    {
        previous = default;
        spokenShape = null;
        announcedForks.Clear();
        lastSpeech = default;
    }
}
