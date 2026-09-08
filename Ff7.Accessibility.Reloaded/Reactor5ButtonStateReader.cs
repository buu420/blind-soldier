using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Gathers what the Reactor 5 button event is visibly doing: whether 128:0's script 3 is
/// still holding everyone and listening, and what Barret's and Tifa's models are playing.
///
/// <para>The event holds the party frozen throughout while it waits for a Confirm, so
/// nothing here may be gated on the player being in control - that would silence the one
/// moment the cue exists for. The identity used instead is the director's own script,
/// which only runs while the game moment is still short of 128.</para>
/// </summary>
public sealed class Reactor5ButtonStateReader
{
    /// <summary>128:0:3 returns immediately once the moment has reached this.</summary>
    public const int CompletedGameMoment = 128;

    /// <summary>
    /// A message window the field is showing, and a movie covering it. Neither is a
    /// reason to stop reading the party's pose, but both mean the screen is not the one
    /// the tone belongs to, so the event is reported as not listening.
    ///
    /// <para>The party being frozen is emphatically not such a reason: this event holds
    /// them still throughout while it waits for a Confirm, so nothing here looks at the
    /// user-control byte.</para>
    /// </summary>
    public const int AddressActiveFieldMessageCount =
        FieldAudibleCueStateReader.AddressActiveFieldMessageCount;
    public const int AddressFieldMovieActive = FieldAudibleCueStateReader.AddressFieldMovieActive;

    private readonly FieldScriptControllerReader controllers;
    private readonly Func<int, byte> readByte;

    public Reactor5ButtonStateReader(ILegacyAddressSpace memory, Func<int, byte> readByte)
    {
        controllers = new FieldScriptControllerReader(
            memory ?? throw new ArgumentNullException(nameof(memory)));
        this.readByte = readByte ?? throw new ArgumentNullException(nameof(readByte));
    }

    public bool TryRead(int fieldId, out Reactor5ButtonObservation observation)
    {
        observation = default;
        if (fieldId != Reactor5ButtonCueTracker.FieldId)
        {
            return false;
        }

        if (!controllers.TryRead(
                fieldId,
                Reactor5ButtonCueTracker.DirectorEntityId,
                Reactor5ButtonCueTracker.DirectorScriptId,
                out var director) ||
            !controllers.TryRead(
                fieldId,
                Reactor5ButtonCueTracker.BarretEntityId,
                Reactor5ButtonCueTracker.PartnerScriptId,
                out var barret) ||
            !controllers.TryRead(
                fieldId,
                Reactor5ButtonCueTracker.TifaEntityId,
                Reactor5ButtonCueTracker.PartnerScriptId,
                out var tifa))
        {
            return false;
        }

        observation = new Reactor5ButtonObservation(
            director.IsControllerActive &&
                ReadGameMoment() < CompletedGameMoment &&
                !IsScreenTakenByDialogueOrMovie(),
            barret,
            tifa);
        return true;
    }

    private bool IsScreenTakenByDialogueOrMovie() =>
        readByte(AddressActiveFieldMessageCount) != 0 ||
        readByte(AddressFieldMovieActive) != 0 ||
        readByte(AddressFieldMovieActive + 1) != 0;

    private int ReadGameMoment() =>
        readByte(FieldNavigationObjectReader.AddressFieldBankBase) |
        (readByte(FieldNavigationObjectReader.AddressFieldBankBase + 1) << 8);
}
