using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// The native popup the save path raises when a save transaction finishes.
/// </summary>
/// <param name="IsActive">
/// The popup is up. <c>FUN_006C4970</c> is the native accessor and returns this byte.
/// </param>
/// <param name="TextIdentity">
/// Which text the popup is showing, as the native pointer identity rather than as
/// decoded English. Comparing the identity is what makes this independent of the
/// player's language and of whether any speech option is switched on.
/// </param>
public readonly record struct NativeSaveResultPopup(bool IsActive, uint TextIdentity)
{
    /// <summary>The save completed.</summary>
    public bool IsSuccess => IsActive && TextIdentity == NativeSaveResultPopupReader.SuccessTextIdentity;

    /// <summary>The save failed.</summary>
    public bool IsFailure => IsActive && TextIdentity == NativeSaveResultPopupReader.FailureTextIdentity;
}

/// <summary>
/// Reads whether a native save actually succeeded.
///
/// <para>Nothing else in the save path can answer that question. <c>FUN_006FEB6D</c>
/// returns to page 1 on <b>both</b> success and failure, so the page transition is not
/// evidence; and it updates the savemap checksum and preview <em>before</em> it opens the
/// file, so a changed checksum is not evidence either - the write can still fail after it.
/// What does distinguish them is the call the save path makes afterwards:
/// <c>FUN_006C497C(0x00925B50, 7)</c> on a zero return and <c>FUN_006C497C(0x00925EA8, ...)</c>
/// otherwise. That function sets the popup-active byte at <c>0x00DC1310</c> and stores its
/// first argument - the text identity - at <c>0x00DC1214</c>.</para>
///
/// <para>Both reads are bookended: the active byte is read, then the identity, then the
/// active byte again, and a disagreement is reported as no reading at all. A torn snapshot
/// must never be allowed to bind a playthrough to the wrong save.</para>
/// </summary>
public sealed class NativeSaveResultPopupReader
{
    /// <summary>Set by <c>FUN_006C497C</c> while a result popup is on screen.</summary>
    public const int AddressPopupActive = 0x00DC1310;

    /// <summary>The popup's text identity, stored from the caller's first argument.</summary>
    public const int AddressPopupTextIdentity = 0x00DC1214;

    /// <summary>The identity the save path passes when <c>FUN_006FEB6D</c> returned zero.</summary>
    public const uint SuccessTextIdentity = 0x00925B50;

    /// <summary>The identity it passes when the save did not complete.</summary>
    public const uint FailureTextIdentity = 0x00925EA8;

    private readonly ILegacyAddressSpace memory;

    public NativeSaveResultPopupReader(ILegacyAddressSpace memory)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    /// <summary>
    /// The popup as it stands, or false when it could not be read coherently. A failed
    /// read is never reported as "no popup": the caller has to be able to tell a save
    /// that did not finish from a sample it could not take.
    /// </summary>
    public bool TryRead(out NativeSaveResultPopup popup)
    {
        popup = default;
        if (!memory.TryReadByte((uint)AddressPopupActive, out var before))
        {
            return false;
        }

        if (before == 0)
        {
            // Nothing on screen. The identity left behind belongs to whatever was shown
            // last and says nothing about now, so it is not read at all.
            popup = new NativeSaveResultPopup(false, 0);
            return true;
        }

        // FUN_006C497C writes exactly 1. Anything else is a byte this reader does not
        // understand, and guessing about it could bind a playthrough to the wrong save.
        if (before != 1)
        {
            return false;
        }

        if (!memory.TryReadUInt32((uint)AddressPopupTextIdentity, out var identity) ||
            identity is 0 or uint.MaxValue ||
            !memory.TryReadByte((uint)AddressPopupActive, out var after) ||
            after != before)
        {
            return false;
        }

        popup = new NativeSaveResultPopup(true, identity);
        return true;
    }
}
