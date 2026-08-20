using Ff7.Accessibility.Reloaded;

internal static class FortCondorTests
{
    private const int EventTable = 0x02404000;

    internal static void Run()
    {
        NpcsUseTheirVisibleModelLabels();
        ModellessEventEntitiesStaySilent();
        ExitsAreNamedAndDirectional();
        MountedClimbKeepsItsNativeDirection();
    }

    /// <summary>
    /// Replays the runtime log at 13:29:23Z on condor2's entrance ladder. The
    /// native ladder state reported <c>input=Up</c>, but a reroute fired at the
    /// moment of mounting, clearing the route's ladder action. Guidance then
    /// derived a direction from the route's next waypoint - still the mount
    /// approach behind the player - and told the player to "Climb left" onto a
    /// ladder that climbs up.
    /// </summary>
    private static void MountedClimbKeepsItsNativeDirection()
    {
        // The shared test planner only resolves triangles in field 900; the
        // field id is incidental, the ladder state and geometry are condor2 as
        // logged.
        const int fieldId = 900;

        // No portals, so the route carries no ladder action, exactly as it does
        // after a reroute lands on the mount frame.
        var planner = new ConfigurableCorridorRoutePlanner();
        var target = new FieldNavigationTarget(
            fieldId,
            FieldNavigationCategory.Exits,
            "Ladder up into Fort Condor",
            -9,
            243,
            0,
            "script-exit:354:4:355",
            DestinationFieldIds: [355]);
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource([target]),
            planner);
        var transform = new FieldNavigationControlTransform(-128);
        var noInput = new FieldNavigationInputSnapshot(0, FieldNavigationInput.None);
        var mountFrame = new FieldPositionSnapshot(1, fieldId, 0, 20, 227, -10, 0, 104);

        controller.HandleAction(FieldNavigationAction.ToggleBeacon, mountFrame, transform);

        // Walk one frame first so the route is live before the ladder mounts.
        var approach = mountFrame with { X = 5, Y = 207 };
        controller.UpdateLiveTracking(
            approach,
            noInput,
            transform,
            isSuppressed: false,
            arrivalDistanceUnits: 5);

        var mounted = new FieldLadderStateSnapshot(
            true,
            true,
            FieldLadderPhase.Climbing,
            FieldNavigationInput.Up,
            new FieldNavigationRouteWaypoint(-17, 298, 285),
            2,
            4,
            1);
        Equal(
            "Ladder mounted. Climb up.",
            controller.UpdateLiveTracking(
                mountFrame,
                noInput,
                transform,
                isSuppressed: false,
                arrivalDistanceUnits: 5,
                ladderState: mounted)?.Speech,
            "a mounted climb keeps the direction the native ladder state reports");
    }

    /// <summary>
    /// Fort Condor was never a reviewed label field, and none of its Talk
    /// entities open with a speaker heading, so the dialogue-guess fallback
    /// returned an empty label for every one of them and the reader dropped
    /// them all. The whole region announced no NPCs: no shop staff, no elder,
    /// no lookout running the fort battle.
    /// </summary>
    private static void NpcsUseTheirVisibleModelLabels()
    {
        var memory = new Dictionary<int, byte>();
        Setup(memory, numModels: 8);

        // convil_1's three villagers, each on its own live model record.
        foreach (var (entityId, modelId) in new (int EntityId, byte ModelId)[] { (27, 1), (28, 2), (29, 3) })
        {
            PlaceModel(memory, entityId, modelId, x: 10 * modelId, y: 20, z: 0);
        }

        // No scripted definitions at all: entity 28 is one the script catalog
        // misses, so the verified table must supply the definition too.
        var reader = new FieldNavigationNpcReader(
            address => ReadInt32(memory, address),
            address => ReadInt16(memory, address),
            address => ReadByte(memory, address),
            (_, _) => ["Hello.", "Do your best. We will, too."],
            _ => Array.Empty<FieldScriptNpcDefinition>());
        var targets = reader.ReadTargets(new FieldPositionSnapshot(1, 355, 0, 0, 0, 0, 0, 0));
        var byId = targets.ToDictionary(target => target.StableId, target => target.Label);

        Equal(3, targets.Count, "every visible Fort Condor villager should be announced");
        Equal("Materia shopkeeper", byId["npc:355:27"], "the materia counter is named by its role");
        Equal("Item shopkeeper", byId["npc:355:28"], "the item shop the script catalog misses is still announced");
        Equal("Fort Condor elder", byId["npc:355:29"], "the elder who holds the Huge Materia is named");
    }

    /// <summary>
    /// convil_2's event2, event3 and itemget entities carry plenty of dialogue
    /// but load no field model, so a sighted player sees nothing there. Now that
    /// Fort Condor is a reviewed label field they must not be guessed into
    /// existence from that dialogue.
    /// </summary>
    private static void ModellessEventEntitiesStaySilent()
    {
        var memory = new Dictionary<int, byte>();
        Setup(memory, numModels: 4);
        PlaceModel(memory, entityId: 9, modelId: 1, x: 0, y: 0, z: 0);

        var reader = new FieldNavigationNpcReader(
            address => ReadInt32(memory, address),
            address => ReadInt16(memory, address),
            address => ReadByte(memory, address),
            // Deliberately dialogue the speaker heuristic WOULD accept, so this
            // fails if Fort Condor ever drops out of the reviewed-label set.
            (_, _) => ["Commander", "Preparations ready?"],
            _ => new[] { new FieldScriptNpcDefinition(356, 9, "event3", [30, 34]) });

        Equal(
            0,
            reader.ReadTargets(new FieldPositionSnapshot(1, 356, 0, 0, 0, 0, 0, 0)).Count,
            "a Fort Condor entity with no field model must not be named from its dialogue");
    }

    /// <summary>
    /// condor1, condor2 and convil_1 share the words "Fort Condor" across their
    /// map names, so generated labels doubled the preposition ("Exit to Entrance
    /// to Fort Condor"), and the hill mouth is a world-map return point with no
    /// map name, so it read as a bare "Exit".
    /// </summary>
    private static void ExitsAreNamedAndDirectional()
    {
        var mapNames = new Dictionary<int, string>
        {
            [353] = "Base of Fort Condor",
            [354] = "Entrance to Fort Condor",
            [355] = "Fort Condor",
            [356] = "Watch Room",
            [358] = "top of the mountain"
        };
        var resolver = new FieldExitLabelResolver(
            fieldId => mapNames.TryGetValue(fieldId, out var name)
                ? FieldMapNameResolution.Known([name])
                : FieldMapNameResolution.Unknown,
            () => "Base of Fort Condor");

        var hill = resolver.Resolve(
        [
            Exit(353, "gateway:353:0:354", 354),
            Exit(353, "gateway:353:1:6", 6)
        ]);
        Equal("Way up to the fort entrance", hill[0].Label, "the climb to the entrance is named");
        Equal(
            "Leave Fort Condor for the world map",
            hill[1].Label,
            "the world-map mouth must not be announced as a bare Exit");

        var fort = resolver.Resolve(
        [
            Exit(355, "gateway:355:0:356", 356),
            Exit(355, "script-exit:355:3:354", 354),
            Exit(355, "script-exit:355:4:354", 354)
        ]);
        Equal("Way up to the Watch Room", fort[0].Label, "the watch room route is named");
        Equal(
            "Ladder down to the fort entrance",
            fort[1].Label,
            "the verified climb down is described as a ladder");
        Equal(
            2,
            fort.Select(target => target.Label).Distinct().Count(),
            "convil_1's two approaches onto one ladder share a label rather than inventing a second route");

        var watch = resolver.Resolve([Exit(356, "script-exit:356:5:358", 358)]);
        Equal(
            "Way up to the top of the mountain",
            watch[0].Label,
            "the route to the mountain top is named");
    }

    private static FieldNavigationTarget Exit(int fieldId, string stableId, int destination) =>
        new(
            fieldId,
            FieldNavigationCategory.Exits,
            "Exit",
            0,
            0,
            0,
            stableId,
            DestinationFieldIds: [destination]);

    private static void Setup(Dictionary<int, byte> memory, byte numModels)
    {
        WriteUInt32(memory, FieldNavigationObjectReader.AddressFieldEventDataPtr, EventTable);
        memory[FieldPositionReader.AddressFieldNumModels] = numModels;
    }

    private static void PlaceModel(
        Dictionary<int, byte> memory, int entityId, byte modelId, int x, int y, int z)
    {
        var record = EventTable + (modelId * FieldNavigationObjectReader.FieldEventDataStride);
        memory[FieldNavigationObjectReader.AddressFieldModelIdArray + entityId] = modelId;
        memory[record + FieldNavigationObjectReader.VisibilityOffset] = 1;
        memory[record + FieldNavigationNpcReader.TalkDisabledOffset] = 0;
        WriteUInt32(
            memory,
            record + FieldNavigationObjectReader.PositionXOffset,
            unchecked((uint)(x * FieldNavigationObjectReader.ModelPositionFixedPointScale)));
        WriteUInt32(
            memory,
            record + FieldNavigationObjectReader.PositionYOffset,
            unchecked((uint)(y * FieldNavigationObjectReader.ModelPositionFixedPointScale)));
        WriteUInt32(
            memory,
            record + FieldNavigationObjectReader.PositionZOffset,
            unchecked((uint)(z * FieldNavigationObjectReader.ModelPositionFixedPointScale)));
    }

    private static void WriteUInt32(Dictionary<int, byte> memory, int address, uint value)
    {
        memory[address] = (byte)(value & 0xFF);
        memory[address + 1] = (byte)((value >> 8) & 0xFF);
        memory[address + 2] = (byte)((value >> 16) & 0xFF);
        memory[address + 3] = (byte)((value >> 24) & 0xFF);
    }

    private static byte ReadByte(Dictionary<int, byte> memory, int address) =>
        memory.TryGetValue(address, out var value) ? value : (byte)0;

    private static int ReadInt32(Dictionary<int, byte> memory, int address) =>
        ReadByte(memory, address) |
        (ReadByte(memory, address + 1) << 8) |
        (ReadByte(memory, address + 2) << 16) |
        (ReadByte(memory, address + 3) << 24);

    private static short ReadInt16(Dictionary<int, byte> memory, int address) =>
        unchecked((short)(ReadByte(memory, address) | (ReadByte(memory, address + 1) << 8)));

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
