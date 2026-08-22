using System.Diagnostics;
using System.Runtime.InteropServices;
using Ff7.Accessibility.Reloaded;

/// <summary>
/// Round-trips <see cref="PrismConfig"/> through the prism.dll this runtime actually ships,
/// using the same DllImport declarations the mod speaks through.
/// </summary>
/// <remarks>
/// Compiled into both runtimes' test projects, so the x86 build measures the x86 DLL and the
/// x64 build measures the x64 one. Before this existed the x64 size of 48 bytes had only ever
/// been computed, never observed; every mod test handed <see cref="PrismNativeSpeaker"/> fake
/// handles and none touched the library.
///
/// <para>The probe runs in a child copy of the test host, because an ABI mismatch corrupts the
/// stack inside native code: the child dies with an access violation and this test reports it,
/// instead of the whole run vanishing. It stops at prism_init and leaves the availability
/// callback null, so Prism neither starts its poll thread nor loads a screen reader, and the
/// check holds on a headless build machine.</para>
/// </remarks>
internal static class PrismAbiProbeTests
{
    internal const string ProbeSwitch = "--prism-abi-probe";
    private const int ProbeTimeoutMilliseconds = 10000;

    public static void Run()
    {
        var expectedSize = nint.Size == 4 ? 32 : 48;
        AssertEqual(expectedSize, Marshal.SizeOf<PrismConfig>(), "marshalled PrismConfig size in this process");

        var library = Path.Combine(AppContext.BaseDirectory, "prism.dll");
        if (!File.Exists(library))
        {
            throw new InvalidOperationException($"prism.dll is not beside the test host: {library}");
        }

        var host = Environment.ProcessPath
            ?? throw new InvalidOperationException("The test host's own path is unknown, so it cannot spawn the probe.");
        var startInfo = new ProcessStartInfo(host)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(ProbeSwitch);
        // PRISM_LOG makes prism_init start a logging thread; keep the probe to the one thing it
        // is measuring.
        startInfo.Environment.Remove("PRISM_LOG");

        using var child = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The Prism ABI probe process did not start.");
        var stdoutTask = child.StandardOutput.ReadToEndAsync();
        var stderrTask = child.StandardError.ReadToEndAsync();
        if (!child.WaitForExit(ProbeTimeoutMilliseconds))
        {
            try { child.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException(
                $"The Prism ABI probe did not finish within {ProbeTimeoutMilliseconds} ms.");
        }

        var stdout = stdoutTask.Result;
        var stderr = stderrTask.Result;
        if (child.ExitCode != 0)
        {
            // 0xC0000005 here is the crash every 0.4.1 launcher user saw, reproduced on purpose.
            throw new InvalidOperationException(
                $"The Prism ABI probe exited with 0x{child.ExitCode:X8}.\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        }

        var evidence = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('=');
            if (separator > 0)
            {
                evidence[line[..separator]] = line[(separator + 1)..];
            }
        }

        AssertEqual(nint.Size.ToString(), Evidence(evidence, "PRISM_POINTER_SIZE"), "probe pointer size");
        AssertEqual(expectedSize.ToString(), Evidence(evidence, "PRISM_CONFIG_SIZE"), "probe PrismConfig size");
        AssertEqual(
            PrismNativeSpeaker.SupportedPrismConfigVersion.ToString(),
            Evidence(evidence, "PRISM_CONFIG_VERSION"),
            "PRISM_CONFIG_VERSION reported by prism_config_init");
        AssertEqual("True", Evidence(evidence, "PRISM_CONTEXT_CREATED"), "prism_init returned a context");
        AssertEqual("True", Evidence(evidence, "PRISM_SHUTDOWN_COMPLETED"), "prism_shutdown returned");

        // The declaration has to be proved against the DLL this runtime ships, not whichever
        // prism.dll happened to be first on the search path.
        var loaded = Evidence(evidence, "PRISM_MODULE_PATH");
        if (!string.Equals(Path.GetFullPath(loaded), Path.GetFullPath(library), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The probe loaded {loaded}, not the prism.dll beside the test host ({library}).");
        }
    }

    /// <summary>The child side: runs the probe and prints its evidence as KEY=VALUE lines.</summary>
    public static int RunProbeChild()
    {
        try
        {
            var result = PrismNativeSpeaker.ProbeAbi();
            Console.WriteLine($"PRISM_POINTER_SIZE={result.PointerSize}");
            Console.WriteLine($"PRISM_CONFIG_SIZE={result.ConfigSize}");
            Console.WriteLine($"PRISM_CONFIG_VERSION={result.ConfigVersion}");
            Console.WriteLine($"PRISM_CONTEXT_CREATED={result.ContextCreated}");
            Console.WriteLine($"PRISM_SHUTDOWN_COMPLETED={result.ShutdownCompleted}");
            Console.WriteLine($"PRISM_LIBRARY={result.Library}");
            Console.WriteLine($"PRISM_MODULE_PATH={result.ModulePath}");
            return result.ContextCreated && result.ShutdownCompleted ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"PRISM_PROBE_FAILURE={exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static string Evidence(Dictionary<string, string> evidence, string key)
    {
        return evidence.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException($"The Prism ABI probe reported no {key}.");
    }

    private static void AssertEqual<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {what} to be '{expected}' but it was '{actual}'.");
        }
    }
}
