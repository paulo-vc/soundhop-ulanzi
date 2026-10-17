// SoundHop native helper. Compiled with the csc.exe that ships with Windows
// (C# 5, .NET Framework 4). Talks to the Node plugin over stdin/stdout:
//   in : <id>\t<command>\t<arg1>\t<arg2>...
//   out: {"id":<id>,"ok":true,"data":...} | {"id":<id>,"ok":false,"error":"..."}
//   out: {"event":"audio"}  (pushed when a default device or mute state changes, no polling)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace SoundHop
{
    // ------------------------------------------------------------------ JSON
    static class Json
    {
        public static string Str(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c); else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        public static string Bool(bool b) { return b ? "true" : "false"; }

        public static string Arr(IEnumerable<string> items) { return "[" + string.Join(",", items) + "]"; }
    }

    static class Out
    {
        static readonly object Gate = new object();
        static readonly TextWriter W;

        static Out()
        {
            var stdout = Console.OpenStandardOutput();
            W = new StreamWriter(stdout, new UTF8Encoding(false)) { AutoFlush = true };
        }

        public static void Line(string json) { lock (Gate) W.WriteLine(json); }
        public static void Ok(string id, string data) { Line("{\"id\":" + id + ",\"ok\":true,\"data\":" + (data ?? "null") + "}"); }
        public static void Err(string id, string msg) { Line("{\"id\":" + id + ",\"ok\":false,\"error\":" + Json.Str(msg) + "}"); }
        public static void Event(string name, string extra)
        {
            Line("{\"event\":" + Json.Str(name) + (string.IsNullOrEmpty(extra) ? "" : "," + extra) + "}");
        }
    }

    /// <summary>Coalesces bursts of notifications into a single event.</summary>
    class Debouncer
    {
        readonly Timer timer;
        public Debouncer(int ms, Action fire)
        {
            this.ms = ms;
            timer = new Timer(delegate { fire(); }, null, Timeout.Infinite, Timeout.Infinite);
        }
        readonly int ms;
        public void Poke() { timer.Change(ms, Timeout.Infinite); }
    }

    // ------------------------------------------------------------- Core Audio
    public enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
    public enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROPERTYKEY
    {
        public Guid fmtid;
        public int pid;
        public PROPERTYKEY(Guid f, int p) { fmtid = f; pid = p; }
    }

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
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
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

    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMNotificationClient
    {
        void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, int state);
        void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        void OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string id);
        void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PROPERTYKEY key);
    }

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr notifyData);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int GetChannelCount(out int count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDB, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDB);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(int channel, float levelDB, ref Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(int channel, float level, ref Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(int channel, out float levelDB);
        [PreserveSig] int GetChannelVolumeLevelScalar(int channel, out float level);
        [PreserveSig] int SetMute(int mute, ref Guid eventContext);
        [PreserveSig] int GetMute(out int mute);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr a, int b, out IntPtr c);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr a, int b, out IntPtr c);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr a, IntPtr b);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr a, IntPtr b);
        [PreserveSig] int GetGroupingParam(out Guid g);
        [PreserveSig] int SetGroupingParam(IntPtr a, IntPtr b);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr a);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr a);
        [PreserveSig] int GetSessionIdentifier(out IntPtr id);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
    }

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
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorComObject { }
    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] class PolicyConfigComObject { }

    class Audio : IMMNotificationClient, IAudioEndpointVolumeCallback
    {
        const int DEVICE_STATE_ACTIVE = 1;
        const int CLSCTX_ALL = 23;
        static Guid IID_Volume = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
        static Guid IID_Meter = new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064");
        static Guid IID_Sessions = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
        static readonly PROPERTYKEY PKEY_FriendlyName = new PROPERTYKEY(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
        static readonly PROPERTYKEY PKEY_DeviceDesc = new PROPERTYKEY(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);
        static readonly PROPERTYKEY PKEY_InterfaceName = new PROPERTYKEY(new Guid("026e516e-b814-414b-83cd-856d6fef4822"), 2);

        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT pvar);

        readonly IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        readonly object gate = new object();
        // Endpoint volume objects we listen to, so mute changes made anywhere arrive as events.
        readonly Dictionary<string, IAudioEndpointVolume> watched = new Dictionary<string, IAudioEndpointVolume>();
        readonly Dictionary<string, bool> lastMute = new Dictionary<string, bool>();
        readonly Debouncer changed;
        readonly Debouncer resync;

        public Audio()
        {
            changed = new Debouncer(80, delegate { Out.Event("audio", null); });
            resync = new Debouncer(300, delegate { SyncWatches(); changed.Poke(); });
            en.RegisterEndpointNotificationCallback(this);
            SyncWatches();
        }

        // --- notifications (called on COM worker threads)
        public void OnDeviceStateChanged(string id, int state) { resync.Poke(); }
        public void OnDeviceAdded(string id) { resync.Poke(); }
        public void OnDeviceRemoved(string id) { resync.Poke(); }
        public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string id) { changed.Poke(); }
        public void OnPropertyValueChanged(string id, PROPERTYKEY key) { }

        public int OnNotify(IntPtr data)
        {
            // Only mute changes matter; plain volume changes are dropped here so
            // dragging a volume slider never reaches Node.
            bool any = false;
            lock (gate)
            {
                foreach (var kv in watched)
                {
                    int m;
                    if (kv.Value.GetMute(out m) != 0) continue;
                    bool now = m != 0;
                    bool before;
                    if (!lastMute.TryGetValue(kv.Key, out before) || before != now) { lastMute[kv.Key] = now; any = true; }
                }
            }
            if (any) changed.Poke();
            return 0;
        }

        void SyncWatches()
        {
            lock (gate)
            {
                var live = new HashSet<string>();
                foreach (EDataFlow flow in new[] { EDataFlow.eRender, EDataFlow.eCapture })
                {
                    foreach (var dev in Devices(flow))
                    {
                        string id;
                        dev.GetId(out id);
                        live.Add(id);
                        if (!watched.ContainsKey(id))
                        {
                            try
                            {
                                var vol = Volume(dev);
                                vol.RegisterControlChangeNotify(this);
                                watched[id] = vol;
                                int m;
                                vol.GetMute(out m);
                                lastMute[id] = m != 0;
                            }
                            catch { }
                        }
                        Marshal.ReleaseComObject(dev);
                    }
                }
                foreach (var id in new List<string>(watched.Keys))
                {
                    if (live.Contains(id)) continue;
                    try { watched[id].UnregisterControlChangeNotify(this); } catch { }
                    Marshal.ReleaseComObject(watched[id]);
                    watched.Remove(id);
                    lastMute.Remove(id);
                }
            }
        }

        List<IMMDevice> Devices(EDataFlow flow)
        {
            var list = new List<IMMDevice>();
            IMMDeviceCollection col;
            if (en.EnumAudioEndpoints(flow, DEVICE_STATE_ACTIVE, out col) != 0) return list;
            int count;
            col.GetCount(out count);
            for (int i = 0; i < count; i++)
            {
                IMMDevice dev;
                if (col.Item(i, out dev) == 0 && dev != null) list.Add(dev);
            }
            Marshal.ReleaseComObject(col);
            return list;
        }

        static IAudioEndpointVolume Volume(IMMDevice dev)
        {
            object o;
            Marshal.ThrowExceptionForHR(dev.Activate(ref IID_Volume, CLSCTX_ALL, IntPtr.Zero, out o));
            return (IAudioEndpointVolume)o;
        }

        static string ReadString(IPropertyStore store, PROPERTYKEY key)
        {
            PROPVARIANT pv;
            if (store.GetValue(ref key, out pv) != 0) return "";
            try { return pv.vt == 31 ? Marshal.PtrToStringUni(pv.pointerValue) : ""; }
            finally { PropVariantClear(ref pv); }
        }

        public string DefaultId(EDataFlow flow, ERole role)
        {
            IMMDevice dev;
            if (en.GetDefaultAudioEndpoint(flow, role, out dev) != 0 || dev == null) return "";
            string id;
            dev.GetId(out id);
            Marshal.ReleaseComObject(dev);
            return id ?? "";
        }

        string ListFlow(EDataFlow flow)
        {
            string defConsole = DefaultId(flow, ERole.eConsole);
            string defComm = DefaultId(flow, ERole.eCommunications);
            var items = new List<string>();
            foreach (var dev in Devices(flow))
            {
                string id;
                dev.GetId(out id);
                string name = "", desc = "", iface = "";
                IPropertyStore store;
                if (dev.OpenPropertyStore(0, out store) == 0 && store != null)
                {
                    name = ReadString(store, PKEY_FriendlyName);
                    desc = ReadString(store, PKEY_DeviceDesc);
                    iface = ReadString(store, PKEY_InterfaceName);
                    Marshal.ReleaseComObject(store);
                }
                bool muted;
                lock (gate) { if (!lastMute.TryGetValue(id, out muted)) muted = false; }
                Marshal.ReleaseComObject(dev);
                items.Add("{\"id\":" + Json.Str(id) + ",\"name\":" + Json.Str(name) + ",\"desc\":" + Json.Str(desc) +
                    ",\"iface\":" + Json.Str(iface) + ",\"isDefault\":" + Json.Bool(id == defConsole) +
                    ",\"isDefaultComm\":" + Json.Bool(id == defComm) + ",\"muted\":" + Json.Bool(muted) + "}");
            }
            return Json.Arr(items);
        }

        public string List()
        {
            return "{\"render\":" + ListFlow(EDataFlow.eRender) + ",\"capture\":" + ListFlow(EDataFlow.eCapture) + "}";
        }

        public void SetDefault(string id)
        {
            var policy = (IPolicyConfig)new PolicyConfigComObject();
            try
            {
                Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, ERole.eConsole));
                Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, ERole.eMultimedia));
                Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, ERole.eCommunications));
            }
            finally { Marshal.ReleaseComObject(policy); }
        }

        public void SetMute(string id, bool mute)
        {
            IMMDevice dev;
            Marshal.ThrowExceptionForHR(en.GetDevice(id, out dev));
            try
            {
                var vol = Volume(dev);
                Guid ctx = Guid.Empty;
                Marshal.ThrowExceptionForHR(vol.SetMute(mute ? 1 : 0, ref ctx));
                Marshal.ReleaseComObject(vol);
            }
            finally { Marshal.ReleaseComObject(dev); }
            // lastMute is left to OnNotify, which then reports the change as an event.
        }

        public IMMDevice Get(string id)
        {
            IMMDevice dev;
            Marshal.ThrowExceptionForHR(en.GetDevice(id, out dev));
            return dev;
        }

        public static IAudioMeterInformation Meter(IMMDevice dev)
        {
            object o;
            Marshal.ThrowExceptionForHR(dev.Activate(ref IID_Meter, CLSCTX_ALL, IntPtr.Zero, out o));
            return (IAudioMeterInformation)o;
        }

        /// <summary>Process ids with an audio session on the device (active only, or any).</summary>
        public static List<uint> SessionPids(IMMDevice dev, bool activeOnly)
        {
            var pids = new List<uint>();
            object o;
            if (dev.Activate(ref IID_Sessions, CLSCTX_ALL, IntPtr.Zero, out o) != 0) return pids;
            var mgr = (IAudioSessionManager2)o;
            IAudioSessionEnumerator sessions;
            if (mgr.GetSessionEnumerator(out sessions) == 0)
            {
                int count;
                sessions.GetCount(out count);
                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl2 s;
                    if (sessions.GetSession(i, out s) != 0 || s == null) continue;
                    int state;
                    s.GetState(out state);
                    uint pid;
                    s.GetProcessId(out pid);
                    bool system = s.IsSystemSoundsSession() == 0;
                    if (!system && (!activeOnly || state == 1)) pids.Add(pid);
                    Marshal.ReleaseComObject(s);
                }
                Marshal.ReleaseComObject(sessions);
            }
            Marshal.ReleaseComObject(mgr);
            return pids;
        }

        public List<string> ActiveIds(EDataFlow flow)
        {
            var ids = new List<string>();
            foreach (var dev in Devices(flow))
            {
                string id;
                dev.GetId(out id);
                ids.Add(id);
                Marshal.ReleaseComObject(dev);
            }
            return ids;
        }

        public List<IMMDevice> DevicesOf(EDataFlow flow) { return Devices(flow); }
    }

    // ----------------------------------------------------------------- Main
    static class Program
    {
        static Audio audio;

        static string Handle(string[] a)
        {
            switch (a[1])
            {
                case "list": return audio.List();
                case "set": audio.SetDefault(a[2]); return null;
                case "mute": audio.SetMute(a[2], a[3] == "1"); return null;
                case "ping": return "\"pong\"";
                default: throw new Exception("unknown command: " + a[1]);
            }
        }

        [MTAThread]
        static int Main()
        {
            var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            try { audio = new Audio(); }
            catch (Exception e) { Out.Err("0", e.Message); return 1; }
            Out.Ok("0", "\"ready\"");

            string line;
            while ((line = stdin.ReadLine()) != null)
            {
                var a = line.Split('	');
                if (a.Length < 2) continue;
                if (a[1] == "exit") break;
                try { Out.Ok(a[0], Handle(a)); }
                catch (Exception e) { Out.Err(a[0], e.Message); }
            }
            return 0;
        }
    }
}
