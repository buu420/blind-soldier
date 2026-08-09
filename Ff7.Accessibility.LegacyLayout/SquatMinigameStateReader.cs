using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public enum SquatMinigameStep : byte
{
    Switch = 0,
    Cancel = 1,
    Ok = 2
}

public readonly record struct SquatMinigameSnapshot(
    bool IsActive,
    SquatMinigameStep ExpectedStep,
    byte CompletedSquats)
{
    public static SquatMinigameSnapshot Inactive { get; } =
        new(false, SquatMinigameStep.Switch, 0);
}

/// <summary>
/// Reads the exact native state used by the Wall Market gym's Cloud script.
/// The field script stores its current Switch, Cancel, OK step in temporary
/// bank 5, index 6 while Cloud entity 4 owns controller script 6.
/// </summary>
public sealed class SquatMinigameStateReader
{
    public const byte FieldModule = 1;
    public const ushort GymFieldId = 197;
    public const byte CloudEntityId = 4;
    public const byte ControllerScriptId = 6;
    public const int ScriptSlotsPerEntity = 8;
    public const uint FieldScriptEntityCountOffset = 2;

    public const int AddressCurrentModule = 0x00CBF9DC;
    public const int AddressCurrentFieldId = 0x00CC15D0;
    public const int AddressFieldScriptPointer = 0x00CBF5E8;
    public const int AddressEntityScriptIds = 0x00CBF9E8;
    public const int AddressEntityScriptPriorities = 0x00CC0B30;
    public const int AddressTemporaryFieldBank = 0x00CC14D0;
    public const int AddressCompletedSquats = AddressTemporaryFieldBank + 3;
    public const int AddressExpectedStep = AddressTemporaryFieldBank + 6;

    private readonly ILegacyAddressSpace memory;

    public SquatMinigameStateReader(ILegacyAddressSpace memory)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    public bool TryRead(out SquatMinigameSnapshot snapshot)
    {
        snapshot = default;
        if (!TryCapture(out var before) ||
            !TryCapture(out var after) ||
            before != after)
        {
            return false;
        }

        var active = before.Module == FieldModule &&
            before.FieldId == GymFieldId &&
            before.ScriptPointer != 0 &&
            before.EntityCount > CloudEntityId &&
            before.Priority < ScriptSlotsPerEntity &&
            before.ScriptId == ControllerScriptId;
        if (!active)
        {
            snapshot = SquatMinigameSnapshot.Inactive;
            return true;
        }

        if (before.ExpectedStep > (byte)SquatMinigameStep.Ok)
        {
            return false;
        }

        snapshot = new SquatMinigameSnapshot(
            true,
            (SquatMinigameStep)before.ExpectedStep,
            before.CompletedSquats);
        return true;
    }

    private bool TryCapture(out Capture capture)
    {
        capture = default;
        if (!memory.TryReadByte((uint)AddressCurrentModule, out var module) ||
            !memory.TryReadUInt16((uint)AddressCurrentFieldId, out var fieldId))
        {
            return false;
        }

        if (module != FieldModule || fieldId != GymFieldId)
        {
            capture = new Capture(module, fieldId, 0, 0, byte.MaxValue, byte.MaxValue, 0, 0);
            return true;
        }

        if (!memory.TryReadUInt32((uint)AddressFieldScriptPointer, out var scriptPointer) ||
            !memory.TryReadByte(
                (uint)(AddressEntityScriptPriorities + CloudEntityId),
                out var priority) ||
            !memory.TryReadByte((uint)AddressExpectedStep, out var expectedStep) ||
            !memory.TryReadByte((uint)AddressCompletedSquats, out var completedSquats))
        {
            return false;
        }

        var entityCount = (byte)0;
        if (scriptPointer != 0)
        {
            if (scriptPointer > uint.MaxValue - FieldScriptEntityCountOffset ||
                !memory.TryReadByte(scriptPointer + FieldScriptEntityCountOffset, out entityCount))
            {
                return false;
            }
        }

        var scriptId = byte.MaxValue;
        if (priority < ScriptSlotsPerEntity &&
            !memory.TryReadByte(
                (uint)(AddressEntityScriptIds +
                    CloudEntityId * ScriptSlotsPerEntity + priority),
                out scriptId))
        {
            return false;
        }

        capture = new Capture(
            module,
            fieldId,
            scriptPointer,
            entityCount,
            priority,
            scriptId,
            expectedStep,
            completedSquats);
        return true;
    }

    private readonly record struct Capture(
        byte Module,
        ushort FieldId,
        uint ScriptPointer,
        byte EntityCount,
        byte Priority,
        byte ScriptId,
        byte ExpectedStep,
        byte CompletedSquats);
}

public sealed class SquatMinigamePromptTracker
{
    private bool active;
    private SquatMinigameStep lastStep;

    public string? Observe(SquatMinigameSnapshot snapshot)
    {
        if (!snapshot.IsActive)
        {
            Reset();
            return null;
        }

        if (active && snapshot.ExpectedStep == lastStep)
        {
            return null;
        }

        active = true;
        lastStep = snapshot.ExpectedStep;
        return snapshot.ExpectedStep switch
        {
            SquatMinigameStep.Switch => "Switch",
            SquatMinigameStep.Cancel => "Cancel",
            SquatMinigameStep.Ok => "OK",
            _ => null
        };
    }

    public void Reset()
    {
        active = false;
        lastStep = default;
    }
}

public sealed class SquatMinigameCueCoordinator
{
    private readonly SquatMinigameStateReader reader;
    private readonly SquatMinigamePromptTracker tracker;

    public SquatMinigameCueCoordinator(
        SquatMinigameStateReader reader,
        SquatMinigamePromptTracker? tracker = null)
    {
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
        this.tracker = tracker ?? new SquatMinigamePromptTracker();
    }

    public string? Observe()
    {
        // An unreadable or torn frame is silence, not an implicit reset. This
        // prevents a transient translation miss from repeating a stale cue.
        return reader.TryRead(out var snapshot)
            ? tracker.Observe(snapshot)
            : null;
    }

    public void Reset() => tracker.Reset();
}
