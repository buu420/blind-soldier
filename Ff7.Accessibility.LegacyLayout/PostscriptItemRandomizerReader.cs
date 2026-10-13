namespace Ff7.Accessibility.LegacyLayout;

/// <summary>
/// Recognizes Postscriptthree 1.78's field item/materia grant replacement from
/// its installed native code. It does not read the RNG, predict a reward, or
/// change field or player state.
/// </summary>
public sealed class PostscriptItemRandomizerReader(ILegacyAddressSpace memory)
{
    // ItemRando hext patches: the two field grant hooks and their shared body.
    // 0061E66C jumps to 009A73F5; 0061EE0A calls 009A76ED. The third
    // hook rewrites the corresponding field receipt text. These small,
    // discontiguous witnesses distinguish the active patch from stock code.
    private static readonly (uint Address, byte[] Bytes)[] Witnesses =
    [
        (0x0061E66C, Convert.FromHexString("E9848D3800")),
        (0x0061EE0A, Convert.FromHexString("E8DE883800")),
        (0x0063172A, Convert.FromHexString("E9495D37009090")),
        (0x009A73F5, Convert.FromHexString("66813DD015CC004501740380F9550F84")),
        (0x009A76ED, Convert.FromHexString("E8ADFFFFFF803D250EDC0003750FC605250EDC000066FF050A0EDC00C3C605250EDC0002C3"))
    ];

    /// <summary>True only while the exact field reward patch cohort is readable and stable.</summary>
    public bool RandomizesFieldPickups()
    {
        ArgumentNullException.ThrowIfNull(memory);
        return MatchesWitnesses() && MatchesWitnesses();
    }

    private bool MatchesWitnesses()
    {
        Span<byte> actual = stackalloc byte[40];
        foreach (var (address, expected) in Witnesses)
        {
            var span = actual[..expected.Length];
            if (!memory.TryRead(address, span) || !span.SequenceEqual(expected))
                return false;
        }
        return true;
    }
}
