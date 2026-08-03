namespace Ff7.Accessibility.Runtime.Abstractions;

public sealed record RuntimeFrameObservation(
    DateTime TimestampUtc,
    GameLifecycleObservation Lifecycle,
    RuntimeDomainUpdate<MenuFrameObservation> Menu,
    RuntimeDomainUpdate<DialoguePageObservation> Dialogue,
    RuntimeDomainUpdate<FieldFrameObservation> Field,
    RuntimeDomainUpdate<BattleFrameObservation> Battle,
    RuntimeDomainUpdate<NavigationWorldObservation> Navigation);
