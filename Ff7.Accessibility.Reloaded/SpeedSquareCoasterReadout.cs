namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Turns the coaster's own visible aim and charge into speech. A sighted player
/// reads the sight and the charge bar off the screen continuously; this gives the
/// same two facts and nothing else. It never reports target positions, hit
/// outcomes or the score multiplier, and it never presses a button.
///
/// The sight is quantised into a coarse grid so holding a direction produces a
/// readable series of steps rather than a stream of pixel values. The native
/// screen is 320 by 240 with the sight starting dead centre, so eight columns and
/// eight rows put one step at 40 by 30 native pixels.
/// </summary>
public sealed class SpeedSquareCoasterReadout
{
    public const int HorizontalStep = SpeedSquareCoasterState.ScreenWidth / 8;
    public const int VerticalStep = SpeedSquareCoasterState.ScreenHeight / 8;
    public const int ChargeSteps = 8;

    private bool wasActive;
    private int lastColumn = int.MinValue;
    private int lastRow = int.MinValue;
    private int lastCharge = int.MinValue;

    public string? Observe(SpeedSquareCoasterState state)
    {
        if (!state.IsActive)
        {
            Reset();
            return null;
        }

        var entering = !wasActive;
        wasActive = true;

        // The native routine ignores every input while this byte is set, so the
        // sight cannot be moving. Announcing during it would describe a frozen
        // screen and talk over the result window.
        if (state.IsSuspended)
        {
            return entering ? "Shooting Coaster." : null;
        }

        var column = Quantise(state.CursorX - SpeedSquareCoasterState.ScreenWidth / 2, HorizontalStep);
        var row = Quantise(state.CursorY - SpeedSquareCoasterState.ScreenHeight / 2, VerticalStep);
        var charge = state.ShotPower * ChargeSteps / SpeedSquareCoasterState.MaximumShotPower;

        if (!entering && column == lastColumn && row == lastRow && charge == lastCharge)
        {
            return null;
        }

        var aimChanged = entering || column != lastColumn || row != lastRow;
        var chargeChanged = entering || charge != lastCharge;
        lastColumn = column;
        lastRow = row;
        lastCharge = charge;

        var parts = new List<string>(3);
        if (entering)
        {
            parts.Add("Shooting Coaster.");
        }

        if (aimChanged)
        {
            parts.Add(DescribeAim(column, row));
        }

        if (chargeChanged)
        {
            parts.Add($"charge {charge} of {ChargeSteps}");
        }

        return string.Join(" ", parts);
    }

    public void Reset()
    {
        wasActive = false;
        lastColumn = int.MinValue;
        lastRow = int.MinValue;
        lastCharge = int.MinValue;
    }

    private static string DescribeAim(int column, int row)
    {
        if (column == 0 && row == 0)
        {
            return "sight centred.";
        }

        var horizontal = column == 0
            ? string.Empty
            : $"{(column < 0 ? "left" : "right")} {Math.Abs(column)}";
        var vertical = row == 0
            ? string.Empty
            : $"{(row < 0 ? "up" : "down")} {Math.Abs(row)}";
        return horizontal.Length == 0
            ? $"sight {vertical}."
            : vertical.Length == 0
                ? $"sight {horizontal}."
                : $"sight {horizontal}, {vertical}.";
    }

    /// <summary>
    /// Rounds away from centre so the first step off centre is announced as soon as
    /// the sight actually leaves the middle cell, not half a cell later.
    /// </summary>
    private static int Quantise(int offset, int step) =>
        offset >= 0 ? offset / step : -((-offset) / step);
}
