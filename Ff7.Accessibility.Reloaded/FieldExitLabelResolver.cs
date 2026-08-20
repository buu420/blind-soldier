namespace Ff7.Accessibility.Reloaded;

public sealed class FieldExitLabelResolver
{
    private readonly Func<int, FieldMapNameResolution> resolveMapNames;
    private readonly Func<string> readCurrentMapName;
    private readonly Func<int, string?> resolveRoomDescriptor;

    public FieldExitLabelResolver(
        Func<int, FieldMapNameResolution> resolveMapNames,
        Func<string> readCurrentMapName,
        Func<int, string?>? resolveRoomDescriptor = null)
    {
        this.resolveMapNames = resolveMapNames;
        this.readCurrentMapName = readCurrentMapName;
        this.resolveRoomDescriptor = resolveRoomDescriptor ?? FieldRoomDescriptorCatalog.Resolve;
    }

    public IReadOnlyList<FieldNavigationTarget> Resolve(
        IReadOnlyList<FieldNavigationTarget> targets)
    {
        if (targets.Count == 0)
        {
            return targets;
        }

        var currentMapName = Normalize(readCurrentMapName());
        return targets
            .Select(target => target with { Label = ResolveLabel(target, currentMapName) })
            .ToArray();
    }

    private string ResolveLabel(FieldNavigationTarget target, string currentMapName)
    {
        var exactLabel = target.StableId switch
        {
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
            _ => null
        };
        if (exactLabel is not null)
        {
            return exactLabel;
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
