using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

internal static class CondorBattleInitializationTests
{
    internal static void Run() => RejectsThePreLoadModuleNineSnapshot();

    /// <summary>
    /// Catches the reader treating a zero collision-record count as a complete
    /// battle. Module 9 is observable before its initializer has copied and
    /// loaded all player-visible state; accepting this snapshot is what spoke
    /// zero gil and cursor 0,0 on 2026-08-22.
    /// </summary>
    private static void RejectsThePreLoadModuleNineSnapshot()
    {
        var reader = new CondorBattleStateReader(new ReadableZeroedAddressSpace());

        if (reader.TryRead() is not null)
        {
            throw new InvalidOperationException(
                "A module 9 snapshot without loaded battlefield geometry must not be spoken.");
        }
    }

    /// <summary>
    /// Every address is readable but still in its pre-initialization zero state.
    /// This is the exact distinction the regression protects: a successful
    /// memory read is not necessarily a finished native battle state.
    /// </summary>
    private sealed class ReadableZeroedAddressSpace : ILegacyAddressSpace
    {
        public bool TryRead(uint address, Span<byte> destination)
        {
            destination.Clear();
            return true;
        }
    }
}
