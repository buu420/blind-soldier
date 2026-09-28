namespace Ff7.Accessibility.Core;

/// <summary>
/// How a native choice list (field ASK) is first announced, in both runtimes.
/// <para>A sighted player sees every option at once, with the cursor on one. Speech gives one
/// option at a time, and an ASK whose choice page has no question line (niv_w dialog 8: the
/// question is the page before) opens on an option that sounds like any other line of
/// dialogue. So the first option said for each list also says it is a choice, where it is and
/// how many there are. Moving the cursor after that says the option alone.</para>
/// </summary>
public static class DialogueChoiceSpeech
{
    public static string FormatOpening(string choice, int position, int count) =>
        count > 1 && position >= 1 && position <= count
            ? $"{choice}, choice {position} of {count}"
            : choice;
}
