namespace FF7_Launcher;

internal interface ISpeechOutput
{
    bool Speak(string text, bool interrupt);
}
