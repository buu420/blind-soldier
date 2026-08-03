namespace Ff7.Accessibility.Reloaded;

public static class FieldOpcodeMessageTextSelector
{
    public static FieldMessageCandidate Select(
        string opcodeSource,
        FieldMessageCandidate messageTableCandidate,
        FieldMessageCandidate flevelCandidate,
        FieldMessageCandidate dialogPointerCandidate)
    {
        var selected = messageTableCandidate.Text.Length != 0
            ? messageTableCandidate
            : flevelCandidate.Text.Length != 0
                ? flevelCandidate
                : new FieldMessageCandidate(string.Empty, string.Empty);

        return selected.Text.Length == 0
            ? new FieldMessageCandidate(string.Empty, string.Empty)
            : new FieldMessageCandidate(opcodeSource, selected.Text);
    }

    public static string CreateDuplicateKey(FieldMessageCandidate candidate) =>
        candidate.Source;
}
