namespace LocalizedDescriptionCueAligner;

/// <summary>How one English instruction corresponds to one localized instruction.</summary>
internal enum Equivalence : byte
{
    /// <summary>Different opcode, length, or an operand no rule below explains.</summary>
    None,
    /// <summary>Same opcode and every byte.</summary>
    Exact,
    /// <summary>Same except an entity operand, which names the entity the entity map pairs.</summary>
    MappedEntity,
    /// <summary>A branch whose only different bytes are its distance; targets are checked separately.</summary>
    Branch,
    /// <summary>MESSAGE, MPNAM or ASK whose only different bytes are text-table ids or choice lines.</summary>
    Text,
    /// <summary>WINDOW, WSIZW or WMOVE whose only different bytes are on-screen geometry.</summary>
    Window
}

internal static class Instructions
{
    public static bool IsBranch(byte opcode) => opcode is >= 0x10 and <= 0x19 or >= 0x30 and <= 0x32 or 0xCB or 0xCC;

    /// <summary>The bytes that hold a branch distance (FieldScriptProgram.Successors and FalseTarget).</summary>
    public static (int Start, int Length) DistanceOperand(byte opcode) => opcode switch
    {
        0x10 or 0x12 => (1, 1),
        0x11 or 0x13 => (1, 2),
        0x14 => (5, 1),
        0x15 => (5, 2),
        0x16 or 0x18 => (7, 1),
        0x17 or 0x19 => (7, 2),
        >= 0x30 and <= 0x32 => (3, 1),
        0xCB or 0xCC => (2, 1),
        _ => (0, 0)
    };

    /// <summary>The slot-relative byte a branch transfers to, measured as its handler measures it.</summary>
    public static int? BranchTarget(NativeInstruction instruction)
    {
        var b = instruction.Bytes;
        var at = instruction.ByteIndex;
        return instruction.Opcode switch
        {
            0x10 => at + 1 + b[1],
            0x11 => at + 1 + BitConverter.ToUInt16(b, 1),
            0x12 => at - b[1],
            0x13 => at - BitConverter.ToUInt16(b, 1),
            0x14 => at + 5 + b[5],
            0x15 => at + 5 + BitConverter.ToUInt16(b, 5),
            0x16 or 0x18 => at + 7 + b[7],
            0x17 or 0x19 => at + 7 + BitConverter.ToUInt16(b, 7),
            >= 0x30 and <= 0x32 => at + 3 + b[3],
            0xCB or 0xCC => at + 2 + b[2],
            _ => null
        };
    }

    /// <summary>
    /// The operand that names another entity of the same field: REQ, REQSW and REQEW (byte 1),
    /// MOVA, TURA, DIRA and CC (byte 1), GETDIR, GETAXY and GETAI (byte 2, after the bank byte).
    /// </summary>
    public static int EntityOperand(byte opcode) => opcode switch
    {
        0x01 or 0x02 or 0x03 or 0xAA or 0xAB or 0xB6 or 0xBF => 1,
        0xB7 or 0xB8 or 0xB9 => 2,
        _ => -1
    };

    // MESSAGE [40 window dialog], MPNAM [43 dialog], ASK [48 banks window dialog first last var]:
    // the text-table id and an ASK's first and last choice lines are per-language text layout.
    private static bool IsTextOperand(byte opcode, int index) => opcode switch
    {
        0x40 => index == 2,
        0x43 => index == 1,
        0x48 => index is 3 or 4 or 5,
        _ => false
    };

    // WINDOW and WSIZW [op id x x y y w w h h], WMOVE [51 id x x y y]: the window id is kept.
    private static bool IsWindowOperand(byte opcode, int index) => opcode switch
    {
        0x50 or 0x2F or 0x51 => index >= 2,
        _ => false
    };

    public static Equivalence Compare(NativeInstruction english, NativeInstruction localized, IReadOnlyDictionary<int, int> entityMap)
    {
        if (english.Opcode != localized.Opcode || english.Bytes.Length != localized.Bytes.Length) return Equivalence.None;
        var entityOperand = EntityOperand(english.Opcode);
        var (distanceStart, distanceLength) = IsBranch(english.Opcode) ? DistanceOperand(english.Opcode) : (0, 0);
        bool mapped = false, branch = false, text = false, window = false;
        for (var index = 1; index < english.Bytes.Length; index++)
        {
            var left = english.Bytes[index];
            var right = localized.Bytes[index];
            if (index == entityOperand)
            {
                // An entity outside the map (a REQ to a missing entity is a native no-op) keeps its raw value.
                var expected = entityMap.TryGetValue(left, out var paired) ? paired : left;
                if (right != expected) return Equivalence.None;
                mapped |= expected != left;
                continue;
            }

            if (left == right) continue;
            if (index >= distanceStart && index < distanceStart + distanceLength) { branch = true; continue; }
            if (IsTextOperand(english.Opcode, index)) { text = true; continue; }
            if (IsWindowOperand(english.Opcode, index)) { window = true; continue; }
            return Equivalence.None;
        }

        return text ? Equivalence.Text : window ? Equivalence.Window : branch ? Equivalence.Branch :
            mapped ? Equivalence.MappedEntity : Equivalence.Exact;
    }
}

/// <summary>
/// A longest common subsequence over two instruction lists in which a pair may match only when
/// <see cref="Instructions.Compare"/> is not <see cref="Equivalence.None"/>, and the test whether an
/// English instruction is matched, to one and the same localized instruction, in every maximum
/// alignment. An instruction that some maximum alignment leaves unmatched or pairs differently is
/// never forced: a repeated or moved instruction cannot be guessed into place.
/// </summary>
internal sealed class ForcedAlignment
{
    private readonly int n;
    private readonly int m;
    private readonly ushort[] prefix;
    private readonly ushort[] suffix;
    private readonly Equivalence[] equivalence;

    public ForcedAlignment(IReadOnlyList<NativeInstruction> english, IReadOnlyList<NativeInstruction> localized,
        IReadOnlyDictionary<int, int> entityMap)
        : this(english.Count, localized.Count, (i, j) => Instructions.Compare(english[i], localized[j], entityMap))
    {
    }

    public ForcedAlignment(int n, int m, Func<int, int, Equivalence> compare)
    {
        this.n = n;
        this.m = m;
        equivalence = new Equivalence[n * m];
        for (var i = 0; i < n; i++)
            for (var j = 0; j < m; j++)
                equivalence[i * m + j] = compare(i, j);
        prefix = new ushort[(n + 1) * (m + 1)];
        suffix = new ushort[(n + 1) * (m + 1)];
        for (var i = 1; i <= n; i++)
            for (var j = 1; j <= m; j++)
                prefix[i * (m + 1) + j] = equivalence[(i - 1) * m + j - 1] != Equivalence.None
                    ? (ushort)(prefix[(i - 1) * (m + 1) + j - 1] + 1)
                    : Math.Max(prefix[(i - 1) * (m + 1) + j], prefix[i * (m + 1) + j - 1]);
        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                suffix[i * (m + 1) + j] = equivalence[i * m + j] != Equivalence.None
                    ? (ushort)(suffix[(i + 1) * (m + 1) + j + 1] + 1)
                    : Math.Max(suffix[(i + 1) * (m + 1) + j], suffix[i * (m + 1) + j + 1]);
        Total = suffix[0];
    }

    public int Total { get; }

    public Equivalence At(int i, int j) => equivalence[i * m + j];

    /// <summary>The single localized index English <paramref name="i"/> takes in every maximum alignment, or null.</summary>
    public int? Forced(int i)
    {
        int? only = null;
        for (var j = 0; j < m; j++)
        {
            if (equivalence[i * m + j] == Equivalence.None) continue;
            if (prefix[i * (m + 1) + j] + 1 + suffix[(i + 1) * (m + 1) + j + 1] != Total) continue;
            if (only is not null) return null;
            only = j;
        }

        if (only is null) return null;
        // A maximum alignment that skips English i: some split point j with prefix(i, j) + suffix(i + 1, j).
        for (var j = 0; j <= m; j++)
            if (prefix[i * (m + 1) + j] + suffix[(i + 1) * (m + 1) + j] == Total) return null;
        return only;
    }

    /// <summary>One maximum alignment, walked forward preferring a pair, for evidence reports only.</summary>
    public IEnumerable<(int? English, int? Localized)> CanonicalPairs()
    {
        int i = 0, j = 0;
        while (i < n && j < m)
        {
            if (equivalence[i * m + j] != Equivalence.None && suffix[i * (m + 1) + j] == suffix[(i + 1) * (m + 1) + j + 1] + 1)
                yield return (i++, j++);
            else if (suffix[(i + 1) * (m + 1) + j] >= suffix[i * (m + 1) + j + 1])
                yield return (i++, null);
            else
                yield return (null, j++);
        }

        while (i < n) yield return (i++, null);
        while (j < m) yield return (null, j++);
    }
}
