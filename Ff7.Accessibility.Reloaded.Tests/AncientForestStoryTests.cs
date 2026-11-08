using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class AncientForestStoryTests
{
    internal static void Run()
    {
        var forest = new Replay();
        var first = AncientForestStoryGuide.EntranceStage1;
        var second = AncientForestStoryGuide.EntranceStage2;
        var p1 = Jump("Right", 0, -126, 243);
        var supershot = Object("Supershot ST", 404, -920, 22, "Item:252");
        var insect1 = Object("Insect 1", -1810, -665, 28);
        var insect2 = Object("Insect 2", -792, -489, 6);
        var insect3 = Object("Insect 3", -848, -735, 17);
        forest.Enter(622, -2016, -748, 85);
        forest.Expect("Collect Supershot ST", [supershot]);
        forest.Collect(AncientForestStoryGuide.Supershot);
        forest.Expect("Pick up Insect 1 to stage by the crossing", [insect1, insect2, insect3]);
        forest.Bank[9] = 1;
        forest.Expect("Stage the insect by the crossing", [insect2, insect3], destination: first);
        forest.At(first.X, first.Y, 30, first.Z);
        forest.Expect("Stage the insect by the crossing", [insect2, insect3], "Press Confirm to set it down here.");
        insect1 = insect1 with { X = first.X, Y = first.Y, Z = first.Z };
        forest.Bank[9] = 0;
        forest.Expect("Pick up Insect 2 to stage by the crossing", [insect1, insect2, insect3]);
        forest.Bank[9] = 2;
        forest.Expect("Stage the insect by the crossing", [insect1, insect3], destination: second);
        insect2 = insect2 with { X = second.X, Y = second.Y, Z = second.Z };
        forest.Bank[9] = 0;
        forest.Expect("Pick up Insect 3", [insect1, insect2, insect3]);
        forest.Bank[9] = 3;
        forest.Bank[19] = 2;
        forest.Expect("Feed pitcher plant 1", [], "Press Confirm to throw.");
        forest.Bank[16] = 1;
        forest.Bank[9] = 0;
        forest.Expect("Pick up Insect 1 for the next pitcher plant", [insect1, insect2, p1]);
        forest.Bank[9] = 1;
        forest.Expect("Jump onto pitcher plant 1", [insect2, p1], destination: (0, -126, 243));
        forest.At(193, -114, 14);
        forest.Bank[19] = 3;
        forest.Expect("Feed pitcher plant 2", [], "Press Confirm to throw.");
        forest.Bank[17] = 1;
        forest.Bank[9] = 0;
        forest.Expect("Return for the staged insect", [], "Hold Left.");
        forest.At(-170, -145, 30);
        forest.Bank[19] = 1;
        forest.Expect("Pick up Insect 2 for the next pitcher plant", [insect2, p1]);
        forest.Bank[9] = 2;
        forest.At(193, -114, 14);
        forest.Bank[19] = 3;
        forest.Expect("Cross pitcher plant 2", [], "Hold Right.");
        forest.At(323, -143, 29);
        forest.Bank[19] = 4;
        forest.Expect("Feed pitcher plant 3", [], "Press Confirm to throw.");
        forest.Bank[18] = 1;
        forest.Bank[9] = 0;
        forest.Expect("Cross pitcher plant 3", [], "Hold Right.");
        forest.At(456, -199, 26);
        forest.Expect("Jump to the far bank", [], "Hold Right.");
        forest.At(636, -269, 226);
        forest.Expect("Collect Spring Gun Clip", [Object("Spring Gun Clip", 1800, -500, 80, "Item:188")]);
        forest.Collect(AncientForestStoryGuide.SpringGun);
        forest.Expect("Continue into the forest", [], exit: 620);

        forest.Enter(620, -2043, -586, 44);
        var westInsect = Object("Insect 1", -1677, -616, -12);
        var middleFrog = Object("Frog 2", 501, -728, -12);
        forest.Expect("Pick up Insect 1", [westInsect, middleFrog]);
        forest.Bank[9] = 1;
        forest.Expect("Walk to the vine and choose Jump", [Object("Vine to pitcher plant 1", -839, -562, -12) with
        {
            TriggerLine = new(-839, -446, -12, -839, -678, -12), LineActivationRadius = 30,
        }]);
        forest.At(-719, 0, 3);
        forest.Bank[20] = 3;
        forest.Expect("Feed pitcher plant 1", [], "Press Confirm to throw.");
        forest.Bank[9] = 0;
        forest.Bank[17] = 1;
        forest.Expect("Jump onto pitcher plant 1", [Jump("Right", -666, 1, 334)]);
        forest.At(-489, -155, 8);
        forest.Expect("Jump from pitcher plant 1 to the stamen", [], "Hold Right.");
        forest.At(-361, -131, 9);
        forest.Expect("Jump from the stamen to the middle bank", [], "Hold Right.");
        forest.At(153, -517, 141);
        forest.Bank[20] = 1;
        forest.Expect("Pick up Frog 2", [middleFrog]);
        forest.Bank[9] = 4;
        forest.Bank[20] = 5;
        forest.Expect("Feed pitcher plant 2", [], "Press Confirm to throw.");
        forest.Bank[9] = 0;
        forest.Bank[18] = forest.Bank[40] = 1;
        forest.Expect("Jump onto pitcher plant 2", [Jump("Left", 937, 59, 122)]);
        forest.At(739, -158, 19);
        forest.Expect("Wait on pitcher plant 2", [], "The frog will launch you to the beehive.");
        forest.Bank[40] = forest.Bank[18] = 0;
        forest.At(345, -231, 16);
        forest.Expect("Pick up Beehive", [Object("Beehive", 285, -245, 527)]);
        forest.Bank[9] = 5;
        forest.Bank[60] = 1;
        forest.Expect("Jump down with the beehive", [Jump("Down", 281, -247, 526)]);
        forest.At(283, -587, 148);
        forest.Bank[20] = 7;
        forest.Expect("Close flytrap 1 with the beehive", [], "Press Confirm to throw.");
        forest.Bank[9] = 0;
        forest.Bank[51] = 1;
        forest.Expect("Collect Slash-All Materia", [Object("Slash-All Materia", -60, -460, 0, "Materia:14")]);
        forest.Collect(AncientForestStoryGuide.SlashAll);
        forest.Expect("Pick up Frog 2", [middleFrog with { X = 603, Y = -648 }]);
        forest.Bank[9] = 4;
        forest.Bank[20] = 6;
        forest.Expect("Feed pitcher plant 3", [], "Press Confirm to throw.");
        forest.Bank[9] = 0;
        forest.Bank[19] = forest.Bank[41] = 1;
        forest.Expect("Jump onto pitcher plant 3", [Jump("Right", 932, 63, 122)]);
        forest.At(1082, 40, 21);
        forest.Expect("Wait on pitcher plant 3", [], "The frog will launch you to the far bank.");
        forest.At(1698, -514, 236);
        forest.Expect("Continue to the fork", [], exit: 623);

        forest.Enter(623, -2303, -492, 224);
        var fork1 = Object("Insect 1", -1995, -436, 26);
        var fork2 = Object("Insect 2", -979, -762, 0);
        var stage = AncientForestStoryGuide.CanopyStage;
        forest.Expect("Pick up Insect 1 to stage by the crossing", [fork1, fork2]);
        forest.Bank[9] = 1;
        forest.Expect("Stage the insect by the crossing", [fork2], destination: stage);
        forest.Bank[9] = 0;
        fork1 = fork1 with { X = stage.X, Y = stage.Y, Z = stage.Z };
        forest.Expect("Pick up Insect 2", [fork1, fork2]);
        forest.At(-979, -762, 179);
        forest.Bank[9] = 2;
        forest.Expect("Feed pitcher plant 1", [Object("Pitcher plant 1, throwing spot", -1772, -253, 80)]);
        forest.Bank[9] = 0;
        forest.Bank[18] = 1;
        forest.Expect("Pick up Insect 1 for pitcher plant 2", [fork1]);
        forest.Bank[9] = 1;
        forest.Expect("Jump onto pitcher plant 1", [Jump("Right", -1736, -236, 84)]);
        forest.At(-1568, -207, 0);
        forest.Bank[22] = 3;
        forest.Expect("Feed pitcher plant 2", [], "Press Confirm to throw.");
        forest.Bank[9] = 0;
        forest.Bank[19] = 1;
        forest.Expect("Cross pitcher plant 2", [], "Hold Right.");
        forest.At(-1438, -197, 2);
        forest.Expect("Jump to the treetop path", [], "Hold Right.");
        forest.At(-1210, -199, 4);
        forest.Expect("Enter the treetops", [], exit: 621);
        forest.Enter(621, -206, -277, 110);
        forest.Expect("Collect Typoon Materia", [Object("Typoon Materia", 800, 500, 0, "Materia:87")]);
        forest.Collect(AncientForestStoryGuide.Typoon);
        forest.Expect("Take the branch down to Minerva Band", [], exit: 620);
        forest.Enter(620, 1083, -1122, 29);
        forest.Expect("Collect Minerva Band", [Object("Minerva Band", 954, -1251, 356, "Item:282")]);
        forest.Collect(AncientForestStoryGuide.Minerva);
        forest.Expect("Drop from the Minerva ledge", [], destination: (858, -482, -12));
        forest.At(858, -482, 168);
        forest.Expect("Pick up Frog 2", [middleFrog]); // Skip the hive; Slash-All persisted.
        forest.Bank[9] = 4;
        forest.Bank[20] = 6;
        forest.Expect("Feed pitcher plant 3", [], "Press Confirm to throw.");
        forest.Enter(621, -206, -277, 110); // After crossing 620 and taking the same two canopy pitches.
        forest.Expect("Return to the raised forest path", [], exit: 623);

        forest.Enter(623, -633, -138, 13);
        var insect3East = Object("Insect 3", 467, -667, 20);
        var insect4East = Object("Insect 4", 998, -753, 2);
        var jumpP4 = Jump("Right", 1081, -285, 48);
        forest.Expect("Pick up Insect 3 for pitcher plant 4", [insect3East, insect4East]);
        forest.Bank[9] = 3;
        forest.At(1069, -295, 101);
        forest.Bank[22] = 9;
        forest.Expect("Feed pitcher plant 4", [], "Press Confirm to throw.");
        forest.Bank[9] = 0;
        forest.Bank[21] = 1;
        forest.Expect("Jump onto pitcher plant 4 toward the hive", [jumpP4]);
        forest.At(1215, -305, 44);
        forest.Expect("Jump up toward the hive", [], "Hold Up.");
        forest.At(1100, -181, 42);
        forest.Expect("Jump left to the beehive", [], "Hold Left.");
        forest.At(963, -35, 39);
        forest.Expect("Pick up Beehive", [Object("Beehive", 961, -60, 333)]);
        forest.Bank[9] = 6;
        forest.Expect("Jump down from the hive ledge", [Jump("Down", 886, -99, 331)]);
        forest.At(872, -324, 118);
        forest.Bank[22] = 13;
        forest.Expect("Close the flytrap with the beehive", [], "Press Confirm to throw.");
        forest.Bank[9] = 0;
        forest.Bank[61] = 1;
        forest.Expect("Pick up Insect 4 to cross to the hollow tree", [insect4East]);
        forest.Bank[9] = 4;
        forest.Bank[22] = 8;
        forest.Expect("Feed pitcher plant 3", [], "Press Confirm to throw.");
        forest.Bank[9] = 0;
        forest.Bank[20] = 1;
        forest.Expect("Cross west toward the hollow tree", [Jump("Left", 432, 7, 48)]);
        forest.At(267, 33, 34);
        forest.Expect("Jump west toward the hollow tree", [], "Hold Left.");
        forest.At(66, -1, 139);
        forest.Expect("Pick up Insect 2 for the hollow tree", [fork2]);
        forest.Bank[9] = 2;
        forest.Bank[22] = 5;
        forest.Expect("Feed the hollow tree to bring out the frog", [], "Press Confirm to throw.");
        forest.Bank[9] = 0;
        forest.Bank[17] = 1;
        var frog = Object("Frog 1", -524, -87, 4);
        forest.Expect("Pick up Frog 1", [frog]);
        forest.Bank[9] = 5;
        forest.Bank[20] = 0;
        forest.Bank[22] = 6;
        forest.Expect("Feed pitcher plant 3", [], "Press Confirm to throw.");
        forest.Bank[9] = 0;
        forest.Bank[20] = forest.Bank[47] = 1;
        forest.Expect("Jump onto pitcher plant 3", [Jump("Right", 67, 10, 56)]);
        forest.At(267, 33, 34);
        forest.Expect("Wait on pitcher plant 3", [], "The frog will launch you to the east bank.");
        forest.Bank[47] = forest.Bank[20] = 0;
        forest.At(644, -82, 128);
        forest.Expect("Pick up Frog 1 for pitcher plant 4", [frog with { X = 702, Y = 75 }]);
        forest.Bank[9] = 5;
        forest.Bank[21] = 0;
        forest.Bank[22] = 9;
        forest.Expect("Feed pitcher plant 4", [], "Press Confirm to throw.");
        forest.Bank[9] = 0;
        forest.Bank[21] = forest.Bank[48] = 1;
        forest.Expect("Jump onto pitcher plant 4", [jumpP4]);
        forest.At(1215, -305, 44);
        forest.Expect("Wait on pitcher plant 4", [], "The frog will launch you to the cave entrance.");
        forest.At(1571, -585, 56);
        forest.Expect("Enter the treasure cave", [], exit: 624);
        forest.Enter(624, -698, -434, 175);
        var apocalypse = Object("Apocalypse", 641, 1024, -369, "Item:140");
        var elixir = Object("Elixir", 791, -275, -391, "Item:5");
        forest.Expect("Collect Apocalypse", [apocalypse, elixir]);
        forest.Collect(AncientForestStoryGuide.Apocalypse);
        forest.Expect("Collect Elixir", [apocalypse, elixir]);
        forest.Collect(AncientForestStoryGuide.Elixir);
        forest.Expect("All seven treasures collected. Leave the Ancient Forest", [], exit: 55);
        forest.Enter(622, -2016, -748, 85);
        forest.Expect("All seven treasures collected. Leave the Ancient Forest", [supershot], exit: 55);
        var partial = new Replay();
        partial.Collect(AncientForestStoryGuide.Supershot);
        partial.Enter(622, -2016, -748, 85);
        partial.Expect("Pick up Insect 1 to stage by the crossing", [supershot, insect1 with { X = -1810, Y = -665 }, insect2, insect3]);
        partial.Enter(623, 1722, -691, 59);
        partial.Expect("There is still treasure on an earlier forest path", [],
            "Press and release Switch to return to the entrance. Collected treasures stay with you.", exitOffered: 624);
        forest.Bank[19] = 0;
        if (forest.Read([insect1]).Count != 0) throw new InvalidOperationException("No directions while a native jump is taking control.");
        StoryCuesFollowNativeChanges();
        Console.WriteLine("Ancient Forest Story replay passed: every reward, crossing, creature and the native reset.");
    }

    private static void StoryCuesFollowNativeChanges()
    {
        var r = new Replay();
        r.Enter(622, 193, -114, 14);
        r.Bank[9] = 1;
        r.Bank[19] = 3;
        var source = new FieldNavigationTargetSource([], storyTargetProvider: _ => r.Read([]));
        var controller = new FieldNavigationController(source);
        if (controller.HandleAction(FieldNavigationAction.NextCategory, r.Position)?.Speech.Contains("Press Confirm to throw", StringComparison.Ordinal) != true)
            throw new InvalidOperationException("The player must hear the manual action on selecting Story.");
        r.Bank[9] = 0;
        r.Bank[17] = 1;
        if (controller.UpdateLiveTracking(r.Position, default, default, isSuppressed: true) is not null)
            throw new InvalidOperationException("The guide must not speak through a native script.");
        var next = controller.UpdateLiveTracking(r.Position, default, default, isSuppressed: false);
        if (next?.Speech != "Return for the staged insect. Hold Left." || controller.BeaconEnabled)
            throw new InvalidOperationException("A change must announce the next action without starting movement.");
        if (controller.UpdateLiveTracking(r.Position, default, default, isSuppressed: false) is not null)
            throw new InvalidOperationException("The next action must not repeat continuously.");
    }

    private static FieldNavigationTarget Object(string label, int x, int y, int z, string key = "Named:0") =>
        new(0, FieldNavigationCategory.Objects, label, x, y, z, $"object:0:0:{key}:Model", InteractionRadius: 30);

    private static FieldNavigationTarget Jump(string direction, int x, int y, int z) =>
        Object($"Jump from here: hold {direction}", x, y, z) with { InteractionRadius = 8 };

    private sealed class Replay
    {
        internal FieldPositionSnapshot Position;
        internal byte[] Bank { get; } = new byte[64];
        private int collected;
        internal void Collect(int bits) => collected |= bits;
        internal void Enter(int field, int x, int y, ushort triangle)
        {
            Array.Clear(Bank);
            Bank[19] = Bank[20] = Bank[22] = 1;
            Position = new(FieldPositionReader.FieldModule, field, 0, x, y, 0, triangle, 0);
            if (field != 622) Bank[19] = 0; // Plant closure in 620 and 623.
            if (field != 620) Bank[20] = 0;
        }
        internal void At(int x, int y, ushort triangle, int z = 0) =>
            Position = Position with { X = x, Y = y, Z = z, TriangleId = triangle };
        internal IReadOnlyList<FieldNavigationTarget> Read(FieldNavigationTarget[] objects, int? exit = null) =>
            new AncientForestStoryGuide(Position, new AncientForestStoryState(Bank, collected, SupportedFieldScriptVariant.Vanilla),
                objects.Select(t => t with { FieldId = Position.FieldId }).ToArray(),
                exit is { } field ? [new(Position.FieldId, FieldNavigationCategory.Exits, "Gateway", 0, 0, 0,
                    $"gateway:{Position.FieldId}:0:{field}", DestinationFieldIds: [field])] : []).ReadTargets();
        internal void Expect(string label, FieldNavigationTarget[] objects, string? instruction = null,
            (int X, int Y, int Z)? destination = null, int? exit = null, int? exitOffered = null)
        {
            var targets = Read(objects, exit ?? exitOffered);
            if (targets.Count != 1 || targets[0].Category != FieldNavigationCategory.Story ||
                targets[0].Label != label || targets[0].ManualNavigationGuidance != instruction ||
                destination is { } point && (targets[0].X, targets[0].Y, targets[0].Z) != point ||
                exit is { } field && (targets[0].DestinationFieldIds?.Contains(field) != true || !targets[0].CompletesOnArrival))
            {
                throw new InvalidOperationException($"At {Position.FieldId} t{Position.TriangleId}, expected {label}; got {string.Join(", ", targets.Select(t => t.Label))}.");
            }
        }
    }
}
