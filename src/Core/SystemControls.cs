using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WRadios = global::Windows.Devices.Radios;

namespace Island.Core;

/// <summary>Lautstärke über die Windows-Audio-Schnittstelle (Core Audio). Nur der Standard-Lautsprecher.</summary>
public static class AudioControl
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    private static IAudioEndpointVolume? Endpoint()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            if (enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var device) != 0 || device == null) return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var obj) != 0) return null;
            return obj as IAudioEndpointVolume;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return null;
        }
    }

    /// <summary>0–1, null = kein Lautsprecher.</summary>
    public static double? Volume
    {
        get
        {
            var ep = Endpoint();
            if (ep == null || ep.GetMasterVolumeLevelScalar(out var v) != 0) return null;
            return v;
        }
        set
        {
            if (value is not double v) return;
            var ep = Endpoint();
            var ctx = Guid.Empty;
            ep?.SetMasterVolumeLevelScalar((float)Math.Clamp(v, 0, 1), ref ctx);
            if (v > 0.001 && Muted == true) Muted = false;
        }
    }

    public static bool? Muted
    {
        get
        {
            var ep = Endpoint();
            if (ep == null || ep.GetMute(out var m) != 0) return null;
            return m;
        }
        set
        {
            if (value is not bool m) return;
            var ep = Endpoint();
            var ctx = Guid.Empty;
            ep?.SetMute(m, ref ctx);
        }
    }
}

/// <summary>
/// Bildschirm-Helligkeit: Laptops über WMI, externe Monitore über DDC/CI (wenn der Monitor das kann).
/// Läuft im Hintergrund, damit nichts ruckelt.
/// </summary>
public static class BrightnessControl
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetMonitorBrightness(IntPtr monitor, out uint min, out uint current, out uint max);
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool SetMonitorBrightness(IntPtr monitor, uint value);
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] monitors);

    private static readonly object Gate = new();
    private static int? _pending;
    private static bool _worker;

    /// <summary>0–100, null = lässt sich nicht steuern.</summary>
    public static Task<int?> GetAsync() => Task.Run(() =>
    {
        lock (Gate)
        {
            var wmi = GetWmi();
            return wmi ?? GetDdc();
        }
    });

    /// <summary>Setzt die Helligkeit. Schnelle Änderungen (Ziehen) werden zusammengefasst.</summary>
    public static void Set(int percent)
    {
        _pending = Math.Clamp(percent, 0, 100);
        if (_worker) return;
        _worker = true;
        Task.Run(() =>
        {
            try
            {
                while (_pending is int value)
                {
                    _pending = null;
                    lock (Gate)
                    {
                        if (!SetWmi(value)) SetDdc(value);
                    }
                    Thread.Sleep(60);
                }
            }
            catch (Exception ex) { App.Log(ex); }
            finally { _worker = false; }
        });
    }

    private static int? GetWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject o in searcher.Get())
                using (o) return Convert.ToInt32(o["CurrentBrightness"]);
        }
        catch { /* kein Laptop-Bildschirm */ }
        return null;
    }

    private static bool SetWmi(int value)
    {
        try
        {
            using var cls = new ManagementClass(@"root\WMI", "WmiMonitorBrightnessMethods", null);
            var any = false;
            foreach (ManagementObject o in cls.GetInstances())
            {
                using (o) o.InvokeMethod("WmiSetBrightness", new object[] { 1u, (byte)value });
                any = true;
            }
            return any;
        }
        catch { return false; }
    }

    private static List<PHYSICAL_MONITOR>? Physical(out PHYSICAL_MONITOR[] all)
    {
        all = Array.Empty<PHYSICAL_MONITOR>();
        var hMon = MonitorFromPoint(new POINT { X = 0, Y = 0 }, 1 /* primär */);
        if (hMon == IntPtr.Zero || !GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out var n) || n == 0) return null;
        all = new PHYSICAL_MONITOR[n];
        return GetPhysicalMonitorsFromHMONITOR(hMon, n, all) ? all.ToList() : null;
    }

    private static int? GetDdc()
    {
        try
        {
            var list = Physical(out var all);
            if (list == null) return null;
            try
            {
                foreach (var m in list)
                    if (GetMonitorBrightness(m.hPhysicalMonitor, out var min, out var cur, out var max) && max > min)
                        return (int)Math.Round(100.0 * (cur - min) / (max - min));
            }
            finally { DestroyPhysicalMonitors((uint)all.Length, all); }
        }
        catch (Exception ex) { App.Log(ex); }
        return null;
    }

    private static void SetDdc(int value)
    {
        try
        {
            var list = Physical(out var all);
            if (list == null) return;
            try
            {
                foreach (var m in list)
                    if (GetMonitorBrightness(m.hPhysicalMonitor, out var min, out _, out var max) && max > min)
                        SetMonitorBrightness(m.hPhysicalMonitor, (uint)(min + (max - min) * value / 100.0));
            }
            finally { DestroyPhysicalMonitors((uint)all.Length, all); }
        }
        catch (Exception ex) { App.Log(ex); }
    }
}

/// <summary>WLAN, Bluetooth und Flugmodus über die Windows-Funk-Schnittstelle.</summary>
public static class RadioControl
{
    private static readonly Dictionary<WRadios.RadioKind, WRadios.RadioState> BeforeFlight = new();

    public static async Task<IReadOnlyList<WRadios.Radio>> GetAsync()
    {
        try
        {
            var access = await WRadios.Radio.RequestAccessAsync();
            if (access != WRadios.RadioAccessStatus.Allowed) return Array.Empty<WRadios.Radio>();
            return (await WRadios.Radio.GetRadiosAsync()).ToList();
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return Array.Empty<WRadios.Radio>();
        }
    }

    public static async Task<WRadios.Radio?> FindAsync(WRadios.RadioKind kind) => (await GetAsync()).FirstOrDefault(r => r.Kind == kind);

    /// <summary>true = geklappt, false = Windows erlaubt es nicht (dann Einstellungen öffnen).</summary>
    public static async Task<bool> ToggleAsync(WRadios.RadioKind kind)
    {
        var radio = await FindAsync(kind);
        if (radio == null) return false;
        var target = radio.State == WRadios.RadioState.On ? WRadios.RadioState.Off : WRadios.RadioState.On;
        try { return await radio.SetStateAsync(target) == WRadios.RadioAccessStatus.Allowed; }
        catch (Exception ex) { App.Log(ex); return false; }
    }

    /// <summary>Flugmodus: alle Funkverbindungen aus – und beim zweiten Mal wieder wie vorher.</summary>
    public static async Task<bool> ToggleFlightAsync()
    {
        var radios = await GetAsync();
        if (radios.Count == 0) return false;
        var anyOn = radios.Any(r => r.State == WRadios.RadioState.On);
        try
        {
            if (anyOn)
            {
                BeforeFlight.Clear();
                foreach (var r in radios)
                {
                    BeforeFlight[r.Kind] = r.State;
                    if (r.State == WRadios.RadioState.On) await r.SetStateAsync(WRadios.RadioState.Off);
                }
            }
            else
            {
                foreach (var r in radios)
                {
                    var wasOn = !BeforeFlight.TryGetValue(r.Kind, out var before) || before == WRadios.RadioState.On;
                    if (wasOn) await r.SetStateAsync(WRadios.RadioState.On);
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return false;
        }
    }

    public static async Task<bool> FlightActiveAsync()
    {
        var radios = await GetAsync();
        return radios.Count > 0 && radios.All(r => r.State != WRadios.RadioState.On);
    }
}
