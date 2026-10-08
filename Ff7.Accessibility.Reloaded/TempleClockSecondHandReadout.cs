using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// The visible second hand while the player crosses a clock-hand bridge. Native line10
/// (entity 18) is the knock-off trigger; its LINON state disables the hazard on the later
/// return. Bearings come only from second's rendered model, never its future angle or timer.
/// </summary>
internal sealed class TempleClockSecondHandReadout
{
    internal const int KnockOffLineEntityId = 18;
    // IDdr's two unlocked ends and their common walkmesh neighbour, from kuro_4.
    private static readonly HashSet<int> HandTriangles =
    [
        93, 2, 3, 78, 12, 13, 79, 0, 1, 89, 66, 67, 72, 60, 61, 73, 54, 55,
        85, 48, 49, 74, 42, 43, 75, 36, 37, 81, 14, 15, 76, 10, 11, 77, 6, 7
    ];
    private static readonly TimeSpan MotionFreshness = TimeSpan.FromSeconds(5);
    private string? lastKey;
    private int lastSeverity;
    private DateTime lastSaidAt = DateTime.MinValue;
    private int previousBearing = -1;
    private DateTime clockwiseAt = DateTime.MinValue;

    public string? CurrentLine { get; private set; }
    public bool CurrentIsUrgent { get; private set; }

    // A repeat must use this pose without advancing the ongoing motion/speech watch.
    // A fresh watch describes position and crossing, without claiming unseen motion.
    internal static string? DescribeCurrentHazard(FieldActivityObservation observation) =>
        new TempleClockSecondHandReadout().Observe(observation, DateTime.MinValue);

    internal static bool IsOnHand(FieldActivityObservation observation) =>
        observation.FieldId == FieldActivityReadout.ClockRoomFieldId &&
        observation.IsPlayerControlled && HandTriangles.Contains(observation.PlayerTriangle);

    public void Reset()
    {
        lastKey = null;
        lastSeverity = 0;
        lastSaidAt = DateTime.MinValue;
        previousBearing = -1;
        clockwiseAt = DateTime.MinValue;
        CurrentLine = null;
        CurrentIsUrgent = false;
    }

    public string? Observe(FieldActivityObservation observation, DateTime now)
    {
        if (!IsOnHand(observation) || observation.IsLineEnabled?.Invoke(KnockOffLineEntityId) == false)
        {
            Reset();
            return null;
        }

        var second = FieldActivityModelReading.Unreadable(FieldActivityReadout.ClockSecondHandEntityId);
        foreach (var reading in observation.Models)
        {
            if (reading.EntityId == FieldActivityReadout.ClockSecondHandEntityId) { second = reading; break; }
        }
        if (second.Status == FieldActivityReadStatus.Hidden)
        {
            Reset();
            return null;
        }

        if (observation.IsLineEnabled is null || second.Status != FieldActivityReadStatus.Visible ||
            (uint)second.Model.Direction > byte.MaxValue)
        {
            previousBearing = -1;
            clockwiseAt = DateTime.MinValue;
            return Publish("unreadable", "Cannot read the second-hand hazard.", 1, now);
        }

        var bearing = second.Model.Direction;
        if (previousBearing >= 0 && previousBearing != bearing)
        {
            var clockwise = (previousBearing - bearing + 256) % 256;
            clockwiseAt = clockwise is > 0 and <= 64 ? now : DateTime.MinValue;
        }
        previousBearing = bearing;

        // Native field direction: six (0) points down the clock at negative Y; three
        // (64) points at positive X. The rendered model supplies the visible pivot.
        var x = (double)observation.PlayerX - second.Model.X;
        var y = (double)observation.PlayerY - second.Model.Y;
        if (x == 0 && y == 0)
        {
            return Publish("position-unreadable", "Cannot read the second-hand hazard.", 1, now);
        }
        var playerBearing = (int)Math.Round(Math.Atan2(x, -y) * 256 / (2 * Math.PI));
        var ahead = ((bearing - playerBearing) % 256 + 256) % 256;
        var movingClockwise = clockwiseAt != DateTime.MinValue && now >= clockwiseAt && now - clockwiseAt <= MotionFreshness;
        var place = FieldActivityReadout.DescribeBearing(bearing);

        if (ahead <= 8 || ahead >= 248)
        {
            return Publish("crossing:" + place, $"Second hand crossing your bridge, {place}.", 2, now);
        }
        if (movingClockwise && ahead <= 42)
        {
            return Publish("approaching:" + place, $"Second hand approaching your bridge, {place}.", 1, now);
        }
        if (movingClockwise && ahead >= 224)
        {
            return Publish("passed:" + place, $"Second hand has passed your bridge, {place}.", 0, now);
        }
        return Publish("watch:" + place, $"Second hand {place}. It can knock you off the clock hands.", 0, now);
    }

    private string? Publish(string key, string line, int severity, DateTime now)
    {
        CurrentLine = line;
        CurrentIsUrgent = severity > 0;
        if (key == lastKey ||
            (lastKey is not null && severity <= lastSeverity && now - lastSaidAt < FieldActivityReadout.NonPendingSpeechInterval))
        {
            return null;
        }
        lastKey = key;
        lastSeverity = severity;
        lastSaidAt = now;
        return line;
    }
}
