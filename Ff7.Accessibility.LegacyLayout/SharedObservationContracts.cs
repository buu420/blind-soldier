using System.Globalization;

namespace Ff7.Accessibility.Reloaded;

public readonly record struct FieldMessageCandidate(string Source, string Text);

public readonly record struct MenuWidgetState(
    string Name,
    int Cursor,
    int Columns,
    int Rows,
    int First = 0,
    int F10 = 0,
    int F14 = 0,
    int F18 = 0,
    uint EnabledMask = 0x7ffu,
    uint DisabledMask = 0,
    InventoryItemSnapshot? InventoryItem = null,
    NativeMenuSelection? NativeSelection = null);

public readonly record struct NativeMenuSelection(string Text, string? Description, string Key);

public readonly record struct MenuCursorDrawObservation(
    string Source,
    int CurrentModule,
    int X,
    int Y,
    int Context);

public readonly record struct MenuTextRenderEntry(string Text, uint X, uint Y, int Color, int Context)
{
    public string ToLogLine() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Menu text render: x={X} y={Y} color=0x{unchecked((uint)Color):X8} context=0x{Context:X8} text={Text}");
}
