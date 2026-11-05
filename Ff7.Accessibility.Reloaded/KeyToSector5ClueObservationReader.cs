using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>Observes the displayed Midgar clue independently of dialogue speech or native hooks.</summary>
public sealed class KeyToSector5ClueObservationReader
{
    private readonly ILegacyAddressSpace memory;
    private readonly FieldMessageReader messages;
    private readonly Func<int, int?> resolveReviewedClueMessage;

    public KeyToSector5ClueObservationReader(ILegacyAddressSpace memory, Func<int, int?> resolveReviewedClueMessage)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.resolveReviewedClueMessage = resolveReviewedClueMessage ?? throw new ArgumentNullException(nameof(resolveReviewedClueMessage));
        messages = new FieldMessageReader(memory);
    }

    public bool Observe(KeyToSector5StoryKnowledge knowledge)
    {
        ArgumentNullException.ThrowIfNull(knowledge);
        if (knowledge.HasClue) return false;
        var revision = knowledge.History.PlaythroughRevision;
        if (!TryReadOwner(out var module, out var fieldId) ||
            module != FieldPositionReader.FieldModule || fieldId != 170 ||
            !memory.TryReadByte(FieldMessageReader.AddressFieldWindowStates, out var state) ||
            state == FieldMessageReader.FreeWindowState ||
            resolveReviewedClueMessage(fieldId) is not { } clueId || clueId < 0 ||
            !messages.TryReadMessagePagesById(clueId, out var pages) ||
            !messages.TryReadVisibleWindows(out var windows) ||
            !messages.TryReadMessagePagesById(clueId, out var afterPages) ||
            !TryReadOwner(out var afterModule, out var afterField) ||
            module != afterModule || fieldId != afterField || resolveReviewedClueMessage(fieldId) != clueId)
            return false;

        var expected = pages.Select(page => string.Join("\n", page.Lines.Select(line => line.Text))).ToArray();
        var checkedAgain = afterPages.Select(page => string.Join("\n", page.Lines.Select(line => line.Text)));
        if (!expected.Select(KeyToSector5StoryKnowledge.NormalizeVisibleText).SequenceEqual(
                checkedAgain.Select(KeyToSector5StoryKnowledge.NormalizeVisibleText), StringComparer.Ordinal))
            return false;
        knowledge.ObserveVisibleClue(module, fieldId, windows, true, revision, expected);
        return knowledge.History.PlaythroughRevision == revision && knowledge.HasClue;
    }

    private bool TryReadOwner(out byte module, out ushort fieldId)
    {
        module = 0;
        fieldId = 0;
        return memory.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out module) &&
            memory.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out fieldId);
    }

    public static int? ResolveReviewedClueMessageId(FieldScriptNavigationReadResult field)
    {
        if (!field.IsUsable) return null;
        var candidates = field.Npcs.Where(npc => npc.FieldId == 170 && npc.EntityId == 7 &&
            npc.EntityName == "gman").Take(2).ToArray();
        return candidates.Length == 1 && candidates[0].OwnDialogIds is { Count: 1 } own &&
            own[0] is >= 0 and <= 255 ? own[0] : null;
    }
}
