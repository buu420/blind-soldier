using System.Security.Cryptography;
namespace Ff7.Accessibility.LegacyLayout;

/// <summary>
/// Resolves one already observed field dialogue character role through Postscriptthree 1.78's
/// native remapping. It does not enumerate recruitment order or touch the game's RNG.
/// Ghidra 009B4240/009B412F and 00753A01/00753BE8 establish the pure local replay;
/// vectors were independently obtained by emulating those original x86 instructions.
/// </summary>
public sealed class PostscriptCharacterOrderReader
{
    public const uint AddressCharacterSeed = 0x00DC0E08;
    private readonly ILegacyAddressSpace memory;
    private static readonly (uint Address, byte[] Bytes)[] Witnesses =
    [
        (0x00631FCA, Convert.FromHexString("E8712238008945FC90908B4DFC")),
        (0x0061BCF4, Convert.FromHexString("0FB6441101E8428539008945FC8BC890")),
        (0x00753A59, Convert.FromHexString("8B550869D2658B585D83C201895508")),
        (0x00753BE8, Convert.FromHexString("558BECA138A1E20083C001A338A1E200"))
    ];

    public PostscriptCharacterOrderReader(ILegacyAddressSpace memory) =>
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));

    /// <summary>A recognized native name-remapping route; full identity may still be unavailable or changed.</summary>
    public bool IsRemappingRequested
    {
        get
        {
            var (address,expected)=Witnesses[0];
            Span<byte> bytes=stackalloc byte[expected.Length];
            return memory.TryRead(address,bytes) && bytes.SequenceEqual(expected);
        }
    }

    /// <summary>Checked execution mode for one current native field. Null seed denotes stock behavior.</summary>
    internal bool TryReadNavigationMode(out ushort? seed)
    {
        seed = null;
        Span<byte> modelRoute=stackalloc byte[Witnesses[1].Bytes.Length];
        var modelRequested=memory.TryRead(Witnesses[1].Address,modelRoute) && modelRoute.SequenceEqual(Witnesses[1].Bytes);
        if (!IsRemappingRequested && !modelRequested) return true;
        if (!MatchesWitnesses() || !MatchesNavigationWitnesses() ||
            !memory.TryReadUInt16(AddressCharacterSeed,out var before) ||
            !memory.TryReadUInt16(AddressCharacterSeed,out var after) || before!=after ||
            !MatchesWitnesses() || !MatchesNavigationWitnesses()) return false;
        seed = before;
        return true;
    }

    private bool MatchesNavigationWitnesses()
    {
        Span<byte> route=stackalloc byte[14];
        return memory.TryRead(0x0060F91D,route) && route.SequenceEqual(Convert.FromHexString("8A81DC08DC00890D2DF96000EB06")) &&
            memory.TryRead(0x0061182C,route[..5]) && route[..5].SequenceEqual(Convert.FromHexString("E96F2A3A00")) &&
            MatchesHash(0x009B42A0,37,"8F363625216BFBFBB30561A858914CCEFF860C549C770CE6F1D016169D31DE44") &&
            MatchesHash(0x0061C120,52,"413C5962D26C5003199120BC38C17B03B493E3C1DEBCA824D9FFF51003B0B91A") &&
            MatchesHash(0x0061BEB2,25,"172FBE4CC9CB9421EEECF4B8A22985B28EC120208354EA4D54025E15DCF7C798") &&
            MatchesHash(0x0061C066,21,"8C7E7ADA969AE1C81B9D29D759A5A7E0465E3DE67C576678D4F201F7611FBDE1");
    }

    /// <summary>
    /// True only for the exact native remapping and a coherent saved seed. Call only
    /// for a canonical role referenced by currently displayed dialogue. This maps role/name identity, not the visual HRC: CHAR changes party ownership and visibility without rebinding the model. Failure leaves
    /// identity unavailable; it must not be used to infer unobserved future characters.
    /// FMV characters are not remapped by these field opcodes.
    /// </summary>
    public bool TryReadDisplayedCharacter(int canonicalCharacterId, out int displayedCharacterId)
    {
        displayedCharacterId = -1;
        if (canonicalCharacterId is < 0 or > 8 || !MatchesWitnesses() ||
            !memory.TryReadUInt16(AddressCharacterSeed, out var seed)) return false;
        var candidate = ResolveVerifiedRole(seed, canonicalCharacterId);
        if (!memory.TryReadUInt16(AddressCharacterSeed, out var after) || after != seed ||
            !MatchesWitnesses()) return false;
        displayedCharacterId = candidate;
        return true;
    }

    private bool MatchesWitnesses()
    {
        Span<byte> buffer = stackalloc byte[64];
        foreach (var (address, expected) in Witnesses)
        {
            var bytes = buffer[..expected.Length];
            if (!memory.TryRead(address, bytes) || !bytes.SequenceEqual(expected)) return false;
        }
        return MatchesHash(0x009B4240,60,"A47BCEAD9F91EBAAB628B6DBA8412A1E7817AE466820FFD6C32C9E3680A93B3A") &&
            MatchesHash(0x009B412F,33,"331F49FB08EB5A65DFAB4D3DFCED0A02FD14BB9B9E5E8D0D8504B2476CE6CC5B");
    }

    private bool MatchesHash(uint address,int length,string hash)
    {
        Span<byte> bytes=stackalloc byte[length];
        return memory.TryRead(address,bytes) && SHA256.HashData(bytes).AsSpan().SequenceEqual(Convert.FromHexString(hash));
    }

    internal static int ResolveVerifiedRole(ushort seed, int role)
    {
        if (role is < 0 or > 8) throw new ArgumentOutOfRangeException(nameof(role));
        // Native byte RNG seed: 17 initial words, then the 521-word recurrence.
        Span<uint> words = stackalloc uint[521];
        uint state = seed, word = 0;
        for (var i = 0; i < 17; i++)
        {
            for (var bit = 0; bit < 32; bit++)
            {
                state = unchecked(state * 0x5D588B65u + 1);
                word = (word >> 1) | (state & 0x80000000u);
            }
            words[i] = word;
        }
        words[16] = (words[16] << 23) ^ (words[0] >> 9) ^ words[15];
        for (var i = 17; i < words.Length; i++)
            words[i] = (words[i - 17] << 23) ^ (words[i - 16] >> 9) ^ words[i - 1];
        Span<byte> bytes = stackalloc byte[521];
        for (var i = 0; i < bytes.Length; i++) bytes[i] = (byte)words[i];
        // Three seed warmups, and one more on the first read (index starts at 520).
        for (var pass = 0; pass < 4; pass++)
        {
            for (var i = 0; i < 32; i++) bytes[i] ^= bytes[i + 489];
            for (var i = 32; i < bytes.Length; i++) bytes[i] ^= bytes[i - 32];
        }
        var index = 0;
        for (var count = 9; count > 0; count--)
        {
            uint random = 0;
            for (var i = 0; i < 4; i++) random = (random << 8) | bytes[index++];
            var pick = (int)(random % (uint)count);
            if (pick == role) return count - 1;
            if (pick < role) role--;
        }
        throw new InvalidOperationException("Native character permutation did not resolve an ordinary role.");
    }
}
