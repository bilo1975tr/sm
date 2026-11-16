using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace StreamMesh.Core.Media.Mpv
{
    internal enum MpvFormat
    {
        None = 0,
        String = 1,
        OsdString = 2,
        Flag = 3,
        Int64 = 4,
        Double = 5,
        Node = 6,
        NodeArray = 7,
        NodeMap = 8,
        ByteArray = 9
    }

    internal enum MpvEventId
    {
        None = 0,
        Shutdown = 1,
        LogMessage = 2,
        GetPropertyReply = 3,
        SetPropertyReply = 4,
        CommandReply = 5,
        StartFile = 6,
        EndFile = 7,
        FileLoaded = 8,
        Seek = 20,
        PlaybackRestart = 21,
        PropertyChange = 22
    }

    internal enum MpvEndFileReason
    {
        Eof = 0,
        Stop = 2,
        Quit = 3,
        Error = 4,
        Redirect = 5
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MpvEvent
    {
        public MpvEventId EventId;
        public int Error;
        public ulong ReplyUserData;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MpvEventProperty
    {
        public IntPtr Name;
        public MpvFormat Format;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MpvEventEndFile
    {
        public MpvEndFileReason Reason;
        public int Error;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct MpvNode
    {
        [FieldOffset(0)] public IntPtr Pointer;
        [FieldOffset(0)] public int Flag;
        [FieldOffset(0)] public long Int64;
        [FieldOffset(0)] public double Double;
        [FieldOffset(8)] public MpvFormat Format;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MpvNodeList
    {
        public int Num;
        public IntPtr Values;
        public IntPtr Keys;
    }

    internal static class MpvNative
    {
        private const string DllName = "libmpv-2.dll";
        private static IntPtr _loadedHandle = IntPtr.Zero;
        private static readonly object _loadLock = new();

        static MpvNative()
        {
            NativeLibrary.SetDllImportResolver(typeof(MpvNative).Assembly, (libraryName, _, _) =>
            {
                if (!string.Equals(libraryName, DllName, StringComparison.OrdinalIgnoreCase))
                    return IntPtr.Zero;

                return EnsureLoaded();
            });
        }

        public static string LastLoadError { get; private set; } = "";

        public static string? FindLibraryPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string localAppMpv = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StreamMesh", "mpv");

            string[] candidates =
            {
                Path.Combine(baseDir, "mpv", "libmpv-2.dll"),
                Path.Combine(baseDir, "libmpv-2.dll"),
                Path.Combine(localAppMpv, "libmpv-2.dll"),
                Path.Combine(baseDir, "mpv", "mpv-2.dll"),
                Path.Combine(baseDir, "mpv-2.dll"),
                Path.Combine(baseDir, "mpv", "mpv-1.dll")
            };

            foreach (var path in candidates)
            {
                try
                {
                    if (File.Exists(path) && new FileInfo(path).Length > 65536)
                        return path;
                }
                catch { }
            }

            return null;
        }

        public static bool IsLibraryAvailable()
        {
            if (_loadedHandle != IntPtr.Zero) return true;
            if (FindLibraryPath() != null) return true;
            return EnsureLoaded() != IntPtr.Zero;
        }

        public static IntPtr EnsureLoaded()
        {
            if (_loadedHandle != IntPtr.Zero)
                return _loadedHandle;

            lock (_loadLock)
            {
                if (_loadedHandle != IntPtr.Zero)
                    return _loadedHandle;

                LastLoadError = "";
                string? path = FindLibraryPath();
                if (!string.IsNullOrEmpty(path))
                {
                    string? dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        try { AddDllDirectory(dir); } catch { }
                        try { SetDllDirectory(dir); } catch { }
                    }

                    if (NativeLibrary.TryLoad(path, out IntPtr handle))
                    {
                        _loadedHandle = handle;
                        return _loadedHandle;
                    }

                    // Fallback for .NET PublishSingleFile where SetDefaultDllDirectories may ignore SetDllDirectory
                    const uint LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x00000100;
                    const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;
                    const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;

                    IntPtr exHandle = LoadLibraryEx(path, IntPtr.Zero, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
                    if (exHandle == IntPtr.Zero)
                        exHandle = LoadLibraryEx(path, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH);

                    if (exHandle != IntPtr.Zero)
                    {
                        _loadedHandle = exHandle;
                        return _loadedHandle;
                    }

                    int winErr = Marshal.GetLastWin32Error();
                    LastLoadError = $"NativeLibrary.TryLoad('{path}') başarısız (Win32={winErr})";
                    Core.Utils.LogService.LogError($"[MPV] {LastLoadError}");
                }

                if (NativeLibrary.TryLoad(DllName, out IntPtr fallbackHandle))
                {
                    _loadedHandle = fallbackHandle;
                    return _loadedHandle;
                }

                if (string.IsNullOrEmpty(LastLoadError))
                {
                    int winErr = Marshal.GetLastWin32Error();
                    LastLoadError = $"libmpv-2.dll bulunamadı veya yüklenemedi (Win32={winErr})";
                }
                return IntPtr.Zero;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr AddDllDirectory(string newDirectory);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string lpLibFileName, IntPtr hFile, uint dwFlags);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_create();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_initialize(IntPtr ctx);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_terminate_destroy(IntPtr ctx);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_option_string(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_property_string(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_property(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            MpvFormat format,
            ref MpvNode data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_property(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            MpvFormat format,
            ref double data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_property(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            MpvFormat format,
            ref int data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_get_property(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            MpvFormat format,
            out double data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_get_property(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            MpvFormat format,
            out long data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_get_property(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            MpvFormat format,
            out int data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_get_property_string(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_command(IntPtr ctx, IntPtr[] args);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_observe_property(
            IntPtr ctx,
            ulong replyUserdata,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            MpvFormat format);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_wakeup(IntPtr ctx);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_free(IntPtr data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_error_string(int error);

        public static string GetErrorString(int error)
        {
            IntPtr ptr = mpv_error_string(error);
            return ptr != IntPtr.Zero ? (Marshal.PtrToStringUTF8(ptr) ?? $"mpv error {error}") : $"mpv error {error}";
        }

        public static string? GetPropertyString(IntPtr ctx, string name)
        {
            if (ctx == IntPtr.Zero) return null;
            IntPtr ptr = mpv_get_property_string(ctx, name);
            if (ptr == IntPtr.Zero) return null;
            try
            {
                return Marshal.PtrToStringUTF8(ptr);
            }
            finally
            {
                mpv_free(ptr);
            }
        }

        public static int Command(IntPtr ctx, params string[] args)
        {
            if (ctx == IntPtr.Zero) return -1;
            var ptrs = new IntPtr[args.Length + 1];
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(args[i] + "\0");
                    IntPtr mem = Marshal.AllocHGlobal(bytes.Length);
                    Marshal.Copy(bytes, 0, mem, bytes.Length);
                    ptrs[i] = mem;
                }
                ptrs[args.Length] = IntPtr.Zero;
                return mpv_command(ctx, ptrs);
            }
            finally
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (ptrs[i] != IntPtr.Zero)
                        Marshal.FreeHGlobal(ptrs[i]);
                }
            }
        }

        public static int SetPropertyStringList(IntPtr ctx, string name, IReadOnlyList<string> items)
        {
            if (ctx == IntPtr.Zero) return -1;
            if (items.Count == 0)
            {
                var emptyNode = new MpvNode { Format = MpvFormat.NodeArray, Pointer = Marshal.AllocHGlobal(Marshal.SizeOf<MpvNodeList>()) };
                try
                {
                    Marshal.StructureToPtr(new MpvNodeList { Num = 0, Values = IntPtr.Zero, Keys = IntPtr.Zero }, emptyNode.Pointer, false);
                    return mpv_set_property(ctx, name, MpvFormat.Node, ref emptyNode);
                }
                finally
                {
                    Marshal.FreeHGlobal(emptyNode.Pointer);
                }
            }

            int nodeSize = Marshal.SizeOf<MpvNode>();
            IntPtr valuesPtr = Marshal.AllocHGlobal(nodeSize * items.Count);
            IntPtr listPtr = Marshal.AllocHGlobal(Marshal.SizeOf<MpvNodeList>());
            var allocatedStrings = new List<IntPtr>(items.Count);

            try
            {
                for (int i = 0; i < items.Count; i++)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(items[i] + "\0");
                    IntPtr strPtr = Marshal.AllocHGlobal(bytes.Length);
                    Marshal.Copy(bytes, 0, strPtr, bytes.Length);
                    allocatedStrings.Add(strPtr);

                    var child = new MpvNode { Format = MpvFormat.String, Pointer = strPtr };
                    Marshal.StructureToPtr(child, IntPtr.Add(valuesPtr, i * nodeSize), false);
                }

                var list = new MpvNodeList { Num = items.Count, Values = valuesPtr, Keys = IntPtr.Zero };
                Marshal.StructureToPtr(list, listPtr, false);

                var root = new MpvNode { Format = MpvFormat.NodeArray, Pointer = listPtr };
                return mpv_set_property(ctx, name, MpvFormat.Node, ref root);
            }
            finally
            {
                foreach (var ptr in allocatedStrings)
                    Marshal.FreeHGlobal(ptr);
                Marshal.FreeHGlobal(valuesPtr);
                Marshal.FreeHGlobal(listPtr);
            }
        }
    }
}
