# Reloaded-II x86 compatibility assets

Modified by Blind Soldier on November 25, 2026.

Blind Soldier redistributes Reloaded-II 1.30.3 under GPL-3.0. The x86
bootstrapper in this directory contains one compatibility correction for a
process, such as stock 7th Heaven 4.6.1, that has already initialized a
compatible CoreCLR runtime before Reloaded is injected.

The managed loader also corrects a delayed-initialization race reproduced with
the original Steam 2013 game. Native callbacks must wait until construction has
finished publishing the hook lists, and only one callback may initialize mods.
Calls made during construction pass through without blocking the game. Reverse
wrappers remain rooted for native callers already executing a disabled hook.
Diagnostic entry indices are copied and checked before logging, so a mismatched
pair from concurrent native calls cannot prevent initialization.

The original source is tag `1.30.3`, commit
`ba2e470d49a33828742ea00fd33088d3868d0f4b`, from:

https://github.com/Reloaded-Project/Reloaded-II

Apply `Reloaded-II-1.30.3-hostfxr.patch` at that commit and build
`source/Reloaded.Mod.Loader.Bootstrapper/Reloaded.Mod.Bootstrapper.vcxproj`
for `Release|Win32` with Visual Studio 2022. The patch also contains a focused
native test for the accepted hostfxr result range.

For the managed correction, apply `Reloaded-II-1.30.3-delay-injection.patch` at
the same commit and run:

```powershell
dotnet build source/Reloaded.Mod.Loader/Reloaded.Mod.Loader.csproj -c Release -p:Platform=x86 -p:PlatformTarget=x86 -p:RobustILLink=false -p:ContinuousIntegrationBuild=true -nr:false -p:UseSharedCompilation=false
```

The distributed 81,920-byte `Reloaded.Mod.Loader.dll` was built with SDK
10.0.203. Its SHA-256 is
`9910FED400FFAE2CEFA45427E897981E45688660A54BB97B568467AD4ABE7757`.
`tools/ReloadedDelayInjectionRegression` in Blind Soldier's source exercises the
actual loader's early, concurrent and reentrant callbacks without native hooks.

The runtime configuration requests only `Microsoft.NETCore.App` 9.0 and sets
`rollForward` to `Major`. Standalone launches use the private .NET 9 runtime.
Reloaded can also join the .NET 10 CoreCLR already loaded by 7th Heaven 4.6.1.

These files change Blind Soldier's bundled Reloaded x86 host and loader. They do not
replace or edit any file in a 7th Heaven installation.
