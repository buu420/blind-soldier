namespace Ff7.Accessibility.Reloaded;

public sealed class StatusMenuSpeechTracker
{
    private const int RootMainMenuContext = 0x3A83126F;
    private const int ConfigContext = 0x3DCCCCCD;
    private static readonly TimeSpan ScreenEvidenceWindow = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan NewScreenGap = TimeSpan.FromMilliseconds(250);

    private readonly TimeSpan settleTime;
    private readonly object sync = new();
    private DateTime lastTitleAt = DateTime.MinValue;
    private DateTime lastDetailsAt = DateTime.MinValue;
    private int generation;
    private PendingStatus? pending;
    private string lastSpokenKey = string.Empty;

    public StatusMenuSpeechTracker(TimeSpan settleTime)
    {
        this.settleTime = settleTime < TimeSpan.Zero ? TimeSpan.Zero : settleTime;
    }

    public void ObserveDraw(MenuTextRenderEntry entry, DateTime now)
    {
        lock (sync)
        {
            if (IsStatusTitle(entry))
            {
                if (lastTitleAt == DateTime.MinValue || now - lastTitleAt > NewScreenGap)
                {
                    generation++;
                    pending = null;
                }

                lastTitleAt = now;
                return;
            }

            if (!IsStatusDetailsSignal(entry) || !IsRecent(lastTitleAt, now))
            {
                return;
            }

            lastDetailsAt = now;
            if (pending is not { Generation: var pendingGeneration } || pendingGeneration != generation)
            {
                pending = new PendingStatus(generation, now);
            }
        }
    }

    public string? Poll(DateTime now, Func<StatusMenuSnapshot?> readSnapshot)
    {
        lock (sync)
        {
            if (pending is not { } current ||
                now - current.SeenAt < settleTime ||
                !IsRecent(lastTitleAt, now) ||
                !IsRecent(lastDetailsAt, now))
            {
                return null;
            }

            var snapshot = readSnapshot();
            if (snapshot is not { } status || string.IsNullOrWhiteSpace(status.Name))
            {
                return null;
            }

            var speech = Format(status);
            var key = $"{current.Generation}\u001f{status.PartySlot}\u001f{speech}";
            if (string.Equals(key, lastSpokenKey, StringComparison.Ordinal))
            {
                return null;
            }

            lastSpokenKey = key;
            return speech;
        }
    }

    public void DiscardPending()
    {
        lock (sync)
        {
            pending = null;
            lastTitleAt = DateTime.MinValue;
            lastDetailsAt = DateTime.MinValue;
        }
    }

    public static string Format(StatusMenuSnapshot status)
    {
        var parts = new List<string>
        {
            status.Name,
            $"Level {status.Level}",
            $"HP {status.CurrentHp} of {status.MaxHp}",
            $"MP {status.CurrentMp} of {status.MaxMp}",
            $"Strength {status.Strength}",
            $"Dexterity {status.Dexterity}",
            $"Vitality {status.Vitality}",
            $"Magic {status.Magic}",
            $"Spirit {status.Spirit}",
            $"Luck {status.Luck}",
            $"Attack {status.Attack}",
            $"Attack percent {status.AttackPercent}",
            $"Defense {status.Defense}",
            $"Defense percent {status.DefensePercent}",
            $"Magic attack {status.MagicAttack}",
            $"Magic defense {status.MagicDefense}",
            $"Magic defense percent {status.MagicDefensePercent}"
        };

        AddEquipment(parts, "Weapon", status.WeaponName);
        AddEquipment(parts, "Armor", status.ArmorName);
        AddEquipment(parts, "Accessory", status.AccessoryName);
        parts.Add($"Experience {status.Experience}");
        parts.Add($"Next level {status.ExperienceToNextLevel}");
        parts.Add($"Limit level {status.LimitLevel}");
        return string.Join(". ", parts);
    }

    private static void AddEquipment(List<string> parts, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{label} {value}");
        }
    }

    private static bool IsRecent(DateTime seenAt, DateTime now) =>
        seenAt != DateTime.MinValue && now - seenAt <= ScreenEvidenceWindow;

    private static bool IsStatusTitle(MenuTextRenderEntry entry) =>
        entry.Context == RootMainMenuContext &&
        entry.X == 508 &&
        entry.Y <= 20 &&
        string.Equals(entry.Text, "Status", StringComparison.OrdinalIgnoreCase);

    private static bool IsStatusDetailsSignal(MenuTextRenderEntry entry) =>
        entry.Context == ConfigContext &&
        entry.X is >= 50 and <= 70 &&
        entry.Y is >= 110 and <= 130 &&
        string.Equals(entry.Text, "Strength", StringComparison.OrdinalIgnoreCase);

    private readonly record struct PendingStatus(int Generation, DateTime SeenAt);
}
