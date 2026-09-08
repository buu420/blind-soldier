namespace Ff7.Accessibility.Reloaded;

public enum FieldNavigationCategory
{
    Exits,
    Story,
    Npcs,
    Objects
}

/// <summary>
/// How the native field engine turns standing next to something into the thing
/// happening. Talk is a Confirm press against the model's own talk radius; Contact
/// is the collision test FUN_00637724 runs while the party is walking, and fires
/// from walking into the model rather than from any button at all.
/// </summary>
public enum FieldNavigationActivation
{
    Default,
    Talk,
    Contact
}

public enum FieldObjectCueKind
{
    None,
    Materia,
    Chest,
    Item
}

public readonly record struct FieldNavigationTriggerLine(
    int StartX,
    int StartY,
    int StartZ,
    int EndX,
    int EndY,
    int EndZ);

public readonly record struct FieldNavigationRouteDetour(
    FieldNavigationTriggerLine BlockedLine,
    int X,
    int Y,
    int Z,
    int Clearance = 0);

public readonly record struct FieldNavigationTarget(
    int FieldId,
    FieldNavigationCategory Category,
    string Label,
    int X,
    int Y,
    int Z,
    string StableId = "",
    FieldObjectCueKind ObjectCueKind = FieldObjectCueKind.None,
    int TriggerEntityId = -1,
    bool CompletesOnArrival = false,
    int InteractionRadius = 0,
    IReadOnlyList<int>? DestinationFieldIds = null,
    FieldNavigationTriggerLine? TriggerLine = null,
    FieldNavigationRouteDetour? RouteDetour = null,
    IReadOnlyList<FieldNavigationRouteDetour>? RouteDetours = null,
    string? ManualNavigationGuidance = null,
    // Some native activations are neither a trigger line nor a gateway: the script
    // polls the party leader's walkmesh triangle and fires when it enters a set of
    // triangles. Those targets are reached only by standing on one of them, so a
    // proximity radius can release auto walk while the player is still outside.
    IReadOnlyList<int>? CompletionTriangles = null,
    FieldNavigationActivation Activation = FieldNavigationActivation.Default);
