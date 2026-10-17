using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace UlanziAudio
{
    public enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
    public enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROPERTYKEY
    {
        public Guid fmtid;
        public int pid;
        public PROPERTYKEY(Guid f, int p) { fmtid = f; pid = p; }
    }

    // Big enough for both x86 (16 bytes) and x64 (24 bytes) PROPVARIANT layouts.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pointerValue;
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }

    // Undocumented but stable since Windows 7; used by every default-device switcher.
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(IntPtr a, IntPtr b);
        [PreserveSig] int GetDeviceFormat(IntPtr a, int b, IntPtr c);
        [PreserveSig] int ResetDeviceFormat(IntPtr a);
        [PreserveSig] int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int SetProcessingPeriod(IntPtr a, IntPtr b);
        [PreserveSig] int GetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int SetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int GetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int SetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    class PolicyConfigComObject { }

    public static class Endpoints
    {
        const int DEVICE_STATE_ACTIVE = 0x1;
        const int STGM_READ = 0;

        static readonly PROPERTYKEY PKEY_Device_FriendlyName = new PROPERTYKEY(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
        static readonly PROPERTYKEY PKEY_Device_DeviceDesc = new PROPERTYKEY(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);
        static readonly PROPERTYKEY PKEY_DeviceInterface_FriendlyName = new PROPERTYKEY(new Guid("026e516e-b814-414b-83cd-856d6fef4822"), 2);

        [DllImport("ole32.dll")]
        static extern int PropVariantClear(ref PROPVARIANT pvar);

        static string ReadString(IPropertyStore store, PROPERTYKEY key)
        {
            PROPVARIANT pv;
            if (store.GetValue(ref key, out pv) != 0) return "";
            try
            {
                const ushort VT_LPWSTR = 31;
                return pv.vt == VT_LPWSTR ? Marshal.PtrToStringUni(pv.pointerValue) : "";
            }
            finally { PropVariantClear(ref pv); }
        }

        static string DefaultId(IMMDeviceEnumerator en, EDataFlow flow, ERole role)
        {
            IMMDevice dev;
            if (en.GetDefaultAudioEndpoint(flow, role, out dev) != 0 || dev == null) return "";
            string id;
            dev.GetId(out id);
            Marshal.ReleaseComObject(dev);
            return id ?? "";
        }

        static string Esc(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        static string ListFlow(IMMDeviceEnumerator en, EDataFlow flow)
        {
            string defConsole = DefaultId(en, flow, ERole.eConsole);
            string defComm = DefaultId(en, flow, ERole.eCommunications);

            IMMDeviceCollection col;
            Marshal.ThrowExceptionForHR(en.EnumAudioEndpoints(flow, DEVICE_STATE_ACTIVE, out col));
            int count;
            col.GetCount(out count);

            var items = new List<string>();
            for (int i = 0; i < count; i++)
            {
                IMMDevice dev;
                if (col.Item(i, out dev) != 0 || dev == null) continue;
                string id;
                dev.GetId(out id);
                string name = "", desc = "", iface = "";
                IPropertyStore store;
                if (dev.OpenPropertyStore(STGM_READ, out store) == 0 && store != null)
                {
                    name = ReadString(store, PKEY_Device_FriendlyName);
                    desc = ReadString(store, PKEY_Device_DeviceDesc);
                    iface = ReadString(store, PKEY_DeviceInterface_FriendlyName);
                    Marshal.ReleaseComObject(store);
                }
                Marshal.ReleaseComObject(dev);

                items.Add(string.Format(
                    "{{\"id\":\"{0}\",\"name\":\"{1}\",\"desc\":\"{2}\",\"iface\":\"{3}\",\"isDefault\":{4},\"isDefaultComm\":{5}}}",
                    Esc(id), Esc(name), Esc(desc), Esc(iface),
                    id == defConsole ? "true" : "false",
                    id == defComm ? "true" : "false"));
            }
            Marshal.ReleaseComObject(col);
            return "[" + string.Join(",", items.ToArray()) + "]";
        }

        // Returns {"render":[...],"capture":[...]}
        public static string List()
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            try
            {
                return "{\"render\":" + ListFlow(en, EDataFlow.eRender) + ",\"capture\":" + ListFlow(en, EDataFlow.eCapture) + "}";
            }
            finally { Marshal.ReleaseComObject(en); }
        }

        // Sets the device as default for console, multimedia and communications.
        public static void SetDefault(string deviceId)
        {
            var policy = (IPolicyConfig)new PolicyConfigComObject();
            try
            {
                Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, ERole.eConsole));
                Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, ERole.eMultimedia));
                Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, ERole.eCommunications));
            }
            finally { Marshal.ReleaseComObject(policy); }
        }
    }
}
