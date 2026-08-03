namespace Ff7.Accessibility.Reloaded;

public enum FieldNavigationCategory
{
    Exits,
    Story,
    Npcs,
    Objects
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
    IReadOnlyList<FieldNavigationRouteDetour>? RouteDetours = null);
