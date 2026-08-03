namespace Ff7.Accessibility.Reloaded;

public sealed class FieldOpcodeMessageSpeechGate
{
    private readonly HashSet<string> activeKeys = new(StringComparer.Ordinal);

    public bool ShouldQueue(string key, int result)
    {
        if (key.Length == 0)
        {
            return false;
        }

        if (result == 0)
        {
            activeKeys.Remove(key);
            return false;
        }

        return activeKeys.Add(key);
    }

    public void Reset() => activeKeys.Clear();
}
