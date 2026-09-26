using Ff7.Accessibility.Reloaded;

namespace GuideRouteAudit;

internal static class NativeStoryWitnesses
{
    internal static FieldNavigationTarget Target(FieldStoryEventDefinition definition,
        int x, int y, int z, int playerRadius, int modelRadius)
    {
        var model = definition.Kind == FieldStoryTargetKind.Model;
        var touch = !model && definition.UsesPlayerCollisionRadius && !definition.CompletesOnArrival;
        var radius = model ? definition.UsesContactRange
            ? FieldNavigationNpcReader.ContactReach(playerRadius, modelRadius) : playerRadius + modelRadius
            : definition.UsesPlayerCollisionRadius && !touch ? Math.Max(0, playerRadius - 1) : 0;
        return new(definition.FieldId, FieldNavigationCategory.Story, definition.Label, x, y, z,
            $"story:{definition.FieldId}:{definition.EntityId}:{definition.TargetGameMoment}:{definition.Label}",
            TriggerEntityId: model ? definition.EntityId : -1,
            CompletesOnArrival: !model && definition.CompletesOnArrival,
            InteractionRadius: radius, TriggerLine: definition.TriggerLine,
            RouteDetour: definition.RouteDetour, RouteDetours: definition.RouteDetours,
            CompletionTriangles: definition.CompletionPlayerTriangles,
            ManualNavigationGuidance: definition.ManualNavigationGuidance,
            Activation: definition.UsesContactRange ? FieldNavigationActivation.Contact :
                model ? FieldNavigationActivation.Talk : FieldNavigationActivation.Default,
            LineActivationRadius: touch ? playerRadius : 0);
    }

    internal static void TestReaderContract()
    {
        var talk = new FieldStoryEventDefinition(100, FieldStoryTargetKind.Model, "Talk", 7);
        var point = new FieldStoryEventDefinition(100, FieldStoryTargetKind.Location, "Point", X: 120, Y: 150, Z: 20);
        var line = point with { Label = "Line", TriggerLine = new(-100, 0, 0, 100, 0, 0),
            UsesPlayerCollisionRadius = true, RequiredEnabledLineEntityId = 7 };
        foreach (var definition in new[] { talk, talk with { UsesContactRange = true }, point, line,
                     line with { CompletesOnArrival = false } })
        {
            const int events = 0x02404000;
            var model = events + FieldNavigationObjectReader.FieldEventDataStride;
            int Int(int a) => a == FieldNavigationObjectReader.AddressFieldEventDataPtr ? events :
                a == model + FieldNavigationObjectReader.PositionXOffset ? 120 << 12 :
                a == model + FieldNavigationObjectReader.PositionYOffset ? 150 << 12 :
                a == model + FieldNavigationObjectReader.PositionZOffset ? 20 << 12 : 0;
            byte Byte(int a) => a == FieldPositionReader.AddressFieldNumModels ? (byte)2 :
                a == FieldNavigationObjectReader.AddressFieldModelIdArray + 7 ? (byte)1 :
                a == model + FieldNavigationObjectReader.VisibilityOffset ? (byte)1 : (byte)0;
            short Short(int a) => a == events + FieldNavigationNpcReader.CollisionRadiusOffset ? (short)30 :
                a == model + FieldNavigationNpcReader.CollisionRadiusOffset ? (short)40 :
                a == model + FieldNavigationNpcReader.TalkRadiusOffset ? (short)80 : (short)0;
            var reader = new FieldStoryTargetReader(Int, Short, Byte, [definition], _ => true);
            var actual = reader.ReadTargets(new(1, 100, 0, -50, 0, 0, 0, 0)).Single();
            var expected = Target(definition, 120, 150, 20, 30, definition.UsesContactRange ? 40 : 80);
            if (actual != expected) throw new InvalidOperationException($"Story audit target differs from the shipping reader: {actual}; expected {expected}.");
        }
        Console.WriteLine("Story audit metadata matches actual reader targets for Talk, Contact, points, touch lines and crossings.");
    }
}
