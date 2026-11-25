# Original Steam 2013 delayed-loader startup race

The isolated English Steam 2013 game reproduced an intermittent startup crash
with the released 1.0.2 payload. The original game without the proxy stayed
responsive. The game received private settings and save directories through the
real launcher handshake; installed files and real Documents were read-only inputs.

The retained Reloaded dump records CLR exception `E0434352`, parameter `80131509`,
raised at `KERNELBASE!RaiseException` (`76DCD444` in that process). Its log identifies:

```text
Delay Injection via Entry Point: d3d9, DebugSetMute
System.InvalidOperationException: Collection was modified; enumeration operation may not execute.
   at Reloaded.Mod.Loader.Utilities.DelayInjector.Dispose()
   at Reloaded.Mod.Loader.Utilities.DelayInjector.HookImpl()
```

Reloaded 1.30.3 activates hooks while appending to its lists. A game thread can
enter before construction finishes; cleanup then enumerates a changing list.
Multiple callbacks can also load the mod more than once. Unpatched launches
included duplicate-runtime rejection and an unloading assembly context.

The x86 correction publishes constructor completion with `Volatile`, lets early
native calls pass through without blocking, and allows exactly one callback to
clean up and initialize through `Interlocked.CompareExchange`. Reverse wrappers
remain rooted for native callers already inside disabled hooks. The original
delayed-start mechanism and hook configuration remain. The x64 loader is unchanged.
The diagnostic entry indices are also bounded before logging; a raced pair must
never abort startup. A separate red/green test exercises invalid pairs.

The exact upstream commit, GPL patch, binary hash and build instructions are in
`installer-assets/reloaded/x86/README.md`. Portable builds pin the corrected DLL
and carry its source patch in `LICENSES`; manager packages derive from that archive.

Verification before final packaging:

- `tools/ReloadedDelayInjectionRegression` reproduces the exception with the actual
  released loader. The corrected DLL passes early, concurrent and reentrant
  callback checks without applying native hooks.
- Four separate 45-second launches of the original 2013 game stayed responsive.
  Each log contains one delayed entry and one accessibility startup speech, with
  no duplicate-runtime rejection, monitor error or unhandled exception.
- Windows PowerShell 5.1/Pester 4.10.1 package checks passed 28/28.
- SHA-256 comparisons found zero differences across all 364 installed 2013 files
  and all 46 files in the real 2013 Documents directory after the probes.

These launches establish startup and accepted speech output on this Windows 11
build. They do not establish a full 2013 playthrough, listening quality, or live
execution of the launcher's older-Windows fallback. Private dumps remain outside
the repository and release packages.
