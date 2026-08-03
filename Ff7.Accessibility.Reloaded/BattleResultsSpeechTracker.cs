namespace Ff7.Accessibility.Reloaded;

public sealed class BattleResultsSpeechTracker
{
    private const int DirectionInputMask = 0x1000 | 0x4000;
    private const int ConfirmInputMask = 0x0800 | 0x0020;
    private const int DirectionRepeatMask = 0x0001 | 0x0002;
    private const uint LimitNameX = 264;
    private const uint LimitLabelX = 329;
    private const uint MateriaNameX = 291;
    private const uint MateriaLabelX = 344;
    private const uint ProgressLabelYOffset = 28;
    private readonly object sync = new();
    private readonly Dictionary<int, BattlePartyProgressSnapshot> baselineByCharacter = new();
    private readonly HashSet<int> spokenLevelChanges = new();
    private readonly HashSet<string> spokenNotifications = new(StringComparer.Ordinal);
    private readonly List<MenuTextRenderEntry> frameEntries = new();
    private readonly Queue<BattleResultsPendingSpeech> pending = new();
    private bool inResults;
    private bool experiencePageQueued;
    private bool rewardPageQueued;
    private bool rewardsCaptured;
    private int capturedGil;
    private IReadOnlyList<BattleRewardItemSnapshot> capturedItems = [];
    private int? lastRewardSelection;
    private IReadOnlyDictionary<int, bool> lastRewardDisposition =
        new Dictionary<int, bool>();
    private bool rewardInputArmed;
    private bool victorySignalActive;
    private bool frameActive;

    public void ObserveBattleProgress(IReadOnlyList<BattlePartyProgressSnapshot> progress)
    {
        lock (sync)
        {
            if (inResults)
            {
                inResults = false;
                experiencePageQueued = false;
                rewardPageQueued = false;
                rewardsCaptured = false;
                capturedGil = 0;
                capturedItems = [];
                lastRewardSelection = null;
                lastRewardDisposition = new Dictionary<int, bool>();
                rewardInputArmed = false;
                victorySignalActive = false;
                frameActive = false;
                spokenLevelChanges.Clear();
                spokenNotifications.Clear();
                frameEntries.Clear();
                pending.Clear();
            }

            baselineByCharacter.Clear();
            foreach (var member in progress)
            {
                baselineByCharacter[member.CharacterId] = member;
            }
        }
    }

    public void ObserveVictorySignal(bool isVictory)
    {
        lock (sync)
        {
            if (isVictory && !victorySignalActive)
            {
                Enqueue(
                    "Victory. The party strikes victory poses.",
                    interrupt: true);
            }

            victorySignalActive = isVictory;
        }
    }

    public void ObserveResults(
        BattleResultsSnapshot results,
        IReadOnlyList<BattlePartyProgressSnapshot> currentProgress)
    {
        if (!results.IsValid)
        {
            return;
        }

        lock (sync)
        {
            inResults = true;
            if (!rewardsCaptured)
            {
                rewardsCaptured = true;
                capturedGil = results.Gil;
                capturedItems = results.Items.ToArray();
            }

            if (!experiencePageQueued && results.State == 0 && results.IsPageReady)
            {
                experiencePageQueued = true;
                Enqueue($"{results.Experience} experience. {results.Ap} AP.");
            }

            if (results.State == 2 && results.RewardTransition == 0)
            {
                ObserveRewardPage(results);
            }

            if (experiencePageQueued)
            {
                QueueNewLevelChanges(currentProgress);
            }
        }
    }

    public void BeginFrame()
    {
        lock (sync)
        {
            frameEntries.Clear();
            frameActive = true;
        }
    }

    public void ObserveDraw(MenuTextRenderEntry entry)
    {
        var text = entry.Text.Trim();
        if (text.Length == 0 || !text.Any(char.IsLetterOrDigit))
        {
            return;
        }

        lock (sync)
        {
            if (inResults && frameActive)
            {
                frameEntries.Add(entry with { Text = text });
            }
        }
    }

    public void CompleteFrame()
    {
        lock (sync)
        {
            if (!frameActive)
            {
                return;
            }

            frameActive = false;
            if (inResults)
            {
                QueueFrameNotifications();
            }

            frameEntries.Clear();
        }
    }

    public string? Poll()
    {
        return PollSpeech()?.Text;
    }

    public BattleResultsPendingSpeech? PollSpeech()
    {
        lock (sync)
        {
            return pending.Count == 0 ? null : pending.Dequeue();
        }
    }

    public void Reset()
    {
        lock (sync)
        {
            inResults = false;
            experiencePageQueued = false;
            rewardPageQueued = false;
            rewardsCaptured = false;
            capturedGil = 0;
            capturedItems = [];
            lastRewardSelection = null;
            lastRewardDisposition = new Dictionary<int, bool>();
            rewardInputArmed = false;
            victorySignalActive = false;
            frameActive = false;
            baselineByCharacter.Clear();
            spokenLevelChanges.Clear();
            spokenNotifications.Clear();
            frameEntries.Clear();
            pending.Clear();
        }
    }

    private void ObserveRewardPage(BattleResultsSnapshot results)
    {
        if (!rewardPageQueued)
        {
            rewardPageQueued = true;
            lastRewardSelection = results.RewardSelection;
            lastRewardDisposition = CaptureRewardDisposition(results.Items);
            if (!results.HasRewardItems)
            {
                Enqueue($"{capturedGil} gil. No items.");
                return;
            }

            var available = capturedItems.Count > 0 ? capturedItems : results.Items;
            var summary =
                $"{capturedGil} gil. Items available: {FormatItems(available)}. " +
                $"Items selected: {FormatSelectedItems(results.Items)}.";
            rewardInputArmed = IsRewardInputNeutral(results);
            if (rewardInputArmed)
            {
                var focus = BuildRewardFocus(results);
                if (!string.IsNullOrWhiteSpace(focus))
                {
                    summary += $" {focus}";
                }
            }

            Enqueue(summary);
            return;
        }

        if (!results.HasRewardItems)
        {
            return;
        }

        var disposition = CaptureRewardDisposition(results.Items);
        var selectionChanged = lastRewardSelection != results.RewardSelection;
        var dispositionChanged = !RewardDispositionEquals(lastRewardDisposition, disposition);
        if (!rewardInputArmed)
        {
            lastRewardSelection = results.RewardSelection;
            lastRewardDisposition = disposition;
            if (IsRewardInputNeutral(results))
            {
                rewardInputArmed = true;
                Enqueue(BuildRewardFocus(results));
            }

            return;
        }

        if (dispositionChanged && selectionChanged)
        {
            var focus = BuildRewardFocus(results);
            Enqueue(
                $"Items selected: {FormatSelectedItems(results.Items)}." +
                (string.IsNullOrWhiteSpace(focus) ? string.Empty : $" {focus}"),
                interrupt: true);
        }
        else if (dispositionChanged)
        {
            var selectedItem = FindSelectedReward(results);
            Enqueue(selectedItem is { } item
                ? $"{FormatItem(item)}. " +
                  (item.IsSelectedToTake ? "Selected to take." : "Not selected.")
                : $"Items selected: {FormatSelectedItems(results.Items)}.",
                interrupt: true);
        }
        else if (selectionChanged && HasDirectionalInput(results))
        {
            Enqueue(BuildRewardFocus(results), interrupt: true);
        }

        lastRewardSelection = results.RewardSelection;
        lastRewardDisposition = disposition;
    }

    private static bool IsRewardInputNeutral(BattleResultsSnapshot results) =>
        (results.InputEdges & DirectionInputMask) == 0 &&
        (results.InputRepeat & DirectionRepeatMask) == 0 &&
        (results.HeldInput & (DirectionInputMask | ConfirmInputMask)) == 0;

    private static bool HasDirectionalInput(BattleResultsSnapshot results) =>
        (results.InputEdges & DirectionInputMask) != 0 ||
        (results.InputRepeat & DirectionRepeatMask) != 0;

    private static string BuildRewardFocus(BattleResultsSnapshot results)
    {
        if (results.RewardSelection == 0)
        {
            return "Take everything.";
        }

        if (results.RewardSelection == 5)
        {
            return "Exit.";
        }

        var item = FindSelectedReward(results);
        return item is { } reward
            ? $"{FormatItem(reward)}. " +
              (reward.IsSelectedToTake ? "Selected to take." : "Not selected.")
            : string.Empty;
    }

    private static BattleRewardItemSnapshot? FindSelectedReward(BattleResultsSnapshot results) =>
        results.Items.FirstOrDefault(item =>
            item.PhysicalSlot == results.RewardSelection - 1) is { Name.Length: > 0 } item
                ? item
                : null;

    private static IReadOnlyDictionary<int, bool> CaptureRewardDisposition(
        IReadOnlyList<BattleRewardItemSnapshot> items) =>
        items.ToDictionary(item => item.PhysicalSlot, item => item.IsSelectedToTake);

    private static bool RewardDispositionEquals(
        IReadOnlyDictionary<int, bool> left,
        IReadOnlyDictionary<int, bool> right) =>
        left.Count == right.Count &&
        left.All(entry => right.TryGetValue(entry.Key, out var selected) && selected == entry.Value);

    private static string FormatSelectedItems(IReadOnlyList<BattleRewardItemSnapshot> items)
    {
        var selected = items.Where(item => item.IsSelectedToTake).ToArray();
        return selected.Length == 0 ? "none" : FormatItems(selected);
    }

    private static string FormatItems(IEnumerable<BattleRewardItemSnapshot> items) =>
        string.Join(", ", items.Select(FormatItem));

    private static string FormatItem(BattleRewardItemSnapshot item) =>
        $"{item.Name} x{item.Quantity}";

    private void QueueNewLevelChanges(IReadOnlyList<BattlePartyProgressSnapshot> currentProgress)
    {
        foreach (var member in currentProgress)
        {
            if (baselineByCharacter.TryGetValue(member.CharacterId, out var previous) &&
                member.Level > previous.Level &&
                spokenLevelChanges.Add(member.CharacterId))
            {
                Enqueue($"{member.Name} reached level {member.Level}.");
            }
        }
    }

    private void QueueFrameNotifications()
    {
        foreach (var label in frameEntries)
        {
            var name = FindNativeProgressName(label);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var speech = $"{name}. {label.Text}";
            if (spokenNotifications.Add(speech))
            {
                Enqueue(speech);
            }
        }
    }

    private void Enqueue(string text, bool interrupt = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (interrupt && pending.Any(candidate => candidate.Interrupt))
        {
            var retained = pending.Where(candidate => !candidate.Interrupt).ToArray();
            pending.Clear();
            foreach (var candidate in retained)
            {
                pending.Enqueue(candidate);
            }
        }

        pending.Enqueue(new BattleResultsPendingSpeech(text.Trim(), interrupt));
    }

    private string? FindNativeProgressName(MenuTextRenderEntry label)
    {
        uint nameX;
        if (label.X == LimitLabelX)
        {
            nameX = LimitNameX;
        }
        else if (label.X == MateriaLabelX)
        {
            nameX = MateriaNameX;
        }
        else
        {
            return null;
        }

        if (label.Y < ProgressLabelYOffset)
        {
            return null;
        }

        var nameY = label.Y - ProgressLabelYOffset;
        foreach (var candidate in frameEntries)
        {
            if (candidate.X == nameX &&
                candidate.Y == nameY &&
                candidate.Context == label.Context &&
                candidate.Text.Length > 0 &&
                candidate.Text.Any(char.IsLetterOrDigit))
            {
                return candidate.Text;
            }
        }

        return null;
    }
}

public readonly record struct BattleResultsPendingSpeech(string Text, bool Interrupt);
