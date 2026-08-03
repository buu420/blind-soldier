using System;
using System.Runtime.InteropServices;

namespace Ff7.Accessibility.Reloaded;

internal static unsafe class SteamAudioNative
{
    public const uint Version = 263681;
    public const int HrtfTypeDefault = 0;
    public const int HrtfNormTypeNone = 0;
    public const int HrtfInterpolationBilinear = 1;

    public enum Error
    {
        Success = 0,
        Failure = 1,
        OutOfMemory = 2,
        Initialization = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct Vector3
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public Vector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ContextSettings
    {
        public uint Version;
        public IntPtr LogCallback;
        public IntPtr AllocateCallback;
        public IntPtr FreeCallback;
        public int SimdLevel;
        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioSettings
    {
        public int SamplingRate;
        public int FrameSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HrtfSettings
    {
        public int Type;
        public IntPtr SofaFileName;
        public IntPtr SofaData;
        public int SofaDataSize;
        public float Volume;
        public int NormType;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BinauralEffectSettings
    {
        public IntPtr Hrtf;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BinauralEffectParams
    {
        public Vector3 Direction;
        public int Interpolation;
        public float SpatialBlend;
        public IntPtr Hrtf;
        public IntPtr PeakDelays;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioBuffer
    {
        public int NumChannels;
        public int NumSamples;
        public IntPtr Data;
    }

    public static string BindingName => IsX64Process
        ? "x64 undecorated exports"
        : "x86 stdcall-decorated exports";

    public static Error ContextCreate(in ContextSettings settings, out IntPtr context) =>
        IsX64Process
            ? X64Imports.ContextCreate(in settings, out context)
            : X86Imports.ContextCreate(in settings, out context);

    public static void ContextRelease(ref IntPtr context)
    {
        if (IsX64Process)
        {
            X64Imports.ContextRelease(ref context);
        }
        else
        {
            X86Imports.ContextRelease(ref context);
        }
    }

    public static Error HrtfCreate(
        IntPtr context,
        in AudioSettings audioSettings,
        in HrtfSettings settings,
        out IntPtr hrtf) =>
        IsX64Process
            ? X64Imports.HrtfCreate(context, in audioSettings, in settings, out hrtf)
            : X86Imports.HrtfCreate(context, in audioSettings, in settings, out hrtf);

    public static void HrtfRelease(ref IntPtr hrtf)
    {
        if (IsX64Process)
        {
            X64Imports.HrtfRelease(ref hrtf);
        }
        else
        {
            X86Imports.HrtfRelease(ref hrtf);
        }
    }

    public static Error BinauralEffectCreate(
        IntPtr context,
        in AudioSettings audioSettings,
        in BinauralEffectSettings settings,
        out IntPtr effect) =>
        IsX64Process
            ? X64Imports.BinauralEffectCreate(context, in audioSettings, in settings, out effect)
            : X86Imports.BinauralEffectCreate(context, in audioSettings, in settings, out effect);

    public static void BinauralEffectRelease(ref IntPtr effect)
    {
        if (IsX64Process)
        {
            X64Imports.BinauralEffectRelease(ref effect);
        }
        else
        {
            X86Imports.BinauralEffectRelease(ref effect);
        }
    }

    public static void BinauralEffectReset(IntPtr effect)
    {
        if (IsX64Process)
        {
            X64Imports.BinauralEffectReset(effect);
        }
        else
        {
            X86Imports.BinauralEffectReset(effect);
        }
    }

    public static int BinauralEffectApply(
        IntPtr effect,
        in BinauralEffectParams parameters,
        in AudioBuffer inputBuffer,
        ref AudioBuffer outputBuffer) =>
        IsX64Process
            ? X64Imports.BinauralEffectApply(effect, in parameters, in inputBuffer, ref outputBuffer)
            : X86Imports.BinauralEffectApply(effect, in parameters, in inputBuffer, ref outputBuffer);

    public static Error AudioBufferAllocate(
        IntPtr context,
        int numChannels,
        int numSamples,
        ref AudioBuffer audioBuffer) =>
        IsX64Process
            ? X64Imports.AudioBufferAllocate(context, numChannels, numSamples, ref audioBuffer)
            : X86Imports.AudioBufferAllocate(context, numChannels, numSamples, ref audioBuffer);

    public static void AudioBufferFree(IntPtr context, ref AudioBuffer audioBuffer)
    {
        if (IsX64Process)
        {
            X64Imports.AudioBufferFree(context, ref audioBuffer);
        }
        else
        {
            X86Imports.AudioBufferFree(context, ref audioBuffer);
        }
    }

    public static void AudioBufferInterleave(
        IntPtr context,
        in AudioBuffer inputBuffer,
        float* outputAudio)
    {
        if (IsX64Process)
        {
            X64Imports.AudioBufferInterleave(context, in inputBuffer, outputAudio);
        }
        else
        {
            X86Imports.AudioBufferInterleave(context, in inputBuffer, outputAudio);
        }
    }

    private static bool IsX64Process
    {
        get
        {
            if (IntPtr.Size == 8)
            {
                return true;
            }

            if (IntPtr.Size == 4)
            {
                return false;
            }

            throw new PlatformNotSupportedException(
                $"Steam Audio requires a 32-bit or 64-bit process; pointer size is {IntPtr.Size} bytes.");
        }
    }

    private static class X86Imports
    {
        [DllImport("phonon.dll", EntryPoint = "_iplContextCreate@8", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern Error ContextCreate(in ContextSettings settings, out IntPtr context);

        [DllImport("phonon.dll", EntryPoint = "_iplContextRelease@4", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern void ContextRelease(ref IntPtr context);

        [DllImport("phonon.dll", EntryPoint = "_iplHRTFCreate@16", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern Error HrtfCreate(IntPtr context, in AudioSettings audioSettings, in HrtfSettings settings, out IntPtr hrtf);

        [DllImport("phonon.dll", EntryPoint = "_iplHRTFRelease@4", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern void HrtfRelease(ref IntPtr hrtf);

        [DllImport("phonon.dll", EntryPoint = "_iplBinauralEffectCreate@16", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern Error BinauralEffectCreate(IntPtr context, in AudioSettings audioSettings, in BinauralEffectSettings settings, out IntPtr effect);

        [DllImport("phonon.dll", EntryPoint = "_iplBinauralEffectRelease@4", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern void BinauralEffectRelease(ref IntPtr effect);

        [DllImport("phonon.dll", EntryPoint = "_iplBinauralEffectReset@4", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern void BinauralEffectReset(IntPtr effect);

        [DllImport("phonon.dll", EntryPoint = "_iplBinauralEffectApply@16", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern int BinauralEffectApply(IntPtr effect, in BinauralEffectParams parameters, in AudioBuffer inputBuffer, ref AudioBuffer outputBuffer);

        [DllImport("phonon.dll", EntryPoint = "_iplAudioBufferAllocate@16", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern Error AudioBufferAllocate(IntPtr context, int numChannels, int numSamples, ref AudioBuffer audioBuffer);

        [DllImport("phonon.dll", EntryPoint = "_iplAudioBufferFree@8", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern void AudioBufferFree(IntPtr context, ref AudioBuffer audioBuffer);

        [DllImport("phonon.dll", EntryPoint = "_iplAudioBufferInterleave@12", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern void AudioBufferInterleave(IntPtr context, in AudioBuffer inputBuffer, float* outputAudio);
    }

    private static class X64Imports
    {
        [DllImport("phonon.dll", EntryPoint = "iplContextCreate", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern Error ContextCreate(in ContextSettings settings, out IntPtr context);

        [DllImport("phonon.dll", EntryPoint = "iplContextRelease", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void ContextRelease(ref IntPtr context);

        [DllImport("phonon.dll", EntryPoint = "iplHRTFCreate", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern Error HrtfCreate(IntPtr context, in AudioSettings audioSettings, in HrtfSettings settings, out IntPtr hrtf);

        [DllImport("phonon.dll", EntryPoint = "iplHRTFRelease", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void HrtfRelease(ref IntPtr hrtf);

        [DllImport("phonon.dll", EntryPoint = "iplBinauralEffectCreate", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern Error BinauralEffectCreate(IntPtr context, in AudioSettings audioSettings, in BinauralEffectSettings settings, out IntPtr effect);

        [DllImport("phonon.dll", EntryPoint = "iplBinauralEffectRelease", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void BinauralEffectRelease(ref IntPtr effect);

        [DllImport("phonon.dll", EntryPoint = "iplBinauralEffectReset", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void BinauralEffectReset(IntPtr effect);

        [DllImport("phonon.dll", EntryPoint = "iplBinauralEffectApply", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int BinauralEffectApply(IntPtr effect, in BinauralEffectParams parameters, in AudioBuffer inputBuffer, ref AudioBuffer outputBuffer);

        [DllImport("phonon.dll", EntryPoint = "iplAudioBufferAllocate", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern Error AudioBufferAllocate(IntPtr context, int numChannels, int numSamples, ref AudioBuffer audioBuffer);

        [DllImport("phonon.dll", EntryPoint = "iplAudioBufferFree", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void AudioBufferFree(IntPtr context, ref AudioBuffer audioBuffer);

        [DllImport("phonon.dll", EntryPoint = "iplAudioBufferInterleave", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void AudioBufferInterleave(IntPtr context, in AudioBuffer inputBuffer, float* outputAudio);
    }
}
