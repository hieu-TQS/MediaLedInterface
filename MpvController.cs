using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MediaLedInterfaceNew
{
    public class MpvController : IDisposable
    {
        private IntPtr _mpvHandle = IntPtr.Zero;
        public IntPtr Handle { get; private set; } = IntPtr.Zero;
        private int _lastOpacity = -1;
        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_CLIPCHILDREN = 0x02000000;
        private const int WS_EX_LAYERED = 0x80000;
        private const int LWA_ALPHA = 0x2;
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr mpv_create();

        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int mpv_initialize(IntPtr mpvHandle);

        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int mpv_command(IntPtr mpvHandle, IntPtr args);

        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int mpv_set_option_string(IntPtr mpvHandle, string name, string data);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void mpv_terminate_destroy(IntPtr mpvHandle);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr mpv_get_property_string(IntPtr mpvHandle, string name);

        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void mpv_free(IntPtr data);

        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int mpv_set_property(IntPtr mpvHandle, string name, int format, ref double data);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int mpv_set_property_string(IntPtr mpvHandle, string name, string data);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
        [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);
        [DllImport("user32.dll")] private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);
        [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int mpv_get_property(IntPtr mpvHandle, string name, int format, ref long data);
        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int mpv_set_property(IntPtr mpvHandle, string name, int format, ref long data);
        [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
        [StructLayout(LayoutKind.Sequential)]
        private struct WNDCLASS
        {
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
        }
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        private WndProcDelegate? _wndProc;

        private IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int mpv_request_log_messages(IntPtr mpvHandle, string minLevel);
        public void Initialize(IntPtr parentHwnd)
        {
            string className = "MpvChild_" + Guid.NewGuid().ToString();
            _wndProc = new WndProcDelegate(CustomWndProc);

            WNDCLASS wc = new WNDCLASS
            {
                lpszClassName = className,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hbrBackground = IntPtr.Zero,
                hCursor = LoadCursor(IntPtr.Zero, 32512)
            };
            RegisterClass(ref wc);
            Handle = CreateWindowEx(
    0,
    className,
    "MpvChild",
    WS_CHILD | WS_CLIPCHILDREN | WS_VISIBLE,
    0, 0, 1280, 720,
    parentHwnd,
    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            _mpvHandle = mpv_create();
            if (_mpvHandle == IntPtr.Zero) return;
            mpv_set_option_string(_mpvHandle, "log-file", "D:\\mpv_debug_log.txt");
            mpv_set_option_string(_mpvHandle, "msg-level", "all=warn,lavfi=debug");
            mpv_request_log_messages(_mpvHandle, "info");
            mpv_set_option_string(_mpvHandle, "wid", Handle.ToInt64().ToString());
            mpv_set_option_string(_mpvHandle, "force-window", "yes");
            mpv_set_option_string(_mpvHandle, "background", "#000000");
            mpv_set_option_string(_mpvHandle, "osc", "no");
            mpv_set_option_string(_mpvHandle, "input-default-bindings", "no");
            mpv_set_option_string(_mpvHandle, "input-vo-keyboard", "no");
            mpv_set_option_string(_mpvHandle, "input-media-keys", "no");
            mpv_set_option_string(_mpvHandle, "vo", "gpu");
            mpv_set_option_string(_mpvHandle, "gpu-context", "angle");
            mpv_set_option_string(_mpvHandle, "gpu-api", "d3d11");
            mpv_set_option_string(_mpvHandle, "hwdec", "auto");
            mpv_set_option_string(_mpvHandle, "ontop", "no");
            mpv_set_option_string(_mpvHandle, "input-default-bindings", "yes");
            mpv_set_option_string(_mpvHandle, "hwdec-codecs", "all");
            mpv_set_option_string(_mpvHandle, "network-timeout", "20");
            mpv_set_option_string(_mpvHandle, "cache", "yes");
            mpv_set_option_string(_mpvHandle, "demuxer-max-bytes", "512MiB");
            mpv_set_option_string(_mpvHandle, "demuxer-max-back-bytes", "512MiB");
            mpv_set_option_string(_mpvHandle, "stream-buffer-size", "5MiB");
            mpv_set_option_string(_mpvHandle, "vd-lavc-threads", "0");
            mpv_set_option_string(_mpvHandle, "video-unscaled", "downscale-big");
            mpv_set_option_string(_mpvHandle, "vd-lavc-software-fallback", "yes");
            mpv_set_option_string(_mpvHandle, "icc-profile-auto", "no");
            mpv_set_option_string(_mpvHandle, "force-window", "no");
            mpv_set_option_string(_mpvHandle, "hr-seek", "yes");
            mpv_set_option_string(_mpvHandle, "hr-seek-framedrop", "yes");
            mpv_set_option_string(_mpvHandle, "image-display-duration", "inf");
            mpv_set_option_string(_mpvHandle, "background", "#000000");
            mpv_set_option_string(_mpvHandle, "ytdl", "yes");
            mpv_set_option_string(_mpvHandle, "ytdl-raw-options", "sub-langs=all,write-sub=,write-auto-sub=");
            mpv_set_option_string(_mpvHandle, "slang", "vi,vie,en,eng");
            mpv_set_option_string(_mpvHandle, "sid", "no");
            mpv_set_option_string(_mpvHandle, "sub-ass-override", "force");
            mpv_set_option_string(_mpvHandle, "sub-use-margins", "yes");
            mpv_set_option_string(_mpvHandle, "sub-ass-force-margins", "yes");
            mpv_initialize(_mpvHandle);
            ShowWindow(parentHwnd, 5);
            _lastOpacity = 255;
            ShowWindow(Handle, 5);
        }
        public void ClearState()
        {
            if (_mpvHandle == IntPtr.Zero) return;
            DoCommand("stop");
            DoCommand("playlist-clear");
            SetPropertyString("cache", "no");
            SetPropertyString("cache", "yes");
        }
        public void PlayLargeImage(string filePath)
        {
            if (_mpvHandle == IntPtr.Zero) return;
            mpv_set_option_string(_mpvHandle, "hwdec", "no");
            mpv_set_option_string(_mpvHandle, "vd-lavc-threads", "0");
            mpv_set_option_string(_mpvHandle, "video-unscaled", "downscale-big");
            DoCommand("loadfile", filePath);
        }
        public string GetPropertyJson(string name)
        {
            if (_mpvHandle == IntPtr.Zero) return "[]";
            IntPtr ptr = mpv_get_property_string(_mpvHandle, name);
            if (ptr != IntPtr.Zero)
            {
                string sVal = Marshal.PtrToStringUTF8(ptr);
                mpv_free(ptr);
                return sVal;
            }
            return "[]";
        }
        public void SetPropertyLong(string name, long value)
        {
            if (_mpvHandle == IntPtr.Zero) return;
            mpv_set_property(_mpvHandle, name, 4, ref value);
        }
        public long GetPropertyLong(string name)
        {
            if (_mpvHandle == IntPtr.Zero) return -1;
            long result = 0;
            mpv_get_property(_mpvHandle, name, 4, ref result);
            return result;
        }
        public void SetOpacity(byte alpha)
        {
            if (_lastOpacity == alpha) return;
            _lastOpacity = alpha;

            if (Handle == IntPtr.Zero) return;
            long exStyle = (long)GetWindowLongPtr(Handle, GWL_EXSTYLE);
            if ((exStyle & WS_EX_LAYERED) == 0)
            {
                SetWindowLongPtr(Handle, GWL_EXSTYLE, (IntPtr)(exStyle | WS_EX_LAYERED));
            }
            SetLayeredWindowAttributes(Handle, 0, alpha, LWA_ALPHA);
            if (alpha == 255)
            {
                SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                    0x0020 | 0x0001 | 0x0002 | 0x0004 | 0x0010);
            }
        }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        public void Resize(int w, int h)
        {
            if (Handle == IntPtr.Zero || !IsWindow(Handle)) return;

            try
            {
                SetWindowPos(Handle, IntPtr.Zero, 0, 0, w, h, 0x0004 | 0x0010);
            }
            catch
            {
            }
        }
        public void Play(string filePath)
        {
            if (_mpvHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(filePath))
                DoCommand("loadfile", filePath);
        }
        public void Stop() { DoCommand("stop"); }
        public void Pause() { DoCommand("set", "pause", "yes"); }
        public void Resume() { DoCommand("set", "pause", "no"); }
        public void SetVolume(double vol)
        {
            if (vol < 0) vol = 0;
            if (vol > 100) vol = 100;
            string volString = vol.ToString(System.Globalization.CultureInfo.InvariantCulture);
            DoCommand("set", "volume", volString);
        }
        public void SetPropertyString(string name, string value)
        {
            if (_mpvHandle != IntPtr.Zero)
            {
                mpv_set_property_string(_mpvHandle, name, value);
            }
        }
        public void ShowOsdText(string text, int durationMs = 3000)
        {
            if (_mpvHandle == IntPtr.Zero) return;
            DoCommand("show-text", text, durationMs.ToString());
        }
        public double GetPropertyDouble(string name)
        {
            if (_mpvHandle == IntPtr.Zero) return 0;
            IntPtr ptr = mpv_get_property_string(_mpvHandle, name);
            if (ptr != IntPtr.Zero)
            {
                try
                {
                    string sVal = Marshal.PtrToStringUTF8(ptr);
                    mpv_free(ptr);
                    if (double.TryParse(sVal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double result))
                    {
                        return result;
                    }
                }
                catch { }
            }
            return 0;
        }
        public void SetPropertyDouble(string name, double value)
        {
            if (_mpvHandle == IntPtr.Zero) return;
            mpv_set_property(_mpvHandle, name, 5, ref value);
        }
        public void Seek(double targetSeconds)
        {
            DoCommand("seek", targetSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), "absolute");
        }

        public void DoCommand(params string[] args)
        {
            if (_mpvHandle == IntPtr.Zero) return;
            IntPtr[] ptrs = new IntPtr[args.Length + 1];
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    byte[] b = Encoding.UTF8.GetBytes(args[i] + "\0");
                    ptrs[i] = Marshal.AllocHGlobal(b.Length);
                    Marshal.Copy(b, 0, ptrs[i], b.Length);
                }
                ptrs[args.Length] = IntPtr.Zero;
                IntPtr argsPtr = Marshal.AllocHGlobal(IntPtr.Size * ptrs.Length);
                Marshal.Copy(ptrs, 0, argsPtr, ptrs.Length);
                mpv_command(_mpvHandle, argsPtr);
                Marshal.FreeHGlobal(argsPtr);
            }
            finally
            {
                for (int i = 0; i < args.Length; i++) if (ptrs[i] != IntPtr.Zero) Marshal.FreeHGlobal(ptrs[i]);
            }
        }
        public void Dispose()
        {
            if (_mpvHandle != IntPtr.Zero) { mpv_terminate_destroy(_mpvHandle); _mpvHandle = IntPtr.Zero; }
            if (Handle != IntPtr.Zero) { DestroyWindow(Handle); Handle = IntPtr.Zero; }
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MpvLogCallback(IntPtr logLevel, string prefix, string text);

        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int mpv_set_log_callback(IntPtr mpvHandle, MpvLogCallback cb, IntPtr data);
        private void HandleMpvLog(IntPtr logLevel, string prefix, string text)
        {
            if (text.Contains("error") || text.Contains("failed") || prefix.Contains("lavfi") || prefix.Contains("ffmpeg"))
            {
                System.Diagnostics.Debug.WriteLine($"[MPV CORE] {prefix}: {text.Trim()}");
            }
        }
        public void AddVideoFilter(string label, string filterString)
        {
            System.Diagnostics.Debug.WriteLine($"[MPV DEBUG] Filter gửi đi: {filterString}");
            DoCommand("vf", "add", $"@{label}:{filterString}");
        }
        public void RemoveVideoFilter(string label)
        {
            DoCommand("vf", "remove", $"@{label}");
        }
        public void AddSubtitlesFromString(string data)
        {
            DoCommand("sub-remove");
            DoCommand("sub-add", $"memory://{data}", "select");
        }
        public void ClearSubtitles()
        {
            DoCommand("sub-remove");
        }
        public void Screenshot(string path)
        {
            string safePath = path.Replace("\\", "/");
            DoCommand("screenshot-to-file", safePath);
        }
        public void AddSubtitle(string path)
        {
            DoCommand("sub-add", path, "select");
        }
        public void SetSubtitleId(long id)
        {
            if (id <= 0)
            {
                DoCommand("set", "sid", "no");
            }
            else
            {
                DoCommand("set", "sid", id.ToString());
                DoCommand("set", "sub-visibility", "yes");
            }
        }
        public void SetSubtitleStyle(int size, string colorHex, int marginV, string align = "bottom")
        {
            DoCommand("set", "sub-font-size", size.ToString());
            DoCommand("set", "sub-color", $"#{colorHex}"); DoCommand("set", "sub-margin-y", marginV.ToString());
            DoCommand("set", "sub-ass-override", "force");
        }
        public void ConfigureYoutubeSubs(string langs)
        {
            mpv_set_option_string(_mpvHandle, "ytdl-raw-options", $"sub-langs={langs},write-sub=,write-auto-sub=");
        }
    }
}