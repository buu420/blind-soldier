using System.Collections.Immutable;

namespace Ff7.Accessibility.Runtime.Abstractions;

public sealed record MenuRowObservation(
    int Index,
    string Text,
    bool Enabled,
    bool Selected);

public sealed record MenuFrameObservation
{
    public MenuFrameObservation(
        string screen,
        bool isOpen,
        int revision,
        IEnumerable<MenuRowObservation> rows)
    {
        Screen = screen;
        IsOpen = isOpen;
        Revision = revision;
        Rows = RuntimeObservationCollections.Copy(rows, nameof(rows));
    }

    public string Screen { get; }

    public bool IsOpen { get; }

    public int Revision { get; }

    public ImmutableArray<MenuRowObservation> Rows { get; }
}
