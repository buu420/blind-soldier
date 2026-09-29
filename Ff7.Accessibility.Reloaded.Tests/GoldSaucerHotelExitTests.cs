using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The Gold Saucer's station (491) to the Hotel Lobby (492), 2026-09-29 12:17:33-34. Auto walk
/// brought the party onto the gateway (-239,1345, 4 units left); on the very next sample the exit
/// was not in the list and the walk ended "Exit to Hotel Lobby is no longer reachable.
/// Navigation off.", and in the same second the game arrived in 492. The party was walking
/// through the door, not shut out of it.
///
/// <para>No native flag says a field change is pending, so the controller holds a short,
/// bounded time when the chosen exit goes away with the party standing at it: a field change in
/// that time is judged by the ordinary rule (the selected gateway into the expected field is
/// "reached"; anywhere else is "Left the area"), the exit coming back carries on, and past the
/// grace it is "no longer reachable" as before. An exit that goes away while the party is
/// anywhere else is reported at once, as before.</para>
/// </summary>
internal static class GoldSaucerHotelExitTests
{
    private const int Station = 491;
    private const int Lobby = 492;
    private static readonly FieldNavigationTriggerLine Gateway = new(-260, 1330, 185, -206, 1346, 185);
    private static readonly DateTime Start = new(2026, 9, 29, 12, 17, 30, DateTimeKind.Utc);

    public static void Run()
    {
        TheLoggedWalkThroughTheDoorIsReached();
        AnExitRemovedWhileFarAwayIsReportedAtOnce();
        AnExitRemovedJustShortOfItIsReportedAtOnce();
        AnExitThatStaysGoneIsReportedAfterTheGrace();
        AnExitThatComesBackCarriesOn();
        ArrivingSomewhereElseIsNotReached();
        CancellingDuringTheGraceSaysNothingLater();
        Console.WriteLine("Gold Saucer hotel exit tests passed.");
    }

    private static void TheLoggedWalkThroughTheDoorIsReached()
    {
        var (controller, present) = Walk(At(-261, 1307));
        Equal(false, Ended(Update(controller, At(-239, 1345), 100)), "on the gateway, still listed: walking on");
        present.Value = false;
        Equal(false, Ended(Update(controller, At(-239, 1345), 200)), "the exit leaves the list with the party on it: held, not refused");
        Equal("Exit to Hotel Lobby reached. Navigation off.",
            Update(controller, new FieldPositionSnapshot(FieldPositionReader.FieldModule, Lobby, 0, -515, -30, -10, 80, 68), 600),
            "the game arrives in the lobby: the exit was reached");
    }

    private static void AnExitRemovedWhileFarAwayIsReportedAtOnce()
    {
        var (controller, present) = Walk(At(-261, 1107));
        present.Value = false;
        Equal("Exit to Hotel Lobby is no longer reachable. Navigation off.", Update(controller, At(-261, 1100), 100),
            "an exit that goes away with the party nowhere near it is reported at once");
    }

    /// <summary>
    /// Near is not on: a door switched off with the party forty units short of it is shut, and is
    /// said so at once (the same rule FieldPuzzleStoryTests holds for scripted doorways).
    /// </summary>
    private static void AnExitRemovedJustShortOfItIsReportedAtOnce()
    {
        var (controller, present) = Walk(At(-261, 1207));
        Equal(false, Ended(Update(controller, At(-255, 1300), 100)), "forty-odd units short of the gateway");
        present.Value = false;
        Equal("Exit to Hotel Lobby is no longer reachable. Navigation off.", Update(controller, At(-255, 1300), 150),
            "switched off before the party reached it: reported at once");
    }

    private static void AnExitThatStaysGoneIsReportedAfterTheGrace()
    {
        var (controller, present) = Walk(At(-261, 1307));
        Equal(false, Ended(Update(controller, At(-239, 1345), 100)), "on the gateway");
        present.Value = false;
        string? spoken = null;
        var ms = 150;
        for (; ms <= 5000 && !Ended(spoken); ms += 50)
        {
            spoken = Update(controller, At(-239, 1345), ms);
        }

        ms -= 50;

        Equal("Exit to Hotel Lobby is no longer reachable. Navigation off.", spoken, "a door that stays shut is still reported");
        Equal(true, ms - 150 <= 2000, $"and within the bounded grace, not left hanging ({ms - 150} ms)");
        Equal(false, controller.BeaconEnabled, "and navigation is off");
    }

    private static void AnExitThatComesBackCarriesOn()
    {
        var (controller, present) = Walk(At(-261, 1307));
        Equal(false, Ended(Update(controller, At(-239, 1345), 100)), "on the gateway");
        present.Value = false;
        Equal(false, Ended(Update(controller, At(-239, 1345), 150)), "gone for a sample");
        present.Value = true;
        Equal(false, Ended(Update(controller, At(-239, 1345), 200)), "back again");
        for (var ms = 250; ms <= 4000; ms += 250)
        {
            Equal(false, Ended(Update(controller, At(-239, 1345), ms)), $"at {ms} ms the route is still on");
        }

        Equal(true, controller.BeaconEnabled, "navigation carries on");
    }

    private static void ArrivingSomewhereElseIsNotReached()
    {
        var (controller, present) = Walk(At(-261, 1307));
        Equal(false, Ended(Update(controller, At(-239, 1345), 100)), "on the gateway");
        present.Value = false;
        Equal(false, Ended(Update(controller, At(-239, 1345), 150)), "held");
        Equal("Left the area. Navigation to Exit to Hotel Lobby cancelled.",
            Update(controller, new FieldPositionSnapshot(FieldPositionReader.FieldModule, 509, 0, 0, 0, 0, 0, 0), 400),
            "a field change to somewhere else is not the Hotel Lobby");
    }

    private static void CancellingDuringTheGraceSaysNothingLater()
    {
        var (controller, present) = Walk(At(-261, 1307));
        Equal(false, Ended(Update(controller, At(-239, 1345), 100)), "on the gateway");
        present.Value = false;
        Equal(false, Ended(Update(controller, At(-239, 1345), 150)), "held");
        _ = controller.HandleAction(FieldNavigationAction.ToggleBeacon, At(-239, 1345), new FieldNavigationControlTransform(-128));
        Equal(false, controller.BeaconEnabled, "the player turned navigation off");
        for (var ms = 200; ms <= 4000; ms += 200)
        {
            Equal(false, Ended(Update(controller, At(-239, 1345), ms)), $"nothing is said for the cancelled route at {ms} ms");
        }
    }

    private sealed class Flag
    {
        public bool Value { get; set; } = true;
    }

    private static (FieldNavigationController Controller, Flag Present) Walk(FieldPositionSnapshot start)
    {
        var present = new Flag();
        var exit = new FieldNavigationTarget(Station, FieldNavigationCategory.Exits, "Exit to Hotel Lobby", -233, 1338, 185,
            StableId: "gateway:491:0:492", DestinationFieldIds: [Lobby], TriggerLine: Gateway);
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource([], exitTargetProvider: _ => present.Value ? [exit] : []), new StraightPlanner());
        var transform = new FieldNavigationControlTransform(-128);
        for (var step = 0; step < 8 && controller.CurrentCategory != FieldNavigationCategory.Exits; step++)
        {
            _ = controller.HandleAction(FieldNavigationAction.NextCategory, start, transform);
        }

        _ = controller.HandleAction(FieldNavigationAction.ToggleBeacon, start, transform);
        Equal(true, controller.BeaconEnabled, "navigation to the Hotel Lobby exit started");
        controller.NoteAutoWalkStarted();
        _ = Update(controller, start, 0);
        return (controller, present);
    }

    private static string? Update(FieldNavigationController controller, FieldPositionSnapshot position, int milliseconds) =>
        controller.UpdateLiveTracking(position, new FieldNavigationInputSnapshot(0, FieldNavigationInput.None),
            new FieldNavigationControlTransform(-128), false, 80, observedAt: Start.AddMilliseconds(milliseconds))?.Speech;

    /// <summary>Whether the route was ended by what was said: arrival, refusal or cancellation.</summary>
    private static bool Ended(string? spoken) =>
        spoken is not null && (spoken.Contains("Navigation off", StringComparison.Ordinal) ||
                               spoken.Contains("cancelled", StringComparison.Ordinal));

    private static FieldPositionSnapshot At(int x, int y) =>
        new(FieldPositionReader.FieldModule, Station, 0, x, y, 175, 332, 96);

    private sealed class StraightPlanner : IFieldNavigationRoutePlanner
    {
        public string LastDiagnostic => "straight fixture";

        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle)
        {
            triangle = position.TriangleId;
            return true;
        }

        public bool TryBuildRoute(FieldPositionSnapshot position, FieldNavigationTarget target, out FieldNavigationRoutePlan plan)
        {
            plan = new(position.FieldId, target.StableId!, [position.TriangleId], [],
                new(target.X, target.Y, target.Z), position.TriangleId);
            return true;
        }

        public bool TryGetNextWaypoint(FieldPositionSnapshot position, FieldNavigationTarget target, out FieldNavigationRouteWaypoint waypoint)
        {
            waypoint = new(target.X, target.Y, target.Z);
            return true;
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Gold Saucer hotel exit - {label}: expected {expected}, got {actual}.");
        }
    }
}
