using System.Collections.Immutable;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime.Menus;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Battle;

/// <summary>
/// The encoded text one native battle-results update draws. Ghidra (unpacked copy
/// of the supported executable): the translated update at host 0x1410623D0 calls
/// the EXP page at 0x141059560, whose Materia, Limit and born notices draw through
/// 0x141570730; in the default text mode that wrapper jumps to the translated
/// FUN_006f5b03 at 0x14115D910, which the menu cohort already hooks. The results
/// detour opens the frame around its original on the game thread; the menu detour
/// adds each validated capture it publishes. Draws from any other thread, or
/// outside an update, never join a frame.
/// </summary>
internal sealed class Steam2026BattleResultsTextFrame
{
    // The EXP page draws about two dozen strings; a frame that does not fit is not
    // the page, so none of it is kept.
    private const int Capacity = 96;

    private readonly MenuTextRenderEntry[] entries = new MenuTextRenderEntry[Capacity];
    private int ownerThreadId;
    private int count;
    private bool overflowed;

    internal void Open()
    {
        Volatile.Write(ref ownerThreadId, 0);
        Array.Clear(entries, 0, count);
        count = 0;
        overflowed = false;
        Volatile.Write(ref ownerThreadId, Environment.CurrentManagedThreadId);
    }

    internal void Observe(TranslatedMenuTextObservation text)
    {
        if (Volatile.Read(ref ownerThreadId) != Environment.CurrentManagedThreadId)
        {
            return;
        }

        if (count == entries.Length)
        {
            overflowed = true;
            return;
        }

        entries[count++] = new MenuTextRenderEntry(
            text.Text,
            unchecked((uint)text.X),
            unchecked((uint)text.Y),
            text.Color,
            text.Context);
    }

    internal ImmutableArray<MenuTextRenderEntry> Close()
    {
        if (Volatile.Read(ref ownerThreadId) != Environment.CurrentManagedThreadId)
        {
            return ImmutableArray<MenuTextRenderEntry>.Empty;
        }

        Volatile.Write(ref ownerThreadId, 0);
        var draws = overflowed
            ? ImmutableArray<MenuTextRenderEntry>.Empty
            : ImmutableArray.Create(entries, 0, count);
        Array.Clear(entries, 0, count);
        count = 0;
        overflowed = false;
        return draws;
    }
}
