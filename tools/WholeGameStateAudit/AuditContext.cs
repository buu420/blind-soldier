using System.Text.Json.Nodes;
using Ff7.Accessibility.Reloaded;

namespace WholeGameStateAudit;

/// <summary>Everything one archive's run shares across fields.</summary>
internal sealed class AuditContext
{
    public AuditContext(string label, string root, bool includeInstructions, bool includeText)
    {
        Label = label;
        Root = root;
        IncludeInstructions = includeInstructions;
        IncludeText = includeText;
        Source = new FlevelDataSource(root);
        Catalog = new FieldScriptNavigationCatalog(root);
        Text = new FlevelFieldTextResolver(root);
        Objects = FieldNavigationObjectCatalog.CreateAllFields().ToLookup(row => row.FieldId);
        Story = FieldStoryEventCatalog.CreateAllFields().ToLookup(row => row.FieldId);
    }

    /// <summary>
    /// A context with no game archive, for synthetic fields: the shipping catalog's
    /// archive-backed reads (NPC list, script exits, text) are absent, while its private
    /// decoder and walker still run on the synthetic bytes.
    /// </summary>
    public AuditContext(
        string label,
        IEnumerable<FieldNavigationObjectDefinition>? objects = null,
        IEnumerable<FieldStoryEventDefinition>? story = null)
    {
        Label = label;
        Root = "<synthetic>";
        IncludeInstructions = true;
        IncludeText = false;
        Objects = (objects ?? []).ToLookup(row => row.FieldId);
        Story = (story ?? []).ToLookup(row => row.FieldId);
    }

    public string Label { get; }

    public string Root { get; }

    public bool IncludeInstructions { get; }

    public bool IncludeText { get; }

    public FlevelDataSource? Source { get; }

    public FieldScriptNavigationCatalog? Catalog { get; }

    public FlevelFieldTextResolver? Text { get; }

    public ILookup<int, FieldNavigationObjectDefinition> Objects { get; }

    public ILookup<int, FieldStoryEventDefinition> Story { get; }

    public GlobalAudit Global { get; } = new();
}

/// <summary>One candidate deficiency with the evidence needed to reproduce it.</summary>
internal sealed record Candidate(
    string Class,
    int Priority,
    int FieldId,
    string FieldName,
    string Summary,
    JsonObject Witness);

/// <summary>A byte of a savemap or temporary block, the unit writers and readers are matched on.</summary>
internal readonly record struct ByteKey(string Block, int Address)
{
    public bool IsGameMoment => Block == "1" && Address is 0 or 1;

    public override string ToString() => $"{Block}:{Address}";

    public static IEnumerable<ByteKey> Of(VariableRef variable)
    {
        for (var offset = 0; offset < Math.Max(1, variable.Width); offset++)
        {
            yield return new ByteKey(variable.Block, variable.Address + offset);
        }
    }

    /// <summary>The mod's bank numbering (1, 3, 5, 11, 13, 15) as a block.</summary>
    public static ByteKey? FromModBank(int bank, int address) =>
        address < 0 || Opcodes.BankBlock(bank) is not { } block ? null : new ByteKey(block, address);
}
