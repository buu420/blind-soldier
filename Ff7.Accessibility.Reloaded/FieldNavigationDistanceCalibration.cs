namespace Ff7.Accessibility.Reloaded;

public static class FieldNavigationDistanceCalibration
{
    private const int MinimumWalkingSamples = 4;
    private const int MinimumRunningSamples = 8;
    private const double MaximumRelativeStandardDeviation = 0.20d;
    private const double MinimumPlausibleUnitsPerCount = 20d;
    private const double MaximumPlausibleUnitsPerCount = 320d;

    private static readonly IReadOnlyDictionary<int, int> MeasuredWalkingUnitsPerStep =
        new Dictionary<int, int>
        {
            [116] = 60,
            [117] = 50,
            [118] = 60,
            [119] = 55,
            [120] = 60,
            [121] = 60,
            [123] = 38,
            [151] = 63,
            [239] = 69,
            [258] = 67,
            [263] = 67,
            [268] = 66
        };

    private static readonly IReadOnlyDictionary<int, int> MeasuredRunningUnitsPerStep =
        new Dictionary<int, int>
        {
            // Twenty-four stable Floor 67 probe samples: average 183.9,
            // standard deviation 1.9, observed range 180.0-191.4.
            [256] = 184,
            [257] = 181,
            [258] = 178,
            [262] = 181,
            [264] = 182,
            // Thirty-seven stable Floor 70 probe samples: average 178.6,
            // standard deviation 7.5.
            [266] = 179,
            [268] = 179
        };

    public static int Resolve(int fieldId, int fallbackUnitsPerCount) =>
        MeasuredWalkingUnitsPerStep.TryGetValue(fieldId, out var measured)
            ? measured
            : Math.Max(1, fallbackUnitsPerCount);

    public static int ResolveForNavigation(
        int fieldId,
        int fallbackUnitsPerCount,
        FieldFootstepCadence cadence,
        FieldFootstepDistanceProbeSummary probeSummary)
    {
        var walkingFallback = Resolve(fieldId, fallbackUnitsPerCount);
        var cadenceFallback = cadence == FieldFootstepCadence.Run &&
                              MeasuredRunningUnitsPerStep.TryGetValue(fieldId, out var measuredRun)
            ? measuredRun
            : walkingFallback;
        var statistics = cadence == FieldFootstepCadence.Run
            ? probeSummary.Run
            : probeSummary.Walk;
        var minimumSamples = cadence == FieldFootstepCadence.Run
            ? MinimumRunningSamples
            : MinimumWalkingSamples;
        return IsConfident(statistics, minimumSamples)
            ? Math.Max(
                1,
                (int)Math.Round(
                    statistics.AverageUnits,
                    MidpointRounding.AwayFromZero))
            : cadenceFallback;
    }

    private static bool IsConfident(
        FieldFootstepDistanceStatistics statistics,
        int minimumSamples)
    {
        if (statistics.SampleCount < minimumSamples ||
            !double.IsFinite(statistics.AverageUnits) ||
            !double.IsFinite(statistics.StandardDeviationUnits) ||
            statistics.AverageUnits < MinimumPlausibleUnitsPerCount ||
            statistics.AverageUnits > MaximumPlausibleUnitsPerCount ||
            statistics.StandardDeviationUnits < 0d)
        {
            return false;
        }

        return statistics.StandardDeviationUnits / statistics.AverageUnits <=
               MaximumRelativeStandardDeviation;
    }
}
