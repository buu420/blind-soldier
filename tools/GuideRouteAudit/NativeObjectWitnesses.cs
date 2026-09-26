using System.Reflection;
using Ff7.Accessibility.Reloaded;

namespace GuideRouteAudit;

internal static class NativeObjectWitnesses
{
    private static readonly MethodInfo IdMethod = RequiredMethod("CreateStableId");
    private static readonly MethodInfo CrossingMethod = RequiredMethod("CrossingLineOf");

    private static MethodInfo RequiredMethod(string name) => typeof(FieldNavigationObjectReader)
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"The shipping Object reader contract changed: {name}.");

    internal static FieldNavigationTarget Target(FieldNavigationObjectDefinition definition,
        string label, int x, int y, int z, int radius,
        FieldNavigationTriggerLine? liveLine = null, int lineActivationRadius = 0) =>
        new(definition.FieldId, FieldNavigationCategory.Objects, label, x, y, z,
            (string)IdMethod.Invoke(null, [definition])!, FieldNavigationObjectCueClassifier.Classify(definition),
            TriggerEntityId: definition.TargetKind == FieldNavigationObjectTargetKind.Model ? definition.EntityId : -1,
            CompletesOnArrival: definition.Kind == FieldNavigationObjectKind.SavePoint,
            InteractionRadius: radius, ManualNavigationGuidance: definition.ManualNavigationGuidance,
            ApproachCrossingLine: (FieldNavigationTriggerLine?)CrossingMethod.Invoke(null, [definition]),
            TriggerLine: liveLine, LineActivationRadius: lineActivationRadius);

    internal static void TestReaderContract()
    {
        var crossing = new FieldNavigationObjectDefinition(100, 9, FieldNavigationObjectKind.Named,
            Label: "Crossing spot", TargetKind: FieldNavigationObjectTargetKind.Location,
            StaticX: 10, StaticY: 0, StaticZ: 0, InteractionRadiusOverride: 8,
            PlayerSideLine: new(0, -100, 0, 0, 100, 0), PlayerSide: 1,
            CrossingLine: new(0, -100, 0, 0, 100, 0));
        var cases = new[]
        {
            new FieldNavigationObjectDefinition(100, 7, FieldNavigationObjectKind.Named, Label: "Talk object", UsesTalkInteraction: true),
            new FieldNavigationObjectDefinition(100, 7, FieldNavigationObjectKind.SavePoint, TargetKind: FieldNavigationObjectTargetKind.Line),
            new FieldNavigationObjectDefinition(100, 7, FieldNavigationObjectKind.Named, Label: "Native line",
                TargetKind: FieldNavigationObjectTargetKind.Line, UsesPlayerCollisionRadius: true),
            crossing,
            crossing with { RequiredBank = 1, RequiredAddress = 6, RequiredMask = 1, RequiredValue = 0 }
        };
        foreach (var definition in cases)
        foreach (var supplyLiveLine in new[] { false, true })
        {
            const int events = 0x02404000;
            var model = events + FieldNavigationObjectReader.FieldEventDataStride;
            var bytes = new Dictionary<int, byte>
            {
                [FieldPositionReader.AddressFieldNumModels] = 2,
                [FieldNavigationObjectReader.AddressFieldModelIdArray + definition.EntityId] = 1,
                [model + FieldNavigationObjectReader.VisibilityOffset] = 1,
                [events + FieldNavigationNpcReader.CollisionRadiusOffset] = 30,
                [model + FieldNavigationNpcReader.TalkRadiusOffset] = 80
            };
            int Int(int a) => a == FieldNavigationObjectReader.AddressFieldEventDataPtr ? events :
                a == model + FieldNavigationObjectReader.PositionXOffset ? 120 << 12 :
                a == model + FieldNavigationObjectReader.PositionYOffset ? 150 << 12 :
                a == model + FieldNavigationObjectReader.PositionZOffset ? 20 << 12 : 0;
            var liveLine = new FieldNavigationTriggerLine(-100, 0, 0, 100, 0, 0);
            var reader = new FieldNavigationObjectReader(Int, a => bytes.GetValueOrDefault(a), _ => null, _ => null,
                [definition], _ => true, readLiveLine: supplyLiveLine ? _ => liveLine : null);
            var actual = reader.ReadTargets(new(1, 100, 0, -50, 0, 0, 0, 0)).Single();
            var isModel = definition.TargetKind == FieldNavigationObjectTargetKind.Model;
            var usesLiveLine = supplyLiveLine && definition.TargetKind == FieldNavigationObjectTargetKind.Line;
            var radius = usesLiveLine ? 0 : definition.UsesTalkInteraction ? 110 : definition.UsesPlayerCollisionRadius ? 29 :
                definition.InteractionRadiusOverride ?? FieldNavigationObjectReader.DefaultInteractionRadius;
            var expected = Target(definition, actual.Label, isModel ? 120 : definition.StaticX,
                isModel ? 150 : definition.StaticY, isModel ? 20 : definition.StaticZ, radius,
                usesLiveLine ? liveLine : null, usesLiveLine ? 30 : 0);
            if (actual != expected) throw new InvalidOperationException($"Object audit target differs from the shipping reader: {actual}; expected {expected}.");
        }
        Console.WriteLine("Object audit metadata matches actual reader targets, including crossing approaches and state-gated negatives.");
    }
}
