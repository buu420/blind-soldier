using System.Runtime.InteropServices;

namespace Ff7.Accessibility.Reloaded;

internal static class RestartManagerProbe
{
    public static bool IsFileOpenByProcess(string path, int processId)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var sessionKey = Guid.NewGuid().ToString("N");
        var result = Native.RmStartSession(out var session, 0, sessionKey);
        if (result != 0)
        {
            return false;
        }

        try
        {
            var resources = new[] { path };
            result = Native.RmRegisterResources(session, (uint)resources.Length, resources, 0, null, 0, null);
            if (result != 0)
            {
                return false;
            }

            uint processInfoNeeded = 0;
            uint processInfoCount = 0;
            uint rebootReasons = 0;
            result = Native.RmGetList(session, out processInfoNeeded, ref processInfoCount, null, ref rebootReasons);
            if (result != Native.ERROR_MORE_DATA || processInfoNeeded == 0)
            {
                return false;
            }

            var processInfo = new Native.RM_PROCESS_INFO[processInfoNeeded];
            processInfoCount = processInfoNeeded;
            result = Native.RmGetList(session, out processInfoNeeded, ref processInfoCount, processInfo, ref rebootReasons);
            if (result != 0)
            {
                return false;
            }

            return processInfo.Take((int)processInfoCount).Any(info => info.Process.dwProcessId == processId);
        }
        finally
        {
            Native.RmEndSession(session);
        }
    }

    private static class Native
    {
        public const int ERROR_MORE_DATA = 234;

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        public static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll")]
        public static extern int RmEndSession(uint pSessionHandle);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        public static extern int RmRegisterResources(
            uint pSessionHandle,
            uint nFiles,
            string[] rgsFilenames,
            uint nApplications,
            RM_UNIQUE_PROCESS[]? rgApplications,
            uint nServices,
            string[]? rgsServiceNames);

        [DllImport("rstrtmgr.dll")]
        public static extern int RmGetList(
            uint dwSessionHandle,
            out uint pnProcInfoNeeded,
            ref uint pnProcInfo,
            [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
            ref uint lpdwRebootReasons);

        [StructLayout(LayoutKind.Sequential)]
        public struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public FILETIME ProcessStartTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string strServiceShortName;
            public uint ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }
    }
}
