using Ff7.Accessibility.Core;

/// <summary>
/// The complete G Bike (arcade) steering: road safety, truck avoidance and biker engagement
/// through the real composer. Root's reproduction against 0.7.9: Cloud (80,0), truck (0,450),
/// biker (280,150), road lateral 80 of half-width 250. Engagement asked for UpRight and the
/// composer drove Left, because any road correction - entered at a quarter of the half-width -
/// replaced the engagement; with the sword swinging (attack timer 19) engagement released the
/// keys and the composer still drove Left.
///
/// <para>Native road bounds: 006539B2 clamps the bike only at the road half-width, which already
/// has 96 taken off it, so the interior is safe to drive in. In the arcade the engagement now
/// uses that interior, including standing still in the sword pocket and through the swing; the
/// truck-collision correction always wins, and the road-edge correction wins from the critical
/// ratio (0.70) until the bike is back inside 0.55. The story chase is unchanged.</para>
/// </summary>
internal static class HighwayArcadeCompositionTests
{
    private static readonly DateTime Start = new(2026, 9, 29, 12, 26, 0, DateTimeKind.Utc);

    internal static void Run()
    {
        TheSafeInteriorIsUsedToApproachABiker();
        AnAlignedBikerIsHeldForTheSword();
        TheSwingIsNotSteeredThrough();
        ACriticalEdgeStillWinsWithHysteresis();
        TruckAvoidanceStillWins();
        AThrottledCueKeepsItsCorrection();
        UnreadableRoadOrActorsFallBackSafely();
        TheStoryChaseIsUnchanged();
        Console.WriteLine("PASS G Bike composition: engagement uses the road interior, critical edge and truck avoidance keep priority, story unchanged.");
    }

    private static void TheSafeInteriorIsUsedToApproachABiker()
    {
        var update = Composer().Update(Arcade(80, Enemy(280, 150)), Road(80), Start, false, steeringAudioEnabled: false);
        Equal(HighwaySteeringDirection.UpRight, update.AutomaticDirection, "root's reproduction: the biker is approached, not the road centre");
        Equal(HighwayControlReason.Engagement, update.ControlReason, "and the reason says so");
    }

    private static void AnAlignedBikerIsHeldForTheSword()
    {
        // The biker 100 right and 50 ahead: inside the native sword pocket.
        var update = Composer().Update(Arcade(80, Enemy(180, 50)), Road(80), Start, false, steeringAudioEnabled: false);
        Equal(HighwaySteeringDirection.None, update.AutomaticDirection, "an aligned biker is held, not centred away from");
        Equal(HighwayControlReason.EngagementHold, update.ControlReason, "held for the sword");
    }

    private static void TheSwingIsNotSteeredThrough()
    {
        var update = Composer().Update(Arcade(80, Enemy(280, 150), attackTimer: 19), Road(80), Start, false, steeringAudioEnabled: false);
        Equal(HighwaySteeringDirection.None, update.AutomaticDirection, "attack timer 19: every key released, no centring");
        Equal(HighwayControlReason.EngagementHold, update.ControlReason, "held through the swing");
    }

    private static void ACriticalEdgeStillWinsWithHysteresis()
    {
        var composer = Composer();
        // Biker further out on the right; the bike at 0.76 of the half-width.
        var edge = composer.Update(Arcade(190, Enemy(400, 150)), Road(190), Start, false, steeringAudioEnabled: false);
        Equal(HighwaySteeringDirection.Left, edge.AutomaticDirection, "at 0.76 the road edge wins");
        Equal(HighwayControlReason.RoadEdgeCritical, edge.ControlReason, "as a critical edge");
        var held = composer.Update(Arcade(150, Enemy(400, 150)), Road(150), Start.AddMilliseconds(100), false, steeringAudioEnabled: false);
        Equal(HighwaySteeringDirection.Left, held.AutomaticDirection, "at 0.60 the correction is held until back inside 0.55");
        var released = composer.Update(Arcade(130, Enemy(400, 150)), Road(130), Start.AddMilliseconds(200), false, steeringAudioEnabled: false);
        Equal(HighwayControlReason.Engagement, released.ControlReason, "at 0.52 engagement has the controls again");
        Equal(HighwaySteeringDirection.UpRight, released.AutomaticDirection, "and heads for the biker");
    }

    private static void TruckAvoidanceStillWins()
    {
        // The truck right in front of Cloud; the biker off to the right.
        var update = Composer().Update(Arcade(0, Enemy(200, 150), truck: new HighwayPoint(10, 100)), Road(0), Start, false,
            steeringAudioEnabled: false);
        Equal(HighwayControlReason.TruckAvoidance, update.ControlReason, "the truck collision correction owns the controls");
        Equal(true, update.AutomaticDirection is HighwaySteeringDirection.Down or HighwaySteeringDirection.DownLeft,
            $"and backs off the truck ({update.AutomaticDirection})");
        var swinging = Composer().Update(Arcade(0, Enemy(200, 150), truck: new HighwayPoint(10, 100), attackTimer: 19), Road(0), Start, false,
            steeringAudioEnabled: false);
        Equal(HighwayControlReason.TruckAvoidance, swinging.ControlReason, "even through a swing");
    }

    private static void AThrottledCueKeepsItsCorrection()
    {
        // Long cue intervals: the second sample at the same timestamp publishes no cue, but the
        // held correction keeps its reason and priority.
        var composer = new HighwayAccessibilityComposer(
            new HighwayAccessibilityTracker(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), 500, 300, 1200, 900),
            new HighwaySteeringTracker(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)),
            new HighwayEngagementSteeringTracker(500, 300));
        var first = composer.Update(Arcade(190, Enemy(400, 150)), Road(190), Start, false, steeringAudioEnabled: true);
        var second = composer.Update(Arcade(190, Enemy(400, 150)), Road(190), Start, false, steeringAudioEnabled: true);
        Equal(HighwayControlReason.RoadEdgeCritical, first.ControlReason, "the first sample corrects the edge");
        Equal(null, second.SteeringCue, "the repeated timestamp throttles the cue");
        Equal((HighwayControlReason.RoadEdgeCritical, HighwaySteeringDirection.Left), (second.ControlReason, second.AutomaticDirection),
            "but not the correction");

        var truck = Arcade(0, Enemy(200, 150), truck: new HighwayPoint(10, 100));
        var truckFirst = composer.Update(truck, Road(0), Start.AddSeconds(1), false, steeringAudioEnabled: true);
        var truckSecond = composer.Update(truck, Road(0), Start.AddSeconds(1), false, steeringAudioEnabled: true);
        Equal((truckFirst.ControlReason, truckFirst.AutomaticDirection), (truckSecond.ControlReason, truckSecond.AutomaticDirection),
            "a held truck correction keeps its reason and direction too");
    }

    private static void UnreadableRoadOrActorsFallBackSafely()
    {
        var noRoad = Composer().Update(Arcade(80, Enemy(280, 150)), null, Start, false, steeringAudioEnabled: false);
        Equal((HighwaySteeringDirection.UpRight, HighwayControlReason.Engagement), (noRoad.AutomaticDirection, noRoad.ControlReason),
            "no road reading: engagement alone");
        var noActors = Composer().Update(null, Road(190), Start, false, steeringAudioEnabled: false);
        Equal((HighwaySteeringDirection.Left, HighwayControlReason.RoadEdge), (noActors.AutomaticDirection, noActors.ControlReason),
            "no actor reading: the road correction alone");
        var nothing = Composer().Update(null, null, Start, false, steeringAudioEnabled: false);
        Equal((HighwaySteeringDirection.None, HighwayControlReason.None), (nothing.AutomaticDirection, nothing.ControlReason),
            "nothing readable: nothing pressed");
        var badRoad = Composer().Update(Arcade(80, Enemy(280, 150)), new HighwayRoadState(double.NaN, 250), Start, false,
            steeringAudioEnabled: false);
        Equal(HighwaySteeringDirection.UpRight, badRoad.AutomaticDirection, "an unreadable road value is no road reading");
    }

    private static void TheStoryChaseIsUnchanged()
    {
        var update = Composer().Update(Arcade(80, Enemy(280, 150)) with { IsStoryChase = true }, Road(80), Start, false,
            steeringAudioEnabled: false);
        Equal(HighwaySteeringDirection.Left, update.AutomaticDirection, "the story chase keeps its road-first steering");
        Equal(HighwayControlReason.RoadEdge, update.ControlReason, "for the same reason as before");
    }

    private static HighwayAccessibilityComposer Composer() =>
        new(
            new HighwayAccessibilityTracker(TimeSpan.Zero, TimeSpan.Zero, 500, 300, 1200, 900),
            new HighwaySteeringTracker(TimeSpan.Zero, TimeSpan.Zero),
            new HighwayEngagementSteeringTracker(500, 300));

    private static HighwayRoadState Road(double lateral) => new(lateral, 250);

    private static HighwayEnemyState Enemy(double lateral, double longitudinal) =>
        new(2, 10, true, 5, new HighwayPoint(lateral, longitudinal));

    private static HighwayAccessibilityState Arcade(
        double cloudLateral, HighwayEnemyState enemy, HighwayPoint? truck = null, int attackTimer = 0) =>
        new(
            new HighwayPoint(cloudLateral, 0),
            truck is { } t ? new HighwayPoint(cloudLateral + t.Lateral, t.Longitudinal) : new HighwayPoint(0, 450),
            [enemy],
            Array.Empty<HighwayPartyHealth>(),
            Score: 0,
            IsStoryChase: false,
            CloudAttackTimer: attackTimer);

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"G Bike composition - {label}: expected {expected}, got {actual}.");
        }
    }
}
