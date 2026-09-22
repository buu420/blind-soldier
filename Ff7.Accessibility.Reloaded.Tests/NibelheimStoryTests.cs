using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The present-day visit to Nibelheim, reported from a Steam 2026 x64 session at GameMoment
/// 523: the party is inside the Shinra Mansion and Story is empty.
///
/// <para>It was empty everywhere in that town, on both runtimes, because this is catalog
/// data rather than runtime code. Every row the catalog had for fields 282-285 and for the
/// fourteen <c>sinin*</c> mansion fields was bounded to the flashback - GameMoments 353-376,
/// with a pair at 677. The present-day crossing is 523-534, and
/// <c>tools/story-regions/MountNibel.ps1</c> curates that window starting at field 311
/// <c>mtnvl2</c>, which is reached from the world map after Nibelheim.</para>
///
/// <para>Story carries the way on and the way out. The optional things are not Story rows:
/// they are in <see cref="NibelheimObjectCatalog"/> and reach the player through Objects,
/// gated on the live LINON state of their own entity and on the native player radius.</para>
///
/// <para>Field identities are from the entity rosters, not the names: 282 <c>nivl</c> is the
/// flashback town, 285 <c>nivl_4</c> is the Lucrecia scene, and <b>284 <c>nivl_3</c></b> is
/// the present-day town - all nine party entities, plus the <c>mant</c> black capes its own
/// Main shows while <c>384 &lt; moment &lt;= 677</c>.</para>
/// </summary>
internal static class NibelheimStoryTests
{
    private const int NibelheimTown = 284;
    private const int MansionHall = 297;
    private const int MansionSideRooms = 298;
    private const int MansionUpstairsLeft = 299;
    private const int MansionUpstairsRight = 300;
    private const int MansionSpiralStair = 301;
    private const int BasementFoot = 302;
    private const int BasementCorridor = 303;
    private const int BasementLibraryFlashback = 304;
    private const int BasementLibrary = 305;
    private const int BasementLibraryLucrecia = 306;
    private const int FarLibraryFlashback = 307;
    private const int ReunionRoom = 308;
    private const int InnerRoomFlashback = 309;
    private const int InnerRoom = 310;

    /// <summary>Cosmo Canyon's exit write, and the last moment before Rocket Town's 535.</summary>
    private const int PresentVisitStart = 523;
    private const int PresentVisitEnd = 534;

    /// <summary>What the fixture writes at the player event's +0x72, as a field model has.</summary>
    private const int PlayerCollisionRadius = 40;

    private static readonly int[] MansionFields =
    [
        MansionHall, MansionSideRooms, MansionUpstairsLeft, MansionUpstairsRight,
        MansionSpiralStair, BasementFoot, BasementCorridor, BasementLibraryFlashback,
        BasementLibrary, BasementLibraryLucrecia, FarLibraryFlashback, ReunionRoom,
        InnerRoomFlashback, InnerRoom,
    ];

    private static readonly int[] TownBuildings = [270, 271, 272, 273, 274, 276, 286, 287];

    /// <summary>Everything that needs no installed archive.</summary>
    public static void Run()
    {
        TheTownOffersTheWayOnTowardMountNibel();
        EveryMansionRoomOffersAWayBackOut();
        EveryTownBuildingGivesTheWayBackOut();
        EveryWayOutCarriesItsNativeCrossing();
        TheMansionIsNotAnErrand();
        TheFlashbackKeepsItsOwnObjectives();
        TheOptionalThingsAreObjectsRatherThanStory();
        TheOptionalThingsSurviveThePresentChapter();
        EveryLineObjectUsesTheNativeActivationRadius();
        ADisabledLineHidesItsObject();
        AnEmptiedSafeStopsBeingOffered();
        OpenedChestRemainsAvailableForItsInscription();
        ThePassageDoorDisappearsAfterOpening();
        TheSafeAndCoffinRemainAvailableBetweenQuestSteps();
        ACoffinThatHasBeenDealtWithStopsBeingOffered();
        TheReunionSceneIsNotOfferedAsAnInteraction();
    }

    /// <summary>
    /// The optional set is not a chapter. Nothing in these scripts has an upper GameMoment
    /// bound, so a player who comes back to Nibelheim after Rocket Town - or at any point
    /// later - still finds them. The first pass bounded every row to 523-534, which hid all
    /// of it the moment the story moved on.
    ///
    /// <para>The lower bounds are each script's own: sinin1_1's note tests
    /// <c>Bank[2][0] &lt; 385</c> and hides itself below that, and the hints and the safe test
    /// <c>&gt; 385</c>. The flashback's own scripted visits stay clear either way.</para>
    /// </summary>
    private static void TheOptionalThingsSurviveThePresentChapter()
    {
        foreach (var field in new[] { 297, 298, 299, 300, 303, 305, 310 })
        {
            foreach (var moment in new[] { 523, 534, 700, 1500 })
            {
                Equal(true, ObjectLabels(field, moment: moment).Length > 0,
                    $"field {field}: its optional things must still be there at moment {moment}");
            }

            foreach (var moment in new[] { 370, 374, 376 })
            {
                Equal(0, ObjectLabels(field, moment: moment).Length,
                    $"field {field}: nothing optional may appear during the flashback at " +
                    $"moment {moment}");
            }
        }

        // The note's own Init is the one that differs: it is there from 385, the others from
        // 386. Both sides of that boundary are the script's, not a rounding.
        Equal(true, ObjectLabels(297, moment: 385).Any(label => label.Contains("Note", StringComparison.Ordinal)),
            "the note is there at 385, which is where its own Init stops hiding it");
        Equal(0, ObjectLabels(298, moment: 385).Length,
            "and the hints are not, because theirs test strictly greater than 385");
    }

    /// <summary>
    /// Every LINE row here is a Go or an [OK]. Both need the party inside the native player
    /// radius at event+0x72 - the same strict squared test the observatory lines use - not
    /// within the reader's default arrival distance, which is wider than the radius and would
    /// stop the walk outside the trigger.
    /// </summary>
    private static void EveryLineObjectUsesTheNativeActivationRadius()
    {
        foreach (var definition in NibelheimObjectCatalog.Create())
        {
            if (definition.TargetKind != FieldNavigationObjectTargetKind.Line)
            {
                continue;
            }

            Equal(true, definition.UsesPlayerCollisionRadius,
                $"field {definition.FieldId} entity {definition.EntityId} " +
                $"('{definition.Label}') is a native line handler and must use the player radius");
        }

        // And the reader has to honour it: with the player radius readable, the offered
        // target carries radius - 1 rather than the default.
        var memory = PresentDay(529, MansionSideRooms);
        var piano = memory.ObjectReader().ReadTargets(At(MansionSideRooms, 0, 0, 0))
            .Single(target => target.Label.Contains("Piano", StringComparison.Ordinal));
        Equal(PlayerCollisionRadius - 1, piano.InteractionRadius,
            "the offered piano must carry the native radius, not the default arrival distance");
    }

    /// <summary>
    /// Bank[13][80] bit 2 is what sininb2's own lin1 Go tests before it does anything; with
    /// it set the handler falls through without running the scene.
    /// </summary>
    private static void ACoffinThatHasBeenDealtWithStopsBeingOffered()
    {
        Equal(true, ObjectLabels(BasementCorridor).Any(label => label.Contains("Coffin", StringComparison.Ordinal)),
            "the coffin is offered while its own gate is clear");

        var memory = PresentDay(529, BasementCorridor);
        memory.SetBank13(80, 4);
        Equal(false,
            memory.ObjectReader().ReadTargets(At(BasementCorridor, 0, 0, 0))
                .Any(target => target.Label.Contains("Coffin", StringComparison.Ordinal)),
            "and stops once Bank[13][80] bit 2 is set");
    }

    /// <summary>
    /// The parts that read the installed field data: the native replay, and the native
    /// evidence for field 308's automatic scene. Fails rather than skips when
    /// <c>FF7_ACCESSIBILITY_DATA_ROOT</c> is configured but unusable - a corrupted archive
    /// must not read as coverage.
    /// </summary>
    public static void RunWithInstalledGameData()
    {
        var gameRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(gameRoot))
        {
            Console.WriteLine("nibelheim: FF7_ACCESSIBILITY_DATA_ROOT is unset, native cases not run");
            return;
        }

        TheReunionSceneRunsFromItsOwnDirector();
        TheReunionRoomIsReachedByAnOrdinaryGateway();
        EveryAuthoredTargetRoutesFromItsNativeEntry();
        EveryOptionalObjectIsReachableFromItsNativeEntry();
        VincentQuestInteractionsMatchInstalledScripts();
        QuestRoutesRespectThePassageAndBasementLocks();
        QuestInteractionsCanActuallyStartNavigation();
    }

    /// <summary>
    /// Standing in the present-day town, the objective is the gateway that puts the party on
    /// the mountain side of the world map, not the one that puts them back where they came
    /// from.
    /// </summary>
    private static void TheTownOffersTheWayOnTowardMountNibel()
    {
        foreach (var moment in new[] { PresentVisitStart, 529, PresentVisitEnd })
        {
            var targets = Targets(NibelheimTown, moment);
            Equal(true, targets.Count > 0,
                $"moment {moment}: the present-day town must offer an objective, not none");
            Equal(true, targets.Any(target => target.X == -39 && target.Y == 3098),
                $"moment {moment}: and it must be gateway 7, the Mt. Nibel side of the world map");
        }
    }

    /// <summary>The reported case: every room in the fourteen-field tree names its way back.</summary>
    private static void EveryMansionRoomOffersAWayBackOut()
    {
        foreach (var field in MansionFields)
        {
            Equal(true, Targets(field).Count > 0,
                $"field {field}: a room in the mansion must offer the way back out, not none");
        }
    }

    /// <summary>
    /// The buildings the user walked through at moment 523 with Story empty: the inn and its
    /// upper floor, the item store, Cloud's house, Tifa's house and its upper floor, and the
    /// house at nvmin1_1 with the room above it. Names are each field's own MPNAM.
    /// </summary>
    private static void EveryTownBuildingGivesTheWayBackOut()
    {
        foreach (var field in TownBuildings)
        {
            Equal(true, Targets(field).Count > 0,
                $"field {field}: a Nibelheim building must offer the way back out, not none");
        }
    }

    /// <summary>
    /// A door target with no trigger line completes by proximity, which is how a route stops
    /// short of the thing the game reacts to. Every way out here carries its own gateway's
    /// exit line, and its point is the midpoint of that line.
    /// </summary>
    private static void EveryWayOutCarriesItsNativeCrossing()
    {
        foreach (var field in MansionFields.Concat(TownBuildings).Append(NibelheimTown))
        {
            foreach (var target in Targets(field))
            {
                Equal(true, target.TriggerLine is not null,
                    $"field {field}: '{target.Label}' must carry its native gateway line");
                var line = target.TriggerLine!.Value;
                var offLine = DistanceSquaredToSegment(target.X, target.Y, line);
                Equal(true, offLine <= 2,
                    $"field {field}: '{target.Label}' must sit on that line, not {offLine} " +
                    "squared units off it");
            }
        }
    }

    /// <summary>
    /// Nothing may send a player who is outside the mansion into it, and no Story row may be
    /// an optional errand: the optional things live in Objects now.
    /// </summary>
    private static void TheMansionIsNotAnErrand()
    {
        var town = Targets(NibelheimTown);

        // Gateway 3 is the mansion door, at the midpoint of (-635,1335,202)-(-567,1380,202).
        Equal(false, town.Any(target => target.X == -601 && target.Y == 1357),
            "the town must not send anyone to the mansion door as a required step");

        foreach (var field in MansionFields.Concat(TownBuildings).Append(NibelheimTown))
        {
            foreach (var target in Targets(field))
            {
                Equal(false, target.Label.Contains("optional", StringComparison.OrdinalIgnoreCase),
                    $"field {field}: Story carries no optional errands - '{target.Label}'");
                Equal(false, target.Label.Contains("safe", StringComparison.OrdinalIgnoreCase),
                    $"field {field}: and never names the safe - '{target.Label}'");
            }
        }
    }

    /// <summary>The flashback rows must be exactly where they were.</summary>
    private static void TheFlashbackKeepsItsOwnObjectives()
    {
        var labels = Targets(BasementLibraryFlashback, 370).Select(target => target.Label).ToArray();
        Equal(true, labels.Contains("Find Sephiroth in the mansion library"),
            "the flashback library objective must survive untouched");
    }

    /// <summary>
    /// The optional set, in the category the user asked for. Most are native LINE handlers
    /// with no model, and the opened chest needs a readable target after its item is gone.
    /// </summary>
    private static void TheOptionalThingsAreObjectsRatherThanStory()
    {
        foreach (var (field, fragment) in new[]
                 {
                     (297, "Note"),
                     (298, "Piano"),
                     (298, "Writing"),
                     (299, "Safe"),
                     (300, "Writing"),
                     (300, "Floor beside the secret passage"),
                     (300, "Secret passage door"),
                     (303, "Coffin"),
                     (305, "Left specimen"),
                     (305, "Right specimen"),
                     (310, "report"),
                 })
        {
            var labels = ObjectLabels(field);
            Equal(true, labels.Any(label => label.Contains(fragment, StringComparison.Ordinal)),
                $"field {field}: Objects must carry '{fragment}'; got " +
                (labels.Length == 0 ? "none" : string.Join(" | ", labels)));
        }

        Equal(4, ObjectLabels(InnerRoom).Count(label => label.Contains("report", StringComparison.Ordinal)),
            "the four documents in the innermost basement room are separate readables");
        Equal(false, ObjectLabels(InnerRoom).Any(label => label.Contains("coffin", StringComparison.OrdinalIgnoreCase)),
            "field 310 has no coffin - its own entities are four document lines and the party");

        // These are ordinary walkable interaction points. ManualNavigationGuidance is
        // reserved for targets navigation cannot approach; setting it disables the beacon.
        foreach (var definition in NibelheimObjectCatalog.Create()
                     .Where(definition => definition.FieldId is 298 or 299 &&
                                          definition.EntityId is 10 or 9))
        {
            Equal(true, string.IsNullOrWhiteSpace(definition.ManualNavigationGuidance),
                $"field {definition.FieldId} entity {definition.EntityId} must be " +
                "navigable rather than marked manual-only");
        }

        // No label may give away a hint, a combination or a reward.
        foreach (var definition in NibelheimObjectCatalog.Create())
        {
            foreach (var forbidden in new[] { "dial", "combination", "left ", "right ", "key" })
            {
                if (forbidden is "left " or "right " && definition.Label?.Contains("specimen") == true)
                {
                    continue;
                }

                Equal(false, definition.Label?.Contains(forbidden, StringComparison.OrdinalIgnoreCase) == true,
                    $"'{definition.Label}' must not expose a solution or a reward");
            }
        }
    }

    /// <summary>
    /// A LINE the script has switched off opens nothing, so the Objects reader must not
    /// offer it. Every row here except the note is a LINE.
    /// </summary>
    private static void ADisabledLineHidesItsObject()
    {
        foreach (var field in new[] { 298, 299, 300, 303, 305, 310 })
        {
            Equal(0, ObjectLabels(field, linesEnabled: false).Length,
                $"field {field}: nothing may be offered while its lines are switched off");
            Equal(true, ObjectLabels(field).Length > 0,
                $"field {field}: and it must come back when they are on");
        }
    }

    /// <summary>
    /// Bank[1][232] bit 1 is the safe emptied - sinin2_1's own lin1 Go sets it at byte 93.
    /// Once it is set there is nothing left to offer.
    /// </summary>
    private static void AnEmptiedSafeStopsBeingOffered()
    {
        var before = ObjectLabels(MansionUpstairsLeft);
        Equal(true, before.Any(label => label.Contains("Safe", StringComparison.Ordinal)),
            "the safe is offered while it still has something in it");

        var memory = PresentDay(529, MansionUpstairsLeft);
        memory.SetBank1(232, 2);
        var after = memory.ObjectReader().ReadTargets(At(MansionUpstairsLeft, 0, 0, 0))
            .Select(target => target.Label).ToArray();
        Equal(false, after.Any(label => label.Contains("Safe", StringComparison.Ordinal)),
            "and stops being offered once its own flag is set");
    }

    private static void OpenedChestRemainsAvailableForItsInscription()
    {
        var memory = PresentDay(529, MansionUpstairsLeft);
        Equal(false, memory.ObjectReader().ReadTargets(At(MansionUpstairsLeft, 0, 0, 0))
            .Any(target => target.Label == "Opened chest in the mansion upstairs room"),
            "the inscription interaction must not duplicate the unopened treasure");
        memory.SetBank15(35, 8);
        var opened = memory.ObjectReader().ReadTargets(At(MansionUpstairsLeft, 0, 0, 0));
        Equal(true, opened.Any(target => target.Label == "Opened chest in the mansion upstairs room"),
            "collecting Enemy Launcher must leave its chest available for the native inscription");
    }

    private static void ThePassageDoorDisappearsAfterOpening()
    {
        var memory = PresentDay(529, MansionUpstairsRight);
        memory.SetBank5(7, 1);
        Equal(false, memory.ObjectReader().ReadTargets(At(MansionUpstairsRight, 0, 0, 0))
            .Any(target => target.Label.StartsWith("Secret passage door", StringComparison.Ordinal)),
            "an open passage must not keep asking the player to open it");
    }

    private static void TheSafeAndCoffinRemainAvailableBetweenQuestSteps()
    {
        var safe = PresentDay(529, MansionUpstairsLeft);
        safe.SetBank1(232, 1);
        Equal(true, safe.ObjectReader().ReadTargets(At(MansionUpstairsLeft, 0, 0, 0))
            .Any(target => target.Label.StartsWith("Safe", StringComparison.Ordinal)),
            "after Lost Number the safe must still lead to the uncollected basement key");
        foreach (var conversationFlags in new[] { 0, 32, 96, 224 })
        {
            var memory = PresentDay(529, BasementCorridor);
            memory.SetBank1(231, conversationFlags);
            Equal(true, memory.ObjectReader().ReadTargets(At(BasementCorridor, 0, 0, 0))
                .Any(target => target.Label.Contains("Coffin", StringComparison.Ordinal)),
                $"coffin remains an interaction until recruitment, conversation bits {conversationFlags}");
        }
    }

    private static void VincentQuestInteractionsMatchInstalledScripts()
    {
        var scripts = Scripts();
        AssertOpcode(scripts.ReadScriptOpcodes(299, 7, 1), 4, [0x14, 0xF0, 0x23, 3, 10, 0x45],
            "opened chest chooses its second interaction after its treasure flag");
        AssertOpcode(scripts.ReadScriptOpcodes(299, 7, 1), 88, [0x40, 0, 0xB2],
            "opened chest still displays its inscription");
        foreach (var (entity, expected) in new (int, byte[])[]
                 {
                     (7, [0xD0, 0x57, 3, 0x92, 2, 0x53, 1, 0xBE, 3, 0x4D, 2, 0x53, 1]),
                     (8, [0xD0, 0x46, 3, 0x48, 1, 0x53, 1, 0x16, 3, 0x48, 1, 0x53, 1]),
                 })
        {
            var line = scripts.ReadScriptOpcodes(300, entity, 0).Single(op => op.Opcode == 0xD0);
            AssertOpcode(scripts.ReadScriptOpcodes(300, entity, 0), entity == 7 ? 0 : 8, expected,
                $"upstairs entity {entity} owns its native interaction line");
            var row = NibelheimObjectCatalog.Create().Single(d => d.FieldId == 300 && d.EntityId == entity);
            var bytes = line.Bytes.ToArray();
            Equal((BitConverter.ToInt16(bytes, 1) + BitConverter.ToInt16(bytes, 7)) / 2, row.StaticX,
                "interaction X is the native line midpoint");
            Equal((BitConverter.ToInt16(bytes, 3) + BitConverter.ToInt16(bytes, 9)) / 2, row.StaticY,
                "interaction Y is the native line midpoint");
            Equal((BitConverter.ToInt16(bytes, 5) + BitConverter.ToInt16(bytes, 11)) / 2, row.StaticZ,
                "interaction Z is the native line midpoint");
        }
        AssertOpcode(scripts.ReadScriptOpcodes(300, 7, 1), 6, [0x80, 0x50, 7, 1],
            "the passage remembers being opened in Bank 5 address 7");
        AssertOpcode(scripts.ReadScriptOpcodes(300, 7, 1), 10, [0x6D, 143, 0, 0], "door unlocks triangle 143");
        AssertOpcode(scripts.ReadScriptOpcodes(300, 7, 1), 14, [0x6D, 67, 0, 0], "door unlocks triangle 67");
        AssertOpcode(scripts.ReadScriptOpcodes(300, 7, 1), 18, [0x6D, 120, 0, 0], "door unlocks triangle 120");
        AssertOpcode(scripts.ReadScriptOpcodes(299, 9, 4), 93, [0x82, 0x10, 232, 1],
            "taking the key empties the safe");
        AssertOpcode(scripts.ReadScriptOpcodes(303, 0, 0), 16, [0x14, 0x10, 232, 1, 10, 5],
            "the coffin-room lock uses that same key-collected bit");
        AssertOpcode(scripts.ReadScriptOpcodes(303, 0, 0), 22, [0x6D, 34, 0, 1],
            "without the key triangle 34 stays locked");
        AssertOpcode(scripts.ReadScriptOpcodes(303, 15, 4), 0, [0x14, 0xD0, 80, 2, 10, 29],
            "the coffin interaction remains until Vincent has joined");
        AssertOpcode(scripts.ReadScriptOpcodes(302, 11, 3), 46, [0x82, 0xD0, 80, 2],
            "the passage back toward the stairs recruits Vincent and retires the coffin target");
    }

    private static void QuestRoutesRespectThePassageAndBasementLocks()
    {
        var upstairs = PresentDay(529, MansionUpstairsRight);
        upstairs.SetLockedTriangles(143, 67, 120);
        var planner = new FieldWalkmeshRoutePlanner(InstalledWalkmesh(MansionUpstairsRight), upstairs.BoundaryReader());
        var start = new FieldPositionSnapshot(1, MansionUpstairsRight, 0, 354, 788, 277, 151, 0);
        var door = upstairs.ObjectReader().ReadTargets(start)
            .Single(target => target.Label.StartsWith("Secret passage door", StringComparison.Ordinal));
        Equal(true, planner.TryBuildRoute(start, door, out var doorPlan),
            "the door interaction must be reachable before the passage unlocks");
        var dx = doorPlan.FinalApproach.X - door.X;
        var dy = doorPlan.FinalApproach.Y - door.Y;
        Equal(true, dx * dx + dy * dy < door.InteractionRadius * door.InteractionRadius,
            "the locked-side approach must finish within the native door activation radius");
        var passage = new FieldNavigationTarget(300, FieldNavigationCategory.Exits,
            "Passage", 947, 665, 339);
        Equal(false, planner.TryBuildRoute(start, passage, out _),
            "navigation must not route through the closed panel");
        upstairs.SetLockedTriangles();
        Equal(true, planner.TryBuildRoute(start, passage, out _), "opening the panel makes its exit walkable");

        var basement = PresentDay(529, BasementCorridor);
        var entry = new FieldPositionSnapshot(1, BasementCorridor, 0, -55, -411, 0, 12, 0);
        var coffin = basement.ObjectReader().ReadTargets(entry).Single(target => target.Label.Contains("Coffin"));
        var basementPlanner = new FieldWalkmeshRoutePlanner(InstalledWalkmesh(BasementCorridor), basement.BoundaryReader());
        basement.SetLockedTriangles(34);
        Equal(false, basementPlanner.TryBuildRoute(entry, coffin, out _), "the key is required to enter the coffin room");
        basement.SetLockedTriangles();
        Equal(true, basementPlanner.TryBuildRoute(entry, coffin, out _), "with the key the coffin is reachable");
    }

    private static void QuestInteractionsCanActuallyStartNavigation()
    {
        foreach (var (field, fragment) in new[] { (299, "Safe"), (300, "Secret passage door"), (298, "Piano") })
        {
            var entry = NativeEntries.Single(row => row.Field == field).Entries[0];
            var reader = InstalledWalkmesh(field);
            var mesh = reader.Read(At(field, 0, 0, 0)).Walkmesh!;
            var start = new FieldPositionSnapshot(1, field, 0, entry.X, entry.Y,
                FloorHeight(mesh, entry.Triangle, entry.X, entry.Y), (ushort)entry.Triangle, 0);
            var target = PresentDay(529, field).ObjectReader().ReadTargets(start)
                .Single(row => row.Label.StartsWith(fragment, StringComparison.Ordinal));
            var controller = new FieldNavigationController(new FieldNavigationTargetSource([target]),
                new FieldWalkmeshRoutePlanner(reader));
            for (var i = 0; i < 4 && controller.CurrentCategory != FieldNavigationCategory.Objects; i++)
                controller.HandleAction(FieldNavigationAction.NextCategory, start);
            var result = controller.HandleAction(FieldNavigationAction.ToggleBeacon, start);
            Equal(true, controller.BeaconEnabled,
                $"{fragment} must start real navigation instead of only speaking instructions: {result?.Speech}");
        }
    }

    /// <summary>
    /// Field 308's scene is not something to walk to or press. Its director's Main runs the
    /// whole thing on entry, so the catalog must offer no target for it - only the ordinary
    /// way back out of that room.
    /// </summary>
    private static void TheReunionSceneIsNotOfferedAsAnInteraction()
    {
        Equal(0, ObjectLabels(ReunionRoom).Length,
            "field 308 has nothing to walk up to; its scene starts by itself");

        var story = Targets(ReunionRoom);
        Equal(1, story.Count,
            "field 308 offers exactly the way back out; got " +
            string.Join(" | ", story.Select(target => target.Label)));
        Equal("Go back through the library rooms", story[0].Label,
            "and that is what it is");
    }

    /// <summary>
    /// The native evidence, read with the production script decoder and anchored to the
    /// entity, the script and the byte offset rather than to a scan of the whole file: field
    /// 308's director entity 0, script 0.
    ///
    /// <para>Offset 31 is <c>IFUB</c> banks 0x10 (bank 1, literal), address 0xE7 = 231,
    /// value 1, comparison 10 - bit off - and a jump of 0x50 past the whole scene. Offset 92
    /// is <c>BITON</c> on the same bit, which is how the scene marks itself done. Together
    /// they are why nothing may be offered to walk to in that room.</para>
    /// </summary>
    private static void TheReunionSceneRunsFromItsOwnDirector()
    {
        var director = Scripts().ReadScriptOpcodes(ReunionRoom, 0, 0);
        Equal(true, director.Count > 0,
            "field 308's director entity 0 script 0 must decode");
        Equal("dir", director[0].EntityName,
            "and entity 0 must be the director this evidence is about");

        AssertOpcode(director, 31, [0x14, 0x10, 0xE7, 0x01, 0x0A, 0x50],
            "the scene runs only while Bank[1][231] bit 1 is clear");
        AssertOpcode(director, 92, [0x82, 0x10, 0xE7, 0x01],
            "and the same script sets that bit when it has run");
    }

    /// <summary>
    /// And the way in is an ordinary gateway, so Exits carries it and nothing has to be
    /// authored. Read from field 305's own triggers section: the gateway whose exit line is
    /// (-117,92,0)-(151,84,0) records destination field 308.
    /// </summary>
    private static void TheReunionRoomIsReachedByAnOrdinaryGateway()
    {
        var triggers = TriggersSection(BasementLibrary);
        int[] line = [-117, 92, 0, 151, 84, 0];
        var matches = 0;
        var destination = -1;
        for (var offset = 0; offset + GatewayRecordSize <= triggers.Length; offset++)
        {
            var isLine = true;
            for (var component = 0; component < line.Length && isLine; component++)
            {
                isLine = BitConverter.ToInt16(triggers, offset + (component * 2)) == line[component];
            }

            if (!isLine)
            {
                continue;
            }

            matches++;
            destination = BitConverter.ToUInt16(triggers, offset + GatewayDestinationFieldOffset);
        }

        Equal(1, matches,
            "field 305's triggers section must carry exactly one gateway on that exit line");
        Equal(ReunionRoom, destination,
            "and that gateway must record field 308 as where it leads");
    }
    /// <summary>
    /// The replay. A midpoint that exists in the catalog is not a target a player can walk
    /// to, so every authored row is routed by the production planner over the installed
    /// walkmesh, from every triangle the native gateway table's own destinationVertex lands
    /// the party on, with the entry height interpolated on that triangle rather than taken
    /// from its centroid. The plan has to finish on the native crossing, not near it.
    /// </summary>
    private static void EveryAuthoredTargetRoutesFromItsNativeEntry()
    {
        var covered = 0;
        foreach (var (field, entries) in NativeEntries)
        {
            var reader = InstalledWalkmesh(field);
            var mesh = reader.Read(At(field, 0, 0, 0)).Walkmesh;
            Equal(true, mesh is not null, $"field {field}: the installed walkmesh must be readable");
            var planner = new FieldWalkmeshRoutePlanner(reader);
            var targets = Targets(field);
            Equal(true, targets.Count > 0, $"field {field}: the replay needs a target to route to");
            foreach (var (entryX, entryY, entryTriangle) in entries)
            {
                Equal(true, entryTriangle < mesh!.Triangles.Count,
                    $"field {field}: native entry triangle {entryTriangle} must exist");
                var start = new FieldPositionSnapshot(
                    FieldPositionReader.FieldModule, field, 0,
                    entryX, entryY, FloorHeight(mesh, entryTriangle, entryX, entryY),
                    (ushort)entryTriangle, 0);
                foreach (var target in targets)
                {
                    Equal(true, planner.TryBuildRoute(start, target, out var plan),
                        $"field {field}: '{target.Label}' must be walkable from the native entry " +
                        $"({entryX},{entryY}) triangle {entryTriangle}: {planner.LastDiagnostic}");

                    // The plan has to end on the crossing itself. A final approach that stops
                    // within a generic arrival distance of a point near the door is the
                    // observatory failure, and it is what a missing trigger line produces.
                    var line = target.TriggerLine!.Value;
                    var endsOnTheLine = DistanceSquaredToSegment(
                        plan.FinalApproach.X, plan.FinalApproach.Y, line);
                    Equal(true, endsOnTheLine <= 4,
                        $"field {field}: '{target.Label}' must finish on its native line from " +
                        $"({entryX},{entryY}); finished {endsOnTheLine} squared units away at " +
                        $"({plan.FinalApproach.X},{plan.FinalApproach.Y})");
                    covered++;
                    Console.WriteLine(
                        $"nibelheim replay: field {field} entry ({entryX},{entryY}) t{entryTriangle} " +
                        $"-> {target.Label} = {plan.TrianglePath.Count} triangles, " +
                        $"final ({plan.FinalApproach.X},{plan.FinalApproach.Y}) d2={endsOnTheLine}");
                }
            }
        }

        Console.WriteLine($"nibelheim replay: {covered} routes over {NativeEntries.Length} fields");
        Equal(22, NativeEntries.Length,
            "twenty-two of the twenty-three authored fields are replayed; 306 has no "
            + "recorded native entry");
    }

    /// <summary>
    /// The same replay for the optional things. A route that returns a plan is not a route
    /// that reaches a native Go or [OK] handler: those fire on <c>distanceSquared &lt;
    /// radius²</c> against the line, so the plan has to finish inside the radius the offered
    /// target carries, not within the reader's default arrival distance.
    /// </summary>
    private static void EveryOptionalObjectIsReachableFromItsNativeEntry()
    {
        var entriesByField = NativeEntries.ToDictionary(entry => entry.Field, entry => entry.Entries);
        var covered = 0;
        foreach (var field in NibelheimObjectCatalog.Create()
                     .Select(definition => definition.FieldId)
                     .Distinct()
                     .OrderBy(field => field))
        {
            Equal(true, entriesByField.ContainsKey(field),
                $"field {field}: an optional object needs a native entry to be replayed from");
            var reader = InstalledWalkmesh(field);
            var mesh = reader.Read(At(field, 0, 0, 0)).Walkmesh;
            Equal(true, mesh is not null, $"field {field}: the installed walkmesh must be readable");
            var planner = new FieldWalkmeshRoutePlanner(reader);
            var memory = PresentDay(529, field);
            // The treasure has already been collected when the chest inscription is used.
            if (field == MansionUpstairsLeft) memory.SetBank15(35, 8);
            var objects = memory.ObjectReader().ReadTargets(At(field, 0, 0, 0));
            Equal(true, objects.Count > 0, $"field {field}: the replay needs an object to route to");

            foreach (var (entryX, entryY, entryTriangle) in entriesByField[field])
            {
                var start = new FieldPositionSnapshot(
                    FieldPositionReader.FieldModule, field, 0,
                    entryX, entryY, FloorHeight(mesh!, entryTriangle, entryX, entryY),
                    (ushort)entryTriangle, 0);
                foreach (var target in objects)
                {
                    Equal(true, planner.TryBuildRoute(start, target, out var plan),
                        $"field {field}: '{target.Label}' must be walkable from the native entry " +
                        $"({entryX},{entryY}) triangle {entryTriangle}: {planner.LastDiagnostic}");

                    var dx = (long)plan.FinalApproach.X - target.X;
                    var dy = (long)plan.FinalApproach.Y - target.Y;
                    var finishedSquared = (dx * dx) + (dy * dy);
                    var radius = (long)target.InteractionRadius;
                    Equal(true, finishedSquared < radius * radius,
                        $"field {field}: '{target.Label}' must finish inside its own activation " +
                        $"radius {target.InteractionRadius} from ({entryX},{entryY}); finished " +
                        $"{finishedSquared} squared units away at " +
                        $"({plan.FinalApproach.X},{plan.FinalApproach.Y})");
                    covered++;
                    Console.WriteLine(
                        $"nibelheim object replay: field {field} entry ({entryX},{entryY}) " +
                        $"t{entryTriangle} -> {target.Label} = {plan.TrianglePath.Count} triangles, " +
                        $"final ({plan.FinalApproach.X},{plan.FinalApproach.Y}) d2={finishedSquared} " +
                        $"< r2={radius * radius}");
                }
            }
        }

        Console.WriteLine($"nibelheim object replay: {covered} routes");
        Equal(true, covered > 0, "the optional replay must actually route something");
    }

    /// <summary>The production script decoder, over the configured archive.</summary>
    private static FieldScriptNavigationCatalog Scripts() =>
        new(Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")
            ?? throw new InvalidOperationException("FF7_ACCESSIBILITY_DATA_ROOT is not set."));

    private static void AssertOpcode(
        IReadOnlyList<FieldScriptOpcodeDefinition> opcodes,
        int byteIndex,
        byte[] expected,
        string label)
    {
        var opcode = opcodes.FirstOrDefault(candidate => candidate.ByteIndex == byteIndex);
        Equal(true, opcode.Bytes is { Count: > 0 },
            $"{label}: there must be an opcode at byte {byteIndex}");
        Equal(
            Convert.ToHexString(expected.ToArray()),
            Convert.ToHexString(opcode.Bytes.ToArray()),
            $"{label}: byte {byteIndex}");
    }

    /// <summary>
    /// A field's triggers section. Section 7 of the nine, each preceded by its own length.
    /// </summary>
    private static byte[] TriggersSection(int fieldId)
    {
        const int sectionCountOffset = 2;
        const int sectionOffsetsOffset = 6;
        const int triggersSectionIndex = 7;
        var bytes = InstalledFieldBytes(fieldId);
        var sectionCount = BitConverter.ToInt32(bytes, sectionCountOffset);
        Equal(true, sectionCount > triggersSectionIndex,
            $"field {fieldId} must have a triggers section");
        var sectionOffset = BitConverter.ToInt32(
            bytes, sectionOffsetsOffset + (triggersSectionIndex * sizeof(int)));
        var length = BitConverter.ToInt32(bytes, sectionOffset);
        Equal(true, length > 0 && sectionOffset + sizeof(int) + length <= bytes.Length,
            $"field {fieldId}'s triggers section must be readable");
        return bytes.AsSpan(sectionOffset + sizeof(int), length).ToArray();
    }

    /// <summary>
    /// A gateway record: six int16 for the two exit-line vertices, the destination vertex and
    /// its triangle, then the destination field id at +18.
    /// </summary>
    private const int GatewayRecordSize = 24;

    private const int GatewayDestinationFieldOffset = 18;

    /// <summary>
    /// Where the party actually arrives, taken from the destinationVertex of every gateway
    /// that leads into each field, plus the two LINE MAPJUMPs that are not in the gateway
    /// table at all - sinin1_1's lin0 into the town and sininb2's lin0 into the basement
    /// library.
    ///
    /// <para>Twenty-two of the twenty-three fields this region authors a row in. <b>306
    /// sininb33 is not here</b>: nothing in the Nibelheim set records an entry into it, so
    /// there is no native arrival to replay from. Its row exists because the field is a
    /// variant of 304 and 305 with the same gateway, and that is the extent of the claim.</para>
    /// </summary>
    private static readonly (int Field, (int X, int Y, int Triangle)[] Entries)[] NativeEntries =
    [
        // sinin1_1's lin0 Move, the present-day MAPJUMP out of the mansion.
        (284, [(-552, 1280, 127)]),
        (297, [(-367, 879, 51), (-327, 313, 116), (2, 36, 119), (4, 733, 90),
               (344, 305, 73), (393, 883, 61)]),
        (298, [(-378, 203, 116), (6, 739, 127), (410, 199, 124)]),
        (299, [(-345, 777, 72)]),
        (300, [(354, 788, 151), (909, 646, 143)]),
        (301, [(7, 70, 37), (225, -99, 202)]),
        (302, [(-52, -460, 29), (-50, -278, 24), (-13, 670, 4)]),
        (303, [(-225, -1043, 23), (-55, -411, 12)]),
        (304, [(-425, -125, 137), (53, 13, 113)]),
        // sininb42's gateways, and sininb2's lin0 MAPJUMP at (-362,-145) triangle 58.
        (305, [(53, 13, 39), (-362, -145, 58)]),
        (307, [(292, 2884, 8), (307, 133, 11)]),
        (308, [(292, 2884, 8), (307, 133, 11)]),
        (309, [(77, 907, 43)]),
        (310, [(77, 907, 43)]),
        (270, [(408, -365, 9)]),
        (271, [(-342, 239, 6), (386, 293, 42)]),
        (272, [(-64, 27, 14)]),
        (273, [(-22, -553, 33), (175, -313, 12)]),
        (274, [(164, -68, 13)]),
        (276, [(-305, 15, 9)]),
        (286, [(-338, 23, 36), (42, -391, 12)]),
        (287, [(106, -382, 41)]),
    ];

    private static string[] ObjectLabels(int field, bool linesEnabled = true, int moment = 529) =>
        PresentDay(moment, field, linesEnabled)
            .ObjectReader()
            .ReadTargets(At(field, 0, 0, 0))
            .Select(target => target.Label)
            .ToArray();

    private static IReadOnlyList<FieldNavigationTarget> Targets(int field, int moment = 529) =>
        PresentDay(moment, field).StoryReader().ReadTargets(At(field, 0, 0, 0));

    private static FieldPositionSnapshot At(int field, int x, int y, int z) =>
        new(FieldPositionReader.FieldModule, field, 0, x, y, z, 0, 0);

    private static NibelheimMemory PresentDay(int moment, int field, bool linesEnabled = true)
    {
        var memory = new NibelheimMemory(field) { LinesEnabled = linesEnabled };
        memory.SetGameMoment(moment);
        return memory;
    }

    /// <summary>The height of the walkmesh under a point, interpolated on its own triangle.</summary>
    private static int FloorHeight(FieldWalkmesh mesh, int triangleIndex, int x, int y)
    {
        var triangle = mesh.Triangles[triangleIndex];
        var a = triangle.Vertex0;
        var b = triangle.Vertex1;
        var c = triangle.Vertex2;
        var denominator = (((double)b.Y - c.Y) * ((double)a.X - c.X)) +
                          (((double)c.X - b.X) * ((double)a.Y - c.Y));
        if (Math.Abs(denominator) < 1e-9d)
        {
            return (int)Math.Round(triangle.GetCentroid().Z);
        }

        var weightA = ((((double)b.Y - c.Y) * (x - c.X)) + (((double)c.X - b.X) * (y - c.Y))) / denominator;
        var weightB = ((((double)c.Y - a.Y) * (x - c.X)) + (((double)a.X - c.X) * (y - c.Y))) / denominator;
        return (int)Math.Round((weightA * a.Z) + (weightB * b.Z) + ((1d - weightA - weightB) * c.Z));
    }

    private static long DistanceSquaredToSegment(int x, int y, FieldNavigationTriggerLine line)
    {
        double ax = line.StartX;
        double ay = line.StartY;
        var abx = (double)line.EndX - ax;
        var aby = (double)line.EndY - ay;
        var lengthSquared = (abx * abx) + (aby * aby);
        var t = lengthSquared <= 0d
            ? 0d
            : Math.Clamp((((x - ax) * abx) + ((y - ay) * aby)) / lengthSquared, 0d, 1d);
        var dx = x - (ax + (t * abx));
        var dy = y - (ay + (t * aby));
        return (long)Math.Round((dx * dx) + (dy * dy));
    }

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        for (var index = 0; index + needle.Length <= haystack.Length; index++)
        {
            var match = true;
            for (var offset = 0; offset < needle.Length && match; offset++)
            {
                match = haystack[index + offset] == needle[offset];
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }

    private static byte[] InstalledFieldBytes(int fieldId)
    {
        var gameRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")
            ?? throw new InvalidOperationException("FF7_ACCESSIBILITY_DATA_ROOT is not set.");
        var dataSource = new FlevelDataSource(gameRoot);
        if (!dataSource.TryReadField(fieldId, out var encoded))
        {
            throw new InvalidOperationException(
                $"Installed field {fieldId} unavailable: {dataSource.Diagnostic}");
        }

        return Ff7LzsDecoder.DecodeFieldFile(encoded);
    }

    private static FieldWalkmeshReader InstalledWalkmesh(int fieldId)
    {
        const int fieldDataBase = 0x02000000;
        var bytes = InstalledFieldBytes(fieldId);

        int ReadInt32Value(int address)
        {
            if (address == FieldWalkmeshReader.AddressFieldDataPtr)
            {
                return fieldDataBase;
            }

            var offset = address - fieldDataBase;
            return offset >= 0 && offset + sizeof(int) <= bytes.Length
                ? BitConverter.ToInt32(bytes, offset)
                : 0;
        }

        short ReadInt16Value(int address)
        {
            var offset = address - fieldDataBase;
            return offset >= 0 && offset + sizeof(short) <= bytes.Length
                ? BitConverter.ToInt16(bytes, offset)
                : (short)0;
        }

        return new FieldWalkmeshReader(ReadInt32Value, ReadInt16Value);
    }

    /// <summary>
    /// Field and savemap state for the LINE interactions, entrance note and opened chest.
    /// </summary>
    private sealed class NibelheimMemory
    {
        private const int FieldState = 0x02800000;
        private const int EventTable = 0x03300000;

        private readonly Dictionary<int, byte> bytes = [];

        public NibelheimMemory(int field)
        {
            bytes[FieldPositionReader.AddressFieldNumModels] = 12;
            for (var entity = 0; entity < 32; entity++)
            {
                bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + entity] = 0xFF;
            }

            // sinin1_1 entity 6 'let', CHAR 4, placed by its own Init at (-550,89,0).
            bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + 6] = 4;
            SetModel(4, -550, 89, 0);
            if (field == MansionUpstairsLeft)
            {
                bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + 6] = 0xFF;
                bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + 7] = 4;
                SetModel(4, -1037, 826, 452);
            }
            SetModel(0, 0, 0, 0);

            // The player's own collision radius, which the native LINE test squares.
            WriteInt16(EventTable + FieldNavigationNpcReader.CollisionRadiusOffset,
                PlayerCollisionRadius);
            SetField(field);
        }

        /// <summary>What the shared FieldScriptLineStateReader reports for every entity.</summary>
        public bool LinesEnabled { get; init; } = true;

        public void SetField(int field)
        {
            bytes[FieldPositionReader.AddressCurrentModule] = FieldPositionReader.FieldModule;
            bytes[FieldPositionReader.AddressFieldId] = (byte)field;
            bytes[FieldPositionReader.AddressFieldId + 1] = (byte)(field >> 8);
        }

        public void SetGameMoment(int moment)
        {
            bytes[FieldNavigationObjectReader.AddressFieldBankBase] = (byte)moment;
            bytes[FieldNavigationObjectReader.AddressFieldBankBase + 1] = (byte)(moment >> 8);
        }

        /// <summary>Bank 1 lives at the field bank base, the same place the GameMoment does.</summary>
        public void SetBank13(int address, int mask) =>
            bytes[Bank13(address)] = (byte)(ReadByte(Bank13(address)) | mask);

        /// <summary>Bank 13 is the field bank base plus 0x300, as the reader resolves it.</summary>
        private static int Bank13(int address) =>
            FieldNavigationObjectReader.AddressFieldBankBase + 0x300 + address;

        public void SetBank1(int address, int mask) =>
            bytes[FieldNavigationObjectReader.AddressFieldBankBase + address] =
                (byte)(ReadByte(FieldNavigationObjectReader.AddressFieldBankBase + address) | mask);

        public void SetBank15(int address, int mask)
        {
            var nativeAddress = FieldNavigationObjectReader.AddressFieldBankBase + 0x400 + address;
            bytes[nativeAddress] = (byte)(ReadByte(nativeAddress) | mask);
        }

        public void SetBank5(int address, int value) =>
            bytes[FieldNavigationObjectReader.AddressTemporaryFieldBankBase + address] = (byte)value;

        public void SetLockedTriangles(params int[] triangles)
        {
            for (var i = 0; i < 64; i++) bytes[FieldState + FieldBoundaryStateReader.BoundaryBitsOffset + i] = 0;
            foreach (var triangle in triangles)
            {
                var address = FieldState + FieldBoundaryStateReader.BoundaryBitsOffset + (triangle >> 3);
                bytes[address] |= (byte)(1 << (triangle & 7));
            }
        }

        public FieldBoundaryStateReader BoundaryReader() => new(ReadInt32, ReadByte, (_, _) => true);

        public FieldStoryTargetReader StoryReader() =>
            new(ReadInt32, ReadInt16, ReadByte, FieldStoryEventCatalog.CreateAllFields(), _ => LinesEnabled);

        public FieldNavigationObjectReader ObjectReader() =>
            new(ReadInt32, ReadByte, _ => null, _ => null,
                FieldNavigationObjectCatalog.CreateAllFields(), _ => LinesEnabled);

        public byte ReadByte(int address) => bytes.GetValueOrDefault(address);

        public short ReadInt16(int address) =>
            (short)(ReadByte(address) | (ReadByte(address + 1) << 8));

        public int ReadInt32(int address) => address switch
        {
            FieldBoundaryStateReader.AddressFieldGlobalObjectPtr => FieldState,
            FieldNavigationObjectReader.AddressFieldEventDataPtr => EventTable,
            _ => ReadByte(address) | (ReadByte(address + 1) << 8) |
                 (ReadByte(address + 2) << 16) | (ReadByte(address + 3) << 24),
        };

        private void WriteInt16(int address, int value)
        {
            bytes[address] = (byte)value;
            bytes[address + 1] = (byte)(value >> 8);
        }

        private void SetModel(int modelId, int x, int y, int z)
        {
            var record = EventTable + (modelId * FieldNavigationObjectReader.FieldEventDataStride);
            foreach (var (offset, value) in new[]
                     {
                         (FieldNavigationObjectReader.PositionXOffset, x),
                         (FieldNavigationObjectReader.PositionYOffset, y),
                         (FieldNavigationObjectReader.PositionZOffset, z),
                     })
            {
                var scaled = value * FieldNavigationObjectReader.ModelPositionFixedPointScale;
                for (var index = 0; index < 4; index++)
                {
                    bytes[record + offset + index] = (byte)(scaled >> (index * 8));
                }
            }

            bytes[record + FieldNavigationObjectReader.VisibilityOffset] = 1;
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"nibelheim story - {label}: expected {expected}, got {actual}");
        }
    }
}
