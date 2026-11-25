using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace BlindSoldier.Ff7Launcher2013
{
	internal enum BlindSoldierLoadState
	{
		NotApplicable,
		AlreadyLoaded,
		Loaded,
		Failed
	}

	internal sealed class BlindSoldierLoadResult
	{
		internal BlindSoldierLoadResult(BlindSoldierLoadState state, string detail)
		{
			State = state;
			Detail = detail ?? string.Empty;
		}

		internal BlindSoldierLoadState State { get; }

		internal string Detail { get; }
	}

	internal interface IBlindSoldierLoader
	{
		BlindSoldierLoadResult Ensure(LegacyInstallation installation, ILegacyGameProcess process);
	}

	internal interface IGameModuleReader
	{
		// Full paths of the modules loaded in the game process.
		IReadOnlyList<string> Read(ILegacyGameProcess process);
	}

	internal interface IRemoteLibraryLoader
	{
		void Load(ILegacyGameProcess process, string libraryPath, TimeSpan timeout);
	}

	// Blind Soldier's x86 version.dll beside ff7_en.exe starts the accessibility
	// runtime when the game loads it. FFNx's AF3DN.P imports VERSION.dll, so with
	// FFNx Windows loads the proxy at start-up. The original 2013 ff7_en.exe and
	// AF3DN.P do not; only the x86 d3d9.dll of Windows 11 24H2 and later imports
	// VERSION.dll, and the Windows 10 and Windows 11 22H2 and 23H2 copies use
	// the KernelBase API sets instead. Without this the game would run without
	// speech there, so after the handshake the same proxy is loaded by its full
	// path. An already loaded proxy is left alone, so it never starts twice.
	internal sealed class BlindSoldierLoader : IBlindSoldierLoader
	{
		internal const string ProxyFileName = "version.dll";
		internal static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(30);

		private readonly IGameModuleReader modules;
		private readonly IRemoteLibraryLoader loader;
		private readonly Action<string> log;

		internal BlindSoldierLoader(IGameModuleReader modules, IRemoteLibraryLoader loader, Action<string> log)
		{
			this.modules = modules ?? throw new ArgumentNullException(nameof(modules));
			this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
			this.log = log ?? delegate { };
		}

		public BlindSoldierLoadResult Ensure(LegacyInstallation installation, ILegacyGameProcess process)
		{
			if (process == null)
			{
				throw new ArgumentNullException(nameof(process));
			}
			if (installation == null || !installation.BlindSoldierInstalled || !installation.BlindSoldierSupportsExecutable)
			{
				return new BlindSoldierLoadResult(BlindSoldierLoadState.NotApplicable, string.Empty);
			}

			string proxy = Path.Combine(installation.Root, ProxyFileName);
			if (process.HasExited)
			{
				return GameClosed();
			}
			try
			{
				if (IsLoaded(process, proxy))
				{
					log("Windows already loaded the Blind Soldier Version proxy: " + proxy);
					return new BlindSoldierLoadResult(BlindSoldierLoadState.AlreadyLoaded, proxy);
				}
				log("Windows did not load the Blind Soldier Version proxy with the original 2013 driver; loading " + proxy + " into game process " + process.Id + ".");
				loader.Load(process, proxy, LoadTimeout);
				if (IsLoaded(process, proxy))
				{
					log("Loaded the Blind Soldier Version proxy into the game.");
					return new BlindSoldierLoadResult(BlindSoldierLoadState.Loaded, proxy);
				}
				return Failed("The game did not keep " + proxy + " loaded.");
			}
			catch (Exception exception) when (exception is Win32Exception || exception is InvalidOperationException ||
				exception is IOException || exception is UnauthorizedAccessException)
			{
				// A game started outside Steam restarts itself through Steam and
				// exits; its exit is reported like any other, not as a load failure.
				return process.HasExited ? GameClosed() : Failed(exception.Message);
			}
		}

		private BlindSoldierLoadResult GameClosed()
		{
			log("The game closed before the Blind Soldier Version proxy could be checked.");
			return new BlindSoldierLoadResult(BlindSoldierLoadState.NotApplicable, "The game closed.");
		}

		private BlindSoldierLoadResult Failed(string detail)
		{
			log("Blind Soldier could not be loaded into the game: " + detail);
			return new BlindSoldierLoadResult(BlindSoldierLoadState.Failed, detail);
		}

		private bool IsLoaded(ILegacyGameProcess process, string proxy)
		{
			return modules.Read(process).Any(path =>
				string.Equals(Path.GetFileName(path), ProxyFileName, StringComparison.OrdinalIgnoreCase) && FileIdentity.Same(path, proxy));
		}
	}

	internal static class FileIdentity
	{
		private const uint FileReadAttributes = 0x80;
		private const uint ShareAll = 0x7;
		private const uint OpenExisting = 3;
		private const uint BackupSemantics = 0x02000000;

		// True when both paths name one file, including through short names.
		internal static bool Same(string left, string right)
		{
			if (string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			return TryIdentify(left, out FileInformation leftFile) && TryIdentify(right, out FileInformation rightFile) &&
				leftFile.VolumeSerialNumber == rightFile.VolumeSerialNumber &&
				leftFile.FileIndexHigh == rightFile.FileIndexHigh &&
				leftFile.FileIndexLow == rightFile.FileIndexLow;
		}

		private static string Normalize(string path)
		{
			try
			{
				return Path.GetFullPath(path);
			}
			catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException)
			{
				return path;
			}
		}

		private static bool TryIdentify(string path, out FileInformation information)
		{
			information = default(FileInformation);
			using (SafeFileHandle file = CreateFileW(path, FileReadAttributes, ShareAll, IntPtr.Zero, OpenExisting, BackupSemantics, IntPtr.Zero))
			{
				return !file.IsInvalid && GetFileInformationByHandle(file, out information);
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct FileInformation
		{
			public uint FileAttributes;
			public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
			public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
			public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
			public uint VolumeSerialNumber;
			public uint FileSizeHigh;
			public uint FileSizeLow;
			public uint NumberOfLinks;
			public uint FileIndexHigh;
			public uint FileIndexLow;
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);
	}

	// Reads the module list through a duplicate of the launch's own process
	// handle, so it always describes the game this launcher started.
	internal sealed class ProcessModuleReader : IGameModuleReader
	{
		private const uint ProcessQueryInformation = 0x0400;
		private const uint ProcessVmRead = 0x0010;
		private const uint ListModulesAll = 0x03;
		private const int ErrorPartialCopy = 299;

		public IReadOnlyList<string> Read(ILegacyGameProcess process)
		{
			if (process == null)
			{
				throw new ArgumentNullException(nameof(process));
			}
			using (SafeProcessHandle target = process.DuplicateHandle(ProcessQueryInformation | ProcessVmRead))
			{
				int lastError = 0;
				for (int attempt = 0; attempt < 20; attempt++)
				{
					if (process.HasExited)
					{
						throw new InvalidOperationException("The game closed before its modules could be checked.");
					}
					var modules = new IntPtr[1024];
					if (EnumProcessModulesEx(target, modules, (uint)(modules.Length * IntPtr.Size), out uint needed, ListModulesAll))
					{
						int count = (int)(needed / (uint)IntPtr.Size);
						if (count <= modules.Length)
						{
							return modules.Take(count).Select(module => ModulePath(target, module)).Where(path => path != null).ToArray();
						}
					}
					else
					{
						lastError = Marshal.GetLastWin32Error();
						if (lastError != ErrorPartialCopy)
						{
							throw new Win32Exception(lastError, "The game's modules could not be listed.");
						}
					}
					// The module list can be partial while Windows is still
					// loading libraries into the process.
					Thread.Sleep(100);
				}
				throw new InvalidOperationException("The game's modules could not be listed (error " + lastError + ").");
			}
		}

		private static string ModulePath(SafeProcessHandle target, IntPtr module)
		{
			var buffer = new StringBuilder(32768);
			uint length = GetModuleFileNameExW(target, module, buffer, (uint)buffer.Capacity);
			return length == 0 ? null : buffer.ToString(0, (int)length);
		}

		[DllImport("kernel32.dll", EntryPoint = "K32EnumProcessModulesEx", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool EnumProcessModulesEx(SafeProcessHandle process, [Out] IntPtr[] modules, uint size, out uint needed, uint filter);

		[DllImport("kernel32.dll", EntryPoint = "K32GetModuleFileNameExW", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern uint GetModuleFileNameExW(SafeProcessHandle process, IntPtr module, StringBuilder fileName, uint size);
	}

	// LoadLibraryW on a thread in the game, so the library's own start-up runs
	// exactly as if Windows had loaded it as a dependency. It works through a
	// duplicate of the launch's own process handle, never a process ID.
	internal sealed class RemoteLibraryLoader : IRemoteLibraryLoader
	{
		private const uint ProcessCreateThread = 0x0002;
		private const uint ProcessVmOperation = 0x0008;
		private const uint ProcessVmRead = 0x0010;
		private const uint ProcessVmWrite = 0x0020;
		private const uint ProcessQueryInformation = 0x0400;
		private const uint Synchronize = 0x00100000;
		private const uint MemCommit = 0x1000;
		private const uint MemReserve = 0x2000;
		private const uint MemRelease = 0x8000;
		private const uint PageReadWrite = 0x04;
		private const uint WaitObject0 = 0;

		public void Load(ILegacyGameProcess process, string libraryPath, TimeSpan timeout)
		{
			if (process == null)
			{
				throw new ArgumentNullException(nameof(process));
			}
			if (Environment.Is64BitProcess)
			{
				throw new InvalidOperationException("The 2013 launcher must run as a 32-bit program to load a library into the 32-bit game.");
			}

			using (SafeProcessHandle target = process.DuplicateHandle(ProcessCreateThread | ProcessQueryInformation | ProcessVmOperation | ProcessVmWrite | ProcessVmRead | Synchronize))
			{
				if (Environment.Is64BitOperatingSystem && (!IsWow64Process(target, out bool wow64) || !wow64))
				{
					throw new InvalidOperationException("The game process is not a 32-bit process.");
				}

				IntPtr loadLibrary = GetProcAddress(GetModuleHandleW("kernel32.dll"), "LoadLibraryW");
				if (loadLibrary == IntPtr.Zero)
				{
					throw new Win32Exception(Marshal.GetLastWin32Error(), "LoadLibraryW could not be found.");
				}

				byte[] path = Encoding.Unicode.GetBytes(libraryPath + "\0");
				IntPtr remote = VirtualAllocEx(target, IntPtr.Zero, (UIntPtr)path.Length, MemCommit | MemReserve, PageReadWrite);
				if (remote == IntPtr.Zero)
				{
					throw new Win32Exception(Marshal.GetLastWin32Error(), "Memory for the library path could not be reserved in the game.");
				}
				bool threadMayReadPath = false;
				try
				{
					if (!WriteProcessMemory(target, remote, path, (UIntPtr)path.Length, out UIntPtr written) || written.ToUInt32() != path.Length)
					{
						throw new Win32Exception(Marshal.GetLastWin32Error(), "The library path could not be written into the game.");
					}
					using (SafeWaitHandle thread = CreateRemoteThread(target, IntPtr.Zero, UIntPtr.Zero, loadLibrary, remote, 0, out _))
					{
						if (thread.IsInvalid)
						{
							throw new InvalidOperationException("CreateRemoteThread failed with error " + Marshal.GetLastWin32Error() + ".");
						}
						threadMayReadPath = true;
						if (WaitForSingleObject(thread, (uint)Math.Min(int.MaxValue, timeout.TotalMilliseconds)) != WaitObject0)
						{
							throw new InvalidOperationException("The game did not finish loading " + libraryPath + " within " + (int)timeout.TotalSeconds + " seconds.");
						}
						threadMayReadPath = false;
						if (!GetExitCodeThread(thread, out uint module))
						{
							throw new Win32Exception(Marshal.GetLastWin32Error(), "The result of loading the library could not be read.");
						}
						if (module == 0)
						{
							throw new InvalidOperationException("Windows could not load " + libraryPath + " into the game.");
						}
					}
				}
				finally
				{
					// Freeing the path while LoadLibraryW may still read it
					// could crash the game, so a timed-out load keeps it.
					if (!threadMayReadPath)
					{
						VirtualFreeEx(target, remote, UIntPtr.Zero, MemRelease);
					}
				}
			}
		}

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool IsWow64Process(SafeProcessHandle process, [MarshalAs(UnmanagedType.Bool)] out bool wow64Process);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern IntPtr GetModuleHandleW(string moduleName);

		[DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
		private static extern IntPtr GetProcAddress(IntPtr module, string procName);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern IntPtr VirtualAllocEx(SafeProcessHandle process, IntPtr address, UIntPtr size, uint allocationType, uint protect);

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool VirtualFreeEx(SafeProcessHandle process, IntPtr address, UIntPtr size, uint freeType);

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool WriteProcessMemory(SafeProcessHandle process, IntPtr baseAddress, byte[] buffer, UIntPtr size, out UIntPtr written);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern SafeWaitHandle CreateRemoteThread(SafeProcessHandle process, IntPtr threadAttributes, UIntPtr stackSize, IntPtr startAddress, IntPtr parameter, uint creationFlags, out uint threadId);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool GetExitCodeThread(SafeWaitHandle thread, out uint exitCode);
	}
}
