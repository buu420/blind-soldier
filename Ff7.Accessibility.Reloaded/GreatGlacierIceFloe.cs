using System.Text;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public enum GreatGlacierIceFloeTargetKind
{
    None,
    Floe,
    NorthShore,
    SouthShore
}

public readonly record struct GreatGlacierIceFloeTarget(GreatGlacierIceFloeTargetKind Kind, int Floe);

/// <summary>
/// The rules of the Great Glacier ice-floe crossing, hyou5_2 (field 665), as its installed
/// script runs them. GreatGlacierIceFloeTests checks every one against the field.
///
/// <para>The floes are a five-by-five grid numbered 1..25 from the north-west corner: row 1
/// lies by the north shore (+Y), column 1 at the west (-X) end. Cloud's script 3 reads his
/// facing with GETDIR, then 128 is one row north, 64 one column east and 0 one row south.
/// Every other facing byte, diagonals included, takes the branch one column west. A jump
/// lands only on a floe whose state is 0. Floe 3 facing 128 always reaches the north shore
/// and floe 23 facing 0 always reaches the south shore.</para>
///
/// <para>Landing on floe k runs ice script k, which turns k's orthogonal neighbours between 0
/// and 1 and leaves k itself alone. After each OK press, unless Cloud stands on floe 3 or
/// 23, the script checks whether any neighbour of his floe can still be landed on. When
/// none can he falls in, BGanime puts the floes back to their starting pattern, and he is
/// returned to the shore he started from.</para>
/// </summary>
public static class GreatGlacierIceFloePuzzle
{
    public const int FieldId = 665;
    public const int Rows = 5;
    public const int Columns = 5;
    public const int FloeCount = Rows * Columns;

    /// <summary>The facing bytes Cloud's script tests. Anything else is the west branch.</summary>
    public const byte NorthFacing = 128;
    public const byte EastFacing = 64;
    public const byte SouthFacing = 0;
    public const byte WestFacing = 192;

    public const int NorthShoreFloe = 3;
    public const int SouthShoreFloe = 23;

    public static int Row(int floe) => (floe - 1) / Columns + 1;

    public static int Column(int floe) => (floe - 1) % Columns + 1;

    public static bool IsFloe(int floe) => floe is >= 1 and <= FloeCount;

    public static GreatGlacierIceFloeTarget ResolveJump(int floe, byte facing)
    {
        if (!IsFloe(floe))
        {
            return default;
        }

        var row = Row(floe);
        var column = Column(floe);
        return facing switch
        {
            NorthFacing when floe == NorthShoreFloe => new(GreatGlacierIceFloeTargetKind.NorthShore, 0),
            NorthFacing => row > 1 ? Floe(floe - Columns) : default,
            EastFacing => column < Columns ? Floe(floe + 1) : default,
            SouthFacing when floe == SouthShoreFloe => new(GreatGlacierIceFloeTargetKind.SouthShore, 0),
            SouthFacing => row < Rows ? Floe(floe + Columns) : default,
            _ => column > 1 ? Floe(floe - 1) : default
        };
    }

    /// <summary>The floes ice script <paramref name="floe"/> turns, in ascending order.</summary>
    public static IReadOnlyList<int> Neighbours(int floe)
    {
        if (!IsFloe(floe))
        {
            return [];
        }

        var neighbours = new List<int>(4);
        if (Row(floe) > 1) neighbours.Add(floe - Columns);
        if (Column(floe) > 1) neighbours.Add(floe - 1);
        if (Column(floe) < Columns) neighbours.Add(floe + 1);
        if (Row(floe) < Rows) neighbours.Add(floe + Columns);
        return neighbours;
    }

    private static GreatGlacierIceFloeTarget Floe(int floe) => new(GreatGlacierIceFloeTargetKind.Floe, floe);
}

/// <summary>
/// One coherent read of hyou5_2's temporary bank.
/// </summary>
/// <param name="CurrentFloe">5[2]: the floe Cloud stands on. 0 and 26 are the ways off the ice.</param>
/// <param name="LandingFloe">5[3]: set when a jump lands, a frame wait before 5[2] follows it.</param>
/// <param name="EnteredFromNorth">5[5]: line50b sets 1, line50a 0. A fall returns Cloud to this shore.</param>
/// <param name="FloeStates">5[7..31]: floe k's state is element k - 1, and 0 is a floe that can be landed on.</param>
public sealed record GreatGlacierIceFloeState(
    int CurrentFloe,
    int LandingFloe,
    bool EnteredFromNorth,
    IReadOnlyList<byte> FloeStates)
{
    /// <summary>Standing on a floe, not in the air between two.</summary>
    public bool IsOnFloe => GreatGlacierIceFloePuzzle.IsFloe(CurrentFloe) && CurrentFloe == LandingFloe;

    /// <summary>Keep navigation suspended during the jump's one-frame position handoff too.</summary>
    public bool IsCrossing => GreatGlacierIceFloePuzzle.IsFloe(CurrentFloe) || GreatGlacierIceFloePuzzle.IsFloe(LandingFloe);

    public bool CanLand(int floe) =>
        GreatGlacierIceFloePuzzle.IsFloe(floe) && FloeStates[floe - 1] == 0;
}

/// <summary>
/// Reads the ice floes the way WutaiBellDoorStateReader reads its door: the temporary bank
/// belongs to whichever field is loaded, so it is read twice between two reads of the field
/// module and id. Anything unreadable, changing or outside field 665 gives no state.
/// </summary>
public sealed class GreatGlacierIceFloeReader
{
    private const int TemporaryBytes = 32;
    private const int CurrentFloeIndex = 2;
    private const int LandingFloeIndex = 3;
    private const int EntrySideIndex = 5;
    private const int FirstFloeIndex = 7;

    private readonly ILegacyAddressSpace addressSpace;

    public GreatGlacierIceFloeReader(ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
    }

    public GreatGlacierIceFloeState? Read()
    {
        Span<byte> first = stackalloc byte[TemporaryBytes];
        Span<byte> second = stackalloc byte[TemporaryBytes];
        if (!TryReadOwner(out var moduleBefore, out var fieldBefore) ||
            moduleBefore != FieldPositionReader.FieldModule ||
            fieldBefore != GreatGlacierIceFloePuzzle.FieldId ||
            !addressSpace.TryRead(FieldNavigationObjectReader.AddressTemporaryFieldBankBase, first) ||
            !addressSpace.TryRead(FieldNavigationObjectReader.AddressTemporaryFieldBankBase, second) ||
            !TryReadOwner(out var moduleAfter, out var fieldAfter))
        {
            return null;
        }

        if (moduleBefore != FieldPositionReader.FieldModule ||
            fieldBefore != GreatGlacierIceFloePuzzle.FieldId ||
            moduleAfter != moduleBefore ||
            fieldAfter != fieldBefore ||
            !first.SequenceEqual(second) ||
            first[CurrentFloeIndex] > 26 || first[LandingFloeIndex] > 26 ||
            first[EntrySideIndex] > 1)
        {
            return null;
        }

        for (var index = FirstFloeIndex; index < TemporaryBytes; index++)
        {
            if (first[index] > 1) return null;
        }

        return new GreatGlacierIceFloeState(
            first[CurrentFloeIndex],
            first[LandingFloeIndex],
            first[EntrySideIndex] != 0,
            first.Slice(FirstFloeIndex, GreatGlacierIceFloePuzzle.FloeCount).ToArray());
    }

    private bool TryReadOwner(out byte module, out ushort fieldId)
    {
        fieldId = 0;
        return addressSpace.TryReadByte(FieldPositionReader.AddressCurrentModule, out module) &&
               addressSpace.TryReadUInt16(FieldPositionReader.AddressFieldId, out fieldId);
    }
}

/// <summary>
/// What a sighted player can see of the crossing, as speech: where Cloud stands, which of
/// the floes around him can be landed on, which way he faces, and the whole grid on request.
/// Each floe is drawn in one of two BG states (ice_pos: state 0 while its
/// flag is 0, state 7 otherwise), and "open" and "blocked" name them by what the landing
/// test does, not by how the art looks. Nothing is predicted and no route is suggested;
/// the player solves the puzzle.
///
/// <para>Directions are the pad's. Engine 006342c6 turns Up, Left, Down and Right into 0,
/// 0x40, 0x80 and 0xC0 and adds the field's signed control byte, so the facing a pad
/// direction produces is known from the control byte alone. For hyou5_2 (control -128)
/// Up is facing 128, the way north.</para>
/// </summary>
public sealed class GreatGlacierIceFloeReadout
{
    private static readonly string[] PadNames =
        ["up", "up-left", "left", "down-left", "down", "down-right", "right", "up-right"];

    private const byte PadUp = 0x00;
    private const byte PadLeft = 0x40;
    private const byte PadDown = 0x80;
    private const byte PadRight = 0xC0;

    private int standingOn;
    private bool enteredFromNorth;
    private byte facing;

    /// <summary>
    /// Call every frame with the loaded field. Returns what changed, or null.
    /// </summary>
    public string? Observe(int fieldId, GreatGlacierIceFloeState? state, byte playerFacing, int signedControlDirection)
    {
        if (fieldId != GreatGlacierIceFloePuzzle.FieldId)
        {
            Reset();
            return null;
        }

        if (state is null)
        {
            return null;
        }

        if (state.IsOnFloe)
        {
            var previous = standingOn;
            var previousFacing = facing;
            standingOn = state.CurrentFloe;
            enteredFromNorth = state.EnteredFromNorth;
            facing = playerFacing;
            if (previous == 0)
            {
                return DescribeStart(state, playerFacing, signedControlDirection);
            }

            if (previous != state.CurrentFloe)
            {
                return DescribeSurroundings(state, signedControlDirection) + ".";
            }

            return previousFacing == playerFacing
                ? null
                : DescribeFacing(state, playerFacing, signedControlDirection);
        }

        if (standingOn == 0 || state.CurrentFloe != state.LandingFloe)
        {
            return null;
        }

        var left = standingOn;
        standingOn = 0;
        if (left == GreatGlacierIceFloePuzzle.NorthShoreFloe && state.CurrentFloe == 0)
        {
            return "Reached the north shore.";
        }

        if (left == GreatGlacierIceFloePuzzle.SouthShoreFloe && state.CurrentFloe == 26)
        {
            return "Reached the south shore.";
        }

        return $"Cloud fell into the water. Back on the {(enteredFromNorth ? "north" : "south")} shore; " +
               "the floes are back as they started.";
    }

    public void Reset()
    {
        standingOn = 0;
        enteredFromNorth = false;
        facing = 0;
    }

    /// <summary>
    /// The whole grid, row 1 by the north shore first, and where Cloud is if he is on it.
    /// </summary>
    public static string DescribeGrid(GreatGlacierIceFloeState state)
    {
        var text = new StringBuilder(
            "Ice floes, from the row by the north shore to the row by the south shore, left to right; open floes can be landed on.");
        for (var row = 1; row <= GreatGlacierIceFloePuzzle.Rows; row++)
        {
            text.Append(" Row ").Append(row).Append(": ");
            for (var column = 1; column <= GreatGlacierIceFloePuzzle.Columns; column++)
            {
                if (column > 1) text.Append(", ");
                var floe = (row - 1) * GreatGlacierIceFloePuzzle.Columns + column;
                text.Append(state.CanLand(floe) ? "open" : "blocked");
            }

            text.Append('.');
        }

        if (state.IsOnFloe)
        {
            text.Append(" You are on row ").Append(GreatGlacierIceFloePuzzle.Row(state.CurrentFloe))
                .Append(", column ").Append(GreatGlacierIceFloePuzzle.Column(state.CurrentFloe)).Append('.');
        }

        return text.ToString();
    }

    /// <summary>"Row r, column c: up ..., down ..., left ..., right ..." without a full stop.</summary>
    public static string DescribeSurroundings(GreatGlacierIceFloeState state, int signedControlDirection)
    {
        var floe = state.CurrentFloe;
        var text = new StringBuilder()
            .Append("Row ").Append(GreatGlacierIceFloePuzzle.Row(floe))
            .Append(", column ").Append(GreatGlacierIceFloePuzzle.Column(floe)).Append(": ");
        var first = true;
        foreach (var pad in new[] { PadUp, PadDown, PadLeft, PadRight })
        {
            if (!first) text.Append(", ");
            first = false;
            var target = GreatGlacierIceFloePuzzle.ResolveJump(floe, FacingFor(pad, signedControlDirection));
            text.Append(PadName(pad)).Append(' ').Append(DescribeTarget(state, target));
        }

        return text.ToString();
    }

    private static string DescribeStart(GreatGlacierIceFloeState state, byte playerFacing, int signedControlDirection) =>
        $"On the ice floes. {DescribeSurroundings(state, signedControlDirection)}. " +
        $"Facing {PadName(PadFor(playerFacing, signedControlDirection))}. Press R for the full grid.";

    private static string DescribeFacing(GreatGlacierIceFloeState state, byte playerFacing, int signedControlDirection)
    {
        var name = PadName(PadFor(playerFacing, signedControlDirection));
        var target = GreatGlacierIceFloePuzzle.ResolveJump(state.CurrentFloe, playerFacing);
        if (playerFacing is GreatGlacierIceFloePuzzle.NorthFacing or GreatGlacierIceFloePuzzle.EastFacing or
            GreatGlacierIceFloePuzzle.SouthFacing or GreatGlacierIceFloePuzzle.WestFacing)
        {
            return $"Facing {name}, {DescribeTarget(state, target)}.";
        }

        // Cloud's script falls through to its west branch for any facing it does not test.
        var west = PadName(PadFor(GreatGlacierIceFloePuzzle.WestFacing, signedControlDirection));
        return target.Kind == GreatGlacierIceFloeTargetKind.None
            ? $"Facing {name}, not straight; OK does nothing."
            : $"Facing {name}, not straight; OK jumps {west}, {DescribeTarget(state, target)}.";
    }

    private static string DescribeTarget(GreatGlacierIceFloeState state, GreatGlacierIceFloeTarget target) =>
        target.Kind switch
        {
            GreatGlacierIceFloeTargetKind.Floe => state.CanLand(target.Floe) ? "open" : "blocked",
            GreatGlacierIceFloeTargetKind.NorthShore => "north shore",
            GreatGlacierIceFloeTargetKind.SouthShore => "south shore",
            _ => "no floe"
        };

    /// <summary>The facing byte a pad direction gives on a field with this control byte.</summary>
    private static byte FacingFor(byte pad, int signedControlDirection) =>
        unchecked((byte)(pad + signedControlDirection));

    private static byte PadFor(byte facing, int signedControlDirection) =>
        unchecked((byte)(facing - signedControlDirection));

    private static string PadName(byte pad) => PadNames[((pad + 16) & 0xFF) / 32];
}
