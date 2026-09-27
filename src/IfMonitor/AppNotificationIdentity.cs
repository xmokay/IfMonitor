using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace IfMonitor;

/// <summary>
/// Win11 toast chrome uses AppUserModelID, not NotifyIcon. Toasts are shown via
/// WinRT COM (no Windows SDK projection) so framework-dependent publishes stay small.
/// A PNG in LocalAppData is registered as IconUri because WpnUserService cannot
/// extract an icon from an elevated process.
/// </summary>
internal static class AppNotificationIdentity
{
    private const string AppUserModelId = "IfMonitor.NetworkMonitor";
    private const string DisplayName = "IfMonitor";

    public static void Register()
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        }
        catch
        {
            // Non-fatal on older Windows.
        }

        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return;
        }

        string? iconPath = TryWriteNotificationIcon();
        string iconForRegistry = iconPath ?? exePath;

        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(
                @"Software\Classes\AppUserModelId\" + AppUserModelId);
            key.SetValue("DisplayName", DisplayName);
            key.SetValue("IconUri", iconForRegistry);
        }
        catch
        {
            // Shortcut AUMID may still be enough for the header icon.
        }

        try
        {
            SaveShortcut(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "IfMonitor.lnk"),
                exePath);
        }
        catch
        {
            // Best-effort; tray monitoring works without the shortcut.
        }
    }

    public static bool TryShowToast(string title, string text)
    {
        try
        {
            RoInitialize(0); // RO_INIT_SINGLETHREADED; S_FALSE / RPC_E_CHANGED_MODE are fine

            var xml = new StringBuilder();
            xml.Append("<toast><visual><binding template=\"ToastGeneric\">");
            xml.Append("<text>").Append(XmlEscape(title)).Append("</text>");
            xml.Append("<text>").Append(XmlEscape(text)).Append("</text>");
            xml.Append("</binding></visual></toast>");

            var xmlFactory = GetActivationFactory<IActivationFactory>("Windows.Data.Xml.Dom.XmlDocument");
            Marshal.ThrowExceptionForHR(xmlFactory.ActivateInstance(out object xmlObj));
            var xmlDoc = (IXmlDocumentIO)xmlObj;
            IntPtr xmlH = CreateHString(xml.ToString());
            try
            {
                Marshal.ThrowExceptionForHR(xmlDoc.LoadXml(xmlH));
            }
            finally
            {
                WindowsDeleteString(xmlH);
            }

            var toastFactory = GetActivationFactory<IToastNotificationFactory>(
                "Windows.UI.Notifications.ToastNotification");
            Marshal.ThrowExceptionForHR(toastFactory.CreateToastNotification(xmlObj, out IToastNotification toast));

            var manager = GetActivationFactory<IToastNotificationManagerStatics>(
                "Windows.UI.Notifications.ToastNotificationManager");
            IntPtr aumidH = CreateHString(AppUserModelId);
            int hr;
            IToastNotifier notifier;
            try
            {
                hr = manager.CreateToastNotifierWithId(aumidH, out notifier);
            }
            finally
            {
                WindowsDeleteString(aumidH);
            }

            if (hr < 0)
            {
                hr = manager.CreateToastNotifier(out notifier);
            }

            Marshal.ThrowExceptionForHR(hr);
            Marshal.ThrowExceptionForHR(notifier.Show(toast));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? TryWriteNotificationIcon()
    {
        try
        {
            Directory.CreateDirectory(ConfigStore.ConfigDirectory);
            string path = Path.Combine(ConfigStore.ConfigDirectory, "notification-icon.png");
            using Bitmap bmp = IconArtwork.Render(256);
            bmp.Save(path, ImageFormat.Png);
            return path;
        }
        catch
        {
            return null;
        }
    }

    private static string XmlEscape(string value) =>
        SecurityElement.Escape(value) ?? value;

    private static void SaveShortcut(string shortcutPath, string exePath)
    {
        Type? type = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"));
        if (type is null)
        {
            return;
        }

        object link = Activator.CreateInstance(type)!;
        try
        {
            var shellLink = (IShellLinkW)link;
            shellLink.SetPath(exePath);
            shellLink.SetWorkingDirectory(Path.GetDirectoryName(exePath) ?? exePath);
            shellLink.SetDescription(DisplayName);
            shellLink.SetIconLocation(exePath, 0);

            var store = (IPropertyStore)link;
            PropertyKey key = PropertyKey.AppUserModelId;
            PropVariant value = PropVariant.FromString(AppUserModelId);
            try
            {
                Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value));
                Marshal.ThrowExceptionForHR(store.Commit());
            }
            finally
            {
                value.Clear();
            }

            ((IPersistFile)link).Save(shortcutPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appId);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;

        public static PropertyKey AppUserModelId { get; } = new()
        {
            FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
            PropertyId = 5,
        };
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] private ushort _vt;
        [FieldOffset(8)] private IntPtr _value;

        public static PropVariant FromString(string value) => new()
        {
            _vt = 31,
            _value = Marshal.StringToCoTaskMemUni(value),
        };

        public void Clear() => PropVariantClear(ref this);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    private static T GetActivationFactory<T>(string runtimeClass) where T : class
    {
        IntPtr className = CreateHString(runtimeClass);
        try
        {
            Guid iid = typeof(T).GUID;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(className, in iid, out IntPtr unk));
            try
            {
                return (T)Marshal.GetObjectForIUnknown(unk);
            }
            finally
            {
                Marshal.Release(unk);
            }
        }
        finally
        {
            WindowsDeleteString(className);
        }
    }

    private static IntPtr CreateHString(string value)
    {
        Marshal.ThrowExceptionForHR(WindowsCreateString(value, (uint)value.Length, out IntPtr hstring));
        return hstring;
    }

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int RoInitialize(int initType);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, in Guid iid, out IntPtr factory);

    [DllImport("combase.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
        uint length,
        out IntPtr hstring);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("00000035-0000-0000-C000-000000000046")]
    private interface IActivationFactory
    {
        void GetIids(out int iidCount, out IntPtr iids);
        void GetRuntimeClassName(out IntPtr className);
        void GetTrustLevel(out int trustLevel);
        [PreserveSig] int ActivateInstance([MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("6CD0E74E-EE65-4489-9EBF-CA43E87BA637")]
    private interface IXmlDocumentIO
    {
        void GetIids(out int iidCount, out IntPtr iids);
        void GetRuntimeClassName(out IntPtr className);
        void GetTrustLevel(out int trustLevel);
        [PreserveSig] int LoadXml(IntPtr xml);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("997E2675-059E-4E60-8B06-1760917C8B80")]
    private interface IToastNotificationFactory
    {
        void GetIids(out int iidCount, out IntPtr iids);
        void GetRuntimeClassName(out IntPtr className);
        void GetTrustLevel(out int trustLevel);
        [PreserveSig] int CreateToastNotification(
            [MarshalAs(UnmanagedType.IUnknown)] object content,
            out IToastNotification notification);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("997E2674-059E-4E60-8B06-1760917C8B80")]
    private interface IToastNotification
    {
        void GetIids(out int iidCount, out IntPtr iids);
        void GetRuntimeClassName(out IntPtr className);
        void GetTrustLevel(out int trustLevel);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("50AC103F-D235-4598-BBEF-98FE4D1A3AD4")]
    private interface IToastNotificationManagerStatics
    {
        void GetIids(out int iidCount, out IntPtr iids);
        void GetRuntimeClassName(out IntPtr className);
        void GetTrustLevel(out int trustLevel);
        [PreserveSig] int CreateToastNotifier(out IToastNotifier notifier);
        [PreserveSig] int CreateToastNotifierWithId(IntPtr applicationId, out IToastNotifier notifier);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("75927B93-03F3-41A5-9C0B-0C3C6E5577F0")]
    private interface IToastNotifier
    {
        void GetIids(out int iidCount, out IntPtr iids);
        void GetRuntimeClassName(out IntPtr className);
        void GetTrustLevel(out int trustLevel);
        [PreserveSig] int Show(IToastNotification notification);
    }
}
