using System.Runtime.InteropServices;

namespace IfMonitor;

/// <summary>
/// iphlpapi entry points used to query a single interface and to receive
/// per-interface change notifications (what "netsh interface ip show interface" reports).
/// </summary>
internal static class NativeMethods
{
    internal const uint NO_ERROR = 0;
    internal const ushort AF_UNSPEC = 0;

    internal enum IfOperStatus
    {
        Up = 1,
        Down = 2,
        Testing = 3,
        Unknown = 4,
        Dormant = 5,
        NotPresent = 6,
        LowerLayerDown = 7,
    }

    internal enum NetIfMediaConnectState
    {
        Unknown = 0,
        Connected = 1,
        Disconnected = 2,
    }

    internal enum MibNotificationType
    {
        ParameterNotification = 0,
        AddInstance = 1,
        DeleteInstance = 2,
        InitialNotification = 3,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MibIfRow2
    {
        internal ulong InterfaceLuid;
        internal uint InterfaceIndex;
        internal Guid InterfaceGuid;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)]
        internal string Alias;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)]
        internal string Description;

        internal uint PhysicalAddressLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        internal byte[] PhysicalAddress;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        internal byte[] PermanentPhysicalAddress;

        internal uint Mtu;
        internal uint Type;
        internal uint TunnelType;
        internal uint MediaType;
        internal uint PhysicalMediumType;
        internal uint AccessType;
        internal uint DirectionType;
        internal byte InterfaceAndOperStatusFlags;
        internal IfOperStatus OperStatus;
        internal uint AdminStatus;
        internal NetIfMediaConnectState MediaConnectState;
        internal Guid NetworkGuid;
        internal uint ConnectionType;

        /// <summary>TransmitLinkSpeed, ReceiveLinkSpeed and the 18 traffic counters.</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        internal ulong[] Counters;
    }

    internal delegate void IpInterfaceChangeCallback(
        IntPtr callerContext,
        IntPtr row,
        MibNotificationType notificationType);

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    internal static extern uint ConvertInterfaceGuidToLuid(in Guid interfaceGuid, out ulong interfaceLuid);

    [DllImport("iphlpapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    internal static extern uint GetIfEntry2(ref MibIfRow2 row);

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    internal static extern uint NotifyIpInterfaceChange(
        ushort family,
        IpInterfaceChangeCallback callback,
        IntPtr callerContext,
        [MarshalAs(UnmanagedType.U1)] bool initialNotification,
        ref IntPtr notificationHandle);

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    internal static extern uint CancelMibChangeNotify2(IntPtr notificationHandle);
}
