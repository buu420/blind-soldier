namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// The current area's description, on demand.
///
/// The automatic delivery happens once when the player arrives, and after that it is
/// gone: an ordinary button press interrupts the screen reader, and the next
/// navigation line replaces whatever generic repeat-last buffer held it. A player who
/// misses the description of a room has no way back to it.
///
/// So the same text is also reachable from the field's existing status command - the
/// one already bound to the navigation "repeat current target" action in both
/// runtimes. It is appended to that status rather than replacing it, and only for the
/// fields that actually have a description, so the command is unchanged everywhere
/// else.
/// </summary>
public static class FieldAreaDescriptionStatus
{
    private static readonly IReadOnlyDictionary<int, string> Descriptions =
        FieldCutsceneDescriptionCatalog.CreateGoldSaucerAreaDescriptions()
            .Where(cue => cue.Opcode == FieldOpcodeAddressResolver.OpcodeMapNameIndex)
            .GroupBy(cue => cue.FieldId)
            .ToDictionary(group => group.Key, group => group.First().Text);

    /// <summary>The area description for a field, or null when it has none.</summary>
    public static string? Describe(int fieldId) =>
        Descriptions.TryGetValue(fieldId, out var text) ? text : null;

    /// <summary>
    /// Appends the current area's description to a status line. Returns the status
    /// unchanged where the field has none, and the description alone where the
    /// status is empty.
    /// </summary>
    public static string Append(string? status, int fieldId)
    {
        var description = Describe(fieldId);
        if (description is null)
        {
            return status ?? string.Empty;
        }

        return string.IsNullOrWhiteSpace(status) ? description : $"{status} {description}";
    }
}
