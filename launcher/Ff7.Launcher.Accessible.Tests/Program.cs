using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FF7_Launcher;

internal static class Program
{
    private static int failures;

    [STAThread]
    private static int Main()
    {
        Run("main choice names are localized", MainChoiceNamesAreLocalized);
        Run("failed Prism delivery is retried and accepted delivery is deduplicated", FailedDeliveryIsRetried);
        Run("Prism receives serialized null-terminated UTF-8", PrismReceivesSerializedUtf8);
        Run("Prism dependency path must be absolute", PrismDependencyPathMustBeAbsolute);
        Run("settings controls expose visible labels and values", SettingsControlsExposeVisibleLabelsAndValues);

        Console.WriteLine(failures == 0
            ? "All launcher accessibility tests passed."
            : failures + " launcher accessibility test(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    private static void MainChoiceNamesAreLocalized()
    {
        AssertEqual("Play", LauncherAccessibilityText.GetButtonName(0, new CultureInfo("en")));
        AssertEqual("Options", LauncherAccessibilityText.GetButtonName(1, new CultureInfo("en")));
        AssertEqual("Exit", LauncherAccessibilityText.GetButtonName(2, new CultureInfo("en")));
        AssertEqual("Jouer", LauncherAccessibilityText.GetButtonName(0, new CultureInfo("fr")));
        AssertEqual("Paramètres", LauncherAccessibilityText.GetButtonName(1, new CultureInfo("fr")));
        AssertEqual("Sortir", LauncherAccessibilityText.GetButtonName(2, new CultureInfo("fr")));
        AssertEqual("Spiel", LauncherAccessibilityText.GetButtonName(0, new CultureInfo("de")));
        AssertEqual("Beenden", LauncherAccessibilityText.GetButtonName(2, new CultureInfo("de")));
        AssertEqual("Configuración", LauncherAccessibilityText.GetButtonName(1, new CultureInfo("es")));
        AssertEqual("Salir", LauncherAccessibilityText.GetButtonName(2, new CultureInfo("es")));
        AssertEqual("Play", LauncherAccessibilityText.GetButtonName(0, new CultureInfo("ja")));
        AssertEqual("Setting", LauncherAccessibilityText.GetButtonName(1, new CultureInfo("ja")));
        AssertEqual("Exit", LauncherAccessibilityText.GetButtonName(2, new CultureInfo("ja")));
    }

    private static void FailedDeliveryIsRetried()
    {
        var output = new SequenceSpeechOutput(false, true, true);
        var speech = new LauncherSpeech(output, delegate { });

        AssertFalse(speech.Speak("Play", true));
        AssertTrue(speech.Speak("Play", true));
        AssertTrue(speech.Speak("Play", true));
        AssertEqual(2, output.CallCount);
        AssertTrue(speech.Speak("Settings", true));
        AssertEqual(3, output.CallCount);
    }

    private static void PrismReceivesSerializedUtf8()
    {
        var current = 0;
        var maximum = 0;
        var received = new List<string>();
        var sync = new object();
        var stopped = false;
        var backendFreed = false;
        var contextShutdown = false;
        var libraryFreed = false;

        Func<IntPtr, IntPtr, bool, PrismError> output = (backend, text, interrupt) =>
        {
            var active = Interlocked.Increment(ref current);
            UpdateMaximum(ref maximum, active);
            Thread.Sleep(15);
            lock (sync)
            {
                received.Add(ReadUtf8(text));
            }
            Interlocked.Decrement(ref current);
            return PrismError.Ok;
        };

        using (var speaker = new PrismNativeSpeaker(
            delegate { },
            new IntPtr(1),
            new IntPtr(2),
            output,
            delegate { stopped = true; },
            delegate { backendFreed = true; },
            delegate { contextShutdown = true; },
            delegate { libraryFreed = true; },
            new IntPtr(3)))
        {
            var tasks = new Task[6];
            for (var index = 0; index < tasks.Length; index++)
            {
                var text = index == 0 ? "Café 日本語" : "choice " + index;
                tasks[index] = Task.Run(() => AssertTrue(speaker.Speak(text, true)));
            }
            Task.WaitAll(tasks);
        }

        AssertEqual(1, maximum);
        AssertTrue(received.Contains("Café 日本語"));
        AssertTrue(stopped);
        AssertTrue(backendFreed);
        AssertTrue(contextShutdown);
        AssertTrue(libraryFreed);
    }

    private static void PrismDependencyPathMustBeAbsolute()
    {
        AssertFalse(PrismNativeSpeaker.IsAbsoluteLibraryPath("prism.dll"));
        AssertTrue(PrismNativeSpeaker.IsAbsoluteLibraryPath(@"C:\launcher_accessibility\native\x86\FFVII_LAUNCHER.prism.x86.dll"));
    }

    private static void SettingsControlsExposeVisibleLabelsAndValues()
    {
        using (var label = new Label { Text = "Master volume:" })
        using (var slider = new TrackBar { Minimum = 0, Maximum = 100, Value = 65 })
        using (var languageLabel = new Label { Text = "&Language" })
        using (var language = new ComboBox())
        using (var apply = new Button { Text = "&Apply", Enabled = false })
        {
            language.Items.Add("English");
            language.SelectedIndex = 0;
            SettingsAccessibility.NameControl(slider, label);
            SettingsAccessibility.NameControl(language, languageLabel);
            SettingsAccessibility.NameButton(apply);

            AssertEqual("Master volume", slider.AccessibleName);
            AssertEqual("Master volume, 65 percent", SettingsAccessibility.Describe(slider));
            AssertEqual("Language, English", SettingsAccessibility.Describe(language));
            AssertEqual("Apply, unavailable", SettingsAccessibility.Describe(apply));
            AssertEqual(AccessibleRole.Slider, slider.AccessibleRole);
            AssertEqual(AccessibleRole.ComboBox, language.AccessibleRole);
            AssertEqual(AccessibleRole.PushButton, apply.AccessibleRole);
        }
    }

    private static string ReadUtf8(IntPtr pointer)
    {
        var bytes = new List<byte>();
        for (var offset = 0; ; offset++)
        {
            var value = Marshal.ReadByte(pointer, offset);
            if (value == 0)
            {
                break;
            }
            bytes.Add(value);
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        int observed;
        do
        {
            observed = maximum;
            if (candidate <= observed)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref maximum, candidate, observed) != observed);
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine("PASS: " + name);
        }
        catch (Exception exception)
        {
            failures++;
            Console.WriteLine("FAIL: " + name);
            Console.WriteLine(exception);
        }
    }

    private static void AssertTrue(bool value)
    {
        if (!value)
        {
            throw new InvalidOperationException("Expected true.");
        }
    }

    private static void AssertFalse(bool value)
    {
        if (value)
        {
            throw new InvalidOperationException("Expected false.");
        }
    }

    private static void AssertEqual<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException("Expected '" + expected + "' but got '" + actual + "'.");
        }
    }

    private sealed class SequenceSpeechOutput : ISpeechOutput
    {
        private readonly Queue<bool> results;

        public SequenceSpeechOutput(params bool[] results)
        {
            this.results = new Queue<bool>(results);
        }

        public int CallCount { get; private set; }

        public bool Speak(string text, bool interrupt)
        {
            CallCount++;
            return results.Count == 0 || results.Dequeue();
        }
    }
}
