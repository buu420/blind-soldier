using System.Collections.Immutable;

namespace Ff7.Accessibility.Runtime.Abstractions;

public sealed record DialogueChoiceObservation(
    int Index,
    string Text,
    bool Enabled,
    bool Selected);

public sealed record DialoguePageObservation
{
    public DialoguePageObservation(
        bool isOpen,
        int windowId,
        int pageRevision,
        string speaker,
        string visibleText,
        IEnumerable<DialogueChoiceObservation> choices)
    {
        IsOpen = isOpen;
        WindowId = windowId;
        PageRevision = pageRevision;
        Speaker = speaker;
        VisibleText = visibleText;
        Choices = RuntimeObservationCollections.Copy(choices, nameof(choices));
    }

    public bool IsOpen { get; }

    public int WindowId { get; }

    public int PageRevision { get; }

    public string Speaker { get; }

    public string VisibleText { get; }

    public ImmutableArray<DialogueChoiceObservation> Choices { get; }
}
