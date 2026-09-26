using System.Reflection;
using Ff7.Accessibility.Reloaded;

namespace GuideRouteAudit;

internal static class NativeNpcWitnesses
{
    // Read the shipping reader's reviewed overrides as well as script discovery.
    // Keeping this audit-only avoids adding an instrumentation API to the mod.
    private static readonly MethodInfo MergeMethod = typeof(FieldNavigationNpcReader).GetMethod(
        "MergeVerifiedDefinitions", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("The shipping NPC merge contract changed; update the audit.");
    private static readonly IReadOnlyDictionary<(int FieldId, int EntityId), string> ReviewedLabels =
        (IReadOnlyDictionary<(int, int), string>)(typeof(FieldNavigationNpcReader).GetField(
            "VerifiedLabels", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null)
        ?? throw new InvalidOperationException("The shipping NPC label contract changed; update the audit."));

    internal static IReadOnlyList<FieldScriptNpcDefinition> Merge(int field, IReadOnlyList<FieldScriptNpcDefinition> native) =>
        (IReadOnlyList<FieldScriptNpcDefinition>)MergeMethod.Invoke(null, [field, native])!;

    internal static string Label(FieldScriptNpcDefinition definition) =>
        ReviewedLabels.TryGetValue((definition.FieldId, definition.EntityId), out var label) ? label : definition.EntityName;

    internal static FieldNavigationTarget Target(FieldScriptNpcDefinition definition, int x, int y, int z,
        int playerRadius, int modelRadius, bool useLine)
    {
        var line = useLine ? definition.InteractionLine : null;
        return new(definition.FieldId, FieldNavigationCategory.Npcs, Label(definition),
            line is { } l ? Midpoint(l.StartX, l.EndX) : x,
            line is { } ly ? Midpoint(ly.StartY, ly.EndY) : y,
            line is { } lz ? Midpoint(lz.StartZ, lz.EndZ) : z,
            $"npc:{definition.FieldId}:{definition.EntityId}",
            TriggerEntityId: useLine ? definition.InteractionLineEntityId!.Value : definition.EntityId,
            InteractionRadius: useLine ? 0 : definition.ContactOnly
                ? FieldNavigationNpcReader.ContactReach(playerRadius, modelRadius) : playerRadius + modelRadius,
            TriggerLine: line,
            Activation: definition.ContactOnly ? FieldNavigationActivation.Contact : FieldNavigationActivation.Default,
            LineActivationRadius: useLine && playerRadius > 1 ? playerRadius : 0);
    }

    private static int Midpoint(int first, int second) =>
        (int)Math.Round((first + second) / 2d, MidpointRounding.AwayFromZero);

    internal static void TestReaderContract()
    {
        // These are emitted by the real reader, not targets embellished by the test.
        var ordinary = new FieldScriptNpcDefinition(100, 7, "man", [1]);
        var counter = Merge(518, []).Single(n => n.EntityId == 15);
        foreach (var definition in new[] { ordinary, counter })
        {
            const int events = 0x02404000;
            var model = events + FieldNavigationObjectReader.FieldEventDataStride;
            var bytes = new Dictionary<int, byte>
            {
                [FieldPositionReader.AddressFieldNumModels] = 2,
                [FieldNavigationObjectReader.AddressFieldModelIdArray + definition.EntityId] = 1,
                [model + FieldNavigationObjectReader.VisibilityOffset] = 1,
                [FieldNavigationObjectReader.AddressFieldBankBase + 0x100 + 129] = 4
            };
            byte Byte(int a) => bytes.GetValueOrDefault(a);
            short Short(int a) => a == events + FieldNavigationNpcReader.CollisionRadiusOffset ? (short)30 :
                a == model + FieldNavigationNpcReader.TalkRadiusOffset ? (short)80 : (short)0;
            int Int(int a) => a == FieldNavigationObjectReader.AddressFieldEventDataPtr ? events :
                a == model + FieldNavigationObjectReader.PositionXOffset ? 120 << 12 :
                a == model + FieldNavigationObjectReader.PositionYOffset ? 150 << 12 :
                a == model + FieldNavigationObjectReader.PositionZOffset ? 20 << 12 : 0;
            var reader = new FieldNavigationNpcReader(Int, Short, Byte, (_, _) => [], _ => [definition], isLineEnabled: _ => true);
            var actual = reader.ReadTargets(new(1, definition.FieldId, 0, 0, 0, 0, 0, 0))
                .Single(t => t.StableId == $"npc:{definition.FieldId}:{definition.EntityId}");
            var expected = Target(definition, 120, 150, 20, 30, 80, definition.InteractionLine.HasValue) with { Label = actual.Label };
            if (actual != expected) throw new InvalidOperationException($"NPC audit target differs from the shipping reader: {actual}; expected {expected}.");
        }
        Console.WriteLine("NPC audit metadata matches actual reader targets, including reviewed counter overrides.");
    }
}
