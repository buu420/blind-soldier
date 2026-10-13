namespace Ff7.Accessibility.LegacyLayout;

/// <summary>
/// Recognizes Postscriptthree 1.78's MateriaDrops native hooks. The patch
/// encodes a displayed materia reward as 0x140 plus the materia index; this
/// reader identifies that layout without touching inventory or game state.
/// </summary>
public sealed class PostscriptMateriaDropCompatibility
{
    public const int FirstMateriaRewardId = 0x140;
    public const int MateriaNameCount = 96;
    public const int LastMateriaRewardId = FirstMateriaRewardId + MateriaNameCount - 1;

    private static readonly (uint Address, byte[] Bytes)[] Witnesses =
    [
        (0x005CA975, Convert.FromHexString("E9D6B9340090")),
        (0x00419B73, Convert.FromHexString("E903C84F00")),
        (0x005CAD0C, Convert.FromHexString("E997B63400")),
        (0x006C716D, Convert.FromHexString("0FBF45E06BC006E857F22400669051")),
        (0x006C71C2, Convert.FromHexString("E809F224006690")),
        (0x005C8551, Convert.FromHexString("E898DE34006690")),
        (0x00916350, Convert.FromHexString("81E2FFFF000081FA400100000F8C1946CBFF81EA40010000")),
        (0x0091637B, Convert.FromHexString("8B45EC25FFFF00003D400100007D076A08E9E737B0FF2D400100006817D9CE3D6A0750E86307DBFFE9DD")),
        (0x009163EE, Convert.FromHexString("0FBF88F2E2990081FA400100007D01C36681EA40015152E8E05CDBFF5A594985C975F258E96422CBFF"))
    ];

    private readonly ILegacyAddressSpace memory;

    public PostscriptMateriaDropCompatibility(ILegacyAddressSpace memory) =>
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));

    public bool HasMateriaDropAliases() => MatchesWitnesses() && MatchesWitnesses();

    public static bool IsMateriaRewardId(int id) => id is >= FirstMateriaRewardId and <= LastMateriaRewardId;

    private bool MatchesWitnesses()
    {
        Span<byte> actual = stackalloc byte[42];
        foreach (var (address, expected) in Witnesses)
        {
            var span = actual[..expected.Length];
            if (!memory.TryRead(address, span) || !span.SequenceEqual(expected))
                return false;
        }
        return true;
    }
}
