using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class MidgarScaffoldNavigationTests
{
    internal static void Run()
    {
        ActualControlGateIsReadWithTheClimb();
        AutomaticNativeClimbsKeepSceneOwnership();
        MountedChoicesStayManualAndNeedCoherentState();
        MountedChoicesDoNotPlayExitSounds();
        MountedGuidanceNeedsNoRoute();
        EmptyCategoryStillExplainsTheLadder();
        TargetPreviewDoesNotCallTheLadderTheWayToAegis();
        Console.WriteLine("PASS Midgar native ladder speech and target previews.");
    }

    private static readonly FieldPositionSnapshot Position = new(1, 733, 0, -3712, -923, 0, 14, 0);
    private static readonly FieldLadderStateSnapshot Mounted = new(true, true, FieldLadderPhase.Mounted,
        FieldNavigationInput.Up, new(-3740, -908, -326), 5, 4, 0);

    private static void ActualControlGateIsReadWithTheClimb()
    {
        const int events=0x02340000;
        byte gate=0;
        var reads=0;
        var torn=false;
        var reader=new FieldLadderStateReader(
            a=>a==FieldNavigationObjectReader.AddressFieldEventDataPtr?events:0,
            a=>(ushort)(a==events+FieldLadderStateReader.TargetTriangleOffset?5:0),
            a=>a==FieldPositionReader.AddressFieldNumModels?(byte)1:
               a==events+FieldLadderStateReader.MovementModeOffset?(byte)4:
               a==FieldLadderStateReader.AddressControlLock?(torn?(byte)(reads++%2):gate):(byte)0);
        Require(reader.Read(Position).State.IsPlayerDriven,"An unlocked native ladder is player-driven.");
        gate=1;
        var automatic=reader.Read(Position);
        Require(automatic.IsUsable && automatic.State.IsMounted && !automatic.State.IsPlayerDriven,"A locked climb still tracks native movement but cannot offer input.");
        var controller=new FieldNavigationController(new FieldNavigationTargetSource([]));
        Require(controller.CreateSpokenGuidance(Position,new(-128),80,ladderState:automatic.State) is null,"Automatic native movement has no manual direction speech.");
        Require(MidgarLadderNavigation.AppendTargets([],Position,[],_=>true,automatic.State).Count==0,"Automatic hidden transitions have no manual ladder rows.");
        var speech=controller.HandleAction(FieldNavigationAction.RepeatTarget,Position,new(-128),automatic.State)?.Speech;
        Require(speech?.Contains("game is moving you") == true && !speech.Contains("climb",StringComparison.OrdinalIgnoreCase),"Menus cannot invent a reverse action during an automatic climb.");
        torn=true;
        Require(!reader.Read(Position).IsUsable,"A control change during the ladder read fails closed.");
    }

    private static void AutomaticNativeClimbsKeepSceneOwnership()
    {
        // Ghidra006342C6 consumes ladder directions only with native UC0;
        // UC1 advances automatically.00614155 mirrors that flag to the cue reader.
        var automatic=new FieldAudibleCueState(true,"scripted control lock",1,1,0,0);
        Require(FieldNavigationSuppressionPolicy.IsNavigationSuppressed(automatic,Mounted with {IsPlayerDriven=false},true),
            "An automatic UC1 climb must not offer player-driven climb/back actions.");
        Require(!FieldNavigationSuppressionPolicy.IsNavigationSuppressed(automatic,Mounted,true),
            "A real manual climb must remain navigable when only the mirrored control byte is stale.");
        var manual=new FieldAudibleCueState(false,"gameplay",1,0,0,0);
        Require(!FieldNavigationSuppressionPolicy.IsNavigationSuppressed(manual,Mounted,true),
            "Native UC0 ladders keep navigation and both real manual directions.");
        Require(FieldNavigationSuppressionPolicy.IsNavigationSuppressed(manual with {IsSuppressed=true,ActiveMessageCount=1},Mounted,true),
            "A mounted ladder never takes input from a native window.");
    }

    private static void MountedGuidanceNeedsNoRoute()
    {
        var controller = new FieldNavigationController(new FieldNavigationTargetSource([]));
        foreach (var field in new[] {733, 734, 735, 225, 561})
        {
            var position = Position with {FieldId = field};
            var speech = controller.CreateSpokenGuidance(position, new(-128), 80, ladderState: Mounted)?.Speech;
            Require(speech?.Contains("climb up, or down to go back") == true, "Both ways off the native ladder need speech without a route.");
            Require(!controller.BeaconEnabled && !controller.TryResolveAutomaticInput(position, new(-128), 80, out _), "Climbing stays manual.");
        }
        Require(controller.CreateSpokenGuidance(Position, new(-128), 80, ladderState: default) is null, "Unreadable ladder state stays silent.");
        Require(controller.CreateSpokenGuidance(Position, new(-128), 80, ladderState: FieldLadderStateSnapshot.NotMounted) is null, "Dismounted state stays silent.");
    }

    private static void EmptyCategoryStillExplainsTheLadder()
    {
        var controller = new FieldNavigationController(new FieldNavigationTargetSource([]));
        var speech = controller.HandleAction(FieldNavigationAction.RepeatTarget, Position, new(-128), Mounted)?.Speech;
        Require(speech?.Contains("on a ladder", StringComparison.OrdinalIgnoreCase) == true && speech.Contains("climb up", StringComparison.OrdinalIgnoreCase),
            $"An empty category must still identify the native ladder: {speech}");
    }

    private static void TargetPreviewDoesNotCallTheLadderTheWayToAegis()
    {
        var target = new FieldNavigationTarget(733, FieldNavigationCategory.Objects, "Aegis Armlet", -3811, 1050, 512);
        var controller = new FieldNavigationController(new FieldNavigationTargetSource([target]));
        for (var i=0; i<3; i++) controller.HandleAction(FieldNavigationAction.NextCategory, Position, new(-128), Mounted);
        var speech = controller.HandleAction(FieldNavigationAction.RepeatTarget, Position, new(-128), Mounted)?.Speech;
        Require(speech?.Contains("Aegis Armlet") == true && speech.Contains("on a ladder", StringComparison.OrdinalIgnoreCase),
            $"Separate the player's current climb from the selected item: {speech}");
        Require(!controller.BeaconEnabled, "Browsing an item must not start a climb.");
    }

    private static void MountedChoicesStayManualAndNeedCoherentState()
    {
        foreach (var field in new[] {733, 734, 735, 736, 778})
        foreach (var direction in new[] {FieldNavigationInput.Up, FieldNavigationInput.Down})
        {
            var position = Position with {FieldId = field};
            var rows = MidgarLadderNavigation.AppendTargets([], position, [], _ => false, Mounted with {RequiredInput = direction});
            Require(rows.Count == 2, "A native arrival ladder offers both manual ends even with no floor exits.");
            var reachable = new ReachableFieldExitTargetProvider(_ => rows, new RejectPlanner());
            Require(reachable.ReadTargets(position).Count == 2, "Manual ladder choices do not need a walking route from the arrival pad.");
            var controller = new FieldNavigationController(new FieldNavigationTargetSource(rows), new RejectPlanner());
            var speech = controller.HandleAction(FieldNavigationAction.ToggleBeacon, position, new(-128), Mounted)?.Speech;
            Require(speech?.Contains(direction == FieldNavigationInput.Up ? "Hold up" : "Hold down") == true, "The native forward climb is selected first and uses normal input.");
            Require(!controller.BeaconEnabled && !controller.TryResolveAutomaticInput(position, new(-128), 80, out _), "Manual ladder rows cannot inject climbing input.");
            Require(MidgarLadderNavigation.AppendTargets([], position, [], _ => true, default).Count == 0, "An unreadable ladder cannot create an entrance.");
            Require(MidgarLadderNavigation.AppendTargets([], position, [], _ => true, FieldLadderStateSnapshot.NotMounted).Count == 0, "Dismount removes manual ends.");
        }
        Require(MidgarLadderNavigation.AppendTargets([], Position with {FieldId=700}, [], _ => true, Mounted).Count == 0, "Do not invent Midgar destinations elsewhere.");
        var ordinaryManual=new FieldNavigationTarget(700,FieldNavigationCategory.Exits,"Another manual exit",0,0,0,"ordinary-manual",
            ManualNavigationGuidance:"Use normal buttons here.");
        Require(new ReachableFieldExitTargetProvider(_ => [ordinaryManual],new RejectPlanner()).ReadTargets(Position with {FieldId=700}).Count==0,
            "Other manual exits keep their existing reachability rules.");
    }

    private static void MountedChoicesDoNotPlayExitSounds()
    {
        var rows=MidgarLadderNavigation.AppendTargets([],Position,[],_=>false,Mounted);
        var tracker=new FieldExitProximityCueTracker(80,640,TimeSpan.Zero);
        Require(tracker.Update(Position,rows,DateTime.UtcNow).Count==0 && !tracker.HasAudibleTargets,
            "Temporary climb choices must not emit exit sounds at the player's own position.");
        var actualExit=new FieldNavigationTarget(Position.FieldId,FieldNavigationCategory.Exits,"A real exit",Position.X,Position.Y,Position.Z,"script-exit:733:10:735");
        Require(tracker.Update(Position,[..rows,actualExit],DateTime.UtcNow).Single().Target.StableId==actualExit.StableId,
            "Real nearby exits retain their sounds while temporary climb choices stay silent.");
    }

    private sealed class RejectPlanner : IFieldNavigationRoutePlanner
    {
        public string LastDiagnostic => "No walking route from a native ladder pad";
        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle) { triangle=-1; return false; }
        public bool TryGetNextWaypoint(FieldPositionSnapshot position, FieldNavigationTarget target, out FieldNavigationRouteWaypoint waypoint) { waypoint=default; return false; }
        public bool TryBuildRoute(FieldPositionSnapshot position, FieldNavigationTarget target, out FieldNavigationRoutePlan route)
        {
            route = default!;
            return false;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
