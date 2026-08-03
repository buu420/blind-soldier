using System.Collections.Immutable;

namespace Ff7.Accessibility.Runtime.Abstractions;

public enum NavigationTargetKind
{
    Exit,
    Story,
    Npc,
    Item,
    Materia,
    Chest,
    SavePoint,
    Ladder
}

public sealed record NavigationTargetObservation(
    string StableId,
    NavigationTargetKind Kind,
    int NativeEntityId,
    float X,
    float Y,
    float Z,
    bool IsAvailable,
    string VerifiedLabelKey);

public sealed record NavigationWorldObservation
{
    public NavigationWorldObservation(
        int fieldId,
        int revision,
        FieldFrameObservation player,
        IEnumerable<NavigationTargetObservation> targets,
        IEnumerable<WalkmeshTriangleObservation> walkmesh)
    {
        FieldId = fieldId;
        Revision = revision;
        Player = player;
        Targets = RuntimeObservationCollections.Copy(targets, nameof(targets));
        Walkmesh = RuntimeObservationCollections.Copy(walkmesh, nameof(walkmesh));
    }

    public int FieldId { get; }

    public int Revision { get; }

    public FieldFrameObservation Player { get; }

    public ImmutableArray<NavigationTargetObservation> Targets { get; }

    public ImmutableArray<WalkmeshTriangleObservation> Walkmesh { get; }
}

public sealed record WalkmeshTriangleObservation(
    int TriangleId,
    int Neighbor0,
    int Neighbor1,
    int Neighbor2,
    float CenterX,
    float CenterY,
    float CenterZ,
    bool Traversable);
