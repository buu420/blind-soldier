using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FF7_Launcher;

internal static class Program
{
    private static int failures;

    // The probe runs in a child copy of this process so that an ABI mismatch, which
    // corrupts the stack inside native code, kills the child and is reported as a
    // failure here rather than taking the whole test run down with it.
    private const string PrismAbiProbeSwitch = "--prism-abi-probe";
    private const int PrismAbiProbeTimeoutMilliseconds = 10000;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == PrismAbiProbeSwitch)
        {
            return RunPrismAbiProbe(args[1]);
        }

        Run("main choice names are localized", MainChoiceNamesAreLocalized);
        Run("failed Prism delivery is retried and accepted delivery is deduplicated", FailedDeliveryIsRetried);
        Run("Prism receives serialized null-terminated UTF-8", PrismReceivesSerializedUtf8);
        Run("Prism dependency path must be absolute", PrismDependencyPathMustBeAbsolute);
        Run("Prism ABI round-trips through the launcher binding against the shipped library", PrismAbiRoundTripsAgainstTheShippedLibrary);
        Run("settings controls expose visible labels and values", SettingsControlsExposeVisibleLabelsAndValues);
        Run("Play launches only the x64 Blind Soldier broker", PlayLaunchesOnlyTheX64Broker);
        Run("missing launch files fail closed with an accessible error", MissingLaunchFilesFailClosed);
        Run("broker failures include cause action and absolute log", BrokerFailuresAreActionable);

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

    private static void PrismAbiRoundTripsAgainstTheShippedLibrary()
    {
        // The static guard (PrismAbiContract.psm1) proves the launcher's PrismConfig
        // agrees with the mod's and with the pinned size. Only this proves either of
        // them agrees with the DLL actually shipped: the launcher's own delegates,
        // in the launcher's own runtime (.NET Framework, x86), against the binary the
        // bundle copies. 0.4.1 would have died here instead of on every user's desk.
        //
        // Deliberately stops at prism_init. Backend selection is where Prism loads
        // screen readers and audio, and a headless runner has neither; a gate that
        // fails for environmental reasons teaches people to bypass it.
        var library = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "launcher_accessibility", "native", "x86", "FFVII_LAUNCHER.prism.x86.dll");
        AssertTrue(File.Exists(library));
        AssertEqual(32, Marshal.SizeOf(typeof(PrismConfig)));

        var startInfo = new ProcessStartInfo(
            typeof(Program).Assembly.Location,
            PrismAbiProbeSwitch + " \"" + library + "\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        // PRISM_LOG makes prism_init start a logging thread; keep the probe to the
        // one thing it is measuring.
        startInfo.EnvironmentVariables.Remove("PRISM_LOG");

        string stdout;
        string stderr;
        int exitCode;
        using (var child = Process.Start(startInfo))
        {
            var stdoutTask = child.StandardOutput.ReadToEndAsync();
            var stderrTask = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit(PrismAbiProbeTimeoutMilliseconds))
            {
                try { child.Kill(); } catch { }
                throw new InvalidOperationException(
                    "The Prism ABI probe did not finish within " +
                    PrismAbiProbeTimeoutMilliseconds + " ms.");
            }
            stdout = stdoutTask.Result;
            stderr = stderrTask.Result;
            exitCode = child.ExitCode;
        }

        if (exitCode != 0)
        {
            // 0xC0000005 here is the 0.4.1 crash, reproduced on purpose.
            throw new InvalidOperationException(
                "The Prism ABI probe exited with 0x" + exitCode.ToString("X8") +
                ".\n--- stdout ---\n" + stdout + "\n--- stderr ---\n" + stderr);
        }

        var evidence = new Dictionary<string, string>();
        foreach (var line in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('=');
            if (separator > 0)
            {
                evidence[line.Substring(0, separator)] = line.Substring(separator + 1);
            }
        }

        AssertEqual("32", Evidence(evidence, "PRISM_CONFIG_SIZE"));
        AssertEqual(PrismNativeSpeaker.SupportedPrismConfigVersion.ToString(),
            Evidence(evidence, "PRISM_CONFIG_VERSION"));
        AssertEqual("True", Evidence(evidence, "PRISM_CONTEXT_CREATED"));
        AssertEqual("True", Evidence(evidence, "PRISM_SHUTDOWN_COMPLETED"));
    }

    private static string Evidence(Dictionary<string, string> evidence, string key)
    {
        string value;
        if (!evidence.TryGetValue(key, out value))
        {
            throw new InvalidOperationException("The Prism ABI probe reported no " + key + ".");
        }
        return value;
    }

    private static int RunPrismAbiProbe(string library)
    {
        try
        {
            var result = PrismNativeSpeaker.ProbeAbi(library, Console.Error.WriteLine);
            Console.WriteLine("PRISM_CONFIG_SIZE=" + result.ConfigSize);
            Console.WriteLine("PRISM_CONFIG_VERSION=" + result.ConfigVersion);
            Console.WriteLine("PRISM_CONTEXT_CREATED=" + result.ContextCreated);
            Console.WriteLine("PRISM_SHUTDOWN_COMPLETED=" + result.ShutdownCompleted);
            return result.ContextCreated && result.ShutdownCompleted ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.WriteLine("PRISM_PROBE_FAILURE=" + exception.Message);
            return 1;
        }
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

    private static void PlayLaunchesOnlyTheX64Broker()
    {
        var root = NewLaunchRoot();
        try
        {
            var game = Path.Combine(root, "FFVII.exe");
            var bootstrap = Path.Combine(root,
                BlindSoldierGameLauncher.BootstrapRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(bootstrap));
            File.WriteAllBytes(game, new byte[] { 1 });
            File.WriteAllBytes(bootstrap, new byte[] { 2 });
            var runner = new RecordingGameProcessRunner { ExitCode = 0 };
            var launchId = new Guid("11111111-2222-3333-4444-555555555555");
            var launcher = new BlindSoldierGameLauncher(runner,
                delegate { return launchId; });

            string error;
            AssertTrue(launcher.TryLaunch(root, "jp", out error));
            AssertEqual(string.Empty, error);
            AssertEqual(1, runner.CallCount);
            AssertEqual(Path.GetFullPath(bootstrap), runner.StartInfo.FileName);
            AssertFalse(runner.StartInfo.UseShellExecute);
            AssertEqual(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar),
                runner.StartInfo.WorkingDirectory);
            AssertTrue(runner.StartInfo.Arguments.Contains("--launch"));
            AssertTrue(runner.StartInfo.Arguments.Contains(
                "--root \"" + Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + "\""));
            AssertTrue(runner.StartInfo.Arguments.Contains(
                "--game \"" + Path.GetFullPath(game) + "\""));
            AssertTrue(runner.StartInfo.Arguments.Contains(
                "--launch-id 11111111-2222-3333-4444-555555555555"));
            AssertTrue(runner.StartInfo.Arguments.EndsWith("--game-arguments jp",
                StringComparison.Ordinal));
            AssertFalse(string.Equals(runner.StartInfo.FileName, game,
                StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(root, true); }
    }

    private static void MissingLaunchFilesFailClosed()
    {
        var root = NewLaunchRoot();
        try
        {
            var runner = new RecordingGameProcessRunner();
            var launcher = new BlindSoldierGameLauncher(runner,
                delegate { return Guid.Empty; });
            string error;
            AssertFalse(launcher.TryLaunch(root, "en", out error));
            AssertTrue(error.Contains("FFVII.exe"));
            AssertTrue(error.Contains("Action:"));
            AssertTrue(Path.IsPathRooted(ExtractLogPath(error)));
            AssertEqual(0, runner.CallCount);

            File.WriteAllBytes(Path.Combine(root, "FFVII.exe"),
                new byte[] { 1 });
            AssertFalse(launcher.TryLaunch(root, "en", out error));
            AssertTrue(error.Contains("Blind-Soldier-Bootstrap-x64.exe"));
            AssertEqual(0, runner.CallCount);
        }
        finally { Directory.Delete(root, true); }
    }

    private static void BrokerFailuresAreActionable()
    {
        var root = NewLaunchRoot();
        try
        {
            var game = Path.Combine(root, "FFVII.exe");
            var bootstrap = Path.Combine(root,
                BlindSoldierGameLauncher.BootstrapRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(bootstrap));
            File.WriteAllBytes(game, new byte[] { 1 });
            File.WriteAllBytes(bootstrap, new byte[] { 2 });
            var launchId = new Guid("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE");

            foreach (var exitCode in new[] { 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 99 })
            {
                var runner = new RecordingGameProcessRunner { ExitCode = exitCode };
                var launcher = new BlindSoldierGameLauncher(runner,
                    delegate { return launchId; });
                string error;
                AssertFalse(launcher.TryLaunch(root, "en", out error));
                AssertTrue(error.Contains("Cause:"));
                AssertTrue(error.Contains("Action:"));
                AssertTrue(error.Contains("Blind-Soldier-Bootstrap-x64-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee.log"));
                AssertFalse(runner.StartInfo.Arguments.Contains("--game-arguments"));
            }

            var throwingRunner = new RecordingGameProcessRunner
            {
                Exception = new InvalidOperationException("fixture start failure")
            };
            var throwingLauncher = new BlindSoldierGameLauncher(throwingRunner,
                delegate { return launchId; });
            string startError;
            AssertFalse(throwingLauncher.TryLaunch(root, "en", out startError));
            AssertTrue(startError.Contains("fixture start failure"));
            AssertTrue(startError.Contains("Action:"));
        }
        finally { Directory.Delete(root, true); }
    }

    private static string NewLaunchRoot()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "blind soldier launcher test " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string ExtractLogPath(string error)
    {
        const string marker = "Log: ";
        var index = error.IndexOf(marker, StringComparison.Ordinal);
        return index < 0 ? string.Empty : error.Substring(index + marker.Length).Trim();
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

    private sealed class RecordingGameProcessRunner : IGameProcessRunner
    {
        public int ExitCode { get; set; }
        public Exception Exception { get; set; }
        public int CallCount { get; private set; }
        public ProcessStartInfo StartInfo { get; private set; }

        public int Run(ProcessStartInfo startInfo)
        {
            CallCount++;
            StartInfo = startInfo;
            if (Exception != null) throw Exception;
            return ExitCode;
        }
    }
}
