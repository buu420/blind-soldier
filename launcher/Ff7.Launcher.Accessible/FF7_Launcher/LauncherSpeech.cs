using System;

namespace FF7_Launcher;

internal sealed class LauncherSpeech
{
    private readonly ISpeechOutput output;
    private readonly Action<string> log;
    private readonly object sync = new object();
    private string lastDelivered;

    internal LauncherSpeech(ISpeechOutput output, Action<string> log)
    {
        this.output = output ?? throw new ArgumentNullException(nameof(output));
        this.log = log ?? delegate { };
    }

    internal bool Speak(string text, bool interrupt = true)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        lock (sync)
        {
            if (string.Equals(text, lastDelivered, StringComparison.Ordinal))
            {
                return true;
            }

            if (!output.Speak(text, interrupt))
            {
                log("Speech was not delivered: " + text);
                return false;
            }

            lastDelivered = text;
            return true;
        }
    }

    internal void ResetDeduplication()
    {
        lock (sync)
        {
            lastDelivered = null;
        }
    }
}
