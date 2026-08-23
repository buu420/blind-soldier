namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Turns Fort Condor battle snapshots into the things a sighted player is told.
/// </summary>
/// <remarks>
/// The battle has a banner that names each event as it happens, a stat panel for
/// whatever the cursor is over, a hire list, and a funds counter. None of it is
/// text, so all of it is reconstructed here from state.
///
/// <para>Only edges are announced. A player moving the cursor across open ground
/// is shown nothing new and is told nothing new; what they can see at a glance
/// and a blind player cannot - where they are, how much money is left, what is
/// closing in - is on the status key instead of in a running commentary.</para>
/// </remarks>
public sealed class CondorBattleSpeechTracker
{
    /// <summary>
    /// The banner messages, by the identifier the renderer draws them from. The
    /// wording is the game's own, recovered from <c>emes00.tex</c>.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, string> Messages =
        new Dictionary<int, string>
        {
            [0] = "Encountered enemy.",
            [1] = "Start combat.",
            [2] = "Halted enemy attack!",
            [3] = "Arrived at the directed position.",
            [7] = "Enemy invasion.",
            [10] = "Enemy destroyed.",
            [12] = "Set units.",
            [13] = "Start the game? Yes. No."
        };

    /// <summary>
    /// Selectable command cells in <c>eunit01.tex</c>, indexed by the native
    /// source-row id. The renderer samples the right-hand column at X=0xC0;
    /// the similarly named yellow words on the left are unit-status labels,
    /// not these choices.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, string> AllyUnitCommands =
        new Dictionary<int, string>
        {
            [0] = "Bomb",
            [2] = "Remove",
            [3] = "Action",
            [5] = "Direction"
        };

    /// <summary>Report cells in <c>emes00.tex</c>, indexed before state adds one.</summary>
    private static readonly IReadOnlyDictionary<int, string> ReportMessages =
        new Dictionary<int, string>
        {
            [0] = "Encountered enemy.",
            [3] = "Arrived at the directed position.",
            [10] = "Set units."
        };

    private const string HelpText =
        "Fort Condor help. Cursor: OK opens Setting Menu. " +
        "Setting Menu: OK hires and sets a unit; Cancel closes; Assist returns to the game; Start pauses. " +
        "Report: OK sends a command to the reporting unit; Cancel lets it move freely. " +
        "Ally Unit: OK sends a command; Page Up raises and Page Down lowers game speed.";

    private const int OutcomeOngoing = 0;
    private const int OutcomeVictory = 1;
    private const int OutcomeInvasion = 2;

    private const int VictoryMessageId = 2;
    private const int InvasionMessageId = 7;

    /// <summary>
    /// The result latch at 0x00CBEDC0, mapped to the banner the game publishes
    /// from it, so the wording has one home.
    /// </summary>
    private static int? ResultBannerFor(int outcome) => outcome switch
    {
        OutcomeVictory => VictoryMessageId,
        OutcomeInvasion => InvasionMessageId,
        _ => null
    };

    /// <summary>
    /// The two banners that end a battle, said with what they mean rather than
    /// as the caption alone.
    /// </summary>
    /// <remarks>
    /// A sighted player does not read "Enemy invasion." and deduce a result;
    /// they watch the enemy reach the fort and the battle stop. On 2026-08-21 a
    /// player fought a whole battle, heard the caption, and still had to ask
    /// whether they had won. The game's own words are kept and what they mean is
    /// added to them.
    /// </remarks>
    private static readonly IReadOnlyDictionary<int, string> Results =
        new Dictionary<int, string>
        {
            [VictoryMessageId] = "Halted enemy attack! Battle won.",
            [InvasionMessageId] = "Enemy invasion. They reached the fort. Battle lost."
        };

    private readonly Action<string> log;
    private readonly HashSet<int> reportedUnknownTypes = [];

    /// <summary>
    /// How many disagreements between the calculated answer and the game's own
    /// flag are worth writing down before the point is made.
    ///
    /// <para>The flag at 0x00CBCC9C is a frame-local render decision, cleared and
    /// recomputed several times per frame, so an outside reader polling it lands
    /// in the clear-to-recompute window and sees a false "blocked". In a real
    /// battle on 2026-08-21 six of the twenty positions the cursor rested on
    /// reported both answers, one of them five times without the cursor moving.
    /// Legality is therefore calculated here from the same inputs the validator
    /// uses, which is exact and does not flicker; the flag is still read, but
    /// only to record where the two disagree.</para>
    /// </summary>
    private const int MaximumLoggedPlacementDisagreements = 40;

    /// <summary>
    /// The units standing at the last reading, keyed by slot, so the ones that
    /// have gone down since can be named.
    /// </summary>
    private readonly Dictionary<int, CondorBattleUnit> standing = [];
    private readonly CondorFieldNavigator navigator = new();

    /// <summary>Said when a unit can be put on the spot under the cursor.</summary>
    public const string CanPlaceText = "Can place";

    /// <summary>Said when one cannot.</summary>
    public const string CannotPlaceText = "Cannot place";

    /// <summary>
    /// The cursor state the readout last spoke for, so that a cursor which has not
    /// moved onto anything new does not say it again. The 2026-08-21 session
    /// produced the identical sentence three times inside one second.
    /// </summary>
    private (int X, int Y, int UnitSlot, bool Legal)? lastCursorKey;

    /// <summary>
    /// Where the cursor was at the previous reading, spoken or not, so that one
    /// still travelling can be told apart from one that has come to rest. The
    /// game's own held-key repeat moves it about twenty units per reading.
    /// </summary>
    /// <remarks>
    /// Position only, deliberately - not the whole readout key. What is standing
    /// on the spot and whether it can take a unit change without the cursor
    /// moving at all: an enemy walking under a resting cursor, or the frontier
    /// advancing past it. Those are not travel and must not be held back as
    /// though they were.
    /// </remarks>
    private (int X, int Y)? lastSampledCursorPosition;

    private bool cursorReadoutSupersedesSpeech;

    private bool started;
    private int lastAdvanceBand = -1;
    private int lastPhase;
    private bool resultSpoken;
    private int lastMessageId;
    private int lastOutcome;
    private bool lastSettingMenuOpen;
    private int? lastHighlightedTypeId;
    private int lastUnitUnderCursorSlot;
    private int lastAlliedCount;
    private int lastGameSpeed;
    private int placementDisagreements;
    private bool pendingStatusRequest;
    private bool pendingPlacementLineRequest;
    private CondorInterfaceView lastInterfaceView;
    private int lastInterfaceSelection = int.MinValue;
    private int lastInterfaceAuxiliary = int.MinValue;
    private (int X, int Y)? lastDestinationSample;
    private bool statefulReadoutSupersedesSpeech;

    public CondorBattleSpeechTracker(Action<string>? log = null) =>
        this.log = log ?? (_ => { });

    /// <summary>Forgets the battle, so re-entering module 9 announces itself again.</summary>
    public void Reset()
    {
        started = false;
        lastAdvanceBand = -1;
        resultSpoken = false;
        standing.Clear();
        reportedUnknownTypes.Clear();
        placementDisagreements = 0;
        navigator.Reset();
        lastCursorKey = null;
        lastSampledCursorPosition = null;
        cursorReadoutSupersedesSpeech = false;
        LastObservationSupersedesSpeech = false;
        pendingStatusRequest = false;
        pendingPlacementLineRequest = false;
        lastInterfaceView = CondorInterfaceView.None;
        lastInterfaceSelection = int.MinValue;
        lastInterfaceAuxiliary = int.MinValue;
        lastDestinationSample = null;
        lastGameSpeed = 0;
        statefulReadoutSupersedesSpeech = false;
    }

    /// <summary>
    /// The battlefield navigator, fed by this tracker because it already works out
    /// who has gone down between two readings.
    /// </summary>
    public CondorFieldNavigator Navigator => navigator;

    /// <summary>
    /// Whether K was pressed before the reader produced a coherent snapshot.
    /// Kept here rather than separately in both hosts so x86 and x64 cannot
    /// disagree about whether a player's request may disappear.
    /// </summary>
    public bool HasPendingStatusRequest => pendingStatusRequest;

    /// <summary>Banks a K press until a coherent battle snapshot can answer it.</summary>
    public void RequestStatus() => pendingStatusRequest = true;

    /// <summary>
    /// Whether P was pressed before the reader produced a coherent snapshot.
    /// </summary>
    public bool HasPendingPlacementLineRequest => pendingPlacementLineRequest;

    /// <summary>Banks a P press until a coherent battle snapshot can answer it.</summary>
    public void RequestPlacementLine() => pendingPlacementLineRequest = true;

    /// <summary>
    /// Answers and consumes the banked P press: where the battle line is now.
    /// </summary>
    /// <remarks>
    /// Unlike the status key this has no opening-line special case to avoid
    /// duplicating, because nothing else ever says where the line is - which was
    /// the entire problem. See <see cref="CondorPlacementLineReadout"/>.
    /// </remarks>
    public string? ConsumeRequestedPlacementLine(CondorBattleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!pendingPlacementLineRequest)
        {
            return null;
        }

        pendingPlacementLineRequest = false;
        return CondorPlacementLineReadout.Describe(snapshot);
    }

    /// <summary>
    /// Whether the batch <see cref="Observe"/> just returned is current
    /// interface state and may therefore replace what the reader is still
    /// saying rather than queueing behind it.
    /// </summary>
    /// <remarks>
    /// Decided here rather than in each host so the two runtimes cannot disagree
    /// about which lines a player is allowed to lose. Position, the highlighted
    /// choice and game speed are state: only the latest one is worth anything,
    /// and hearing a row the cursor has already left is worse than hearing
    /// nothing. A banner, casualty or result is an event, so when it coincides
    /// with a blocking prompt both are combined before the batch may supersede.
    /// </remarks>
    public bool LastObservationSupersedesSpeech { get; private set; }

    /// <summary>
    /// Answers and consumes the banked K press. On the first accepted snapshot,
    /// <see cref="Observe"/> starts with the identical status line, so the
    /// request is consumed without adding a duplicate interrupting copy.
    /// </summary>
    public string? ConsumeRequestedStatus(
        CondorBattleSnapshot snapshot,
        bool openingStatusWillBeSpoken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!pendingStatusRequest)
        {
            return null;
        }

        pendingStatusRequest = false;
        return openingStatusWillBeSpoken ? null : DescribeStatus(snapshot);
    }

    /// <summary>
    /// Applies one navigation action and returns what to say.
    /// </summary>
    /// <param name="moveCursor">
    /// Moves the game's own cursor, returning whether the write took. Only the
    /// jump uses it; passing null means a jump reports that it could not move.
    /// </param>
    public string? Navigate(
        CondorNavigationAction action,
        Func<int, int, bool>? moveCursor = null) =>
        navigator.Handle(action, moveCursor);

    /// <summary>
    /// How many times the calculated placement answer differed from the game's
    /// own flag while both answers describe the same ordinary-cursor state.
    /// Report overlays and the snapshot in which a hire completes are excluded:
    /// the game does not validate the flag then, or the async read can straddle
    /// the allocation event, so those comparisons have no geometric meaning.
    /// </summary>
    public int PlacementDisagreements => placementDisagreements;

    /// <summary>
    /// The lines to speak for this snapshot, in the order they should be heard.
    /// </summary>
    public IReadOnlyList<string> Observe(CondorBattleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        ReportUnknownUnitTypes(snapshot);
        RecordPlacementFlagDisagreement(snapshot);
        cursorReadoutSupersedesSpeech = false;
        statefulReadoutSupersedesSpeech = false;
        LastObservationSupersedesSpeech = false;

        if (!started)
        {
            started = true;

            var opening = new List<string> { DescribeStatus(snapshot) };

            // Confirmation deliberately holds the first setup reading back.
            // The accepted reading is therefore not merely a baseline: its
            // banner, result and Setting Menu are already visible and must be
            // included now or they can disappear before the next poll.
            var resultBanner = ResultBannerFor(snapshot.Outcome);
            if (resultBanner is { } outcomeBanner)
            {
                resultSpoken = true;
                opening.Add(Results[outcomeBanner]);
            }

            if (snapshot.MessageId != resultBanner && !InterfaceOwnsMessage(snapshot))
            {
                if (Results.TryGetValue(snapshot.MessageId, out var openingResult))
                {
                    if (!resultSpoken)
                    {
                        resultSpoken = true;
                        opening.Add(openingResult);
                    }
                }
                else if (Messages.TryGetValue(snapshot.MessageId, out var message))
                {
                    opening.Add(message);
                }
            }

            if (snapshot.Outcome != OutcomeOngoing)
            {
                log(
                    $"Fort Condor: result latch set to {snapshot.Outcome} with banner " +
                    $"{snapshot.MessageId}, {snapshot.LivingAllies} allied and " +
                    $"{snapshot.LivingEnemies} enemy units still standing.");
            }

            // Populate the navigator before the host applies keys banked during
            // confirmation. Otherwise the first accepted state can be spoken
            // correctly while J/L still answer "None."
            navigator.Update(snapshot.Units, snapshot.CursorX, snapshot.CursorY);

            if (snapshot.SettingMenuOpen)
            {
                statefulReadoutSupersedesSpeech = true;
                opening.Add($"Setting menu. {snapshot.Gil} gil.");
                if (DescribeHighlightedUnit(snapshot) is { } highlighted)
                {
                    opening.Add(highlighted);
                }
            }

            opening.AddRange(ObserveInterface(snapshot));

            // The reader now admits only initialized snapshots: setup has held
            // steady across two samples, or a later phase has already passed
            // the initializer. DescribeStatus includes these coordinates and
            // either the unit here or the placement answer, so this accepted
            // cursor is the baseline. A later real move still changes the key.
            lastCursorKey = CursorKey(snapshot);

            // Both, so the position the opening just announced counts as already
            // at rest. Priming only the spoken key would make the next reading
            // look like the end of a move and say it a second time.
            lastSampledCursorPosition = (snapshot.CursorX, snapshot.CursorY);
            RememberStanding(snapshot);
            lastAdvanceBand = AdvanceBand(snapshot.EnemyAdvance);
            Remember(snapshot);

            // Entry is a finite ordered description, not a stream of stale
            // highlights. Preserve its established multi-line delivery; later
            // selection changes may supersede one another once play begins.
            statefulReadoutSupersedesSpeech = false;
            return opening;
        }

        var lines = new List<string>();

        // The result latch is the game's own decision and it is set before the
        // banner is published from it, so it is the authority on who won. It
        // was followed through the end of a battle in
        // analysis/ghidra/fort-condor-combat-result-20260821.md: zero is no
        // result, one is the enemy stopped, two is the enemy reaching the fort,
        // and nothing else is ever written.
        if (snapshot.Outcome != lastOutcome && !resultSpoken &&
            ResultBannerFor(snapshot.Outcome) is { } latchedBanner)
        {
            resultSpoken = true;
            lines.Add(Results[latchedBanner]);
        }

        // The banner is the loudest thing on screen and it names events the
        // player cannot otherwise detect, so it goes first.
        var bannerChanged = snapshot.MessageId != lastMessageId;
        if (bannerChanged && Results.TryGetValue(snapshot.MessageId, out var result))
        {
            // Published from the latch above, so this only ever speaks if the
            // latch changed and went back to normal between two reads.
            if (!resultSpoken)
            {
                resultSpoken = true;
                lines.Add(result);
            }
        }
        else if (bannerChanged &&
                 !InterfaceOwnsMessage(snapshot) &&
                 Messages.TryGetValue(snapshot.MessageId, out var message))
        {
            lines.Add(message);
        }

        if (snapshot.Outcome != lastOutcome && snapshot.Outcome != OutcomeOngoing)
        {
            log(
                $"Fort Condor: result latch set to {snapshot.Outcome} with banner " +
                $"{snapshot.MessageId}, {snapshot.LivingAllies} allied and " +
                $"{snapshot.LivingEnemies} enemy units still standing.");
        }

        lines.AddRange(ObserveCasualties(snapshot, bannerChanged));
        lines.AddRange(ObserveEnemyAdvance(snapshot));

        if (snapshot.GameSpeed != lastGameSpeed)
        {
            statefulReadoutSupersedesSpeech = true;
            lines.Add($"Game speed {snapshot.GameSpeed} of 4.");
        }

        // After the casualty diff, so a unit that has just fallen is already in
        // the losses list rather than still counted among the living.
        navigator.Update(snapshot.Units, snapshot.CursorX, snapshot.CursorY);

        if (snapshot.SettingMenuOpen && !lastSettingMenuOpen)
        {
            lines.Add($"Setting menu. {snapshot.Gil} gil.");
            if (DescribeHighlightedUnit(snapshot) is { } opened)
            {
                lines.Add(opened);
            }
        }
        else if (snapshot.SettingMenuOpen &&
                 snapshot.HighlightedTypeId != lastHighlightedTypeId &&
                 DescribeHighlightedUnit(snapshot) is { } moved)
        {
            statefulReadoutSupersedesSpeech = true;
            lines.Add(moved);
        }

        // A unit appearing on the player's side after the menu closes is a hire
        // that went through. The game shows the new unit and the funds dropping;
        // this is the same fact. Say completion before a direction prompt: the
        // prompt is the choice that owns the controls now and must be the last
        // thing left in the player's ear.
        if (snapshot.AlliedCount > lastAlliedCount && lastSettingMenuOpen && !snapshot.SettingMenuOpen)
        {
            lines.Add($"Placed. {snapshot.Gil} gil.");
        }

        lines.AddRange(ObserveInterface(snapshot));

        if (snapshot.ModalState == 0 &&
            !snapshot.SettingMenuOpen &&
            snapshot.ReportState == 0 &&
            snapshot.InteractionMode == CondorBattleSnapshot.CursorInteractionMode)
        {
            lines.AddRange(ObserveCursor(snapshot));
        }

        Remember(snapshot);
        return FinalizeObservation(lines);
    }

    private IReadOnlyList<string> FinalizeObservation(List<string> lines)
    {
        // Menu rows and other current interface state are allowed to replace an
        // older row that is still queued. If a one-shot event happened in the
        // same sample, keep it by joining the complete ordered batch into the
        // same utterance; otherwise interrupting the prompt would erase the
        // event, while queueing it would leave the player on a stale choice.
        if (statefulReadoutSupersedesSpeech && lines.Count > 1)
        {
            var combined = string.Join(" ", lines);
            lines.Clear();
            lines.Add(combined);
        }

        LastObservationSupersedesSpeech =
            lines.Count == 1 &&
            (cursorReadoutSupersedesSpeech || statefulReadoutSupersedesSpeech);
        return lines;
    }

    /// <summary>
    /// Speaks the texture-backed interfaces that do not use the Setting Menu's
    /// modal or row globals. Modal state is only one axis: reports and the Ally
    /// Unit list remain open at modal zero, which is why a modal-only reader
    /// left the original battle choices silent.
    /// </summary>
    private IReadOnlyList<string> ObserveInterface(CondorBattleSnapshot snapshot)
    {
        var view = CurrentInterfaceView(snapshot);
        var (selection, auxiliary) = InterfaceSelection(snapshot, view);
        var opened = view != lastInterfaceView;

        if (view == CondorInterfaceView.None)
        {
            var resumed = lastInterfaceView == CondorInterfaceView.Pause;
            RememberInterface(view, selection, auxiliary);
            lastDestinationSample = null;
            statefulReadoutSupersedesSpeech |= resumed;
            return resumed ? ["Battle resumed."] : [];
        }

        if (view == CondorInterfaceView.Destination)
        {
            var current = (snapshot.DestinationX, snapshot.DestinationY);
            if (!opened)
            {
                var settled = lastDestinationSample == current;
                lastDestinationSample = current;
                if (!settled ||
                    (selection == lastInterfaceSelection && auxiliary == lastInterfaceAuxiliary))
                {
                    return [];
                }
            }
            else
            {
                lastDestinationSample = current;
            }
        }
        else if (!opened &&
                 selection == lastInterfaceSelection &&
                 auxiliary == lastInterfaceAuxiliary)
        {
            return [];
        }

        var line = view switch
        {
            CondorInterfaceView.AllyUnit => DescribeAllyUnitMenu(snapshot, opened),
            CondorInterfaceView.Destination =>
                opened
                    ? $"Choose destination. Cursor at {snapshot.DestinationX}, {snapshot.DestinationY}."
                    : $"Destination {snapshot.DestinationX}, {snapshot.DestinationY}.",
            CondorInterfaceView.StartGame => DescribeStartGame(snapshot, opened),
            CondorInterfaceView.Direction => DescribeDirection(snapshot),
            CondorInterfaceView.CrowdedUnit => DescribeCrowdedUnit(snapshot, opened),
            CondorInterfaceView.Report => DescribeReport(snapshot),
            CondorInterfaceView.Pause => "Paused.",
            CondorInterfaceView.Help => HelpText,
            _ => null
        };

        RememberInterface(view, selection, auxiliary);
        statefulReadoutSupersedesSpeech |= line is not null;
        return line is null ? [] : [line];
    }

    private static CondorInterfaceView CurrentInterfaceView(CondorBattleSnapshot snapshot)
    {
        // A report owns OK and Cancel even after modal 17 has finished sliding
        // it in, so it outranks the underlying interaction mode.
        if (snapshot.ReportState != 0)
        {
            return CondorInterfaceView.Report;
        }

        return snapshot.ModalState switch
        {
            CondorBattleSnapshot.NewUnitDirectionModalState or
                CondorBattleSnapshot.CommandDirectionModalState => CondorInterfaceView.Direction,
            CondorBattleSnapshot.PauseModalState => CondorInterfaceView.Pause,
            CondorBattleSnapshot.StartGameModalState => CondorInterfaceView.StartGame,
            CondorBattleSnapshot.HelpModalState => CondorInterfaceView.Help,
            CondorBattleSnapshot.CrowdedUnitModalState => CondorInterfaceView.CrowdedUnit,
            0 when snapshot.InteractionMode == CondorBattleSnapshot.AllyUnitInteractionMode =>
                CondorInterfaceView.AllyUnit,
            0 when snapshot.InteractionMode == CondorBattleSnapshot.DestinationInteractionMode =>
                CondorInterfaceView.Destination,
            _ => CondorInterfaceView.None
        };
    }

    private static bool InterfaceOwnsMessage(CondorBattleSnapshot snapshot)
    {
        if (snapshot.ModalState == CondorBattleSnapshot.StartGameModalState &&
            snapshot.MessageId == 13)
        {
            return true;
        }

        return snapshot.ReportState != 0 &&
               ReportMessages.TryGetValue(snapshot.ReportMessageCell, out var report) &&
               Messages.TryGetValue(snapshot.MessageId, out var banner) &&
               string.Equals(report, banner, StringComparison.Ordinal);
    }

    private static (int Selection, int Auxiliary) InterfaceSelection(
        CondorBattleSnapshot snapshot,
        CondorInterfaceView view) => view switch
    {
        CondorInterfaceView.AllyUnit => (snapshot.AllyUnitMenu?.HighlightedRow ?? -1, 0),
        CondorInterfaceView.Destination => (snapshot.DestinationX, snapshot.DestinationY),
        CondorInterfaceView.StartGame => (snapshot.StartGameSelection, 0),
        CondorInterfaceView.Direction => (snapshot.DirectionSelection, snapshot.ModalState),
        CondorInterfaceView.CrowdedUnit => (snapshot.CrowdedUnitMenu?.HighlightedRow ?? -1, 0),
        CondorInterfaceView.Report => (snapshot.ReportMessageCell, snapshot.ReportUnitSlot),
        _ => (0, 0)
    };

    private static string DescribeAllyUnitMenu(CondorBattleSnapshot snapshot, bool opened)
    {
        if (snapshot.AllyUnitMenu is not { } menu ||
            menu.HighlightedCommandId is not { } commandId)
        {
            return opened ? "Ally unit. No commands." : "No commands.";
        }

        var command = AllyUnitCommands.GetValueOrDefault(commandId, "Unknown command");
        var row = $"{command}. {menu.HighlightedRow + 1} of {menu.CommandIds.Count}.";
        return opened ? $"Ally unit. {row}" : row;
    }

    private static string DescribeStartGame(CondorBattleSnapshot snapshot, bool opened)
    {
        var yes = snapshot.StartGameSelection == 0;
        var row = yes ? "Yes. 1 of 2." : "No. 2 of 2.";
        return opened ? $"Start the game? {row}" : row;
    }

    private static string DescribeDirection(CondorBattleSnapshot snapshot)
    {
        // FUN_006047AC stores selection - 0x200 as the selected unit's angle.
        // FUN_00605D59 then converts that 0x1000-per-turn angle into the arrow's
        // screen-space vector. Selection 0 is 45 degrees down-right, 0x200 is
        // straight down, and 0x400 is 45 degrees down-left. The ordinal alone
        // would tell a blind player which keypress they made, but not the visual
        // direction the selector exists to show.
        var signedAngle = snapshot.DirectionSelection - 0x200;
        var degrees = (int)Math.Round(
            Math.Abs(signedAngle) * 360d / 0x1000,
            MidpointRounding.AwayFromZero);
        var orientation = signedAngle switch
        {
            < 0 => $"{degrees} degrees right of down",
            > 0 => $"{degrees} degrees left of down",
            _ => "Straight down"
        };

        return $"Direction. {orientation}. {snapshot.DirectionOrdinal} of 33.";
    }

    private static string DescribeCrowdedUnit(CondorBattleSnapshot snapshot, bool opened)
    {
        if (snapshot.CrowdedUnitMenu is not { } menu ||
            menu.HighlightedUnitSlot is not { } slot)
        {
            return opened ? "Choose a unit. Selection unavailable." : "Selection unavailable.";
        }

        var unit = snapshot.HighlightedCrowdedUnit;
        var description = unit is null
            ? $"Unit slot {slot}"
            : $"{unit.Describe()}, at {unit.X}, {unit.Y}";
        var row = $"{description}. {menu.HighlightedRow + 1} of {menu.UnitSlots.Count}.";
        return opened ? $"Choose a unit. {row}" : row;
    }

    private static string DescribeReport(CondorBattleSnapshot snapshot)
    {
        var message = ReportMessages.GetValueOrDefault(
            snapshot.ReportMessageCell,
            "Message unavailable.");
        var unit = snapshot.ReportingUnit is { } reporting
            ? $" {reporting.Describe()}."
            : string.Empty;
        return $"Report. {message}{unit} " +
               "OK sends a command to this unit. Cancel lets it move freely.";
    }

    private void RememberInterface(
        CondorInterfaceView view,
        int selection,
        int auxiliary)
    {
        lastInterfaceView = view;
        lastInterfaceSelection = selection;
        lastInterfaceAuxiliary = auxiliary;
    }

    /// <summary>
    /// The units that have gone down since the last reading.
    /// </summary>
    /// <remarks>
    /// This is the drumbeat of the battle and what a sighted player is actually
    /// watching: whose line is thinning. Without it module 9 tells a blind
    /// player nothing between placing a unit and losing, which is exactly what
    /// happened on 2026-08-21 - two and a half minutes of fighting passed in
    /// silence and the player had to ask who had won.
    ///
    /// <para>Only read across a steady phase. The live array is rebuilt when the
    /// battle changes phase, and reporting that as twenty deaths would be a lie
    /// told loudly.</para>
    /// </remarks>
    private IEnumerable<string> ObserveCasualties(CondorBattleSnapshot snapshot, bool bannerChanged)
    {
        var lines = new List<string>();
        var alive = snapshot.Units
            .Where(unit => !unit.IsDying)
            .ToDictionary(unit => unit.Slot);

        if (snapshot.Phase == lastPhase)
        {
            var lost = standing.Values
                .Where(unit => !alive.ContainsKey(unit.Slot))
                .ToList();

            var allies = lost.Where(unit => !unit.IsEnemy).ToList();

            // Where they fell outlives them. The game drops a dead unit from its
            // arrays, and the ground it died on is usually the ground that matters
            // most - it is where the line gave way.
            foreach (var fallen in allies)
            {
                navigator.RecordLoss(fallen);
            }

            if (allies.Count > 0)
            {
                var what = allies.Count == 1
                    ? $"Lost {allies[0].Name}."
                    : $"Lost {allies.Count} units.";
                var left = Pluralize(snapshot.LivingAllies, "unit", "units");
                lines.Add($"{what} {left} left.");
            }

            // The game's own banner already announces an enemy going down, so
            // repeating it would double every kill. What gets added is the count
            // the banner does not give.
            var enemies = lost.Where(unit => unit.IsEnemy).ToList();
            if (enemies.Count > 0 && !bannerChanged)
            {
                var what = enemies.Count == 1
                    ? $"{Capitalize(enemies[0].Name)} destroyed."
                    : $"{enemies.Count} enemies destroyed.";
                var left = Pluralize(snapshot.LivingEnemies, "enemy", "enemies");
                lines.Add($"{what} {left} left.");
            }
        }

        standing.Clear();
        foreach (var unit in alive.Values)
        {
            standing[unit.Slot] = unit;
        }

        return lines;
    }

    /// <summary>
    /// How far the enemy has come, when that changes by enough to matter.
    /// </summary>
    /// <remarks>
    /// The game keeps this as a value from zero to ninety-six, derived from the
    /// leading enemy's position, and draws it as a row of segments that sits on
    /// screen for the whole battle. It is the one thing a sighted player can
    /// check at a glance to know whether they are losing, so it is reported at
    /// quarters rather than left to be inferred from casualties.
    ///
    /// <para>Reported in both directions: the gauge falls back when the line is
    /// pushed away from the fort, and a player who has just spent their last gil
    /// deserves to hear that it worked.</para>
    /// </remarks>
    private IEnumerable<string> ObserveEnemyAdvance(CondorBattleSnapshot snapshot)
    {
        var band = AdvanceBand(snapshot.EnemyAdvance);
        if (band == lastAdvanceBand)
        {
            yield break;
        }

        var closing = band > lastAdvanceBand;
        lastAdvanceBand = band;
        yield return DescribeAdvance(band, closing);
    }

    /// <summary>The gauge in quarters, which is how a row of segments reads.</summary>
    private static int AdvanceBand(int advance) => advance switch
    {
        >= CondorBattleSnapshot.EnemyAdvanceFull => 4,
        >= 72 => 3,
        >= 48 => 2,
        >= 24 => 1,
        _ => 0
    };

    private static string DescribeAdvance(int band, bool closing) => band switch
    {
        >= 4 => "Enemies at the fort.",
        3 => "Enemy advance three quarters.",
        2 => "Enemy advance halfway.",
        1 => "Enemy advance a quarter.",
        _ => closing ? "Enemy advance a quarter." : "Enemies pushed back."
    };

    private void RememberStanding(CondorBattleSnapshot snapshot)
    {
        standing.Clear();
        foreach (var unit in snapshot.Units.Where(unit => !unit.IsDying))
        {
            standing[unit.Slot] = unit;
        }
    }

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>
    /// The picture a sighted player takes in at a glance: what is left, what it
    /// costs, where the cursor is, and what is nearest to it.
    /// </summary>
    public string DescribeStatus(CondorBattleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var parts = new List<string>
        {
            $"{snapshot.Gil} gil",
            Pluralize(snapshot.AlliedCount, "unit", "units"),
            Pluralize(snapshot.EnemyCount, "enemy", "enemies")
        };

        parts.Add($"cursor at {snapshot.CursorX}, {snapshot.CursorY}");

        if (snapshot.UnitUnderCursor is { } under)
        {
            parts.Add($"on {under.Describe()}");
        }
        else
        {
            parts.Add(
                CondorPlacementRegion.IsLegalAt(snapshot, snapshot.CursorX, snapshot.CursorY)
                    ? CanPlaceText
                    : CannotPlaceText);
        }

        if (snapshot.NearestEnemy is { } nearest)
        {
            // Coordinates, like everything else: a bearing from the cursor stops
            // being true as soon as the cursor moves, and this line is most useful
            // when the player is about to move it.
            parts.Add($"nearest {nearest.Describe()}, at {nearest.X}, {nearest.Y}");
        }

        // Three illuminated markers plus the unlit minimum form four visible
        // levels. The native value is initialized to two and clamped to 1..4.
        parts.Add($"game speed {snapshot.GameSpeed} of 4");

        // The advance gauge is drawn for the whole battle, so it belongs in the
        // glance rather than only in the moment it changes.
        parts.Add(snapshot.EnemyAdvance <= 0
            ? "no enemy advance"
            : $"enemy advance {snapshot.EnemyAdvance * 100 / CondorBattleSnapshot.EnemyAdvanceFull} percent");

        return string.Join(". ", parts) + ".";
    }

    /// <summary>
    /// Where the cursor is, and what is on that spot.
    /// </summary>
    /// <remarks>
    /// <para>Spoken wherever the cursor comes to rest, not only when the ground
    /// under it changes character. The version before 2026-08-22 announced the
    /// placement <em>band</em> and stayed silent while the cursor swept inside
    /// one, so most of Brice's moves said nothing at all. Position is what is
    /// being asked for; whether a unit fits there is the qualifier on it, not the
    /// subject.</para>
    ///
    /// <para><b>Where it comes to rest, not every place it passes through.</b> The
    /// first version of this spoke every sample the cursor had moved in, and the
    /// game's own held-key repeat carries it about twenty units per sample - so
    /// two seconds of holding a direction queued sixteen announcements, each of
    /// which takes longer than a sample to say. The speech fell further and
    /// further behind the cursor and carried on talking after the key was
    /// released, which Brice reported as the key appearing to stick. A sighted
    /// player watching the cursor slide does not read out the rows it crosses;
    /// they see where it is now. So does this.</para>
    ///
    /// <para>Keyed on the state rather than on the finished sentence so that a
    /// unit's HP ticking down under a resting cursor does not re-announce it ten
    /// times a second, while a cursor that genuinely moved always does.</para>
    /// </remarks>
    private IEnumerable<string> ObserveCursor(CondorBattleSnapshot snapshot)
    {
        var key = CursorKey(snapshot);
        var position = (snapshot.CursorX, snapshot.CursorY);
        var settled = position == lastSampledCursorPosition;
        lastSampledCursorPosition = position;

        // Still travelling. Saying this row would cost more time than the cursor
        // will spend on it.
        if (!settled)
        {
            yield break;
        }

        if (key == lastCursorKey)
        {
            yield break;
        }

        lastCursorKey = key;
        cursorReadoutSupersedesSpeech = true;
        yield return
            $"{snapshot.CursorX}, {snapshot.CursorY}. {DescribeUnderCursor(snapshot, key.Legal)}.";
    }

    private static (int X, int Y, int UnitSlot, bool Legal) CursorKey(
        CondorBattleSnapshot snapshot) =>
        (
            snapshot.CursorX,
            snapshot.CursorY,
            snapshot.UnitUnderCursorSlot,
            CondorPlacementRegion.IsLegalAt(snapshot, snapshot.CursorX, snapshot.CursorY));

    /// <summary>
    /// What occupies the spot under the cursor: the unit standing there, or
    /// whether one can be put there.
    /// </summary>
    /// <remarks>
    /// Deliberately just the two answers. The extent of the placement band, the
    /// nearest legal row and the count of remaining bands were all removed on
    /// 2026-08-22 at Brice's direction: they buried the coordinates the readout
    /// exists to deliver.
    /// </remarks>
    private static string DescribeUnderCursor(CondorBattleSnapshot snapshot, bool legal) =>
        snapshot.UnitUnderCursor is { } unit
            ? unit.Describe()
            : legal ? CanPlaceText : CannotPlaceText;

    /// <summary>
    /// Notes where the calculated answer and the game's own flag disagree.
    /// </summary>
    private void RecordPlacementFlagDisagreement(CondorBattleSnapshot snapshot)
    {
        if (!started ||
            snapshot.ModalState != 0 ||
            snapshot.ReportState != 0 ||
            lastSettingMenuOpen ||
            snapshot.AlliedCount != lastAlliedCount ||
            snapshot.InteractionMode != CondorBattleSnapshot.CursorInteractionMode)
        {
            return;
        }

        var calculated = CondorPlacementRegion.IsLegalAt(snapshot, snapshot.CursorX, snapshot.CursorY);
        if (calculated == snapshot.CursorPlacementLegal)
        {
            return;
        }

        placementDisagreements++;
        if (placementDisagreements <= MaximumLoggedPlacementDisagreements)
        {
            log(
                $"Fort Condor placement: calculated {calculated} but the native flag read " +
                $"{snapshot.CursorPlacementLegal} at ({snapshot.CursorX},{snapshot.CursorY}). " +
                $"phase={snapshot.Phase}, frontier={snapshot.DeploymentFrontierY}, " +
                $"limit={CondorPlacementRegion.VerticalLimit(snapshot)}, " +
                $"allied={snapshot.AlliedCount}, triangles={snapshot.CollisionTriangles.Count}.");
        }
    }

    private string? DescribeHighlightedUnit(CondorBattleSnapshot snapshot)
    {
        if (snapshot.HighlightedTypeId is not { } typeId)
        {
            return null;
        }

        var unit = CondorUnitCatalog.ResolveByRecordIndex(typeId);
        return unit is null
            ? $"Unit type {typeId}."
            : CondorUnitCatalog.DescribeForHire(unit, snapshot.Gil);
    }

    /// <summary>
    /// Records an out-of-range unit type the atlas cannot name.
    ///
    /// <para>All 24 cells in <c>emes01.tex</c> are mapped. Logging anything
    /// outside that table keeps corrupted or version-specific state honest, and
    /// costs one line per unseen type per battle.</para>
    /// </summary>
    private void ReportUnknownUnitTypes(CondorBattleSnapshot snapshot)
    {
        foreach (var unit in snapshot.Units)
        {
            if (CondorUnitCatalog.ResolveName(unit.TypeId) is not null ||
                !reportedUnknownTypes.Add(unit.TypeId))
            {
                continue;
            }

            log(
                $"Fort Condor: unnamed unit type {unit.TypeId} in slot {unit.Slot} " +
                $"({(unit.IsEnemy ? "enemy" : "allied")}), {unit.CurrentHp} of {unit.MaximumHp} HP, " +
                $"attack {unit.Attack}.");
        }
    }

    private void Remember(CondorBattleSnapshot snapshot)
    {
        lastMessageId = snapshot.MessageId;
        lastPhase = snapshot.Phase;
        lastOutcome = snapshot.Outcome;
        lastSettingMenuOpen = snapshot.SettingMenuOpen;
        lastHighlightedTypeId = snapshot.HighlightedTypeId;
        lastUnitUnderCursorSlot = snapshot.UnitUnderCursorSlot;
        lastAlliedCount = snapshot.AlliedCount;
        lastGameSpeed = snapshot.GameSpeed;
    }

    private static string Pluralize(int count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";

    private enum CondorInterfaceView
    {
        None,
        AllyUnit,
        Destination,
        StartGame,
        Direction,
        CrowdedUnit,
        Report,
        Pause,
        Help
    }
}
