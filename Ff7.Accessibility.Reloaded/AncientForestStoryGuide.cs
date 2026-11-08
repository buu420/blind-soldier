namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// An ordered route through the English Ancient Forest scripts. Each read starts from the
/// game's current position, visible Objects and native puzzle/collection state. There is
/// no saved step counter: re-entry, a timer, a dropped creature or Switch can change the
/// answer. Keyed jumps, throws, pickups and the vine choice remain player actions.
/// </summary>
internal sealed class AncientForestStoryGuide(
    FieldPositionSnapshot position,
    AncientForestStoryState state,
    IReadOnlyList<FieldNavigationTarget> objects,
    IReadOnlyList<FieldNavigationTarget> exits)
{
    // On the neutral (zone 1) side of bdl8/rk0lt, before the throw line.
    // Two separate spots keep the entrance insects from overlapping.
    internal static readonly (int X, int Y, int Z) EntranceStage1 = (-170, -145, 164);
    internal static readonly (int X, int Y, int Z) EntranceStage2 = (-165, -220, 119);
    internal static readonly (int X, int Y, int Z) CanopyStage = (-1940, -270, 61);

    internal const int Typoon = 0x04;
    internal const int Supershot = 0x08;
    internal const int SlashAll = 0x10;
    internal const int Minerva = 0x20;
    internal const int Apocalypse = 0x40;
    internal const int Elixir = 0x80;
    internal const int SpringGun = 0x100;
    private const int BeforeCave = Supershot | SpringGun | SlashAll | Typoon | Minerva;
    internal const int AllTreasures = BeforeCave | Apocalypse | Elixir;

    internal static bool IsForest(int field) => field is >= 620 and <= 624;
    private int Field => position.FieldId;
    private int Triangle => position.TriangleId;
    private byte[] Bank => state.Bank;
    private int Carry => Bank[9];
    private int Zone => Bank[Field switch { 620 => 20, 622 => 19, _ => 22 }];
    private bool Has(int flag) => (state.Collected & flag) != 0;
    private bool CanopyDone => Has(Typoon) && Has(Minerva);

    internal IReadOnlyList<FieldNavigationTarget> ReadTargets()
    {
        // 0 is used while a native jump, drop or entry script owns the leader.
        if (AncientForestPuzzleReadout.HasPuzzle(Field) && (Zone == 0 || Field == 620 && Bank[57] != 0))
            return [];

        var step = Field switch
        {
            622 => Entrance(),
            620 => Middle(),
            623 => Fork(),
            621 => Canopy(),
            624 => Cave(),
            _ => null
        };
        return step is { } target ? [target] : [];
    }

    private FieldNavigationTarget? Entrance()
    {
        if ((state.Collected & AllTreasures) == AllTreasures && ReachableExit(55) is { } outside)
            return Traverse(outside, "All seven treasures collected. Leave the Ancient Forest");

        var one = Bank[16] == 1;
        var two = Bank[17] == 1;
        var three = Bank[18] == 1;
        // ujp0 polls Left/Right only while standing on these actual plant tops.
        if (Triangle is 2 or 14)
        {
            if (two && (Carry != 0 || three)) return Hold("Cross pitcher plant 2", "Right");
            if (two) return Hold("Return for the staged insect", "Left");
            if (Carry != 0) return Feed(2, 3);
            return Hold("Return for the staged insect", "Left");
        }
        if (Triangle is 25 or 29)
        {
            if (three) return Hold("Cross pitcher plant 3", "Right");
            if (Carry != 0) return Feed(3, 4);
            return one ? Hold("Return for another insect", "Left")
                : Wait("Wait for pitcher plant 2 to reopen", "It will drop you back on the west bank.");
        }
        if (Triangle is 26 or 27)
            return Hold("Jump to the far bank", "Right");

        if (position.X >= 520)
        {
            if (!Has(SpringGun)) return Reward("Item", 188);
            return Has(Supershot) ? Exit(620, "Continue into the forest") : Restart("Supershot ST is still on the entrance side");
        }

        if (!Has(Supershot))
        {
            if (Carry != 0) return Stage(EntranceStage1, "Set down the creature before taking Supershot ST");
            return Reward("Item", 252);
        }

        var insects = Named("Insect ").Where(t => t.X < 400).ToArray();
        var staged = insects.Where(t => At(t, EntranceStage1, 22) || At(t, EntranceStage2, 22)).ToArray();
        if (!one)
        {
            // Insects are eaten. Set the other two down first so the player won't need to
            // cross the room while a plant is closing. After a mistimed attempt a frog can
            // stand in for an insect, but it is not a substitute for all three feeds.
            int needed = (two ? 0 : 1) + (three ? 0 : 1);
            if (staged.Length < needed && insects.Length + (Carry is >= 1 and <= 3 ? 1 : 0) > needed)
            {
                if (Carry == 0)
                    return Pickup(insects.Except(staged), "to stage by the crossing");
                var point = staged.Any(t => At(t, EntranceStage1, 22)) ? EntranceStage2 : EntranceStage1;
                return Stage(point, "Stage the insect by the crossing");
            }
            if (Carry != 0) return Feed(1, 2);
            return Pickup(insects.OrderBy(t => At(t, EntranceStage1, 22) || At(t, EntranceStage2, 22)))
                ?? Pickup(Named("Frog ").Where(t => t.X < 400))
                ?? Restart("There is no creature left on this bank to feed pitcher plant 1");
        }

        if (Carry == 0 && (!two || !three))
        {
            return Pickup(staged.Length > 0 ? staged : insects, "for the next pitcher plant")
                ?? Pickup(Named("Frog ").Where(t => t.X < 400), "for the next pitcher plant")
                ?? (two ? TakeOff(0, -126, "Jump onto pitcher plant 1")
                        : Wait("Wait for pitcher plant 1 to reopen", "Then use a creature to try the crossing again."));
        }
        return TakeOff(0, -126, two && three ? "Cross all three pitcher plants" : "Jump onto pitcher plant 1");
    }

    private FieldNavigationTarget? Middle()
    {
        var one = Bank[17] == 1;
        var two = Bank[18] == 1;
        var three = Bank[19] == 1;
        var westJawClosed = Bank[51] == 1;
        if (Triangle is >= 29 and <= 34)
        {
            if (!Has(Minerva)) return Reward("Item", 282);
            // The planner follows the keyless treejp0 to its actual landing. No new
            // connection is invented between the ledge and the ground.
            return Point((858, -482, -12), "Drop from the Minerva ledge", "You're back on the forest floor.", 10);
        }
        if (Triangle is >= 25 and <= 28) return null; // Native entry jumps onto the Minerva ledge.
        if (Triangle is >= 13 and <= 18)
        {
            if (Bank[60] == 1) return TakeOff(281, -247, "Jump down with the beehive");
            return Carry == 0 ? Pickup(Named("Beehive")) : Restart("Put the carried creature down before taking the hive");
        }
        if (Triangle is 19 or 20)
            return Bank[40] == 1 ? Wait("Wait on pitcher plant 2", "The frog will launch you to the beehive.")
                : Hold("Return to the middle bank", "Right");
        if (Triangle is 21 or 22)
            return Bank[41] == 1 ? Wait("Wait on pitcher plant 3", "The frog will launch you to the far bank.")
                : Hold("Return to the middle bank", "Left");

        if (Triangle is 7 or 8)
            return Hold("Jump from pitcher plant 1 to the stamen", "Right");
        if (Triangle is 9 or 10)
            return Hold("Jump from the stamen to the middle bank", "Right");
        if (Triangle is >= 2 and <= 4)
        {
            if (one) return TakeOff(-666, 1, "Jump onto pitcher plant 1");
            if (Carry != 0) return Feed(1, 3);
            return TakeOff(-677, 10, "Jump back for an insect", press: true);
        }

        if (ReachableExit(623) is { } east)
            return Has(SlashAll) ? Traverse(east, "Continue to the fork")
                : Restart("Slash-All is still behind the west flytrap");
        if (position.X < -125 && !westJawClosed)
        {
            if (Carry == 0 && !one)
                return Pickup(Named("Insect ").Where(t => t.X < -270))
                    ?? Restart("There is no insect left to reach the first pitcher plant");
            // Touching bleftl runs its Go-once slot and opens the Jump/Stay choice.
            return Named("Vine to pitcher plant 1").FirstOrDefault() is { TriggerLine: not null } vine
                ? Follow(vine, "Walk to the vine and choose Jump", "Choose Jump when prompted.")
                : null;
        }
        if (!Has(SlashAll))
        {
            if (westJawClosed) return Reward("Materia", 14);
            if (Carry == 5) return Throw("Close flytrap 1 with the beehive", 7, "Mutant Flytrap 1,");
            if (two)
                return Bank[40] == 1 ? TakeOff(937, 59, "Jump onto pitcher plant 2")
                    : Wait("Wait for pitcher plant 2 to reopen", "Then feed it a frog.");
            if (Carry is 3 or 4) return Feed(2, 5);
            if (Carry != 0) return DropAtCurrentSpot();
            return PickMiddleFrog();
        }
        if (three)
            return Bank[41] == 1 ? TakeOff(932, 63, "Jump onto pitcher plant 3")
                : Wait("Wait for pitcher plant 3 to reopen", "Then feed it a frog.");
        if (Carry is 3 or 4) return Feed(3, 6);
        if (Carry != 0) return DropAtCurrentSpot();
        return PickMiddleFrog();
    }

    private FieldNavigationTarget? Fork()
    {
        var one = Bank[18] == 1;
        var two = Bank[19] == 1;
        var three = Bank[20] == 1;
        var four = Bank[21] == 1;
        var jawClosed = Bank[61] == 1;
        bool west = Triangle is 32 or 33 or >= 137 and <= 228;
        if (Triangle is >= 46 and <= 68)
            return (state.Collected & BeforeCave) == BeforeCave ? Exit(624, "Enter the treasure cave")
                : Restart("There is still treasure on an earlier forest path");
        if (Triangle is 4 or 5 || Triangle is >= 6 and <= 24 && !CanopyDone)
            return Exit(621, "Enter the treetops");
        if (Triangle is 229 or 230) return null; // First landing of the native stamen jump.

        // The canopy entrance is reached from the west by two pitchers. On returning
        // from the canopy's near exit we are on raised ground (t6..24), beyond them.
        if (Triangle is 0 or 1)
            return two ? Hold("Cross pitcher plant 2", "Right")
                : Carry != 0 ? Feed(2, 3) : Hold("Return for the staged insect", "Left");
        if (Triangle is 2 or 3) return Hold("Jump to the treetop path", "Right");
        if (west && !jawClosed)
        {
            var insects = Named("Insect ").Where(t => t.X < 80).ToArray();
            var staged = insects.Where(t => At(t, CanopyStage, 22)).ToArray();
            if (!one)
            {
                if (staged.Length == 0 && insects.Length + (Carry is >= 1 and <= 4 ? 1 : 0) >= 2)
                    return Carry == 0 ? Pickup(insects, "to stage by the crossing")
                        : Stage(CanopyStage, "Stage the insect by the crossing");
                if (Carry != 0) return Feed(1, 2);
                return Pickup(insects.Except(staged).Concat(staged)) ?? Restart("The two treetop pitchers need insects");
            }
            if (!two && Carry == 0)
                return Pickup(staged.Length > 0 ? staged : insects, "for pitcher plant 2")
                    ?? Wait("Wait for pitcher plant 1 to reopen", "Then try the crossing again.");
            return TakeOff(-1736, -236, "Jump onto pitcher plant 1");
        }

        if (Triangle is >= 36 and <= 41)
        {
            if (Carry == 6 || jawClosed) return TakeOff(886, -99, "Jump down from the hive ledge");
            return Carry == 0 ? Pickup(Named("Beehive")) : DropAtCurrentSpot();
        }
        if (Triangle is 42 or 43)
            return Hold("Jump left to the beehive", "Left");
        if (Triangle is 44 or 45)
            return Bank[48] == 1 ? Wait("Wait on pitcher plant 4", "The frog will launch you to the cave entrance.")
                : jawClosed ? Hold("Jump back to collect the frog", "Left")
                : Hold("Jump up toward the hive", "Up");
        if (Triangle is 34 or 35)
            return Bank[47] == 1 ? Wait("Wait on pitcher plant 3", "The frog will launch you to the east bank.")
                : Carry == 5 || Named("Frog 1").Any(t => t.X >= 80)
                    ? Hold("Jump east with the frog route", "Right")
                    : Hold("Jump west toward the hollow tree", "Left");

        if (!jawClosed)
        {
            if (Carry == 6) return Throw("Close the flytrap with the beehive", 13, "Mutant Flytrap 1,");
            if (west) return Restart("The hive route starts from the treetops");
            if (four)
                return Bank[48] == 1 ? TakeOff(1081, -285, "Jump onto pitcher plant 4")
                    : TakeOff(1081, -285, "Jump onto pitcher plant 4 toward the hive");
            if (Carry is >= 1 and <= 4) return Feed(4, 9);
            if (Carry != 0) return DropAtCurrentSpot();
            return Pickup(Named("Insect ").Where(t => t.X >= 450), "for pitcher plant 4")
                ?? Restart("The route to the beehive needs a creature");
        }

        if (west)
        {
            if (Carry == 5)
                return three ? TakeOff(67, 10, "Carry the frog across pitcher plant 3") : Feed(3, 6);
            if (Bank[17] == 0)
                return Carry is >= 1 and <= 4 ? Throw("Feed the hollow tree to bring out the frog", 5, "Hollow tree")
                    : Pickup(Named("Insect ").Where(t => t.X < 80), "for the hollow tree")
                        ?? Restart("The hollow tree needs an insect");
            if (three && Bank[47] == 1) return TakeOff(67, 10, "Jump onto pitcher plant 3");
            if (Pickup(Named("Frog 1").Where(t => t.X < 80)) is { } frog)
                return Carry == 0 ? frog : DropAtCurrentSpot();
            if (three) return TakeOff(67, 10, "Cross to the frog's east landing");
            if (Carry is >= 1 and <= 4) return Feed(3, 6);
            return Pickup(Named("Insect ").Where(t => t.X < 80), "to cross to the frog")
                ?? Restart("The frog is on the other bank and the pitcher is open");
        }

        if (four && Bank[48] == 1) return TakeOff(1081, -285, "Jump onto pitcher plant 4");
        if (Carry == 5)
            return four ? Wait("Wait for pitcher plant 4 to reopen", "Then throw the frog into it.") : Feed(4, 9);
        if (Bank[17] == 1 && Pickup(Named("Frog 1").Where(t => t.X >= 80), "for pitcher plant 4") is { } eastFrog)
            return Carry == 0 ? eastFrog : DropAtCurrentSpot();
        if (three) return TakeOff(432, 7, "Cross west toward the hollow tree");
        if (Carry != 0) return Feed(3, 8, otherSide: true);
        return Pickup(Named("Insect ").Where(t => t.X >= 400), "to cross to the hollow tree")
            ?? Restart("The crossing to the hollow tree needs an insect");
    }

    private FieldNavigationTarget? Canopy() =>
        !Has(Typoon) ? Reward("Materia", 87)
        : !Has(Minerva) ? Exit(620, "Take the branch down to Minerva Band")
        : Exit(623, "Return to the raised forest path");

    private FieldNavigationTarget? Cave() =>
        !Has(Apocalypse) ? Reward("Item", 140)
        : !Has(Elixir) ? Reward("Item", 5)
        : (state.Collected & AllTreasures) != AllTreasures ? Exit(623, "Return for the remaining forest treasures")
        : Exit(55, "All seven treasures collected. Leave the Ancient Forest");

    private FieldNavigationTarget? PickMiddleFrog() =>
        Pickup(Named("Frog 2")) ?? Pickup(Named("Frog 1"))
            ?? Restart("There is no frog available for the next launch");

    private FieldNavigationTarget? Feed(int plant, int zone, bool otherSide = false) =>
        Throw($"Feed pitcher plant {plant}", zone,
            otherSide ? $"Pitcher plant {plant}, other side" : $"Pitcher plant {plant}, throwing spot");

    private FieldNavigationTarget? Throw(string action, int zone, string label)
    {
        const string confirm = "Press Confirm to throw.";
        if (Zone == zone) return Cue(action, confirm);
        return Named(label).OrderBy(DistanceSquared).FirstOrDefault() is { StableId: not null } target
            ? Follow(target, action, "Check the next Story instruction before throwing.")
            : null;
    }

    private FieldNavigationTarget? TakeOff(int x, int y, string action, bool press = false)
    {
        var target = objects.FirstOrDefault(t => t.X == x && t.Y == y && t.Label.StartsWith("Jump ", StringComparison.Ordinal));
        if (target.StableId is null) return null;
        string direction = target.Label.Contains("Right", StringComparison.Ordinal) ? "Right"
            : target.Label.Contains("Left", StringComparison.Ordinal) ? "Left" : "Down";
        return Follow(target, action, $"{(press ? "Press" : "Hold")} {direction} to jump.");
    }

    private FieldNavigationTarget? Reward(string kind, int nativeId)
    {
        var key = $":{kind}:{nativeId}:";
        var target = objects.FirstOrDefault(t => t.StableId?.Contains(key, StringComparison.Ordinal) == true);
        return target.StableId is null ? null : Follow(target, $"Collect {target.Label}", "Press Confirm to collect it.");
    }

    private FieldNavigationTarget? Pickup(IEnumerable<FieldNavigationTarget> candidates, string? purpose = null)
    {
        var target = candidates.FirstOrDefault();
        return target.StableId is null ? null : Follow(target,
            $"Pick up {target.Label}{(purpose is null ? "" : $" {purpose}")}", "Press Confirm to pick it up.");
    }

    private FieldNavigationTarget Stage((int X, int Y, int Z) point, string action) =>
        Zone == 1 && Near(point, 13) ? Cue(action, "Press Confirm to set it down here.")
        : Point(point, action, "Check the next Story instruction before setting it down.", 8);

    private FieldNavigationTarget? DropAtCurrentSpot() =>
        Zone == 1 ? Cue("Set down the carried creature", "Press Confirm to set it down here.")
        : Restart("A creature is already in your hands");

    private FieldNavigationTarget? Exit(int field, string action) =>
        ReachableExit(field) is { } target ? Traverse(target, action) : null;

    private FieldNavigationTarget? ReachableExit(int field)
    {
        var target = exits.FirstOrDefault(t => t.DestinationFieldIds?.Contains(field) == true);
        return target.StableId is null ? null : target;
    }

    private static FieldNavigationTarget Traverse(FieldNavigationTarget target, string label) =>
        target with { Category = FieldNavigationCategory.Story, Label = label, CompletesOnArrival = true };

    private static FieldNavigationTarget Follow(FieldNavigationTarget target, string label, string instruction) =>
        target with
        {
            Category = FieldNavigationCategory.Story,
            Label = label,
            StableId = $"forest-story:{label}:{target.StableId}",
            InteractionInstruction = instruction
        };

    private FieldNavigationTarget Point((int X, int Y, int Z) point, string label, string instruction, int radius) =>
        new(Field, FieldNavigationCategory.Story, label, point.X, point.Y, point.Z, $"forest-story:{label}",
            InteractionRadius: radius, InteractionInstruction: instruction);

    private FieldNavigationTarget Cue(string label, string instruction) =>
        new(Field, FieldNavigationCategory.Story, label, position.X, position.Y, position.Z, $"forest-story:{label}",
            ManualNavigationGuidance: instruction);

    private FieldNavigationTarget Hold(string label, string direction) => Cue(label, $"Hold {direction}.");
    private FieldNavigationTarget Wait(string label, string result) => Cue(label, result);
    private FieldNavigationTarget Restart(string reason) => Cue(reason,
        $"{(state.Variant == SupportedFieldScriptVariant.EchoS124 ? "Press" : "Press and release")} Switch to return to the entrance. Collected treasures stay with you.");

    private IEnumerable<FieldNavigationTarget> Named(string prefix) =>
        objects.Where(t => t.Label.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(DistanceSquared);
    private long DistanceSquared(FieldNavigationTarget target) =>
        Square((long)target.X - position.X) + Square((long)target.Y - position.Y) + Square((long)target.Z - position.Z);
    private bool Near((int X, int Y, int Z) point, int radius) =>
        Square((long)point.X - position.X) + Square((long)point.Y - position.Y) + Square((long)point.Z - position.Z) <= radius * radius;
    private static bool At(FieldNavigationTarget target, (int X, int Y, int Z) point, int radius) =>
        Square((long)target.X - point.X) + Square((long)target.Y - point.Y) + Square((long)target.Z - point.Z) <= radius * radius;
    private static long Square(long value) => value * value;
}
