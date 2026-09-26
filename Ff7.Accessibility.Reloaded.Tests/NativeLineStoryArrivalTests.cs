using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class NativeLineStoryArrivalTests
{
    internal static void Run()
    {
        const int eventTable = 0x03000000;
        var line = new FieldNavigationTriggerLine(3086, -2102, 1043, 3132, -2102, 1043);
        var definition = new FieldStoryEventDefinition(464, FieldStoryTargetKind.Location,
            "Native proximity interaction", TriggerLine: line, CompletesOnArrival: false,
            UsesPlayerCollisionRadius: true);
        var position = new FieldPositionSnapshot(1, 464, 0, 3109, -2042, 1043, 247, 0);
        short radius = 40;
        var tableAvailable = true;
        var reader = new FieldStoryTargetReader(
            address => address == FieldNavigationObjectReader.AddressFieldEventDataPtr && tableAvailable ? eventTable : 0,
            address => address == eventTable + 0x72 ? radius : (short)0,
            address => address == FieldPositionReader.AddressFieldNumModels ? (byte)1 : (byte)0,
            [definition]);

        foreach (var nativeRadius in new short[] { 8, 40, 72 })
        {
            radius = nativeRadius;
            var target = reader.ReadTargets(position).Single();
            // 00637ABB: on while the leader touches the LINE strictly inside its radius, so the
            // step is reached on that touch, not at a circle round the line's middle.
            Equal(((int)nativeRadius, 0), (target.LineActivationRadius, target.InteractionRadius),
                "Go proximity must stop on the touch within the current native player collision radius");
            Equal(false, target.CompletesOnArrival, "the native interaction remains active until its own state changes");
        }
        foreach (var invalidRadius in new short[] { -1, 0, 1 })
        {
            radius = invalidRadius;
            Equal(0, reader.ReadTargets(position).Count, "unusable native range must not fall back to configured80");
        }
        radius = 40;
        tableAvailable = false;
        Equal(0, reader.ReadTargets(position).Count, "missing native player data cannot invent a range");
        tableAvailable = true;
        Equal(0, reader.ReadTargets(position with { ModelIndex = 1 }).Count,
            "out-of-range player model cannot supply interaction geometry");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
    }
}
