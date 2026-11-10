namespace Ff7.Accessibility.Reloaded;

public sealed class BattleResultsSpeechTracker
{
    private const int DirectionInputMask = 0x1000 | 0x4000;
    private const int ConfirmInputMask = 0x0800 | 0x0020;
    private const int DirectionRepeatMask = 0x0001 | 0x0002;

    // The 640x480 EXP page (Ghidra: legacy ff7_en.exe; the Steam 2026 translation
    // keeps these constants). FUN_006c7b54 draws party row s at y = 120 * (s + 1),
    // the row's character at (0x8D, row + 0x18) and a notice box at (0xFB, row + 9).
    // FUN_006c8643 puts a levelled Materia at box + (0x28, 0x0B) over "LEVEL UP" at
    // box + (0x5D, 0x27); FUN_006c87bb puts a learned Limit at box + (0x0D, 0x0B)
    // over battle text 0x24 at box + (0x4E, 0x27). FUN_006c86f6 puts a newly born
    // Materia at (0x100, 0x25) over battle text 0x25 at (0x10D, 0x41).
    private const uint LimitNameX = 264;
    private const uint LimitLabelX = 329;
    private const uint MateriaNameX = 291;
    private const uint MateriaLabelX = 344;
    private const uint BornNameX = 256;
    private const uint BornLabelX = 269;
    private const uint RowCharacterX = 141;
    private const uint ProgressLabelYOffset = 28;
    private const uint RowCharacterLabelYOffset = 24;

    // A row's notice is drawn on 0x50 updates (DAT_00DC12A2 and DAT_00DC12A5) and its
    // next notice on the update after; a born notice on 0x96 updates (DAT_00DC121C).
    private const int RowNoticeUpdates = 0x50;
    private const int BornNoticeUpdates = 0x96;
    private readonly object sync = new();
    private readonly Dictionary<int, BattlePartyProgressSnapshot> baselineByCharacter = new();
    private readonly HashSet<int> spokenLevelChanges = new();
    private readonly Dictionary<(string? Character, int Row), PresentedNotice> presentedNotices = new();
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
                presentedNotices.Clear();
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
            presentedNotices.Clear();
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

    /// <summary>
    /// One announcement per presented notice. A notice belongs to its row's character
    /// (or to the single born box) and stays the same presentation while that channel
    /// keeps drawing it, through slides and an unseen update, until its native timer
    /// has run out: the row's next identical notice then starts a new presentation.
    /// </summary>
    private void QueueFrameNotifications()
    {
        foreach (var label in frameEntries)
        {
            if (!TryReadNativeNotice(label, out var channel, out var notice, out var lifetime))
            {
                continue;
            }

            if (presentedNotices.TryGetValue(channel, out var presented) &&
                string.Equals(presented.Notice, notice, StringComparison.Ordinal) &&
                presented.Updates < lifetime)
            {
                presentedNotices[channel] = presented with { Updates = presented.Updates + 1 };
                continue;
            }

            presentedNotices[channel] = new PresentedNotice(notice, 1);
            Enqueue(notice);
        }
    }

    private bool TryReadNativeNotice(
        MenuTextRenderEntry label,
        out (string? Character, int Row) channel,
        out string notice,
        out int lifetime)
    {
        channel = default;
        notice = string.Empty;
        lifetime = 0;
        uint nameX;
        var isRowNotice = true;
        if (label.X == LimitLabelX)
        {
            nameX = LimitNameX;
        }
        else if (label.X == MateriaLabelX)
        {
            nameX = MateriaNameX;
        }
        else if (label.X == BornLabelX)
        {
            nameX = BornNameX;
            isRowNotice = false;
        }
        else
        {
            return false;
        }

        if (label.Y < ProgressLabelYOffset ||
            FindFrameText(nameX, label.Y - ProgressLabelYOffset, label.Context) is not { } name)
        {
            return false;
        }

        var shown = JoinNotice(name, label.Text);
        if (!isRowNotice)
        {
            notice = shown;
            lifetime = BornNoticeUpdates;
            return true;
        }

        var characterY = label.Y - RowCharacterLabelYOffset;
        if (FindFrameText(RowCharacterX, characterY, label.Context) is not { } character)
        {
            return false;
        }

        // Character names are editable. Use their order within the drawn page to
        // distinguish equal names while keeping each identity through the page slide.
        var namesAbove = frameEntries
            .Where(entry => entry.X == RowCharacterX && entry.Y < characterY &&
                entry.Context == label.Context && entry.Text == character)
            .Select(entry => entry.Y)
            .Distinct()
            .Count();
        channel = (character, namesAbove);
        notice = $"{character}. {shown}";
        lifetime = RowNoticeUpdates;
        return true;
    }

    // " gained." and " was born." continue the name drawn above them.
    private static string JoinNotice(string name, string label) =>
        char.IsLower(label[0]) ? $"{name} {label}" : $"{name}. {label}";

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

    private string? FindFrameText(uint x, uint y, int context)
    {
        foreach (var candidate in frameEntries)
        {
            if (candidate.X == x &&
                candidate.Y == y &&
                candidate.Context == context)
            {
                return candidate.Text;
            }
        }

        return null;
    }

    private readonly record struct PresentedNotice(string Notice, int Updates);
}

public readonly record struct BattleResultsPendingSpeech(string Text, bool Interrupt);
