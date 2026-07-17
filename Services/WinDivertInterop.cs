using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MonolithVpnClient.Services;

public static class WinDivertInterop
{
    public const int WINDIVERT_LAYER_NETWORK = 0;

    public const ulong WINDIVERT_FLAG_NO_INSTALL = 0x0010;

    private const int InvalidHandleValue = -1;

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct WINDIVERT_ADDRESS
    {
        public long Timestamp;
        public uint Flags1;
        public uint Reserved2;
        public fixed byte Union[64];

        private const uint OutboundBit = 1u << 17;

        public bool Outbound
        {
            readonly get => (Flags1 & OutboundBit) != 0;
            set => Flags1 = value ? (Flags1 | OutboundBit) : (Flags1 & ~OutboundBit);
        }

        public uint IfIdx
        {
            readonly get { fixed (byte* p = Union) return *(uint*)p; }
            set { fixed (byte* p = Union) *(uint*)p = value; }
        }

        public uint SubIfIdx
        {
            readonly get { fixed (byte* p = Union) return *(uint*)(p + 4); }
            set { fixed (byte* p = Union) *(uint*)(p + 4) = value; }
        }
    }

    [DllImport("WinDivert.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr WinDivertOpen(string filter, int layer, short priority, ulong flags);

    [DllImport("WinDivert.dll", SetLastError = true)]
    private static extern bool WinDivertRecv(IntPtr handle, byte[] pPacket, uint packetLen, out uint pRecvLen, out WINDIVERT_ADDRESS pAddr);

    [DllImport("WinDivert.dll", SetLastError = true)]
    private static extern bool WinDivertSend(IntPtr handle, byte[] pPacket, uint packetLen, out uint pSendLen, in WINDIVERT_ADDRESS pAddr);

    [DllImport("WinDivert.dll", SetLastError = true)]
    private static extern bool WinDivertClose(IntPtr handle);

    [DllImport("WinDivert.dll", SetLastError = true)]
    private static extern bool WinDivertHelperCalcChecksums(byte[] pPacket, uint packetLen, ref WINDIVERT_ADDRESS pAddr, ulong flags);

    private static bool _resolverRegistered;
    private static readonly object ResolverLock = new();

    public static string EnsureExtracted()
    {
        lock (ResolverLock)
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MonolithVPN", "windivert");
            Directory.CreateDirectory(dir);

            ExtractResource("WinDivert.dll", Path.Combine(dir, "WinDivert.dll"));
            ExtractResource("WinDivert64.sys", Path.Combine(dir, "WinDivert64.sys"));
            ExtractResource("WinDivert-LICENSE.txt", Path.Combine(dir, "WinDivert-LICENSE.txt"));

            if (!_resolverRegistered)
            {
                NativeLibrary.SetDllImportResolver(typeof(WinDivertInterop).Assembly, (name, assembly, searchPath) =>
                    name == "WinDivert.dll" && NativeLibrary.TryLoad(Path.Combine(dir, "WinDivert.dll"), out var handle)
                        ? handle
                        : IntPtr.Zero);
                _resolverRegistered = true;
            }

            return dir;
        }
    }

    private static void ExtractResource(string resourceName, string path)
    {
        using var resourceStream = typeof(WinDivertInterop).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"{resourceName} resource is missing from this build - reinstall the app.");

        if (!File.Exists(path) || new FileInfo(path).Length != resourceStream.Length)
        {
            using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            resourceStream.CopyTo(fileStream);
        }
    }

    public static IntPtr Open(string filter)
    {
        EnsureExtracted();
        IntPtr handle = WinDivertOpen(filter, WINDIVERT_LAYER_NETWORK, 0, 0);
        if (handle == IntPtr.Zero || handle.ToInt64() == InvalidHandleValue)
            throw new InvalidOperationException($"WinDivertOpen failed (Win32 error {Marshal.GetLastWin32Error()}).");
        return handle;
    }

    public static bool TryReceive(IntPtr handle, byte[] buffer, out uint length, out WINDIVERT_ADDRESS address) =>
        WinDivertRecv(handle, buffer, (uint)buffer.Length, out length, out address);

    public static bool Send(IntPtr handle, byte[] buffer, uint length, in WINDIVERT_ADDRESS address) =>
        WinDivertSend(handle, buffer, length, out _, in address);

    public static void RecalculateChecksums(byte[] buffer, uint length, ref WINDIVERT_ADDRESS address) =>
        WinDivertHelperCalcChecksums(buffer, length, ref address, 0);

    public static void Close(IntPtr handle) => WinDivertClose(handle);
}
