using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public sealed class FieldExitLabelResolver
{
    private readonly Func<int, FieldMapNameResolution> resolveMapNames;
    private readonly Func<string> readCurrentMapName;
    private readonly Func<int, string?> resolveRoomDescriptor;
    private readonly Func<int?>? readGlacierCorridorState;

    /// <param name="readGlacierCorridorState">
    /// Bank 1 byte 184 while a Great Glacier passage screen (670..675) is loaded, e.g.
    /// <see cref="GreatGlacierCorridorStateReader.Read"/>. Without it passage exits keep
    /// their plain name; every other glacier exit is named either way.
    /// </param>
    public FieldExitLabelResolver(
        Func<int, FieldMapNameResolution> resolveMapNames,
        Func<string> readCurrentMapName,
        Func<int, string?>? resolveRoomDescriptor = null,
        Func<int?>? readGlacierCorridorState = null)
    {
        this.resolveMapNames = resolveMapNames;
        this.readCurrentMapName = readCurrentMapName;
        this.resolveRoomDescriptor = resolveRoomDescriptor ?? FieldRoomDescriptorCatalog.Resolve;
        this.readGlacierCorridorState = readGlacierCorridorState;
    }

    public IReadOnlyList<FieldNavigationTarget> Resolve(
        IReadOnlyList<FieldNavigationTarget> targets)
    {
        if (targets.Count == 0)
        {
            return targets;
        }

        var currentMapName = Normalize(readCurrentMapName());
        // One read of the passage state per publication, and only if a passage exit asks.
        int? corridorState = null;
        var corridorStateRead = false;
        int? ReadCorridorState()
        {
            if (!corridorStateRead)
            {
                corridorStateRead = true;
                try
                {
                    corridorState = readGlacierCorridorState?.Invoke();
                }
                catch (Exception)
                {
                    corridorState = null;
                }
            }

            return corridorState;
        }

        return targets
            .Select(target => target with
            {
                Label = ResolveLabel(target, currentMapName, ReadCorridorState)
            })
            .ToArray();
    }

    private string ResolveLabel(FieldNavigationTarget target, string currentMapName, Func<int?> readCorridorState)
    {
        if (TempleChaseLayout.ResolveLabel(target) is { } chaseLabel)
        {
            return chaseLabel;
        }

        if (GoldSaucerPlatformExitCatalog.ResolveLabel(target) is { } platformLabel)
        {
            return platformLabel;
        }

        var exactLabel = target.StableId switch
        {
            // Highwind rooms share the broad map name "Highwind". Name the native
            // gateway/LINE rather than making the player find it through a Story milestone.
            // Live gateway switches, LINE enable state and MAPJUMP guards still decide which
            // doors exist. The forward LINE selects bridge variants 68/70/72; its stable id
            // retains all three even after the guards resolve the current destination.
            "gateway:66:0:74" or "gateway:67:0:74" => "Enter Highwind corridor",
            "script-exit:70:6:74" or "gateway:72:0:74" or "gateway:73:0:74" or
            "gateway:76:0:74" => "Return to Highwind corridor",
            "script-exit:73:3:72" or "script-exit:74:6:68,70,72" => "Enter Highwind bridge",
            "gateway:74:0:66" or "gateway:75:0:66" => "Go to Highwind outside deck",
            "gateway:74:1:73" or "gateway:75:1:73" => "Enter Highwind operations room",
            "gateway:74:2:76" or "gateway:75:2:76" => "Enter Highwind Chocobo hold",
            "script-exit:74:7:744" => "Leave Highwind for Northern Crater",
            "gateway:148:0:151" => "Exit to Sector 7 Slums, ground floor",
            "gateway:148:1:151" => "Exit to Sector 7 Slums, upstairs",
            "script-exit:161:1:161,163" => "South through the winding tunnel",
            "script-exit:161:2:161,162" => "North through the winding tunnel",
            "script-exit:167:6:164" => "Climb back to the large duct",
            "script-exit:218:13:220" => "Enter the Group Room",
            "script-exit:218:14:220" => "Enter the &$#% Room",
            "script-exit:218:15:219" => "Enter the Queen's Room",
            "script-exit:218:16:219" => "Enter the Lover's Room",
            "script-exit:238:14:232" => "Lower-floor elevator, left door; press OK",
            "script-exit:238:15:232" => "Lower-floor elevator, right door; press OK",
            "script-exit:238:16:233" => "Upper-floor elevator, right door; press OK",
            "script-exit:238:17:233" => "Upper-floor elevator, left door; press OK",
            "gateway:242:2:244" => "Enter Peace Preservation and Weapon Development Research Library",
            "gateway:242:3:244" => "Enter Space Development Research Library",
            "gateway:242:4:243" => "Enter Urban Development Research Library",
            "gateway:242:5:243" => "Enter Scientific Research Library",
            // Mansion gateways share only the broad MPNAM labels (1f, 2f, Basement).
            // Name the separate wings and the stair/library direction so the quest's
            // outward and return routes can be distinguished. IDs are native gateways.
            "gateway:297:1:298" => "Door to the mansion rear rooms",
            "gateway:297:2:298" => "Door to the mansion left ground-floor room",
            "gateway:297:3:298" => "Door to the mansion right ground-floor room",
            "gateway:297:4:299" => "Door to the mansion upstairs left wing",
            "gateway:297:5:300" => "Door to the mansion upstairs right wing",
            "gateway:298:0:297" => "Return to the mansion hall through the rear door",
            "gateway:298:1:297" => "Return to the mansion hall through the left door",
            "gateway:298:2:297" => "Return to the mansion hall through the right door",
            "gateway:299:0:297" or "gateway:300:0:297" => "Return to the mansion upstairs landing",
            "gateway:300:1:301" => "Secret passage to the spiral stairs",
            "gateway:301:0:300" => "Leave the spiral stairs for the upstairs room",
            "gateway:301:1:302" => "Leave the spiral stairs for the basement passage",
            "gateway:302:0:301" => "Return to the spiral stairs",
            "gateway:302:1:303" => "Continue along the basement corridor",
            "gateway:303:0:302" or "gateway:303:1:302" => "Return toward the spiral stairs",
            "gateway:303:2:304" or "script-exit:303:14:305" => "Enter the basement library",
            "gateway:304:1:303" or "gateway:305:1:303" or "gateway:306:1:303" => "Leave the library for the basement corridor",
            "gateway:304:0:307" or "gateway:305:0:308" or "gateway:306:0:308" => "Continue into the deeper library room",
            "gateway:307:0:309" or "gateway:308:0:310" => "Enter the innermost library room",
            "gateway:307:1:304" or "gateway:307:2:304" or "gateway:307:3:304" or
            "gateway:308:1:305" or "gateway:308:2:305" or "gateway:308:3:305" => "Return through the basement library",
            "gateway:309:0:307" or "gateway:310:0:308" => "Leave the innermost library room",
            // Gaea's Cliff's icicle caves (gaiin_3 693, gaiin_4 696, gaiin_5 697) all share
            // the map name "Inside of Gaea's Cliff", and gaiin_5's four icicle battle lines are
            // script exits too (each leaves for gaiin_3 only by the jump the game offers after
            // its battle). Each way out is named for where it goes.
            // The first two ice caves, gaiin_1 (690) and gaiin_2 (691), are joined by four doors
            // each way on separate walkmesh levels: gaiin_1's entrance, middle and upper levels,
            // gaiin_2's main and boulder levels. Each door is named by the level it leaves and
            // the level it lands on (installed walkmesh, both runtimes).
            "gateway:690:0:691" => "Door from the entrance level to the second cave's main level",
            "gateway:690:1:691" => "Door from the upper level to the second cave's main level",
            "gateway:690:2:691" => "Door from the middle level to the second cave's boulder level",
            "gateway:690:3:691" => "Door from the middle level to the second cave's main level",
            "gateway:691:0:690" => "Door from the main level to the first cave's entrance level",
            "gateway:691:1:690" => "Door from the main level to the first cave's upper level, the way on",
            "gateway:691:2:690" => "Door from the boulder level to the first cave's middle level",
            "gateway:691:3:690" => "Door from the main level to the first cave's middle level",
            "script-exit:693:15:692" => "Back out onto the cliff face",
            "script-exit:693:16:696" => "Passage out to the ledge by the icicles",
            "script-exit:693:17:696" => "Passage beyond the fallen ice, out to the far ledge",
            "gateway:696:0:693" => "Back into the cave, beyond the fallen ice",
            "gateway:696:1:693" => "Back into the cave by the icicle passage",
            "gateway:696:2:697" => "Along the ledge to the icicles",
            "gateway:696:3:697" => "Along the ledge to the way down",
            "script-exit:697:3:693" => "First icicle, then the jump down into the cave",
            "script-exit:697:4:693" => "Second icicle, then the jump down into the cave",
            "script-exit:697:5:693" => "Third icicle, then the jump down into the cave",
            "script-exit:697:6:693" => "Fourth icicle, then the jump down into the cave",
            "script-exit:697:7:694" => "Out onto the cliff face, the way on up",
            "script-exit:697:8:696" => "Back along the ledge from the icicles",
            "script-exit:697:9:696" => "Back along the ledge from the way down",
            "gateway:335:0:329" => "Enter Item Store",
            "gateway:335:1:330" => "Enter Bar",
            "gateway:335:2:328" => "Enter Materia Store",
            "gateway:335:3:328" => "Enter Weapon Store",
            "gateway:335:4:341" => "Enter Kalm Traveler's house",
            "gateway:335:5:338" => "Enter house with rear tower",
            "gateway:335:6:336" => "Enter west house",
            "gateway:335:7:333" => "Enter house beside the inn",
            "gateway:335:8:331" => "Enter Kalm Inn",
            "gateway:328:0:335" => "Leave Materia Store for Kalm",
            "gateway:328:1:335" => "Leave Weapon Store for Kalm",
            "gateway:338:2:340" => "Enter rear tower",
            "gateway:340:0:338" => "Return to house",
            "script-exit:340:4:335" => "Exit rear tower to Kalm",
            "gateway:343:0:344" => "Enter Choco Bill's house",
            "gateway:343:1:345" => "Enter Chocobo stables",
            "gateway:344:0:343" => "Leave Choco Bill's house for Chocobo Farm",
            "gateway:345:0:343" => "Leave Chocobo stables for Chocobo Farm",
            // Mythril Mine (psdun_1..4, fields 349-352). Every gateway here leads to another
            // Mythril Mine screen, so the generated label was "Exit to Mythril Mine" for all
            // of them and the two world-map mouths fell back to a bare "Exit". Three
            // identical strings in one cavern left no way to tell the way onward from the
            // side chamber or from the way back out. Topology from the native gateways:
            // 350 is the entrance cavern (world map, 349, 351), 349 holds the Junon-side
            // mouth (world map, 350, 352), and 351 and 352 are dead-end chambers.
            "gateway:349:0:352" => "Tunnel to the side chamber",
            "gateway:349:1:350" => "Tunnel back toward the mine entrance",
            "gateway:349:2:5" => "Leave the mine for the world map, Junon side",
            "gateway:350:0:349" => "Tunnel deeper into the mine",
            "gateway:350:1:351" => "Tunnel to the side chamber",
            "gateway:350:2:4" => "Leave the mine for the world map, Midgar side",
            "gateway:351:0:350" => "Tunnel back to the mine entrance",
            "gateway:352:0:349" => "Tunnel back to the main cavern",
            // Midgar slums. Contiguous gateway chains are collapsed by
            // FieldExitPresentationPolicy; what is left here are real pairs of doors that
            // share a destination map name. Height is the discriminator wherever the game
            // stacks them: the two Sector 7 Weapon Shop doors below sit at z=0 and z=276,
            // matching the already-verified "ground floor"/"upstairs" pair on field 148.
            "gateway:145:1:146" => "Exit to Sector 7 Station, upper walkway",
            "gateway:145:2:146" => "Exit to Sector 7 Station, ground level",
            "gateway:146:1:145" => "Exit to Train Graveyard, upper walkway",
            "gateway:146:2:145" => "Exit to Train Graveyard, ground level",
            "gateway:151:0:156" => "Exit to the Sector 7 Slums crossroads",
            "gateway:151:3:148" => "Enter Sector 7 Weapon Shop, ground floor",
            "gateway:151:4:148" => "Enter Sector 7 Weapon Shop, upstairs",
            "gateway:151:6:150" => "Exit to Sector 7 Slums, upper walkway",
            "gateway:172:1:177" => "Exit to the Sector 5 Slums square",
            "gateway:172:2:173" => "Exit to Sector 5 Slum, church road",
            "gateway:188:0:187" => "Exit to the garden and the slums",
            "gateway:188:1:190" => "Stairs to the upper floor",
            "gateway:192:0:191" => "Exit to Sector 6, road to the Sector 5 Slums",
            "gateway:192:1:194" => "Exit to Sector 6, road to Wall Market",
            "gateway:193:0:191" => "Exit to Sector 6, road to the Sector 5 Slums",
            "gateway:193:1:194" => "Exit to Sector 6, road to Wall Market",
            "gateway:205:1:222" => "Exit to the Wall Market side street",
            "gateway:205:4:195" => "Exit to the Wall Market shopping street",
            "gateway:207:0:210" => "Exit to Corneo Hall 2nd floor, main landing",
            "gateway:207:2:208" => "Exit to Corneo Hall 2nd floor, side room",
            "gateway:218:0:216" => "Exit to Honey Bee Inn, dressing room",
            "gateway:218:1:214" => "Exit to Honey Bee Inn, entrance",
            // Junon. Both remaining pairs lead to different screens that share one map name.
            "gateway:368:0:367" => "Exit to the Barracks, toward the street",
            "gateway:368:1:369" => "Exit to the Barracks, inner room",
            "gateway:393:0:392" => "Exit to Junon Path, upper level",
            "gateway:393:1:394" => "Exit to Junon Path, lower level",

            // Fort Condor. The generated labels doubled the preposition on the
            // two screens whose map names already start with a place word
            // ("Exit to Entrance to Fort Condor"), and the hill mouth is a
            // world-map return point with no map name, so it read as a bare
            // "Exit". Directions follow the native map names: the base sits
            // below the entrance, which sits below the fort itself.
            "gateway:353:0:354" => "Way up to the fort entrance",
            "gateway:353:1:6" => "Leave Fort Condor for the world map",
            "gateway:354:0:353" => "Way back down to the base of the fort",
            "gateway:355:0:356" => "Way up to the Watch Room",
            "gateway:356:0:355" => "Way back down into Fort Condor",

            // Verified as climbs in flevel: convil_1 entities 3 and 4 are two
            // approaches onto one ladder down, and condor2 entity 4 is the climb
            // up into the fort.
            "script-exit:354:4:355" => "Ladder up into Fort Condor",
            "script-exit:355:3:354" => "Ladder down to the fort entrance",
            "script-exit:355:4:354" => "Ladder down to the fort entrance",
            "script-exit:356:5:358" => "Way up to the top of the mountain",
            // del1/wmJump boards the return ship after crew1 enables its LINE.
            // MAPJUMP 39 is a world-map transport entry with no room name.
            "script-exit:441:6:39" => "Board the ship to Junon",
            // gongaga/line4 leaves the village on a Confirm press. Whether it reaches the
            // world map or the jungle is chosen by bank 3 address 132 bit 6, so the label
            // names neither.
            "script-exit:518:11:17,514" => "Leave Gongaga; press Confirm",
            // Godo's Pagoda keeps its five floors in one field, so the destination's map name
            // is this room's own. Each staircase exit is one floor's (FieldScriptNavigationCatalog
            // offers it only on that floor), and the label says where it goes.
            "script-exit:586:14:586:floor0" => "Stairs up to the second floor",
            "script-exit:586:14:586:floor1" => "Stairs up to the third floor",
            "script-exit:586:14:586:floor2" => "Stairs up to the fourth floor",
            "script-exit:586:14:586:floor3" => "Stairs up to the fifth floor",
            "script-exit:586:15:586:floor1" => "Stairs down to the first floor",
            "script-exit:586:15:586:floor2" => "Stairs down to the second floor",
            "script-exit:586:15:586:floor3" => "Stairs down to the third floor",
            "script-exit:586:15:586:floor4" => "Stairs down to the fourth floor",
            // The Great Glacier's ways back onto the snowfield are world entries (64 from the
            // All cave, 60 from the cabin's front gateway), which have no map name and were
            // a bare "Exit".
            "script-exit:682:1:64" => "Leave the cave for the snowfield",
            "gateway:686:1:60" => "Leave for the snowfield",
            _ => null
        };
        if (exactLabel is not null)
        {
            return exactLabel;
        }

        if (GreatGlacierExitLabels.ResolveLabel(target, readCorridorState) is { } glacierLabel)
        {
            return glacierLabel;
        }

        var destinationFieldIds = target.DestinationFieldIds;
        if (destinationFieldIds is null || destinationFieldIds.Count == 0)
        {
            return IsOrdinalExitLabel(target.Label) ? "Exit" : target.Label;
        }

        var names = destinationFieldIds
            .Distinct()
            .Select(fieldId => ResolveDestinationName(fieldId, currentMapName))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return names.Length == 0
            ? "Exit"
            : $"Exit to {string.Join(" or ", names)}";
    }

    private string? ResolveDestinationName(int fieldId, string currentMapName)
    {
        var resolution = resolveMapNames(fieldId);
        if (!resolution.IsKnownField)
        {
            return null;
        }

        var names = resolution.Names
            .Select(Normalize)
            .Where(name => name.Length != 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string resolvedName;
        if (currentMapName.Length != 0)
        {
            var currentMatch = names.FirstOrDefault(
                name => string.Equals(name, currentMapName, StringComparison.OrdinalIgnoreCase));
            if (currentMatch is not null)
            {
                resolvedName = currentMatch;
                return AppendRoomDescriptor(fieldId, resolvedName);
            }

        }

        resolvedName = names.Length switch
        {
            0 => string.Empty,
            1 => names[0],
            _ => string.Join(" or ", names)
        };
        return resolvedName.Length == 0
            ? null
            : AppendRoomDescriptor(fieldId, resolvedName);
    }

    private string AppendRoomDescriptor(int fieldId, string name)
    {
        var descriptor = Normalize(resolveRoomDescriptor(fieldId) ?? string.Empty);
        return descriptor.Length == 0 || name.Contains(descriptor, StringComparison.OrdinalIgnoreCase)
            ? name
            : $"{name}, {descriptor}";
    }

    private static bool IsOrdinalExitLabel(string label) =>
        label.StartsWith("Exit ", StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(label.AsSpan(5), out _);

    private static string Normalize(string value) =>
        string.Join(
            " ",
            (value ?? string.Empty).Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}

/// <summary>
/// Where each Great Glacier exit finally comes out. Every glacier screen has the map name
/// "Great Glacier", and most exits lead into one of the six passage screens (move_s, move_i,
/// move_f, move_r, move_u, move_d: fields 670..675), whose event scripts send the party on
/// according to savemap bank 1 byte 184. A path from an ordinary screen always writes the
/// same value, so where it comes out is fixed. Inside a passage it depends on the live byte.
///
/// <para>The tables were built from the installed scripts: each LINE's MAPJUMP and the byte
/// its script leaves, followed through each passage by the one line that does not lead back.
/// GreatGlacierExitLabelTests rebuilds every entry from the installed field archive. The
/// landmark names are what each destination screen shows (its background, or its native
/// hot spring, lake crossing and caves), cross-checked with Absolute Steve's guide. No label
/// names a treasure.</para>
/// </summary>
public static class GreatGlacierExitLabels
{
    public const int FirstPassageField = 670;
    public const int LastPassageField = 675;

    private const string GlacierExit = "Exit to Great Glacier";

    public static bool IsPassage(int fieldId) => fieldId is >= FirstPassageField and <= LastPassageField;

    /// <summary>
    /// The label for a glacier LINE exit, or null to leave the ordinary name: an exit this
    /// table does not know, a passage exit without a readable state, or a destination with a
    /// map name of its own (Frostbite Cave, Cave).
    /// </summary>
    public static string? ResolveLabel(FieldNavigationTarget target, Func<int?> readCorridorState)
    {
        if (!TryParseScriptExit(target.StableId, out var field, out var entity))
        {
            return null;
        }

        if (IsPassage(field))
        {
            return readCorridorState() is int state &&
                   CorridorFinals.TryGetValue((field, state, entity), out var hop)
                ? Describe(IsPassage(hop.Next), hop.Final)
                : null;
        }

        return FixedFinals.TryGetValue((field, entity), out var final)
            ? Describe(target.DestinationFieldIds?.Any(IsPassage) == true, final)
            : null;
    }

    public static int? FixedFinal(int field, int entity) =>
        FixedFinals.TryGetValue((field, entity), out var final) ? final : null;

    public static (int Next, int Final)? CorridorFinal(int field, int state, int entity) =>
        CorridorFinals.TryGetValue((field, state, entity), out var hop) ? hop : null;

    public static IEnumerable<(int Field, int Entity)> FixedKeys => FixedFinals.Keys;

    public static IEnumerable<(int Field, int State, int Entity)> CorridorKeys => CorridorFinals.Keys;

    public static int FixedEntryCount => FixedFinals.Count;

    public static int CorridorEntryCount => CorridorFinals.Count;

    /// <summary>Every label the tables can produce, both ways a destination can be reached.</summary>
    public static IEnumerable<string> AllLabels() =>
        FixedFinals.Values.Concat(CorridorFinals.Values.Select(hop => hop.Final))
            .Distinct()
            .SelectMany(final => new[] { Describe(false, final), Describe(true, final) })
            .OfType<string>()
            .Distinct();

    private static string? Describe(bool throughPassage, int final)
    {
        if (final == 48)
        {
            return throughPassage ? $"{GlacierExit}, passage toward the world map" : "Leave the glacier for the world map";
        }

        if (final is >= 61 and <= 64)
        {
            return throughPassage ? $"{GlacierExit}, passage toward the snowfield" : "Leave for the snowfield";
        }

        if (final is 661 or 662)
        {
            return throughPassage ? $"{GlacierExit}, passage toward Frostbite Cave" : null;
        }

        if (final is 666 or 678 or 682 or 684)
        {
            return throughPassage ? $"{GlacierExit}, passage toward a cave" : null;
        }

        if (!Landmarks.TryGetValue(final, out var landmark))
        {
            return null;
        }

        return throughPassage ? $"{GlacierExit}, passage toward the {landmark}" : $"{GlacierExit}, {landmark}";
    }

    private static bool TryParseScriptExit(string stableId, out int field, out int entity)
    {
        field = 0;
        entity = 0;
        var parts = stableId.Split(':');
        return parts.Length >= 3 &&
               parts[0] == "script-exit" &&
               int.TryParse(parts[1], out field) &&
               int.TryParse(parts[2], out entity) &&
               field is >= 658 and <= 684;
    }

    // What each destination screen shows. 658 has the "ICE GATE" sign; 659 is snow-laden pine
    // forest; 660 a snowy valley between rocky slopes; 663 open snow slopes; 664 flat plates of
    // ice above a stand of pines, beside the lake; 665 the frozen lake of the ice-floe crossing;
    // 667 its north shore; 668 a fallen tree lying across a ravine; 676 open snow with a single
    // pine; 677 a field of snow-covered pillars around a cave; 679 rocky ridges where four
    // paths meet; 680 the hot spring (native, dialogs 54 and 55); 681 a rocky mountain trail;
    // 683 a ravine between cliffs below the cave in 684.
    private static readonly IReadOnlyDictionary<int, string> Landmarks = new Dictionary<int, string>
    {
        [658] = "Ice Gate",
        [659] = "pine forest",
        [660] = "rocky valley",
        [663] = "open snow slopes",
        [664] = "ice flats",
        [665] = "frozen lake",
        [667] = "north shore of the frozen lake",
        [668] = "fallen-tree bridge",
        [676] = "lone pine",
        [677] = "snow pillars",
        [679] = "rocky crossroads",
        [680] = "hot spring",
        [681] = "mountain trail",
        [683] = "cliff ravine"
    };


    // Ordinary screens: (field, LINE entity) -> the screen the path finally reaches.
    private static readonly Dictionary<(int Field, int Entity), int> FixedFinals = new()
    {
        [(658, 4)] = 660, [(658, 5)] = 659, [(658, 6)] = 663, [(658, 7)] = 48,
        [(659, 4)] = 664, [(659, 5)] = 658, [(659, 6)] = 658, [(659, 7)] = 663,
        [(660, 4)] = 658, [(660, 5)] = 679, [(660, 6)] = 664, [(660, 7)] = 661,
        [(661, 5)] = 660, [(661, 6)] = 662, [(661, 7)] = 662, [(661, 8)] = 662,
        [(662, 2)] = 661, [(662, 3)] = 48,
        [(663, 4)] = 659, [(663, 5)] = 659, [(663, 6)] = 658, [(663, 7)] = 658, [(663, 8)] = 658, [(663, 9)] = 658, [(663, 10)] = 677, [(663, 11)] = 677, [(663, 12)] = 677, [(663, 13)] = 677, [(663, 14)] = 677, [(663, 15)] = 677, [(663, 16)] = 677, [(663, 17)] = 677, [(663, 18)] = 677,
        [(664, 4)] = 676, [(664, 5)] = 676, [(664, 6)] = 676, [(664, 7)] = 676, [(664, 8)] = 660, [(664, 9)] = 660, [(664, 10)] = 659, [(664, 11)] = 659, [(664, 12)] = 659, [(664, 13)] = 665,
        [(665, 11)] = 664, [(665, 12)] = 667, [(665, 13)] = 667, [(665, 14)] = 667, [(665, 15)] = 667, [(665, 16)] = 667, [(665, 17)] = 667, [(665, 18)] = 667, [(665, 19)] = 667, [(665, 20)] = 667,
        [(666, 2)] = 667, [(666, 3)] = 667,
        [(667, 4)] = 666, [(667, 5)] = 665,
        [(668, 4)] = 676, [(668, 5)] = 676, [(668, 6)] = 676, [(668, 7)] = 676, [(668, 8)] = 676, [(668, 9)] = 676, [(668, 10)] = 681, [(668, 11)] = 681, [(668, 12)] = 681, [(668, 13)] = 681, [(668, 14)] = 681, [(668, 15)] = 681, [(668, 16)] = 677,
        [(676, 4)] = 664, [(676, 5)] = 679, [(676, 6)] = 679, [(676, 7)] = 679, [(676, 8)] = 668,
        [(677, 4)] = 668, [(677, 5)] = 668, [(677, 6)] = 663, [(677, 7)] = 681, [(677, 8)] = 681, [(677, 9)] = 681, [(677, 10)] = 678, [(677, 11)] = 678,
        [(678, 1)] = 677, [(678, 2)] = 677,
        [(679, 4)] = 660, [(679, 5)] = 680, [(679, 6)] = 676, [(679, 7)] = 680,
        [(680, 6)] = 62, [(680, 7)] = 62, [(680, 8)] = 62, [(680, 9)] = 62, [(680, 10)] = 62, [(680, 11)] = 62, [(680, 12)] = 679, [(680, 13)] = 679, [(680, 14)] = 679, [(680, 15)] = 679, [(680, 16)] = 679, [(680, 17)] = 679, [(680, 18)] = 679, [(680, 19)] = 679,
        [(681, 4)] = 61, [(681, 5)] = 668, [(681, 6)] = 668, [(681, 7)] = 668, [(681, 8)] = 677,
        [(682, 1)] = 64,
        [(683, 4)] = 63, [(683, 5)] = 63, [(683, 6)] = 63, [(683, 7)] = 63, [(683, 8)] = 679, [(683, 9)] = 679, [(683, 10)] = 679, [(683, 11)] = 679, [(683, 12)] = 679, [(683, 13)] = 679, [(683, 14)] = 679, [(683, 15)] = 684,
        [(684, 3)] = 683,
    };

    // Passage screens: (field, bank 1 byte 184, LINE entity) -> (next screen, screen finally reached).
    private static readonly Dictionary<(int Field, int State, int Entity), (int Next, int Final)> CorridorFinals = new()
    {
        [(670, 27, 4)] = (658, 658), [(670, 27, 5)] = (670, 663),
        [(670, 28, 4)] = (658, 658), [(670, 28, 5)] = (670, 663),
        [(670, 29, 4)] = (670, 658), [(670, 29, 5)] = (670, 663),
        [(670, 30, 4)] = (670, 658), [(670, 30, 5)] = (670, 663),
        [(670, 31, 4)] = (670, 658), [(670, 31, 5)] = (663, 663),
        [(670, 32, 4)] = (670, 658), [(670, 32, 5)] = (663, 663),
        [(670, 33, 4)] = (663, 663), [(670, 33, 5)] = (671, 677),
        [(670, 34, 4)] = (663, 663), [(670, 34, 5)] = (671, 677),
        [(670, 35, 4)] = (671, 663), [(670, 35, 5)] = (677, 677),
        [(670, 36, 4)] = (671, 663), [(670, 36, 5)] = (677, 677),
        [(670, 37, 4)] = (671, 676), [(670, 37, 5)] = (668, 668),
        [(670, 38, 4)] = (671, 676), [(670, 38, 5)] = (668, 668),
        [(670, 39, 4)] = (668, 668), [(670, 39, 5)] = (670, 677),
        [(670, 40, 4)] = (668, 668), [(670, 40, 5)] = (670, 677),
        [(670, 41, 4)] = (670, 668), [(670, 41, 5)] = (677, 677),
        [(670, 42, 4)] = (670, 668), [(670, 42, 5)] = (677, 677),
        [(670, 43, 4)] = (670, 681), [(670, 43, 5)] = (677, 677),
        [(670, 44, 4)] = (670, 681), [(670, 44, 5)] = (677, 677),
        [(670, 45, 4)] = (681, 681), [(670, 45, 5)] = (670, 677),
        [(670, 46, 4)] = (681, 681), [(670, 46, 5)] = (670, 677),
        [(670, 47, 4)] = (668, 668), [(670, 47, 5)] = (681, 681),
        [(670, 48, 4)] = (668, 668), [(670, 48, 5)] = (681, 681),
        [(670, 49, 4)] = (670, 61), [(670, 49, 5)] = (681, 681),
        [(670, 50, 4)] = (670, 61), [(670, 50, 5)] = (681, 681),
        [(670, 51, 4)] = (61, 61), [(670, 51, 5)] = (670, 681),
        [(670, 52, 4)] = (61, 61), [(670, 52, 5)] = (670, 681),
        [(670, 53, 4)] = (675, 680), [(670, 53, 5)] = (62, 62),
        [(670, 54, 4)] = (675, 680), [(670, 54, 5)] = (62, 62),
        [(670, 55, 4)] = (671, 683), [(670, 55, 5)] = (63, 63),
        [(670, 56, 4)] = (671, 683), [(670, 56, 5)] = (63, 63),
        [(671, 21, 4)] = (673, 683), [(671, 21, 5)] = (670, 63),
        [(671, 22, 4)] = (673, 683), [(671, 22, 5)] = (670, 63),
        [(671, 23, 4)] = (676, 676), [(671, 23, 5)] = (670, 668),
        [(671, 24, 4)] = (676, 676), [(671, 24, 5)] = (670, 668),
        [(671, 25, 4)] = (670, 663), [(671, 25, 5)] = (670, 677),
        [(671, 26, 4)] = (670, 663), [(671, 26, 5)] = (670, 677),
        [(672, 0, 6)] = (659, 659), [(672, 0, 7)] = (659, 659), [(672, 0, 8)] = (659, 659), [(672, 0, 9)] = (659, 659), [(672, 0, 10)] = (658, 658), [(672, 0, 11)] = (658, 658), [(672, 0, 12)] = (658, 658), [(672, 0, 13)] = (658, 658), [(672, 0, 14)] = (658, 658), [(672, 0, 15)] = (658, 658), [(672, 0, 16)] = (658, 658),
        [(672, 1, 6)] = (659, 659), [(672, 1, 7)] = (659, 659), [(672, 1, 8)] = (659, 659), [(672, 1, 9)] = (659, 659), [(672, 1, 10)] = (658, 658), [(672, 1, 11)] = (658, 658), [(672, 1, 12)] = (658, 658), [(672, 1, 13)] = (658, 658), [(672, 1, 14)] = (658, 658), [(672, 1, 15)] = (658, 658), [(672, 1, 16)] = (658, 658),
        [(672, 3, 6)] = (659, 659), [(672, 3, 7)] = (659, 659), [(672, 3, 8)] = (659, 659), [(672, 3, 9)] = (659, 659), [(672, 3, 10)] = (663, 663), [(672, 3, 11)] = (663, 663), [(672, 3, 12)] = (663, 663), [(672, 3, 13)] = (663, 663), [(672, 3, 14)] = (663, 663), [(672, 3, 15)] = (663, 663), [(672, 3, 16)] = (663, 663),
        [(672, 4, 6)] = (659, 659), [(672, 4, 7)] = (659, 659), [(672, 4, 8)] = (659, 659), [(672, 4, 9)] = (659, 659), [(672, 4, 10)] = (663, 663), [(672, 4, 11)] = (663, 663), [(672, 4, 12)] = (663, 663), [(672, 4, 13)] = (663, 663), [(672, 4, 14)] = (663, 663), [(672, 4, 15)] = (663, 663), [(672, 4, 16)] = (663, 663),
        [(672, 5, 6)] = (664, 664), [(672, 5, 7)] = (664, 664), [(672, 5, 8)] = (664, 664), [(672, 5, 9)] = (664, 664), [(672, 5, 10)] = (659, 659), [(672, 5, 11)] = (659, 659), [(672, 5, 12)] = (659, 659), [(672, 5, 13)] = (659, 659), [(672, 5, 14)] = (659, 659), [(672, 5, 15)] = (659, 659), [(672, 5, 16)] = (659, 659),
        [(672, 6, 6)] = (664, 664), [(672, 6, 7)] = (664, 664), [(672, 6, 8)] = (664, 664), [(672, 6, 9)] = (664, 664), [(672, 6, 10)] = (659, 659), [(672, 6, 11)] = (659, 659), [(672, 6, 12)] = (659, 659), [(672, 6, 13)] = (659, 659), [(672, 6, 14)] = (659, 659), [(672, 6, 15)] = (659, 659), [(672, 6, 16)] = (659, 659),
        [(672, 7, 6)] = (676, 676), [(672, 7, 7)] = (676, 676), [(672, 7, 8)] = (676, 676), [(672, 7, 9)] = (676, 676), [(672, 7, 10)] = (664, 664), [(672, 7, 11)] = (664, 664), [(672, 7, 12)] = (664, 664), [(672, 7, 13)] = (664, 664), [(672, 7, 14)] = (664, 664), [(672, 7, 15)] = (664, 664), [(672, 7, 16)] = (664, 664),
        [(672, 8, 6)] = (676, 676), [(672, 8, 7)] = (676, 676), [(672, 8, 8)] = (676, 676), [(672, 8, 9)] = (676, 676), [(672, 8, 10)] = (664, 664), [(672, 8, 11)] = (664, 664), [(672, 8, 12)] = (664, 664), [(672, 8, 13)] = (664, 664), [(672, 8, 14)] = (664, 664), [(672, 8, 15)] = (664, 664), [(672, 8, 16)] = (664, 664),
        [(673, 9, 4)] = (673, 660), [(673, 9, 5)] = (658, 658),
        [(673, 10, 4)] = (673, 660), [(673, 10, 5)] = (658, 658),
        [(673, 11, 4)] = (660, 660), [(673, 11, 5)] = (673, 658),
        [(673, 12, 4)] = (660, 660), [(673, 12, 5)] = (673, 658),
        [(673, 13, 4)] = (660, 660), [(673, 13, 5)] = (664, 664),
        [(673, 14, 4)] = (660, 660), [(673, 14, 5)] = (664, 664),
        [(673, 15, 4)] = (673, 679), [(673, 15, 5)] = (660, 660),
        [(673, 17, 4)] = (674, 679), [(673, 17, 5)] = (673, 660),
        [(673, 19, 4)] = (683, 683), [(673, 19, 5)] = (671, 63),
        [(673, 20, 4)] = (683, 683), [(673, 20, 5)] = (671, 63),
        [(674, 57, 4)] = (674, 679), [(674, 57, 5)] = (673, 660),
        [(674, 58, 4)] = (679, 679), [(674, 58, 5)] = (674, 660),
        [(674, 59, 4)] = (674, 679), [(674, 59, 5)] = (676, 676),
        [(674, 60, 4)] = (674, 679), [(674, 60, 5)] = (674, 676),
        [(674, 61, 4)] = (679, 679), [(674, 61, 5)] = (674, 676),
        [(674, 63, 4)] = (674, 679), [(674, 63, 5)] = (680, 680),
        [(674, 64, 4)] = (679, 679), [(674, 64, 5)] = (674, 680),
        [(674, 65, 4)] = (683, 683), [(674, 65, 5)] = (674, 679),
        [(674, 66, 4)] = (674, 683), [(674, 66, 5)] = (679, 679),
        [(675, 74, 4)] = (679, 679), [(675, 74, 5)] = (679, 679), [(675, 74, 6)] = (679, 679), [(675, 74, 7)] = (679, 679), [(675, 74, 8)] = (675, 680), [(675, 74, 9)] = (675, 680), [(675, 74, 10)] = (675, 680),
        [(675, 75, 4)] = (675, 679), [(675, 75, 5)] = (675, 679), [(675, 75, 6)] = (675, 679), [(675, 75, 7)] = (675, 679), [(675, 75, 8)] = (680, 680), [(675, 75, 9)] = (680, 680), [(675, 75, 10)] = (680, 680),
        [(675, 76, 4)] = (680, 680), [(675, 76, 5)] = (680, 680), [(675, 76, 6)] = (680, 680), [(675, 76, 7)] = (680, 680), [(675, 76, 8)] = (670, 62), [(675, 76, 9)] = (670, 62), [(675, 76, 10)] = (670, 62),
    };
}

/// <summary>
/// Bank 1 byte 184 while a Great Glacier passage screen is loaded: read twice between two
/// reads of the field module and id, and null for anything unreadable, changing, or outside
/// fields 670..675.
/// </summary>
public sealed class GreatGlacierCorridorStateReader
{
    public const uint CorridorStateAddress = FieldNavigationObjectReader.AddressFieldBankBase + 184;

    private readonly ILegacyAddressSpace addressSpace;

    public GreatGlacierCorridorStateReader(ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
    }

    public int? Read()
    {
        if (!TryReadOwner(out var moduleBefore, out var fieldBefore) ||
            !addressSpace.TryReadByte(CorridorStateAddress, out var first) ||
            !addressSpace.TryReadByte(CorridorStateAddress, out var second) ||
            !TryReadOwner(out var moduleAfter, out var fieldAfter))
        {
            return null;
        }

        return moduleBefore == FieldPositionReader.FieldModule &&
               GreatGlacierExitLabels.IsPassage(fieldBefore) &&
               moduleAfter == moduleBefore &&
               fieldAfter == fieldBefore &&
               first == second
            ? first
            : null;
    }

    private bool TryReadOwner(out byte module, out ushort fieldId)
    {
        fieldId = 0;
        return addressSpace.TryReadByte(FieldPositionReader.AddressCurrentModule, out module) &&
               addressSpace.TryReadUInt16(FieldPositionReader.AddressFieldId, out fieldId);
    }
}
