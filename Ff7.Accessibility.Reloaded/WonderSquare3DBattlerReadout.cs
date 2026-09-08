using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// The visible state of a 3D Battler match: which opponent is up, and how many hits
/// each fighter has taken so far.
/// </summary>
/// <param name="IsPlaying">
/// The shared match loop is executing. This is the controller identity, not a guess
/// from bank values that every script in the room shares.
/// </param>
/// <param name="Stage">The opponent stage the loop is on.</param>
/// <param name="PlayerHitsTaken">
/// Hits the player's own fighter has taken. Bank[5][12], incremented by that
/// fighter's own recoil scripts.
/// </param>
/// <param name="OpponentHitsTaken">Hits the opponent has taken. Bank[5][13].</param>
public readonly record struct WonderSquare3DBattlerState(
    bool IsPlaying,
    int Stage,
    int PlayerHitsTaken,
    int OpponentHitsTaken,
    bool IsFinished);

/// <summary>
/// Reads the 3D Battler's own state in games_2.
///
/// Evidence, installed flevel field 507 games_2. Both cabinet sides, kakul1(10) and
/// kakul2(11) script 4, request dic(0) script 3, which is the shared match loop, so
/// dic running script 3 is the controller identity for a live match.
///
/// Inside that loop:
///  - byte 0 seeds Bank[5][14] to 1 and byte 4 runs while it is above zero, so
///    Bank[5][14] is the opponent stage. Bytes 14, 46, 57 and 68 map stages 1..4 to
///    entities 13..16, the four opponent models.
///  - byte 84 rolls the opponent's move *before* the player's own input is read at
///    bytes 91..121. That value is never reported: it would say what the opponent is
///    about to do, which the screen does not.
///  - the resolved exchange is what the screen shows. f1(12), the player's own
///    fighter, has scripts 10, 11 and 12: each plays one of animations 13, 14 and 15
///    and then increments Bank[5][12], so that counter is hits taken *by the player's
///    fighter*. The opponents f2..f5(13..16) have scripts 8, 9 and 10: each plays one
///    of animations 7, 8 and 9 and then increments Bank[5][13], hits taken by the
///    opponent. They are not player-points and opponent-points.
///  - byte 707 ends the match when Bank[5][12] passes 9 and requests f1 script 13,
///    the player fighter's defeat; byte 910 ends it when Bank[5][13] does. Bytes 968
///    and 972 clear both counters and byte 961 advances the stage.
///
/// A stage byte inside its range is not evidence of a live match. An idle room whose
/// temporary block happens to hold a 1 there satisfies any range test, which is why
/// the controller identity is required instead.
/// </summary>
public sealed class WonderSquare3DBattlerStateReader
{
    public const int BattlerFieldId = 507;
    public const int DirectorEntityId = 0;

    /// <summary>dic's script 3, the shared match loop both cabinet sides request.</summary>
    public const int MatchScriptId = 3;

    /// <summary>Set to 1 by the loop when either fighter reaches ten hits.</summary>
    public const int MatchOverOffset = 4;

    public const int PlayerHitsTakenOffset = 12;
    public const int OpponentHitsTakenOffset = 13;
    public const int StageOffset = 14;

    public const int FirstStage = 1;

    /// <summary>
    /// Byte 961 can advance the stage past the fourth opponent, and no further
    /// opponent entity is requested there.
    /// </summary>
    public const int LastOpponentStage = 4;
    public const int ExhaustedStage = 5;

    /// <summary>The native instruction says the first to ten points wins.</summary>
    public const int WinningHits = 10;

    private readonly ILegacyAddressSpace memory;
    private readonly FieldScriptControllerReader controller;

    public WonderSquare3DBattlerStateReader(ILegacyAddressSpace memory)
        : this(memory, new FieldScriptControllerReader(memory))
    {
    }

    public WonderSquare3DBattlerStateReader(
        ILegacyAddressSpace memory,
        FieldScriptControllerReader controller)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public string LastDiagnostic { get; private set; } = string.Empty;

    public void Reset() => LastDiagnostic = string.Empty;

    public bool TryRead(out WonderSquare3DBattlerState state)
    {
        state = default;
        if (!memory.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module) ||
            module != FieldPositionReader.FieldModule)
        {
            LastDiagnostic = "not the field module";
            return false;
        }

        if (!memory.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var fieldId) ||
            fieldId != BattlerFieldId)
        {
            LastDiagnostic = $"field {fieldId} is not Wonder Square building two";
            return false;
        }

        if (!controller.TryRead(BattlerFieldId, DirectorEntityId, MatchScriptId, out var director))
        {
            LastDiagnostic = "3D Battler controller state unreadable";
            return false;
        }

        if (!director.IsControllerActive)
        {
            state = new WonderSquare3DBattlerState(false, 0, 0, 0, false);
            LastDiagnostic = "the 3D Battler match loop is not running";
            return true;
        }

        var bank = (uint)JunonMinigameStateReader.AddressTemporaryFieldBank;
        if (!memory.TryReadByte(bank + StageOffset, out var stage) ||
            !memory.TryReadByte(bank + PlayerHitsTakenOffset, out var playerHitsTaken) ||
            !memory.TryReadByte(bank + OpponentHitsTakenOffset, out var opponentHitsTaken) ||
            !memory.TryReadByte(bank + MatchOverOffset, out var matchOver))
        {
            LastDiagnostic = "3D Battler temp bank unreadable";
            return false;
        }

        if (stage is < FirstStage or > ExhaustedStage ||
            playerHitsTaken > WinningHits ||
            opponentHitsTaken > WinningHits)
        {
            // The loop owns these bytes while it runs, so a value outside its own
            // range is a torn read rather than a state to report.
            LastDiagnostic =
                $"3D Battler values out of range: stage={stage}, {opponentHitsTaken}-{playerHitsTaken}";
            return false;
        }

        state = new WonderSquare3DBattlerState(
            IsPlaying: true,
            Stage: stage,
            PlayerHitsTaken: playerHitsTaken,
            OpponentHitsTaken: opponentHitsTaken,
            IsFinished: matchOver != 0 ||
                        playerHitsTaken >= WinningHits ||
                        opponentHitsTaken >= WinningHits);
        LastDiagnostic =
            $"3D Battler stage={stage}, opponent hits {opponentHitsTaken}, player hits {playerHitsTaken}, " +
            $"over={matchOver}";
        return true;
    }
}

/// <summary>
/// Says what each exchange resolved to and how the hits stand.
///
/// There is no numeric panel on the cabinet - root's reviewed close frame of a live
/// contest shows none - so nothing here claims to read one. What it reports is the
/// resolved hit, which the fighters' recoil animations show plainly, and a running
/// tally of hits that have already been seen. Both are things a sighted player has
/// watched happen.
///
/// It never reports the opponent's move, because the loop rolls that before the
/// player's own input is read; saying it would be advance knowledge the screen does
/// not give. Counting is per fighter and never presented as one simultaneous
/// exchange: if both counters have moved between two observations, that is two
/// resolved hits seen late, not a double knockdown.
/// </summary>
public sealed class WonderSquare3DBattlerReadout
{
    private bool wasPlaying;
    private int lastStage;
    private int lastPlayerHitsTaken;
    private int lastOpponentHitsTaken;
    private bool announcedFinish;

    public IReadOnlyList<string> Observe(WonderSquare3DBattlerState state)
    {
        if (!state.IsPlaying)
        {
            Reset();
            return [];
        }

        var lines = new List<string>(3);
        if (!wasPlaying)
        {
            wasPlaying = true;
            Baseline(state);
            lines.Add($"{Ordinal(state.Stage)} opponent. First to ten points.");
            return lines;
        }

        if (state.Stage != lastStage)
        {
            // Byte 968 and 972 clear the counters with the stage, so the new pair is
            // the baseline rather than a swing from the previous opponent.
            Baseline(state);
            lines.Add(state.Stage > WonderSquare3DBattlerStateReader.LastOpponentStage
                ? "You have beaten every opponent."
                : $"{Ordinal(state.Stage)} opponent.");
            return lines;
        }

        var opponentHits = state.OpponentHitsTaken - lastOpponentHitsTaken;
        var playerHits = state.PlayerHitsTaken - lastPlayerHitsTaken;
        lastOpponentHitsTaken = state.OpponentHitsTaken;
        lastPlayerHitsTaken = state.PlayerHitsTaken;

        // Each resolved hit is reported in its own right. Two counters moving between
        // one pair of observations means two exchanges were seen late, not one
        // simultaneous one.
        if (opponentHits > 0)
        {
            lines.Add(Hits("You land a hit.", opponentHits, state));
        }

        if (playerHits > 0)
        {
            lines.Add(Hits("They land a hit.", playerHits, state));
        }

        if (state.IsFinished && !announcedFinish && lines.Count > 0)
        {
            announcedFinish = true;
        }

        return lines;
    }

    /// <summary>
    /// How a batch reaches the player.
    ///
    /// <see cref="Observe"/> can return two lines at once, when both counters moved
    /// between one pair of observations and two exchanges were therefore seen late.
    /// Speaking each of them as its own interrupting utterance destroys the batch:
    /// the second cuts the first off part way through, so the player hears "They land
    /// a hit" and never learns they landed one too. Both runtimes used to do exactly
    /// that.
    ///
    /// They are joined into a single utterance instead. It still interrupts, because
    /// a newer exchange supersedes an older one the same way the cabinet's own score
    /// does, but nothing inside the batch can cut off anything else in it.
    /// </summary>
    public static IReadOnlyList<(string Text, bool Interrupt)> Deliver(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var spoken = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        return spoken.Length == 0
            ? Array.Empty<(string, bool)>()
            : [(string.Join(" ", spoken), true)];
    }

    public void Reset()
    {
        wasPlaying = false;
        lastStage = 0;
        lastPlayerHitsTaken = 0;
        lastOpponentHitsTaken = 0;
        announcedFinish = false;
    }

    private void Baseline(WonderSquare3DBattlerState state)
    {
        lastStage = state.Stage;
        lastPlayerHitsTaken = state.PlayerHitsTaken;
        lastOpponentHitsTaken = state.OpponentHitsTaken;
        announcedFinish = false;
    }

    private static string Hits(string lead, int count, WonderSquare3DBattlerState state)
    {
        var tally = $"{state.OpponentHitsTaken} to {state.PlayerHitsTaken}.";
        return count > 1
            ? $"{lead} That is {count} in a row. {tally}"
            : $"{lead} {tally}";
    }

    private static string Ordinal(int stage) => stage switch
    {
        1 => "First",
        2 => "Second",
        3 => "Third",
        4 => "Fourth",
        _ => "Last"
    };
}
