using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace IfMonitor;

/// <summary>
/// Enables/disables a NIC in-process via SetupAPI (same path as Device Manager).
/// Falls back to netsh if SetupAPI fails. Requires an elevated process.
/// </summary>
public static class AdapterPower
{
    private static readonly Guid GuidDevClassNet = new("4d36e972-e325-11ce-bfc1-08002be10318");

    private const int DigcfPresent = 0x00000002;
    private const int DifPropertyChange = 0x00000012;
    private const int DicsEnable = 0x00000001;
    private const int DicsDisable = 0x00000002;
    private const int DicsFlagGlobal = 0x00000001;
    private const int DicsFlagConfigSpecific = 0x00000002;
    private const int DiregDrv = 0x00000002;
    private const int ErrorNotDisableable = unchecked((int)0xE0000231);
    private const int KeyRead = 0x20019;

    public static bool TrySetEnabled(string adapterId, bool enabled, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(adapterId))
        {
            error = "Adapter id is empty.";
            return false;
        }

        if (TrySetEnabledSetupApi(adapterId, enabled, out error))
        {
            return true;
        }

        string setupError = error;
        if (TrySetEnabledNetsh(adapterId, enabled, out error))
        {
            return true;
        }

        error = $"SetupAPI failed ({setupError}); netsh failed ({error}).";
        return false;
    }

    private static bool TrySetEnabledSetupApi(string adapterId, bool enabled, out string error)
    {
        error = string.Empty;
        string want = NormalizeGuid(adapterId);

        Guid netClass = GuidDevClassNet;
        IntPtr deviceInfoSet = SetupDiGetClassDevs(
            ref netClass,
            IntPtr.Zero,
            IntPtr.Zero,
            DigcfPresent);

        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
        {
            error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }

        try
        {
            var deviceInfoData = new SpDevinfoData { CbSize = Marshal.SizeOf<SpDevinfoData>() };
            for (uint index = 0; SetupDiEnumDeviceInfo(deviceInfoSet, index, ref deviceInfoData); index++)
            {
                if (!TryGetNetCfgInstanceId(deviceInfoSet, ref deviceInfoData, out string? instanceId)
                    || instanceId is null)
                {
                    continue;
                }

                if (!string.Equals(NormalizeGuid(instanceId), want, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return ApplyPropertyChange(deviceInfoSet, ref deviceInfoData, enabled, out error);
            }

            error = "Network device not found for the given adapter id.";
            return false;
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private static bool ApplyPropertyChange(
        IntPtr deviceInfoSet,
        ref SpDevinfoData deviceInfoData,
        bool enabled,
        out string error)
    {
        error = string.Empty;
        var parameters = new SpPropchangeParams
        {
            ClassInstallHeader = new SpClassInstallHeader
            {
                CbSize = Marshal.SizeOf<SpClassInstallHeader>(),
                InstallFunction = DifPropertyChange,
            },
            StateChange = enabled ? DicsEnable : DicsDisable,
            Scope = DicsFlagConfigSpecific,
            HwProfile = 0,
        };

        if (!SetupDiSetClassInstallParams(
                deviceInfoSet,
                ref deviceInfoData,
                ref parameters,
                Marshal.SizeOf(parameters)))
        {
            error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }

        if (!SetupDiCallClassInstaller(DifPropertyChange, deviceInfoSet, ref deviceInfoData))
        {
            int code = Marshal.GetLastWin32Error();
            error = code == ErrorNotDisableable
                ? "This adapter cannot be disabled programmatically."
                : new Win32Exception(code).Message;
            return false;
        }

        return true;
    }

    private static bool TryGetNetCfgInstanceId(
        IntPtr deviceInfoSet,
        ref SpDevinfoData deviceInfoData,
        out string? instanceId)
    {
        instanceId = null;
        IntPtr key = SetupDiOpenDevRegKey(
            deviceInfoSet,
            ref deviceInfoData,
            DicsFlagGlobal,
            0,
            DiregDrv,
            KeyRead);

        if (key == IntPtr.Zero || key == new IntPtr(-1))
        {
            return false;
        }

        try
        {
            uint type = 0;
            uint size = 0;
            int status = RegQueryValueEx(key, "NetCfgInstanceId", IntPtr.Zero, ref type, null, ref size);
            if (status != 0 || size == 0)
            {
                return false;
            }

            byte[] buffer = new byte[size];
            status = RegQueryValueEx(key, "NetCfgInstanceId", IntPtr.Zero, ref type, buffer, ref size);
            if (status != 0)
            {
                return false;
            }

            instanceId = Encoding.Unicode.GetString(buffer).TrimEnd('\0');
            return !string.IsNullOrWhiteSpace(instanceId);
        }
        finally
        {
            RegCloseKey(key);
        }
    }

    private static bool TrySetEnabledNetsh(string adapterId, bool enabled, out string error)
    {
        error = string.Empty;
        if (!TryResolveAlias(adapterId, out string alias, out error))
        {
            return false;
        }

        string admin = enabled ? "ENABLED" : "DISABLED";
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"interface set interface name=\"{alias}\" admin={admin}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (process is null)
            {
                error = "Failed to start netsh.";
                return false;
            }

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(15_000);
            if (process.ExitCode != 0)
            {
                error = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                if (string.IsNullOrWhiteSpace(error))
                {
                    error = $"netsh exit code {process.ExitCode}";
                }

                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryResolveAlias(string adapterId, out string alias, out string error)
    {
        alias = string.Empty;
        error = string.Empty;
        if (!Guid.TryParse(adapterId, out Guid guid))
        {
            error = "Invalid adapter GUID.";
            return false;
        }

        if (NativeMethods.ConvertInterfaceGuidToLuid(guid, out ulong luid) != NativeMethods.NO_ERROR)
        {
            error = "Could not resolve interface LUID.";
            return false;
        }

        var row = new NativeMethods.MibIfRow2 { InterfaceLuid = luid };
        if (NativeMethods.GetIfEntry2(ref row) != NativeMethods.NO_ERROR
            || string.IsNullOrWhiteSpace(row.Alias))
        {
            error = "Could not resolve interface alias.";
            return false;
        }

        alias = row.Alias;
        return true;
    }

    private static string NormalizeGuid(string value)
    {
        if (Guid.TryParse(value, out Guid guid))
        {
            return guid.ToString("B");
        }

        return value.Trim();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpClassInstallHeader
    {
        public int CbSize;
        public int InstallFunction;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpPropchangeParams
    {
        public SpClassInstallHeader ClassInstallHeader;
        public int StateChange;
        public int Scope;
        public int HwProfile;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public int CbSize;
        public Guid ClassGuid;
        public int DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        IntPtr enumerator,
        IntPtr hwndParent,
        int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet,
        uint memberIndex,
        ref SpDevinfoData deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiOpenDevRegKey(
        IntPtr deviceInfoSet,
        ref SpDevinfoData deviceInfoData,
        int scope,
        int hwProfile,
        int keyType,
        int samDesired);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiSetClassInstallParams(
        IntPtr deviceInfoSet,
        ref SpDevinfoData deviceInfoData,
        ref SpPropchangeParams classInstallParams,
        int classInstallParamsSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiCallClassInstaller(
        int installFunction,
        IntPtr deviceInfoSet,
        ref SpDevinfoData deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegQueryValueEx(
        IntPtr hKey,
        string lpValueName,
        IntPtr lpReserved,
        ref uint lpType,
        byte[]? lpData,
        ref uint lpcbData);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegCloseKey(IntPtr hKey);
}
