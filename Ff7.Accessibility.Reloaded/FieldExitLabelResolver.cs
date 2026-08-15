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
