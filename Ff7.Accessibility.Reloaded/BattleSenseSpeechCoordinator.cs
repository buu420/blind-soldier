using Ff7.Accessibility.Runtime.Abstractions;

namespace Ff7.Accessibility.Reloaded;

public sealed class BattleSenseSpeechCoordinator
{
    private readonly object sync = new();
    private readonly Func<int, BattleRuntimeTextResolution?> resolveBattleText;
    private readonly Func<int, BattleSenseObservation?> readSenseResult;
    private readonly Func<int, string?> resolveElementName;
    private readonly BattleMessageSpeechTracker messageTracker = new(_ => null);
    private int activeBuffer = -1;
    private SuppressionStage suppressionStage;
    private BattleSenseObservation? activeSense;
    private int weaknessIndex;

    public BattleSenseSpeechCoordinator(
        Func<int, BattleRuntimeTextResolution?> resolveBattleText,
        Func<int, BattleSenseObservation?> readSenseResult,
        Func<int, string?> resolveElementName)
    {
        this.resolveBattleText = resolveBattleText ?? throw new ArgumentNullException(nameof(resolveBattleText));
        this.readSenseResult = readSenseResult ?? throw new ArgumentNullException(nameof(readSenseResult));
        this.resolveElementName = resolveElementName ?? throw new ArgumentNullException(nameof(resolveElementName));
    }

    public void ObserveActiveBuffer(short bufferIndex)
    {
        lock (sync)
        {
            if (bufferIndex < 0)
            {
                ResetCore();
                return;
            }

            if (bufferIndex == activeBuffer)
            {
                return;
            }

            activeBuffer = bufferIndex;
            var resolution = resolveBattleText(bufferIndex);
            if (resolution is null)
            {
                if (suppressionStage != SuppressionStage.None)
                {
                    ContinueFailClosedSuppression();
                }

                messageTracker.ObserveActiveBuffer(bufferIndex, null);
                return;
            }

            if (TryRecognizeSenseHeader(
                    resolution.Controls,
                    out var actorId,
                    out var targetId,
                    out var level))
            {
                if (TryCreateAtomicSenseSpeech(actorId, targetId, level, out var speech))
                {
                    messageTracker.ObserveActiveBuffer(bufferIndex, speech);
                }
                else
                {
                    BeginFailClosedSuppression();
                    messageTracker.ObserveActiveBuffer(bufferIndex, null);
                }

                return;
            }

            if (TrySuppressSenseFragment(resolution.Controls))
            {
                messageTracker.ObserveActiveBuffer(bufferIndex, null);
                return;
            }

            CancelSuppression();
            messageTracker.ObserveActiveBuffer(bufferIndex, resolution.Text);
        }
    }

    public string? Poll()
    {
        lock (sync)
        {
            return messageTracker.Poll();
        }
    }

    public void Reset()
    {
        lock (sync)
        {
            ResetCore();
        }
    }

    private bool TryCreateAtomicSenseSpeech(
        int actorId,
        int targetId,
        int level,
        out string speech)
    {
        speech = string.Empty;
        if (targetId is < 0 or >= 26)
        {
            return false;
        }

        BattleSenseObservation? snapshot;
        try
        {
            snapshot = readSenseResult(actorId);
        }
        catch
        {
            snapshot = null;
        }

        if (snapshot is not { IsEnemy: true, IsSensed: true } ||
            snapshot.ActorId != actorId ||
            snapshot.Level is not int snapshotLevel ||
            snapshotLevel != level ||
            snapshot.CurrentHp is not int currentHp ||
            snapshot.MaximumHp is not int maximumHp ||
            snapshot.CurrentMp is not int currentMp ||
            snapshot.MaximumMp is not int maximumMp ||
            string.IsNullOrWhiteSpace(snapshot.Name))
        {
            return false;
        }

        var weaknessNames = new List<string>(snapshot.WeaknessElementIds.Length);
        foreach (var elementId in snapshot.WeaknessElementIds)
        {
            var elementName = resolveElementName(elementId)?.Trim();
            if (string.IsNullOrWhiteSpace(elementName))
            {
                return false;
            }

            weaknessNames.Add(elementName);
        }

        speech = $"{snapshot.Name.Trim()} {(char)('A' + targetId)}. Level {level}. " +
            $"HP {currentHp} of {maximumHp}. MP {currentMp} of {maximumMp}.";
        if (weaknessNames.Count > 0)
        {
            speech += $" Weak against {JoinNaturalLanguage(weaknessNames)}.";
        }

        activeSense = snapshot;
        suppressionStage = SuppressionStage.Hp;
        weaknessIndex = 0;
        return true;
    }

    private static bool TryRecognizeSenseHeader(
        IReadOnlyList<BattleRuntimeTextControl> controls,
        out int actorId,
        out int targetId,
        out int level)
    {
        actorId = -1;
        targetId = -1;
        level = -1;
        if (controls.Count != 3 ||
            controls[0].Kind != BattleRuntimeTextControlKind.TargetName ||
            controls[1].Kind != BattleRuntimeTextControlKind.TargetId ||
            controls[2].Kind != BattleRuntimeTextControlKind.Number)
        {
            return false;
        }

        actorId = controls[0].Argument;
        targetId = controls[1].Argument;
        level = controls[2].Argument;
        return true;
    }

    private bool TrySuppressSenseFragment(
        IReadOnlyList<BattleRuntimeTextControl> controls)
    {
        if (controls.Count == 0)
        {
            return false;
        }

        if (suppressionStage == SuppressionStage.FailClosedHp)
        {
            if (!controls.All(control => control.Kind == BattleRuntimeTextControlKind.Number))
            {
                return false;
            }

            suppressionStage = controls.Count >= 4
                ? SuppressionStage.FailClosedWeakness
                : SuppressionStage.FailClosedMp;
            return true;
        }

        if (suppressionStage == SuppressionStage.FailClosedMp)
        {
            if (!controls.All(control => control.Kind == BattleRuntimeTextControlKind.Number))
            {
                return false;
            }

            suppressionStage = SuppressionStage.FailClosedWeakness;
            return true;
        }

        if (suppressionStage == SuppressionStage.FailClosedWeakness)
        {
            if (!controls.All(control => control.Kind == BattleRuntimeTextControlKind.Element))
            {
                return false;
            }

            CancelSuppression();
            return true;
        }

        if (activeSense is not { } snapshot)
        {
            return false;
        }

        if (suppressionStage == SuppressionStage.Hp)
        {
            var hp = NumberPair(snapshot.CurrentHp, snapshot.MaximumHp);
            var mp = NumberPair(snapshot.CurrentMp, snapshot.MaximumMp);
            if (ControlsEqual(controls, hp))
            {
                suppressionStage = SuppressionStage.Mp;
                return true;
            }

            if (ControlsEqual(controls, [.. hp, .. mp]))
            {
                AdvancePastMp(snapshot);
                return true;
            }

            return false;
        }

        if (suppressionStage == SuppressionStage.Mp)
        {
            if (!ControlsEqual(controls, NumberPair(snapshot.CurrentMp, snapshot.MaximumMp)))
            {
                return false;
            }

            AdvancePastMp(snapshot);
            return true;
        }

        if (suppressionStage != SuppressionStage.Weakness ||
            controls.Any(control => control.Kind != BattleRuntimeTextControlKind.Element) ||
            weaknessIndex + controls.Count > snapshot.WeaknessElementIds.Length)
        {
            return false;
        }

        for (var index = 0; index < controls.Count; index++)
        {
            if (controls[index].Argument != snapshot.WeaknessElementIds[weaknessIndex + index])
            {
                return false;
            }
        }

        weaknessIndex += controls.Count;
        if (weaknessIndex == snapshot.WeaknessElementIds.Length)
        {
            CancelSuppression();
        }

        return true;
    }

    private void AdvancePastMp(BattleSenseObservation snapshot)
    {
        if (snapshot.WeaknessElementIds.Length == 0)
        {
            CancelSuppression();
        }
        else
        {
            suppressionStage = SuppressionStage.Weakness;
        }
    }

    private static BattleRuntimeTextControl[] NumberPair(int? first, int? second) =>
    [
        new(BattleRuntimeTextControlKind.Number, first!.Value),
        new(BattleRuntimeTextControlKind.Number, second!.Value)
    ];

    private static bool ControlsEqual(
        IReadOnlyList<BattleRuntimeTextControl> actual,
        IReadOnlyList<BattleRuntimeTextControl> expected) =>
        actual.Count == expected.Count && actual.SequenceEqual(expected);

    private static string JoinNaturalLanguage(IReadOnlyList<string> values) =>
        values.Count switch
        {
            0 => string.Empty,
            1 => values[0],
            2 => $"{values[0]} and {values[1]}",
            _ => $"{string.Join(", ", values.Take(values.Count - 1))}, and {values[^1]}"
        };

    private void CancelSuppression()
    {
        suppressionStage = SuppressionStage.None;
        activeSense = null;
        weaknessIndex = 0;
    }

    private void BeginFailClosedSuppression()
    {
        activeSense = null;
        suppressionStage = SuppressionStage.FailClosedHp;
        weaknessIndex = 0;
    }

    private void ContinueFailClosedSuppression()
    {
        activeSense = null;
        suppressionStage = suppressionStage switch
        {
            SuppressionStage.Hp => SuppressionStage.FailClosedHp,
            SuppressionStage.Mp => SuppressionStage.FailClosedMp,
            SuppressionStage.Weakness => SuppressionStage.FailClosedWeakness,
            _ => suppressionStage
        };
        weaknessIndex = 0;
    }

    private void ResetCore()
    {
        activeBuffer = -1;
        CancelSuppression();
        messageTracker.Reset();
    }

    private enum SuppressionStage
    {
        None,
        FailClosedHp,
        FailClosedMp,
        FailClosedWeakness,
        Hp,
        Mp,
        Weakness
    }
}
