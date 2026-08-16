using System.Collections.Immutable;

namespace Ff7.Accessibility.Runtime.Abstractions;

public sealed record BattleFrameObservation
{
    public BattleFrameObservation(
        bool isActive,
        int revision,
        int readyActorId,
        int commandId,
        int abilityId,
        int itemId,
        uint allyTargetMask,
        uint enemyTargetMask,
        IEnumerable<BattleActorObservation> actors)
    {
        IsActive = isActive;
        Revision = revision;
        ReadyActorId = readyActorId;
        CommandId = commandId;
        AbilityId = abilityId;
        ItemId = itemId;
        AllyTargetMask = allyTargetMask;
        EnemyTargetMask = enemyTargetMask;
        Actors = RuntimeObservationCollections.Copy(actors, nameof(actors));
    }

    public bool IsActive { get; }

    public int Revision { get; }

    public int ReadyActorId { get; }

    public int CommandId { get; }

    public int AbilityId { get; }

    public int ItemId { get; }

    public uint AllyTargetMask { get; }

    public uint EnemyTargetMask { get; }

    public ImmutableArray<BattleActorObservation> Actors { get; }
}

public sealed record BattleActorObservation
{
    private bool isEnemy;
    private bool isSensed;
    private int currentHp;
    private int maximumHp;
    private int currentMp;
    private int maximumMp;
    private uint statusMask;

    public BattleActorObservation(
        int actorId,
        bool isEnemy,
        bool isActive,
        bool isSensed,
        int currentHp,
        int maximumHp,
        int currentMp,
        int maximumMp,
        uint statusMask)
    {
        ActorId = actorId;
        IsActive = isActive;
        this.isEnemy = isEnemy;
        this.isSensed = isSensed;
        if (CanExposeDetails)
        {
            this.currentHp = currentHp;
            this.maximumHp = maximumHp;
            this.currentMp = currentMp;
            this.maximumMp = maximumMp;
            this.statusMask = statusMask;
        }
    }

    public int ActorId { get; init; }

    public bool IsEnemy
    {
        get => isEnemy;
        init
        {
            isEnemy = value;
            RedactPrivateEnemyDetails();
        }
    }

    public bool IsActive { get; init; }

    public bool IsSensed
    {
        get => isSensed;
        init
        {
            isSensed = value;
            RedactPrivateEnemyDetails();
        }
    }

    public int CurrentHp
    {
        get => currentHp;
        init => currentHp = CanExposeDetails ? value : 0;
    }

    public int MaximumHp
    {
        get => maximumHp;
        init => maximumHp = CanExposeDetails ? value : 0;
    }

    public int CurrentMp
    {
        get => currentMp;
        init => currentMp = CanExposeDetails ? value : 0;
    }

    public int MaximumMp
    {
        get => maximumMp;
        init => maximumMp = CanExposeDetails ? value : 0;
    }

    public uint StatusMask
    {
        get => statusMask;
        init => statusMask = CanExposeDetails ? value : 0;
    }

    public void Deconstruct(
        out int actorId,
        out bool isEnemy,
        out bool isActive,
        out bool isSensed,
        out int currentHp,
        out int maximumHp,
        out int currentMp,
        out int maximumMp,
        out uint statusMask)
    {
        actorId = ActorId;
        isEnemy = IsEnemy;
        isActive = IsActive;
        isSensed = IsSensed;
        currentHp = CurrentHp;
        maximumHp = MaximumHp;
        currentMp = CurrentMp;
        maximumMp = MaximumMp;
        statusMask = StatusMask;
    }

    private bool CanExposeDetails => !isEnemy || isSensed;

    private void RedactPrivateEnemyDetails()
    {
        if (CanExposeDetails)
        {
            return;
        }

        currentHp = 0;
        maximumHp = 0;
        currentMp = 0;
        maximumMp = 0;
        statusMask = 0;
    }
}

public sealed record BattleSenseObservation
{
    public BattleSenseObservation(
        int actorId,
        string name,
        bool isEnemy,
        bool isSensed,
        int level,
        int currentHp,
        int maximumHp,
        int currentMp,
        int maximumMp,
        IEnumerable<int> weaknessElementIds)
    {
        ActorId = actorId;
        Name = name ?? string.Empty;
        IsEnemy = isEnemy;
        IsSensed = isSensed;
        if (!isEnemy || isSensed)
        {
            Level = level;
            CurrentHp = currentHp;
            MaximumHp = maximumHp;
            CurrentMp = currentMp;
            MaximumMp = maximumMp;
            WeaknessElementIds = RuntimeObservationCollections.Copy(
                weaknessElementIds,
                nameof(weaknessElementIds));
        }
        else
        {
            WeaknessElementIds = [];
        }
    }

    public int ActorId { get; }

    public string Name { get; }

    public bool IsEnemy { get; }

    public bool IsSensed { get; }

    public int? Level { get; }

    public int? CurrentHp { get; }

    public int? MaximumHp { get; }

    public int? CurrentMp { get; }

    public int? MaximumMp { get; }

    public ImmutableArray<int> WeaknessElementIds { get; }
}
