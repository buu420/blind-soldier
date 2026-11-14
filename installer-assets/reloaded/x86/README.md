# Reloaded-II x86 host compatibility asset

Blind Soldier redistributes Reloaded-II 1.30.3 under GPL-3.0. The x86
bootstrapper in this directory contains one compatibility correction for a
process, such as stock 7th Heaven 4.6.1, that has already initialized a
compatible CoreCLR runtime before Reloaded is injected.

The original source is tag `1.30.3`, commit
`ba2e470d49a33828742ea00fd33088d3868d0f4b`, from:

https://github.com/Reloaded-Project/Reloaded-II

Apply `Reloaded-II-1.30.3-hostfxr.patch` at that commit and build
`source/Reloaded.Mod.Loader.Bootstrapper/Reloaded.Mod.Bootstrapper.vcxproj`
for `Release|Win32` with Visual Studio 2022. The patch also contains a focused
native test for the accepted hostfxr result range.

The runtime configuration requests only `Microsoft.NETCore.App` 9.0 and sets
`rollForward` to `Major`. Standalone launches use the private .NET 9 runtime.
Reloaded can also join the .NET 10 CoreCLR already loaded by 7th Heaven 4.6.1.

These files change Blind Soldier's bundled Reloaded x86 host only. They do not
replace or edit any file in a 7th Heaven installation.
