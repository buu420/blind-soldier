namespace Ff7.Accessibility.Reloaded;

public readonly record struct FieldFootstepDistanceStatistics(
    int SampleCount,
    double AverageUnits,
    double StandardDeviationUnits,
    double MinimumUnits,
    double MaximumUnits);

public readonly record struct FieldFootstepDistanceProbeSummary(
    FieldFootstepDistanceStatistics Walk,
    FieldFootstepDistanceStatistics Run,
    FieldFootstepDistanceStatistics Combined,
    int SuggestedDistanceUnitsPerCount);

public readonly record struct FieldFootstepDistanceProbeObservation(
    bool AcceptedSample,
    double SampleDistanceUnits,
    FieldFootstepDistanceProbeSummary FieldSummary,
    string? Report);

public sealed class FieldFootstepDistanceProbe
{
    private static readonly TimeSpan MaximumObservationGap = TimeSpan.FromMilliseconds(250);

    private readonly int reportEverySamples;
    private readonly RunningStatistics walk = new();
    private readonly RunningStatistics run = new();
    private readonly RunningStatistics combined = new();
    private readonly Dictionary<int, ProbeStatistics> statisticsByField = new();
    private FieldPositionSnapshot? previousPosition;
    private DateTime previousObservedAt = DateTime.MinValue;
    private FieldFootstepCadence segmentCadence;
    private bool hasFootstepAnchor;
    private double distanceSinceFootstep;

    public FieldFootstepDistanceProbe(int reportEverySamples = 8)
    {
        this.reportEverySamples = Math.Max(1, reportEverySamples);
    }

    public FieldFootstepDistanceProbeSummary Summary
    {
        get
        {
            return CreateSummary(walk, run, combined);
        }
    }

    public FieldFootstepDistanceProbeSummary GetFieldSummary(int fieldId) =>
        statisticsByField.TryGetValue(fieldId, out var statistics)
            ? CreateSummary(statistics.Walk, statistics.Run, statistics.Combined)
            : CreateSummary(new RunningStatistics(), new RunningStatistics(), new RunningStatistics());

    public string? Observe(
        FieldPositionSnapshot position,
        DateTime now,
        bool isForeground,
        FieldNavigationInput input,
        FieldFootstepCadence cadence,
        bool footstepTriggered) =>
        ObserveCore(
            position,
            now,
            isForeground,
            IsDirectional(input),
            cadence,
            footstepTriggered).Report;

    public FieldFootstepDistanceProbeObservation ObserveControlled(
        FieldPositionSnapshot position,
        DateTime now,
        bool isForeground,
        bool isDirectionalMovement,
        FieldFootstepCadence cadence,
        bool footstepTriggered) =>
        ObserveCore(
            position,
            now,
            isForeground,
            isDirectionalMovement,
            cadence,
            footstepTriggered);

    private FieldFootstepDistanceProbeObservation ObserveCore(
        FieldPositionSnapshot position,
        DateTime now,
        bool isForeground,
        bool isDirectionalMovement,
        FieldFootstepCadence cadence,
        bool footstepTriggered)
    {
        if (!isForeground ||
            !FieldPositionReader.IsUsable(position) ||
            !isDirectionalMovement)
        {
            ResetCurrentStride();
            return default;
        }

        if (previousPosition is null ||
            previousPosition.Value.FieldId != position.FieldId ||
            previousPosition.Value.ModelIndex != position.ModelIndex ||
            segmentCadence != cadence)
        {
            Prime(position, now, cadence, footstepTriggered);
            return default;
        }

        var elapsed = now - previousObservedAt;
        if (elapsed <= TimeSpan.Zero || elapsed > MaximumObservationGap)
        {
            Prime(position, now, cadence, footstepTriggered);
            return default;
        }

        var previous = previousPosition.Value;
        if (FieldNavigationPositionContinuity.IsDiscontinuous(
                previous,
                previousObservedAt,
                position,
                now,
                out _))
        {
            Prime(position, now, cadence, footstepTriggered: false);
            return default;
        }

        previousPosition = position;
        previousObservedAt = now;
        var dx = position.X - previous.X;
        var dy = position.Y - previous.Y;
        var distance = Math.Sqrt(dx * (double)dx + dy * (double)dy);
        if (distance <= 0d)
        {
            ResetCurrentStride();
            return default;
        }

        distanceSinceFootstep += distance;
        if (!footstepTriggered)
        {
            return default;
        }

        if (!hasFootstepAnchor)
        {
            hasFootstepAnchor = true;
            distanceSinceFootstep = 0d;
            return default;
        }

        var sampleDistance = distanceSinceFootstep;
        distanceSinceFootstep = 0d;
        AddSample(position.FieldId, cadence, sampleDistance);
        var summary = GetFieldSummary(position.FieldId);
        var report = summary.Combined.SampleCount == 1 ||
                     summary.Combined.SampleCount % reportEverySamples == 0
            ? FormatReport(position.FieldId, summary)
            : null;
        return new FieldFootstepDistanceProbeObservation(
            true,
            sampleDistance,
            summary,
            report);
    }

    public void ResetCurrentStride()
    {
        previousPosition = null;
        previousObservedAt = DateTime.MinValue;
        hasFootstepAnchor = false;
        distanceSinceFootstep = 0d;
    }

    private void Prime(
        FieldPositionSnapshot position,
        DateTime now,
        FieldFootstepCadence cadence,
        bool footstepTriggered)
    {
        previousPosition = position;
        previousObservedAt = now;
        segmentCadence = cadence;
        hasFootstepAnchor = footstepTriggered;
        distanceSinceFootstep = 0d;
    }

    private void AddSample(int fieldId, FieldFootstepCadence cadence, double distance)
    {
        (cadence == FieldFootstepCadence.Run ? run : walk).Add(distance);
        combined.Add(distance);
        if (!statisticsByField.TryGetValue(fieldId, out var fieldStatistics))
        {
            fieldStatistics = new ProbeStatistics();
            statisticsByField[fieldId] = fieldStatistics;
        }

        (cadence == FieldFootstepCadence.Run ? fieldStatistics.Run : fieldStatistics.Walk).Add(distance);
        fieldStatistics.Combined.Add(distance);
    }

    private static FieldFootstepDistanceProbeSummary CreateSummary(
        RunningStatistics walkStatistics,
        RunningStatistics runStatistics,
        RunningStatistics combinedStatistics)
    {
        var walkSnapshot = walkStatistics.Snapshot();
        var combinedSnapshot = combinedStatistics.Snapshot();
        var suggestedStatistics = walkSnapshot.SampleCount == 0 ? combinedSnapshot : walkSnapshot;
        return new FieldFootstepDistanceProbeSummary(
            walkSnapshot,
            runStatistics.Snapshot(),
            combinedSnapshot,
            suggestedStatistics.SampleCount == 0
                ? 0
                : (int)Math.Round(suggestedStatistics.AverageUnits, MidpointRounding.AwayFromZero));
    }

    private static string FormatReport(int fieldId, FieldFootstepDistanceProbeSummary summary) =>
        $"field={fieldId}; walk {FormatStatistics(summary.Walk)}; run {FormatStatistics(summary.Run)}; " +
        $"combined {FormatStatistics(summary.Combined)}; " +
        $"suggested distance units per count={summary.SuggestedDistanceUnitsPerCount}";

    private static string FormatStatistics(FieldFootstepDistanceStatistics statistics) =>
        $"samples={statistics.SampleCount}, average={statistics.AverageUnits:0.0}, " +
        $"standard deviation={statistics.StandardDeviationUnits:0.0}, " +
        $"range={statistics.MinimumUnits:0.0}-{statistics.MaximumUnits:0.0}";

    private static bool IsDirectional(FieldNavigationInput input) =>
        input is >= FieldNavigationInput.Up and <= FieldNavigationInput.UpLeft;

    private sealed class ProbeStatistics
    {
        public RunningStatistics Walk { get; } = new();
        public RunningStatistics Run { get; } = new();
        public RunningStatistics Combined { get; } = new();
    }

    private sealed class RunningStatistics
    {
        private int count;
        private double mean;
        private double sumSquaredDifferences;
        private double minimum = double.PositiveInfinity;
        private double maximum = double.NegativeInfinity;

        public void Add(double value)
        {
            count++;
            var delta = value - mean;
            mean += delta / count;
            var nextDelta = value - mean;
            sumSquaredDifferences += delta * nextDelta;
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
        }

        public FieldFootstepDistanceStatistics Snapshot() => count == 0
            ? new FieldFootstepDistanceStatistics(0, 0d, 0d, 0d, 0d)
            : new FieldFootstepDistanceStatistics(
                count,
                mean,
                Math.Sqrt(sumSquaredDifferences / count),
                minimum,
                maximum);
    }
}
