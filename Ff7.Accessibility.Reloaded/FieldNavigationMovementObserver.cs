namespace Ff7.Accessibility.Reloaded;

public enum FieldNavigationInput
{
    None,
    Up,
    UpRight,
    Right,
    DownRight,
    Down,
    DownLeft,
    Left,
    UpLeft,
    Conflicting
}

public readonly record struct FieldNavigationInputSnapshot(
    uint RawStatus,
    FieldNavigationInput Direction)
{
    public bool IsDirectionalRun =>
        (RawStatus & FieldNavigationInputReader.RunMask) != 0 &&
        Direction is >= FieldNavigationInput.Up and <= FieldNavigationInput.UpLeft;
}

public sealed class FieldNavigationInputReader
{
    public const int AddressCurrentKeyInput = 0x00CC0DF0;
    public const uint UpMask = 0x00001000;
    public const uint RightMask = 0x00002000;
    public const uint DownMask = 0x00004000;
    public const uint LeftMask = 0x00008000;
    public const uint RunMask = 0x00000040;

    private readonly Func<int, uint> readUInt32;

    public FieldNavigationInputReader(Func<int, uint> readUInt32)
    {
        this.readUInt32 = readUInt32;
    }

    public FieldNavigationInputSnapshot Read()
    {
        var raw = readUInt32(AddressCurrentKeyInput);
        var up = (raw & UpMask) != 0;
        var right = (raw & RightMask) != 0;
        var down = (raw & DownMask) != 0;
        var left = (raw & LeftMask) != 0;
        if ((up && down) || (left && right))
        {
            return new FieldNavigationInputSnapshot(raw, FieldNavigationInput.Conflicting);
        }

        var direction = (up, right, down, left) switch
        {
            (true, false, false, false) => FieldNavigationInput.Up,
            (true, true, false, false) => FieldNavigationInput.UpRight,
            (false, true, false, false) => FieldNavigationInput.Right,
            (false, true, true, false) => FieldNavigationInput.DownRight,
            (false, false, true, false) => FieldNavigationInput.Down,
            (false, false, true, true) => FieldNavigationInput.DownLeft,
            (false, false, false, true) => FieldNavigationInput.Left,
            (true, false, false, true) => FieldNavigationInput.UpLeft,
            (false, false, false, false) => FieldNavigationInput.None,
            _ => FieldNavigationInput.Conflicting
        };
        return new FieldNavigationInputSnapshot(raw, direction);
    }
}

public readonly record struct FieldNavigationMovementObservation(
    bool IsUsable,
    bool IsMoving,
    FieldNavigationInput Input,
    int DeltaX,
    int DeltaY,
    double Distance,
    string Diagnostic);

public readonly record struct FieldNavigationStickRecommendation(
    FieldNavigationInput Input,
    FieldNavigationStickDirection Stick,
    bool IsObserved,
    string Diagnostic);

public sealed class FieldNavigationMovementObserver
{
    private const double MinimumMovementDistance = 4d;
    private const double ConsistentDirectionDot = 0.75d;
    private const int VerifiedSampleCount = 2;
    private const double FullTurn = Math.PI * 2d;
    private const double DirectionUnitsPerTurn = 256d;
    private const float Diagonal = 0.70710677f;

    private static readonly FieldNavigationInput[] CandidateDirections =
    [
        FieldNavigationInput.Up,
        FieldNavigationInput.UpRight,
        FieldNavigationInput.Right,
        FieldNavigationInput.DownRight,
        FieldNavigationInput.Down,
        FieldNavigationInput.DownLeft,
        FieldNavigationInput.Left,
        FieldNavigationInput.UpLeft
    ];

    private readonly Dictionary<FieldNavigationInput, DirectionEvidence> evidence = new();
    private FieldPositionSnapshot? previousPosition;
    private FieldNavigationInputSnapshot previousInput;
    private int fieldId = -1;
    private int modelIndex = -1;
    private int? stableControlDirection;

    public FieldNavigationMovementObservation Observe(
        FieldPositionSnapshot position,
        FieldNavigationInputSnapshot input,
        FieldNavigationControlTransform nativeTransform,
        bool isSuppressed)
    {
        if (!FieldPositionReader.IsUsable(position) || isSuppressed || input.Direction == FieldNavigationInput.Conflicting)
        {
            ClearPendingSample();
            return new FieldNavigationMovementObservation(
                false,
                false,
                input.Direction,
                0,
                0,
                0d,
                isSuppressed ? "movement observation suppressed" : "movement observation unavailable");
        }

        if (fieldId != position.FieldId || modelIndex != position.ModelIndex)
        {
            Reset();
            fieldId = position.FieldId;
            modelIndex = position.ModelIndex;
        }

        if (input.Direction == FieldNavigationInput.None)
        {
            if (stableControlDirection is null)
            {
                stableControlDirection = nativeTransform.SignedControlDirection;
            }
            else if (stableControlDirection.Value != nativeTransform.SignedControlDirection)
            {
                ResetCalibration();
                stableControlDirection = nativeTransform.SignedControlDirection;
            }
        }

        if (previousPosition is null)
        {
            StorePendingSample(position, input);
            return new FieldNavigationMovementObservation(
                true,
                false,
                FieldNavigationInput.None,
                0,
                0,
                0d,
                "movement baseline captured");
        }

        var usedInput = previousInput.Direction;
        var dx = position.X - previousPosition.Value.X;
        var dy = position.Y - previousPosition.Value.Y;
        var distance = Math.Sqrt(dx * (double)dx + dy * (double)dy);
        StorePendingSample(position, input);
        if (!IsDirectional(usedInput) || distance < MinimumMovementDistance)
        {
            return new FieldNavigationMovementObservation(
                true,
                false,
                usedInput,
                dx,
                dy,
                distance,
                IsDirectional(usedInput) ? "directional input was blocked or stationary" : "no directional input");
        }

        AddEvidence(usedInput, dx / distance, dy / distance);
        var samples = evidence[usedInput].Count;
        return new FieldNavigationMovementObservation(
            true,
            true,
            usedInput,
            dx,
            dy,
            distance,
            $"input={usedInput}, dx={dx}, dy={dy}, distance={distance:0.0}, samples={samples}");
    }

    public FieldNavigationStickRecommendation ResolveStickDirection(
        int desiredWorldX,
        int desiredWorldY,
        FieldNavigationControlTransform nativeTransform)
    {
        var desiredLength = Math.Sqrt(desiredWorldX * (double)desiredWorldX + desiredWorldY * (double)desiredWorldY);
        if (desiredLength <= 0d)
        {
            return new FieldNavigationStickRecommendation(
                FieldNavigationInput.None,
                new FieldNavigationStickDirection(0f, 0f),
                false,
                "desired route vector is zero");
        }

        var desiredX = desiredWorldX / desiredLength;
        var desiredY = desiredWorldY / desiredLength;
        var bestInput = FieldNavigationInput.Up;
        var bestScore = double.NegativeInfinity;
        foreach (var candidate in CandidateDirections)
        {
            var (worldX, worldY) = PredictWorldDirection(candidate, nativeTransform);

            var score = worldX * desiredX + worldY * desiredY;
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestInput = candidate;
        }

        var observedSupportsNative = TryGetObservedDirection(bestInput, out var observedX, out var observedY) &&
                                     observedX * desiredX + observedY * desiredY >= ConsistentDirectionDot;

        return new FieldNavigationStickRecommendation(
            bestInput,
            ToStickDirection(bestInput),
            observedSupportsNative,
            $"input={bestInput}, source=native, alignment={bestScore:0.000}, " +
            $"observedValidation={(observedSupportsNative ? "supporting" : "unavailable-or-conflicting")}");
    }

    public void Reset()
    {
        evidence.Clear();
        ClearPendingSample();
        fieldId = -1;
        modelIndex = -1;
        stableControlDirection = null;
    }

    public static FieldNavigationStickDirection ToStickDirection(FieldNavigationInput input) => input switch
    {
        FieldNavigationInput.Up => new FieldNavigationStickDirection(0f, -1f),
        FieldNavigationInput.UpRight => new FieldNavigationStickDirection(Diagonal, -Diagonal),
        FieldNavigationInput.Right => new FieldNavigationStickDirection(1f, 0f),
        FieldNavigationInput.DownRight => new FieldNavigationStickDirection(Diagonal, Diagonal),
        FieldNavigationInput.Down => new FieldNavigationStickDirection(0f, 1f),
        FieldNavigationInput.DownLeft => new FieldNavigationStickDirection(-Diagonal, Diagonal),
        FieldNavigationInput.Left => new FieldNavigationStickDirection(-1f, 0f),
        FieldNavigationInput.UpLeft => new FieldNavigationStickDirection(-Diagonal, -Diagonal),
        _ => new FieldNavigationStickDirection(0f, 0f)
    };

    private void AddEvidence(FieldNavigationInput input, double x, double y)
    {
        if (!evidence.TryGetValue(input, out var current))
        {
            evidence[input] = new DirectionEvidence(x, y, 1);
            return;
        }

        var currentLength = Math.Sqrt(current.SumX * current.SumX + current.SumY * current.SumY);
        var alignment = currentLength <= 0d
            ? 1d
            : current.SumX / currentLength * x + current.SumY / currentLength * y;
        evidence[input] = alignment >= ConsistentDirectionDot
            ? new DirectionEvidence(current.SumX + x, current.SumY + y, current.Count + 1)
            : new DirectionEvidence(x, y, 1);
    }

    private bool TryGetObservedDirection(FieldNavigationInput input, out double x, out double y)
    {
        if (TryGetVerifiedEvidence(input, out x, out y))
        {
            return true;
        }

        var opposite = GetOppositeInput(input);
        if (!TryGetVerifiedEvidence(opposite, out x, out y))
        {
            return false;
        }

        x = -x;
        y = -y;
        return true;
    }

    private bool TryGetVerifiedEvidence(FieldNavigationInput input, out double x, out double y)
    {
        x = 0d;
        y = 0d;
        if (!evidence.TryGetValue(input, out var current) || current.Count < VerifiedSampleCount)
        {
            return false;
        }

        var length = Math.Sqrt(current.SumX * current.SumX + current.SumY * current.SumY);
        if (length <= 0d)
        {
            return false;
        }

        x = current.SumX / length;
        y = current.SumY / length;
        return true;
    }

    private static FieldNavigationInput GetOppositeInput(FieldNavigationInput input) => input switch
    {
        FieldNavigationInput.Up => FieldNavigationInput.Down,
        FieldNavigationInput.UpRight => FieldNavigationInput.DownLeft,
        FieldNavigationInput.Right => FieldNavigationInput.Left,
        FieldNavigationInput.DownRight => FieldNavigationInput.UpLeft,
        FieldNavigationInput.Down => FieldNavigationInput.Up,
        FieldNavigationInput.DownLeft => FieldNavigationInput.UpRight,
        FieldNavigationInput.Left => FieldNavigationInput.Right,
        FieldNavigationInput.UpLeft => FieldNavigationInput.DownRight,
        _ => FieldNavigationInput.None
    };

    private static (double X, double Y) PredictWorldDirection(
        FieldNavigationInput input,
        FieldNavigationControlTransform transform)
    {
        var stick = ToStickDirection(input);
        var inputAngle = Math.Atan2(-stick.X, -stick.Y);
        var controlAngle = transform.SignedControlDirection * FullTurn / DirectionUnitsPerTurn;
        var worldAngle = inputAngle + controlAngle;
        return (Math.Sin(worldAngle), -Math.Cos(worldAngle));
    }

    private void StorePendingSample(FieldPositionSnapshot position, FieldNavigationInputSnapshot input)
    {
        previousPosition = position;
        previousInput = input;
    }

    private void ClearPendingSample()
    {
        previousPosition = null;
        previousInput = default;
    }

    private void ResetCalibration()
    {
        evidence.Clear();
        ClearPendingSample();
    }

    private static bool IsDirectional(FieldNavigationInput input) =>
        input is >= FieldNavigationInput.Up and <= FieldNavigationInput.UpLeft;

    private readonly record struct DirectionEvidence(double SumX, double SumY, int Count);
}
