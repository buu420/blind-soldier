namespace Ff7.Accessibility.Reloaded;

public static class FieldRoomDescriptorCatalog
{
    private static readonly IReadOnlyDictionary<int, string> Descriptors =
        new Dictionary<int, string>
        {
            [116] = "train platform",
            [117] = "station forecourt",
            [118] = "industrial yard and reactor approach",
            [119] = "exterior entrance bridge",
            [120] = "entrance and security rooms",
            [121] = "elevator room",
            [122] = "main staircase",
            [123] = "upper piping and ladder room",
            [124] = "lower piping and save room",
            [125] = "core and bomb room",
            [126] = "outside catwalk and bridge approach",
            [127] = "Air Buster bridge",
            [128] = "central control and security rooms",
            [129] = "main staircase",
            [130] = "upper piping and ladder room",
            [131] = "lower piping and save room",
            [132] = "core and bomb-placement room",
            [214] = "entrance",
            [216] = "dressing room",
            [218] = "lobby",
            [219] = "Lover's and Queen's rooms",
            [220] = "Group and &$#% rooms"
        };

    public static string? Resolve(int fieldId) =>
        Descriptors.TryGetValue(fieldId, out var descriptor) ? descriptor : null;
}
