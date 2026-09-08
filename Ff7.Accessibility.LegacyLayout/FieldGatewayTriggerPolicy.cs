namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// The fields whose own Director switches every static gateway off, so that the gateway
/// table still describes doors the game will never open.
///
/// <para>FFVII's <c>MPJPO</c> opcode disables all gateway triggers at once; the field's
/// movement handler then skips the gateway crossing check entirely
/// (<c>FUN_00636c41</c> guards its call to <c>FUN_00637eba</c> on that flag). A field that
/// calls it in its Director's <c>Init</c> and never re-enables has a gateway table full of
/// doors that cannot be taken for the whole visit.</para>
///
/// <para>This was found the hard way. In the 2026-09-08 recording the player selected
/// "Exit to Ticket Office" in the Chocobo Square racing room and auto walk drove at it for
/// ninety-nine consecutive samples, crossing the gateway's own line each time, without the
/// field ever changing - because <c>crcin_2</c>'s Director calls <c>MPJPO</c> at byte 191
/// of its Init. Offering a door the game has switched off is worse than offering nothing:
/// the player is told there is a way out where there is not one, and auto walk grinds
/// against it until they take over.</para>
///
/// <para>The rule an entry has to meet is stricter than it first looks, and the first
/// version of this list got it wrong. It is not enough that the Director's Init contains a
/// disabling <c>MPJPO</c>: that call has to actually run on every path out of Init. The
/// Junon airfield and the Icicle Inn slope both reach theirs only inside a GameMoment
/// branch - junair's IFSW at byte 15 and IFUB at 23 both jump to 41, past the MPJPO at 33,
/// and itown12's IFSW at 14 jumps to 35, past its MPJPO at 33 - so their doors do work and
/// listing them would have hidden real exits. Absence of a re-enable proves nothing about
/// whether the disable ever ran.</para>
///
/// <para><c>FieldGatewayTriggerPolicyTests</c> therefore re-derives the whole list from the
/// installed scripts and checks that no branch before the disable can jump over it, so the
/// list cannot drift from the data it claims to describe. Fields that disable and then
/// re-enable, such as <c>crcin_1</c>, are absent for the same reason: their gateways do
/// work, at the times the script says.</para>
///
/// <para>Scripted exits are untouched. <c>MPJPO</c> only stops the static gateway table;
/// a LINE whose script performs a MAPJUMP still works, which is exactly how the prison and
/// the Highwind cockpit are left.</para>
/// </summary>
public static class FieldGatewayTriggerPolicy
{
    private static readonly HashSet<int> FieldsWithoutWorkingGateways =
    [
        70,  // fship_23, the Highwind cockpit: the way out is entity 6's own [OK] line.
        471, // jail1, the Corel Prison drop point: four dead gateways to jail3.
        478, // jail3.
        479, // jail4.
        512, // crcin_2, the Chocobo Square racing room: leave by talking to Ester.
        706, // trnad_51, the Whirlwind Maze struggle room.
        285, // nivl_4, the Nibelheim illusion: eight dead gateways, disabled at Init byte 4.
        482, // desert2, and
        765  // las4_2 both disable at Init and carry no gateway at all, so this changes
             // nothing for them; they are listed so the rule and the list agree.
    ];

    /// <summary>
    /// Whether this field's static gateway table describes doors the game will honour.
    /// </summary>
    public static bool AreGatewayTriggersUsable(int fieldId) =>
        !FieldsWithoutWorkingGateways.Contains(fieldId);

    /// <summary>
    /// The fields this policy suppresses, in ascending order, for tests and diagnostics.
    /// </summary>
    public static IReadOnlyList<int> SuppressedFields =>
        FieldsWithoutWorkingGateways.Order().ToArray();
}
