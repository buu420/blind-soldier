namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Why auto walk had no direction to give this sample.
///
/// <para>The distinction matters because two of these look identical from outside and mean
/// opposite things. A party standing at a ladder prompt waiting for the player to press the
/// button is auto walk behaving correctly; a party whose every candidate direction failed
/// the native clearance probe is auto walk stuck against a wall. Treating both as "nothing
/// to measure" is what let the second one grind silently forever.</para>
/// </summary>
public enum FieldAutoWalkHoldReason
{
    /// <summary>A direction was produced. There is something to measure.</summary>
    None = 0,

    /// <summary>No beacon, no route, or the party is not in the route's field.</summary>
    NoRoute,

    /// <summary>The route is finished - the party is already within arrival distance.</summary>
    Arrived,

    /// <summary>
    /// The route is deliberately holding for something only the player can do: an
    /// interaction point, a ladder that has to be mounted or dismounted by hand. Auto walk
    /// must never press these, so standing still here is correct behaviour.
    /// </summary>
    PlayerAction,

    /// <summary>
    /// There is a route, a waypoint and somewhere to go, and no direction toward it came
    /// back clear. This is the deadlock: measurable, and previously mistaken for a pause.
    /// </summary>
    NoClearDirection,
}

/// <summary>
/// One auto walk sample as a runtime sees it, so the two runtimes can share the decision
/// rather than each reimplementing it.
/// </summary>
/// <param name="IsAutoWalkEnabled">Whether field auto walk is switched on at all.</param>
/// <param name="RouteIdentity">
/// What is being walked to: field and target together. A change of either is a new
/// measurement, because a fresh target's remaining distance has nothing to do with the old
/// one's and must not inherit its stall.
/// </param>
/// <param name="IsHeldByGame">
/// The game has the party: a scripted control lock, a dialogue, a movie, a lost window
/// focus. Auto walk cannot move then and it is not auto walk's failure.
/// </param>
/// <param name="Hold">Why no direction was produced, when none was.</param>
/// <param name="PortalIndex">How far along the route's portals the party has got.</param>
/// <param name="RemainingDistance">How far is left to the target.</param>
public readonly record struct FieldAutoWalkConvergenceSample(
    bool IsAutoWalkEnabled,
    string RouteIdentity,
    bool IsHeldByGame,
    FieldAutoWalkHoldReason Hold,
    int PortalIndex,
    double RemainingDistance);

/// <summary>
/// Whether field auto walk is still getting anywhere.
///
/// <para>This is the field's copy of the guard the world map has had since it was written -
/// <c>WorldMapAutoWalkConvergenceTracker</c> - and it exists because the field did not have
/// one. In the 2026-09-08 recording, auto walk drove at a gateway the field's own Director
/// had switched off for ninety-nine consecutive samples without a word, and the player had
/// to take over manually to find out anything was wrong. Silence is the worst possible
/// answer there: a sighted player can see the character shuffling on the spot.</para>
///
/// <para>It watches two things, exactly as the world map does. Route progress counts: a
/// portal passed is progress even if the straight-line distance has not moved, because a
/// route can legitimately go around. Otherwise the remaining distance has to improve by a
/// real margin, so that the dither of a party stepping back and forth across a waypoint is
/// not mistaken for closing in.</para>
///
/// <para>The lifecycle is the part that has to be right, and there are three different
/// things a runtime can do here rather than one:</para>
///
/// <list type="bullet">
/// <item><description><see cref="Reset"/> - forget everything. Auto walk went off and on,
/// the target changed, the field changed, or the route arrived. None of the old
/// measurement means anything now.</description></item>
/// <item><description><see cref="Suspend"/> - the game has the party, or the route is
/// waiting for the player to press something. The paused time itself is exempt, but the
/// stall either side of it is <b>kept</b>, so a hold that flickers on and off cannot hide a
/// party that is going nowhere between the flickers.</description></item>
/// <item><description><see cref="Observe(int,double,System.DateTime)"/> - measure. This
/// includes the case where the route produced no direction because nothing probed clear,
/// which used to reset the guard on every sample and so could never trip.</description>
/// </item>
/// </list>
///
/// <para>A gap between samples larger than <see cref="MaximumObservedSampleGap"/> restarts
/// the measurement rather than counting: the mod can be suspended for a battle, a menu or a
/// scene, and none of that is auto walk failing to make progress.</para>
/// </summary>
public sealed class FieldAutoWalkConvergenceTracker
{
    /// <summary>How long without progress is long enough to give up and say so.</summary>
    public static readonly TimeSpan NoProgressTimeout = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan MaximumObservedSampleGap = TimeSpan.FromSeconds(2);

    // One running step is about forty-eight units and one walking step sixteen, measured
    // from the recorded session. Sixteen is therefore the smallest gain that means the
    // party is actually travelling rather than dithering across a waypoint.
    private const double MeaningfulProgressDistance = 16d;

    private DateTime lastObservedAt = DateTime.MinValue;
    private DateTime lastProgressAt = DateTime.MinValue;
    private TimeSpan stalledBeforeSuspend = TimeSpan.Zero;
    private bool isResuming;
    private double bestRemainingDistance = double.PositiveInfinity;
    private int bestPortalIndex = -1;
    private string routeIdentity = string.Empty;

    /// <summary>
    /// Records one sample and decides for itself which of the three lifecycle cases it is.
    /// Both runtimes call this, so the behaviour is genuinely shared rather than copied.
    /// Returns true once the route has gone <see cref="NoProgressTimeout"/> of measurable
    /// time without improving.
    /// </summary>
    public bool Observe(FieldAutoWalkConvergenceSample sample, DateTime observedAt)
    {
        if (!sample.IsAutoWalkEnabled)
        {
            // Off. Coming back on is a new walk, not the continuation of an old one.
            routeIdentity = string.Empty;
            Reset();
            return false;
        }

        var identity = sample.RouteIdentity ?? string.Empty;
        if (!string.Equals(identity, routeIdentity, StringComparison.Ordinal))
        {
            // A different target, or the same target in a different field. Its remaining
            // distance is a different number about a different journey; inheriting the old
            // stall would stop the new walk almost immediately.
            routeIdentity = identity;
            Reset();
        }

        if (sample.IsHeldByGame)
        {
            Suspend();
            return false;
        }

        switch (sample.Hold)
        {
            case FieldAutoWalkHoldReason.NoRoute:
            case FieldAutoWalkHoldReason.Arrived:
                Reset();
                return false;

            case FieldAutoWalkHoldReason.PlayerAction:
                Suspend();
                return false;
        }

        return Observe(sample.PortalIndex, sample.RemainingDistance, observedAt);
    }

    /// <summary>
    /// Records one measurable sample. Returns true once the route has gone
    /// <see cref="NoProgressTimeout"/> without improving.
    /// </summary>
    public bool Observe(int portalIndex, double remainingDistance, DateTime observedAt)
    {
        if (!double.IsFinite(remainingDistance) || remainingDistance < 0d)
        {
            Reset();
            return false;
        }

        if (lastObservedAt == DateTime.MinValue ||
            observedAt <= lastObservedAt ||
            observedAt - lastObservedAt > MaximumObservedSampleGap)
        {
            Begin(portalIndex, remainingDistance, observedAt);
            return false;
        }

        lastObservedAt = observedAt;
        if (portalIndex > bestPortalIndex ||
            remainingDistance <= bestRemainingDistance - MeaningfulProgressDistance)
        {
            bestPortalIndex = Math.Max(bestPortalIndex, portalIndex);
            bestRemainingDistance = Math.Min(bestRemainingDistance, remainingDistance);
            lastProgressAt = observedAt;

            // Real progress clears what was banked before any pause. The party is
            // travelling; whatever it was doing five seconds ago no longer counts against
            // it.
            stalledBeforeSuspend = TimeSpan.Zero;
            return false;
        }

        return stalledBeforeSuspend + (observedAt - lastProgressAt) >= NoProgressTimeout;
    }

    /// <summary>
    /// Everything about this walk is over: switched off, a new target, a new field, or
    /// arrived. Nothing measured before this means anything after it.
    /// </summary>
    public void Reset()
    {
        lastObservedAt = DateTime.MinValue;
        lastProgressAt = DateTime.MinValue;
        stalledBeforeSuspend = TimeSpan.Zero;
        isResuming = false;
        bestRemainingDistance = double.PositiveInfinity;
        bestPortalIndex = -1;
    }

    /// <summary>
    /// The party is legitimately held and cannot be expected to move.
    ///
    /// <para>The held time is exempt - that is the whole point - but the stall either side
    /// of it is banked and carried across, and the distance measurement restarts from
    /// wherever the party actually resumes. Without the banking, anything that holds the
    /// party even momentarily would restart the clock every time and the guard could never
    /// trip; without the restart, a party that legitimately ends up further away after a
    /// climb would be stopped for it.</para>
    /// </summary>
    public void Suspend()
    {
        if (lastObservedAt == DateTime.MinValue)
        {
            // Already suspended, or never started. The pause does not accumulate against
            // itself.
            return;
        }

        var stalled = lastObservedAt - lastProgressAt;
        if (stalled > TimeSpan.Zero)
        {
            stalledBeforeSuspend += stalled;
        }

        lastObservedAt = DateTime.MinValue;
        lastProgressAt = DateTime.MinValue;
        isResuming = true;
    }

    private void Begin(int portalIndex, double remainingDistance, DateTime observedAt)
    {
        lastObservedAt = observedAt;
        lastProgressAt = observedAt;
        if (!isResuming)
        {
            // A restart that is not a resume - the first sample, or a gap long enough that
            // the mod simply was not watching - knows nothing about what came before.
            stalledBeforeSuspend = TimeSpan.Zero;
        }

        isResuming = false;
        bestRemainingDistance = remainingDistance;
        bestPortalIndex = portalIndex;
    }
}
