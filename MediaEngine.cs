using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MediaLedInterfaceNew
{
    public struct LogoLayer
    {
        public string Path;
        public int X;
        public int Y;
        public int Width;
        public int Height;
    }
    public class SubtitleTrack
    {
        public long Id { get; set; }
        public string Title { get; set; }
        public string Lang { get; set; }
        public bool IsSelected { get; set; }
        public bool IsExternal { get; set; }
    }
    public class OnlineVideoFormat
    {
        public string Id { get; set; }
        public int Height { get; set; }
        public string Codec { get; set; }
        public string Label { get; set; }
    }
    public class StreamVariant
    {
        public string Name { get; set; } = ""; public long Id { get; set; } = -1; public bool IsCurrent { get; set; } = false;
    }
    public class MediaEngine : IDisposable
    {
        public bool IsCurrentContentImage { get; private set; } = false;
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateSolidBrush(uint crColor);
        [DllImport("user32.dll")] public static extern ushort RegisterClass(ref WNDCLASS lpWndClass);
        [DllImport("user32.dll")] public static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        private const int WS_EX_NOACTIVATE = 0x08000000;
        [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(IntPtr hwndDestination, IntPtr hwndSource, out IntPtr thumb);
        [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(IntPtr hThumb, ref DWM_THUMBNAIL_PROPERTIES props);
        [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(IntPtr thumb);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private const int GWL_STYLE = -16;
        private const int WS_CHILD = 0x40000000;
        public const int WS_POPUP = unchecked((int)0x80000000);
        private Process? _activeProcess = null;
        private bool _isLogoActive = false;
        private bool _isTickerActive = false;
        private struct TickerParams
        {
            public string Content;
            public double Speed;
            public string TextColor;
            public int YPos;
            public bool UseBg;
            public string BgColor;
            public int BgHeight;
            public bool IsFullWidth;
            public string FontName;
            public int FontSize;
            public bool IsBold;
            public bool IsItalic;
            public int LoopCount;
        }
        private TickerParams? _cachedTicker = null;
        private List<SponsorSegment> _currentSponsors = new List<SponsorSegment>();
        public bool IsSponsorBlockEnabled { get; set; } = true; public HashSet<string> AllowedSponsorCategories { get; set; } = new HashSet<string>() { "sponsor" }; private string _lastFilterString = "";
        public double GetBufferedPosition()
        {
            // Lấy thông tin thời gian đã buffer từ MPV
            if (ActivePlayer != null)
            {
                return ActivePlayer.GetPropertyDouble("demuxer-cache-time");
            }
            return 0;
        }
        private void RebuildMasterFilter()
        {
            bool hasLogo = _cachedLogos != null && _cachedLogos.Count > 0;
            bool hasTicker = _isTickerActive; if (!hasLogo && !hasTicker && string.IsNullOrEmpty(_lastFilterString)) return;

            try
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("[in]scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2[base];");
                string currentStream = "[base]";
                if (hasLogo)
                {
                    for (int i = 0; i < _cachedLogos.Count; i++)
                    {
                        var logo = _cachedLogos[i];
                        string safePath = EscapeFfmpegPath(logo.Path);

                        string logoOut = $"[lg{i}]";
                        string bgOut = (i == _cachedLogos.Count - 1 && !hasTicker) ? "" : (hasTicker && i == _cachedLogos.Count - 1 ? "[after_logo]" : $"[bg{i}]");
                        sb.Append($"movie='{safePath}',loop=loop=-1:size=1:start=0,scale={logo.Width}:{logo.Height}{logoOut};");
                        sb.Append($"{currentStream}{logoOut}overlay={logo.X}:{logo.Y}:eof_action=pass{bgOut}");
                        if (!string.IsNullOrEmpty(bgOut))
                        {
                            sb.Append(";");
                            currentStream = bgOut;
                        }
                    }
                }
                if (hasTicker && !hasLogo)
                {
                }

                string newGraph = sb.ToString();
                if (newGraph.EndsWith(";")) newGraph = newGraph.Substring(0, newGraph.Length - 1);
                if (newGraph == _lastFilterString) return;
                _lastFilterString = newGraph;
                UpdateFilterAtomically("master_vfx", newGraph);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Filter Error: " + ex.Message);
            }
        }
        private void UpdateFilterAtomically(string label, string newGraphString)
        {
            ApplyToSinglePlayer(_playerA, label, newGraphString);
            ApplyToSinglePlayer(_playerB, label, newGraphString);
        }

        private void ApplyToSinglePlayer(MpvController player, string label, string newGraphString)
        {
            if (player == null || player.Handle == IntPtr.Zero) return;
            if (string.IsNullOrEmpty(newGraphString))
            {
                player.DoCommand("vf", "remove", $"@{label}");
                return;
            }

            try
            {
                string currentVfJson = player.GetPropertyJson("vf");
                var vfNode = JsonNode.Parse(currentVfJson);
                JsonArray vfArray = vfNode as JsonArray ?? new JsonArray();

                bool found = false;
                var newFilterObj = new JsonObject
                {
                    ["name"] = "lavfi",
                    ["label"] = label,
                    ["params"] = new JsonObject { ["graph"] = newGraphString },
                    ["enabled"] = true
                };
                for (int i = 0; i < vfArray.Count; i++)
                {
                    if (vfArray[i]?["label"]?.ToString() == label)
                    {
                        vfArray[i] = newFilterObj;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    vfArray.Add(newFilterObj);
                }
                player.SetPropertyString("vf", vfArray.ToJsonString());
            }
            catch
            {
                player.DoCommand("vf", "remove", $"@{label}");
                player.DoCommand("vf", "add", $"@{label}:lavfi=[{newGraphString}]");
            }
        }
        public async void LoadSponsorBlock(string videoPath)
        {
            _currentSponsors.Clear();
            if (!IsSponsorBlockEnabled) return;
            if (!videoPath.Contains("youtube.com") && !videoPath.Contains("youtu.be")) return;

            string videoId = "";
            try
            {
                var match = System.Text.RegularExpressions.Regex.Match(videoPath, @"(?:v=|\/)([0-9A-Za-z_-]{11})");
                if (match.Success) videoId = match.Groups[1].Value;
            }
            catch { }

            if (string.IsNullOrEmpty(videoId)) return;
            ShowOsdText($"🛡️ SponsorBlock: Đang đồng bộ dữ liệu...", 2000);

            try
            {
                var allSegments = await SponsorBlockClient.GetSegmentsAsync(videoId);
                if (AllowedSponsorCategories != null)
                {
                    _currentSponsors = allSegments
                        .Where(s => AllowedSponsorCategories.Contains(s.Category.ToLower()))
                        .ToList();
                }
                else
                {
                    _currentSponsors = allSegments;
                }
                if (_currentSponsors.Count > 0)
                {
                    ShowOsdText($"✅ SponsorBlock: Đã kích hoạt ({_currentSponsors.Count} phân đoạn).", 3000);
                }
                else
                {
                }
            }
            catch
            {
            }
        }
        public void CheckAndSkipSponsor()
        {
            if (!IsSponsorBlockEnabled || _currentSponsors.Count == 0 || Duration <= 0) return;

            double currentTime = Position;
            foreach (var seg in _currentSponsors)
            {
                if (currentTime >= seg.Segment[0] && currentTime < seg.Segment[1])
                {
                    Seek(seg.Segment[1]);
                    string categoryVn = "Phân đoạn";
                    switch (seg.Category.ToLower())
                    {
                        case "sponsor": categoryVn = "Nội dung tài trợ"; break;
                        case "intro": categoryVn = "Đoạn mở đầu (Intro)"; break;
                        case "outro": categoryVn = "Đoạn kết (Outro)"; break;
                        case "selfpromo": categoryVn = "Tự quảng cáo"; break;
                        case "interaction": categoryVn = "Kêu gọi tương tác"; break;
                        case "preview": categoryVn = "Preview tập sau"; break;
                        case "music_offtopic": categoryVn = "Phần không phải âm nhạc"; break;
                        case "filler": categoryVn = "Nội dung đệm (Filler)"; break;
                        default: categoryVn = seg.Category; break;
                    }
                    ShowOsdText($"⏩ Đã tự động bỏ qua: {categoryVn}");

                    break;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);
        public const int WS_VISIBLE = 0x10000000;



        [StructLayout(LayoutKind.Sequential)]
        public struct WNDCLASS { public uint style; public IntPtr lpfnWndProc; public int cbClsExtra; public int cbWndExtra; public IntPtr hInstance; public IntPtr hIcon; public IntPtr hCursor; public IntPtr hbrBackground; public string lpszMenuName; public string lpszClassName; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int left; public int top; public int right; public int bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DWM_THUMBNAIL_PROPERTIES { public int dwFlags; public RECT rcDestination; public RECT rcSource; public byte opacity; public bool fVisible; public bool fSourceClientAreaOnly; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public int dwFlags; }

        public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT lprcMonitor, IntPtr dwData);
        public const uint WM_CLOSE = 0x0010;

        private IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_CLOSE) return IntPtr.Zero;
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }
        private IntPtr _hostHwnd = IntPtr.Zero;
        private IntPtr _dwmThumb = IntPtr.Zero;
        private IntPtr _mainAppHwnd = IntPtr.Zero;

        private MpvController _playerA;
        private MpvController _playerB;
        private bool _isUsingA = true;
        public bool IsTransitioning { get; private set; } = false;
        private bool _hasContent = false;

        private Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
        private WndProcDelegate _wndProc;

        private string _backgroundImagePath = "";
        public bool IsShowingWallpaper { get; private set; } = false;
        private bool _isLedOn = false;
        private int _currentMonitorIndex = 0;
        private bool _isPlayerMode = false;
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern uint SetThreadExecutionState(uint esFlags);

        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;
        private const uint ES_DISPLAY_REQUIRED = 0x00000002;
        public void PreventSleep(bool enable)
        {
            if (enable)
            {
                SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
            }
            else
            {
                SetThreadExecutionState(ES_CONTINUOUS);
            }
        }
        public struct MediaTechInfo
        {
            public string Filename;
            public string Resolution;
            public string Fps;
            public string VideoCodec;
            public string AudioCodec;
            public string FileSize;
            public string Duration;
        }
        public MediaTechInfo GetTechnicalInfo()
        {
            var info = new MediaTechInfo();

            if (ActivePlayer.Handle == IntPtr.Zero) return info;
            info.Filename = ActivePlayer.GetPropertyJson("media-title").Replace("\"", "");
            if (string.IsNullOrEmpty(info.Filename))
                info.Filename = ActivePlayer.GetPropertyJson("filename").Replace("\"", "");
            long w = ActivePlayer.GetPropertyLong("video-params/w");
            long h = ActivePlayer.GetPropertyLong("video-params/h");
            info.Resolution = (w > 0 && h > 0) ? $"{w} x {h}" : "N/A";
            double fps = ActivePlayer.GetPropertyDouble("estimated-vf-fps");
            if (fps <= 1) fps = ActivePlayer.GetPropertyDouble("container-fps");
            info.Fps = fps > 0 ? $"{fps:F2} fps" : "N/A";
            info.VideoCodec = ActivePlayer.GetPropertyJson("video-codec").Replace("\"", "").ToUpper();
            info.AudioCodec = ActivePlayer.GetPropertyJson("audio-codec").Replace("\"", "").ToUpper();

            if (string.IsNullOrEmpty(info.VideoCodec)) info.VideoCodec = "Không có hình ảnh";
            if (string.IsNullOrEmpty(info.AudioCodec)) info.AudioCodec = "Không có âm thanh";
            long fileSize = ActivePlayer.GetPropertyLong("file-size");
            double videoBitrate = ActivePlayer.GetPropertyDouble("video-bitrate") / 1000.0;
            double audioBitrate = ActivePlayer.GetPropertyDouble("audio-bitrate") / 1000.0;

            if (fileSize > 0)
            {
                double sizeMb = fileSize / (1024.0 * 1024.0);
                info.FileSize = $"{sizeMb:F2} MB";
            }
            else
            {
                info.FileSize = $"V: {videoBitrate:F0} kbps | A: {audioBitrate:F0} kbps";
            }
            double dur = Duration; if (dur > 0)
            {
                TimeSpan t = TimeSpan.FromSeconds(dur);
                info.Duration = (t.TotalHours >= 1) ? t.ToString(@"hh\:mm\:ss") : t.ToString(@"mm\:ss");
            }
            else
            {
                info.Duration = "Live / Unknown";
            }

            return info;
        }
        public const int WS_CLIPCHILDREN = 0x02000000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public void SetPropertyString(string name, string value)
        {
            _playerA?.SetPropertyString(name, value);
            _playerB?.SetPropertyString(name, value);
        }

        public async Task SetBackgroundImage(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                _backgroundImagePath = "";
                if (IsShowingWallpaper) Stop();
                return;
            }

            string finalPath = path;
            await Task.Run(() =>
            {
                finalPath = ImageUtils.GetCachedPath(path);
            });

            _backgroundImagePath = finalPath;

            if (!IsPlaying() && IsShowingWallpaper)
            {
                ShowWallpaper();
            }
        }

        public void ShowWallpaper()
        {
            ActivePlayer.SetPropertyString("background", "#FF000000");
            ActivePlayer.SetPropertyString("background-color", "#000000");

            if (string.IsNullOrEmpty(_backgroundImagePath) || !System.IO.File.Exists(_backgroundImagePath))
            {
                ActivePlayer.DoCommand("stop");
                ActivePlayer.SetOpacity(255);

                IsShowingWallpaper = true;
                _hasContent = false;
                ToggleDwm(true);
                return;
            }

            IsShowingWallpaper = true;
            IsCurrentContentImage = true;

            ActivePlayer.DoCommand("stop");
            ActivePlayer.SetPropertyLong("video-rotate", 0);
            ActivePlayer.SetPropertyDouble("video-zoom", 0);
            ActivePlayer.SetPropertyDouble("video-pan-x", 0);
            ActivePlayer.SetPropertyDouble("video-pan-y", 0);
            ActivePlayer.SetPropertyString("hwdec", "no");
            ActivePlayer.SetPropertyString("vd-lavc-threads", "0");
            ActivePlayer.SetPropertyString("video-unscaled", "no");
            ActivePlayer.SetPropertyString("interpolation", "no");
            ActivePlayer.SetPropertyString("tscale", "nearest");

            ActivePlayer.DoCommand("set", "mute", "yes");
            ActivePlayer.DoCommand("show-text", "", "0");
            ActivePlayer.DoCommand("sub-remove");

            ActivePlayer.DoCommand("loadfile", _backgroundImagePath, "replace");
            ActivePlayer.SetOpacity(255);

            _hasContent = true;
            ToggleDwm(true);
        }
        private string GetMonitorPositionName(RECT mainRect, RECT targetRect)
        {
            int mainCenterX = (mainRect.left + mainRect.right) / 2;
            int mainCenterY = (mainRect.top + mainRect.bottom) / 2;

            int targetCenterX = (targetRect.left + targetRect.right) / 2;
            int targetCenterY = (targetRect.top + targetRect.bottom) / 2;

            if (mainRect.left == targetRect.left && mainRect.top == targetRect.top)
            {
                return " (Trung tâm)";
            }

            int deltaX = targetCenterX - mainCenterX;
            int deltaY = targetCenterY - mainCenterY;

            if (Math.Abs(deltaX) > Math.Abs(deltaY))
            {
                return deltaX < 0 ? " (Trái)" : " (Phải)";
            }
            else
            {
                return deltaY < 0 ? " (Trên)" : " (Dưới)";
            }
        }
        public MediaEngine(IntPtr mainAppHwnd, Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
        {
            _mainAppHwnd = mainAppHwnd;
            _dispatcher = dispatcher;

            InitializeHostWindow();

            _playerA = new MpvController();
            _playerA.Initialize(_hostHwnd);

            _playerB = new MpvController();
            _playerB.Initialize(_hostHwnd);
            void SetupSubStyle(MpvController p)
            {
                p.SetPropertyString("sub-font-size", "45"); p.SetPropertyString("sub-border-size", "2"); p.SetPropertyString("sub-color", "#FFFFFFFF"); p.SetPropertyString("sub-shadow-offset", "1"); p.SetPropertyString("sub-margin-y", "50");
            }

            SetupSubStyle(_playerA);
            SetupSubStyle(_playerB);
            if (_mainAppHwnd != IntPtr.Zero && _hostHwnd != IntPtr.Zero)
            {
                DwmRegisterThumbnail(_mainAppHwnd, _hostHwnd, out _dwmThumb);
            }

            BringToFront(_playerA);
            MoveWindow(_hostHwnd, -20000, -20000, 1280, 720, false);
        }
        public class MonitorInfo
        {
            public int Index { get; set; }
            public string Name { get; set; }
            public string Resolution { get; set; }
            public bool IsPrimary { get; set; }
            public IntPtr HMonitor { get; set; }
            public RECT Rect { get; set; }
            public IntPtr Handle { get; set; }
            public string ResolutionDisplay => $"Độ phân giải: {Resolution}";
        }
        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        private const uint MONITOR_DEFAULTTONEULL = 0;
        private const uint MONITOR_DEFAULTTOPRIMARY = 1;

        public List<MonitorInfo> GetSecondaryMonitors()
        {
            var allMonitors = new List<MonitorInfo>();
            IntPtr appMonitor = MonitorFromWindow(_mainAppHwnd, 1);

            MONITORINFO appMi = new MONITORINFO();
            appMi.cbSize = Marshal.SizeOf(appMi);
            GetMonitorInfo(appMonitor, ref appMi);
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                delegate (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData)
                {
                    MONITORINFO mi = new MONITORINFO();
                    mi.cbSize = Marshal.SizeOf(mi);
                    GetMonitorInfo(hMonitor, ref mi);
                    int width = Math.Abs(mi.rcMonitor.right - mi.rcMonitor.left);
                    int height = Math.Abs(mi.rcMonitor.bottom - mi.rcMonitor.top);
                    string resolution = $"{width}x{height}";
                    string positionName = GetMonitorPositionName(appMi.rcMonitor, mi.rcMonitor);
                    string finalName = $"Màn hình{positionName} - {resolution}";

                    allMonitors.Add(new MonitorInfo
                    {
                        HMonitor = hMonitor,
                        Rect = mi.rcMonitor,
                        Name = finalName,
                        IsPrimary = (hMonitor == appMonitor)
                    });

                    return true;
                },
                IntPtr.Zero);
            if (allMonitors.Count > 1)
            {
                return allMonitors.Where(m => !m.IsPrimary).ToList();
            }
            return allMonitors;
        }
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x00000010;
        private void InitializeHostWindow()
        {
            string className = "MediaHostStage_" + Guid.NewGuid();
            _wndProc = new WndProcDelegate(CustomWndProc);

            IntPtr hCustomIcon = IntPtr.Zero;
            try
            {
                string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");

                if (System.IO.File.Exists(iconPath))
                {
                    hCustomIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Lỗi load icon: " + ex.Message);
            }
            WNDCLASS wc = new WNDCLASS
            {
                lpszClassName = className,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hbrBackground = CreateSolidBrush(0x000000),
                hIcon = hCustomIcon
            };

            RegisterClass(ref wc);
            _hostHwnd = CreateWindowEx(
                WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                className,
                "MediaLed Output Screen",
                WS_POPUP | WS_CLIPCHILDREN,
                -20000, -20000, 1280, 720,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }
        public void SetOpacity(byte alpha)
        {
            if (ActivePlayer != null)
            {
                ActivePlayer.SetOpacity(alpha);
            }
        }
        public void TogglePreview(bool enable)
        {
            ToggleDwm(enable);
        }

        private void ToggleDwm(bool visible)
        {
            try
            {
                if (_dwmThumb == IntPtr.Zero) return;
                if (!IsWindow(_hostHwnd)) return;

                DWM_THUMBNAIL_PROPERTIES props = new DWM_THUMBNAIL_PROPERTIES();
                props.dwFlags = 0x8; props.fVisible = visible;
                DwmUpdateThumbnailProperties(_dwmThumb, ref props);
            }
            catch
            {
            }
        }
        public async Task<string> EnterEditMode()
        {
            Pause();
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mpv_snapshot.jpg");
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            ActivePlayer.Screenshot(path);
            int retries = 0;
            while (!System.IO.File.Exists(path) && retries < 20)
            {
                await Task.Delay(50); retries++;
            }
            ToggleDwm(false);
            if (_isPlayerMode)
            {
                ToggleVideoVisibility(false);
            }

            return path;
        }
        public void ExitEditMode()
        {
            ToggleDwm(true);

            if (_isPlayerMode)
            {
                ToggleVideoVisibility(true);
            }
            Resume();
        }
        public List<string> CachedFormats { get; private set; } = new List<string>();
        private string _currentScanningUrl = "";


        public async void PlayTransition(string filePath)
        {
            IsTransitioning = true;
            _cachedUrl = "";
            lock (_cachedFormats) { _cachedFormats.Clear(); }
            _currentSponsors.Clear();
            IsShowingWallpaper = false;
            if (_playerA != null) _playerA.DoCommand("set", "mute", "no");
            if (_playerB != null) _playerB.DoCommand("set", "mute", "no");
            MpvController activePlayer = _isUsingA ? _playerA : _playerB;
            MpvController nextPlayer = _isUsingA ? _playerB : _playerA;

            if (_isPlayerMode)
            {
                nextPlayer = _playerA;
                activePlayer = _playerB;
            }
            else
            {
                _isUsingA = !_isUsingA;
            }
            nextPlayer.ClearState();
            nextPlayer.RemoveVideoFilter("bg_video");
            nextPlayer.RemoveVideoFilter("viz");
            nextPlayer.RemoveVideoFilter("led_logo");
            nextPlayer.SetPropertyString("audio-files", "");
            nextPlayer.SetPropertyString("sub-files", "");
            nextPlayer.SetPropertyString("sid", "no");
            nextPlayer.SetPropertyString("speed", "1.0");
            nextPlayer.SetPropertyString("pause", "no");
            nextPlayer.SetPropertyString("start", "0");
            nextPlayer.SetPropertyString("hwdec", "auto-copy");
            nextPlayer.SetPropertyString("video-unscaled", "no");
            nextPlayer.SetPropertyString("vd-lavc-threads", "0");
            string ext = System.IO.Path.GetExtension(filePath).ToLower();
            bool isImage = ext == ".jpg" || ext == ".jpeg" || ext == ".png" ||
                           ext == ".bmp" || ext == ".tif" || ext == ".tiff" || ext == ".webp";
            IsCurrentContentImage = isImage;
            if (isImage)
            {
                _ = HideTicker();
            }
            string commonUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
            nextPlayer.SetPropertyString("user-agent", commonUA);
            string finalPathToPlay = filePath;

            if (isImage)
            {
                nextPlayer.SetPropertyString("interpolation", "no");
                nextPlayer.SetPropertyString("tscale", "nearest");
                await Task.Run(() => { finalPathToPlay = ImageUtils.GetCachedPath(filePath); });
                nextPlayer.Play(finalPathToPlay);
                nextPlayer.SetPropertyLong("video-rotate", 0);
            }
            else
            {
                nextPlayer.SetPropertyString("interpolation", "no");
                string format = "";
                string ytdlOptions = "sub-langs=all,write-sub=,write-auto-sub=";

                if (filePath.Contains("youtube.com") || filePath.Contains("youtu.be"))
                {
                    format = GetQualityFormat(TargetResolution);
                }
                else if (filePath.Contains("tiktok.com"))
                {
                    nextPlayer.SetPropertyString("referrer", "https://www.tiktok.com/");
                    nextPlayer.SetPropertyString("http-header-fields", $"Referer: https://www.tiktok.com/,User-Agent: {commonUA}");
                    format = "bestvideo[vcodec^=h264]+bestaudio/best";
                }
                else if (filePath.Contains("facebook.com") || filePath.Contains("fb.watch"))
                {
                    nextPlayer.SetPropertyString("referrer", "https://www.facebook.com/"); format = "";
                }

                nextPlayer.SetPropertyString("ytdl-format", format);
                nextPlayer.SetPropertyString("ytdl-raw-options", ytdlOptions);
                nextPlayer.Play(filePath);
            }

            if (_isPlayerMode)
            {
                _playerA.SetOpacity(255);
                BringToFront(_playerA);
            }
            else
            {
                nextPlayer.SetOpacity(0);
                int delayMs = isImage ? 200 : (filePath.Contains("tiktok") ? 1200 : 600);
                await Task.Delay(delayMs);
                BringToFront(nextPlayer);
                for (int i = 0; i <= 255; i += 15)
                {
                    nextPlayer.SetOpacity((byte)i);
                    await Task.Delay(20);
                }
                nextPlayer.SetOpacity(255);
                await Task.Delay(50);
                activePlayer.SetOpacity(0);
                activePlayer.Stop();
                ToggleDwm(true);
            }
            IsTransitioning = false;
            if (filePath.Contains("youtube.com"))
            {
                await Task.Delay(2000);
                LoadSponsorBlock(filePath);
            }
            if (filePath.StartsWith("http") && !isImage && !filePath.Contains("tiktok") && !filePath.Contains("facebook"))
            {
                Task.Run(async () => { await GetRealOnlineFormats(filePath); });
            }
        }
        public void DoCommand(params string[] args)
        {
            if (ActivePlayer != null)
            {
                ActivePlayer.DoCommand(args);
            }
            if (args.Length > 0 && args[0] == "stop")
            {
                _playerA?.DoCommand("stop");
                _playerB?.DoCommand("stop");
            }
        }

        public MonitorInfo GetCurrentAppMonitor()
        {
            IntPtr hMonitor = MonitorFromWindow(_mainAppHwnd, 2);
            MONITORINFOEX mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));

            if (GetMonitorInfo(hMonitor, ref mi))
            {
                string friendlyName = "Màn hình chính (App đang chạy)";
                DISPLAY_DEVICE device = new DISPLAY_DEVICE();
                device.cb = Marshal.SizeOf(device);
                if (EnumDisplayDevices(mi.szDevice, 0, ref device, 0))
                {
                    friendlyName = device.DeviceString;
                }

                int width = mi.rcMonitor.right - mi.rcMonitor.left;
                int height = mi.rcMonitor.bottom - mi.rcMonitor.top;

                return new MonitorInfo
                {
                    Index = -1,
                    Name = friendlyName,
                    Resolution = $"{width}x{height}",
                    Handle = hMonitor,
                    IsPrimary = true
                };
            }

            return new MonitorInfo { Name = "Unknown", Resolution = "N/A" };
        }
        public List<OnlineVideoFormat> GetCurrentCachedFormats()
        {
            if (_cachedFormats == null) return new List<OnlineVideoFormat>();
            lock (_cachedFormats)
            {
                return new List<OnlineVideoFormat>(_cachedFormats);
            }
        }
        public bool IsChangingQuality { get; private set; } = false;

        public async void SwitchVariant(StreamVariant variant)
        {
            if (ActivePlayer.Handle == IntPtr.Zero) return;
            IsChangingQuality = true;

            try
            {
                double currentPos = Position;
                string currentUrl = ActivePlayer.GetPropertyJson("path").Replace("\"", "");

                if (variant.Id == -1)
                {
                    ActivePlayer.SetPropertyString("vid", "auto");
                    ShowOsdText("Chế độ: Auto Bitrate");
                }
                else
                {
                    ActivePlayer.SetPropertyLong("vid", variant.Id);
                    ShowOsdText($"Đang chuyển sang: {variant.Name}...");
                }
                if (!string.IsNullOrEmpty(currentUrl))
                {
                    ActivePlayer.DoCommand("loadfile", currentUrl, "replace");
                }
                if (Duration > 0 && currentPos > 0)
                {
                    await Task.Delay(500); Seek(currentPos);
                }
                else
                {
                    await Task.Delay(1000);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Switch Error: " + ex.Message);
            }
            finally
            {
                IsChangingQuality = false;
            }
        }
        public bool IsLiveStream()
        {
            return Duration <= 0;
        }
        private string GetSafePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string safe = path.Replace("\\", "/");
            safe = safe.Replace(":", "\\:");
            safe = safe.Replace("'", "'\\''");
            return safe;
        }

        public void SetSpeed(double speed)
        {
            string speedStr = speed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ActivePlayer.SetPropertyString("speed", speedStr);

        }
        public string ScanStatus { get; private set; } = "Chưa quét";
        private async void ScanFormatsInBackground(string url)
        {
            if (url == _currentScanningUrl && CachedFormats.Count > 0) return;

            _currentScanningUrl = url;
            string baseDir = AppContext.BaseDirectory;
            string exePath = System.IO.Path.Combine(baseDir, "yt-dlp.exe");

            if (!System.IO.File.Exists(exePath))
            {
                string debugPath = System.IO.Path.Combine(baseDir, "..\\..\\..\\yt-dlp.exe");
                if (System.IO.File.Exists(debugPath)) exePath = debugPath;
            }

            ScanStatus = "Đang tìm yt-dlp...";
            lock (CachedFormats) { CachedFormats.Clear(); }

            await Task.Run(async () =>
            {
                try
                {
                    if (!System.IO.File.Exists(exePath))
                    {
                        ScanStatus = $"LỖI: Không thấy yt-dlp.exe";
                        return;
                    }

                    ScanStatus = "Đang quét dữ liệu...";

                    var psi = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = $"--dump-json --no-playlist \"{url}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = System.Text.Encoding.UTF8
                    };

                    using (var process = Process.Start(psi))
                    {
                        if (process == null) return;
                        string jsonOutput = await process.StandardOutput.ReadToEndAsync();
                        process.WaitForExit();

                        if (!string.IsNullOrEmpty(jsonOutput))
                        {
                            var root = System.Text.Json.Nodes.JsonNode.Parse(jsonOutput);
                            var formats = root?["formats"]?.AsArray();

                            if (formats != null)
                            {
                                HashSet<int> heights = new HashSet<int>();
                                foreach (var f in formats)
                                {
                                    int h = f?["height"]?.GetValue<int>() ?? 0;
                                    if (h >= 144) heights.Add(h);
                                }

                                lock (CachedFormats)
                                {
                                    CachedFormats = heights.OrderByDescending(x => x).Select(x => x.ToString()).ToList();
                                }
                                ScanStatus = $"Xong: {CachedFormats.Count} định dạng.";
                            }
                        }
                    }
                }
                catch (Exception ex) { ScanStatus = "Lỗi: " + ex.Message; }
            });
        }

        public List<string> GetCachedFormatsSafe()
        {
            lock (CachedFormats)
            {
                return new List<string>(CachedFormats);
            }
        }

        private void BringToFront(MpvController player)
        {
            RECT r;
            GetWindowRect(_hostHwnd, out r);
            int w = r.right - r.left;
            int h = r.bottom - r.top;

            player.Resize(w, h);
            SetWindowPos(player.Handle, IntPtr.Zero, 0, 0, 0, 0,
                0x0001 | 0x0002 | 0x0040 | 0x0020);
        }
        public void SetLedScreen(bool on, RECT targetRect)
        {
            if (_isPlayerMode) return;

            _isLedOn = on;
            MpvController activePlayer = _isUsingA ? _playerA : _playerB;

            if (on)
            {
                int width = targetRect.right - targetRect.left;
                int height = targetRect.bottom - targetRect.top;
                MoveWindow(_hostHwnd, targetRect.left, targetRect.top, width, height, true);

                activePlayer.Resize(width, height);
                ShowWindow(_hostHwnd, 5);
                SetWindowPos(_hostHwnd, new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002);
            }
            else
            {
                int standardW = 1920;
                int standardH = 1080;
                MoveWindow(_hostHwnd, -20000, -20000, standardW, standardH, false);
                activePlayer.Resize(standardW, standardH);
                ShowWindow(_hostHwnd, 5);
                SetWindowPos(_hostHwnd, new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002);
            }
        }

        private RECT GetMonitorRect(int targetIndex)
        {
            var monitors = new List<RECT>();
            MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data) =>
            {
                MONITORINFO mi = new MONITORINFO();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (GetMonitorInfo(hMonitor, ref mi)) monitors.Add(mi.rcMonitor);
                return true;
            };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            if (targetIndex >= 0 && targetIndex < monitors.Count) return monitors[targetIndex];
            return monitors.Count > 0 ? monitors[0] : new RECT { left = 0, top = 0, right = 1920, bottom = 1080 };
        }
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        public async Task SetMode(bool isPlayerMode)
        {
            if (_isPlayerMode == isPlayerMode) return;
            DestroyThumbnail();
            if (ActivePlayer != null) ActivePlayer.SetOpacity(0);
            await Task.Delay(50);
            _isPlayerMode = isPlayerMode;

            if (_isPlayerMode)
            {
                _isLedOn = false;
                SetParent(_hostHwnd, _mainAppHwnd);
                long style = (long)GetWindowLongPtr(_hostHwnd, GWL_STYLE);
                style &= ~WS_POPUP;
                style |= WS_CHILD;
                SetWindowLongPtr(_hostHwnd, GWL_STYLE, (IntPtr)style);
                _isUsingA = true;
                _playerA.SetOpacity(255);
                _playerB.SetOpacity(0);
                BringToFront(_playerA);
                ShowWindow(_hostHwnd, 5);
            }
            else
            {
                SetParent(_hostHwnd, IntPtr.Zero);
                long style = (long)GetWindowLongPtr(_hostHwnd, GWL_STYLE);
                style &= ~WS_CHILD;
                style |= WS_POPUP;
                SetWindowLongPtr(_hostHwnd, GWL_STYLE, (IntPtr)style);
                MoveWindow(_hostHwnd, -20000, -20000, 1920, 1080, false);
                ShowWindow(_hostHwnd, 5);
                _isLedOn = false;
                _isUsingA = true;
                _playerA.SetOpacity(255);
                _playerB.SetOpacity(0);
                _playerA.Resize(1920, 1080);
                _playerB.Resize(1920, 1080);
            }
            if (!string.IsNullOrEmpty(_backgroundImagePath))
            {
                ShowWallpaper();
            }
        }
        public void UpdateLayout(int x, int y, int w, int h, bool isVisible = true)
        {
            if (w <= 0 || h <= 0) return;
            if (_hostHwnd == IntPtr.Zero || !IsWindow(_hostHwnd)) return;

            try
            {
                if (_isPlayerMode)
                {
                    uint flags = 0x0010 | 0x0004; if (isVisible) flags |= 0x0040; else flags |= 0x0080;
                    SetWindowPos(_hostHwnd, IntPtr.Zero, x, y, w, h, flags);

                    MpvController activePlayer = _isUsingA ? _playerA : _playerB;
                    if (activePlayer != null) activePlayer.Resize(w, h);
                }
                else
                {

                    if (_dwmThumb == IntPtr.Zero)
                    {
                        int hr = DwmRegisterThumbnail(_mainAppHwnd, _hostHwnd, out _dwmThumb);
                        if (hr != 0) return;
                    }
                    double sourceW = 1920.0;
                    double sourceH = 1080.0;
                    double sourceRatio = sourceW / sourceH;
                    double targetRatio = (double)w / h;

                    int finalW, finalH, finalX, finalY;

                    if (targetRatio > sourceRatio)
                    {
                        finalH = h;
                        finalW = (int)(h * sourceRatio);
                        finalY = y;
                        finalX = x + (w - finalW) / 2;
                    }
                    else
                    {
                        finalW = w;
                        finalH = (int)(w / sourceRatio);
                        finalX = x;
                        finalY = y + (h - finalH) / 2;
                    }
                    DWM_THUMBNAIL_PROPERTIES props = new DWM_THUMBNAIL_PROPERTIES();
                    props.fVisible = isVisible;

                    props.dwFlags = 0x1 | 0x2 | 0x8 | 0x10 | 0x4; props.opacity = 255;
                    props.fSourceClientAreaOnly = true;

                    props.rcSource = new RECT { left = 0, top = 0, right = 1920, bottom = 1080 };
                    props.rcDestination = new RECT
                    {
                        left = finalX,
                        top = finalY,
                        right = finalX + finalW,
                        bottom = finalY + finalH
                    };

                    DwmUpdateThumbnailProperties(_dwmThumb, ref props);
                }
            }
            catch { }
        }

        public void DestroyThumbnail()
        {
            if (_dwmThumb != IntPtr.Zero)
            {
                DwmUnregisterThumbnail(_dwmThumb);
                _dwmThumb = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            if (_dwmThumb != IntPtr.Zero) DwmUnregisterThumbnail(_dwmThumb);
            _playerA?.Dispose();
            _playerB?.Dispose();
        }
        private MpvController ActivePlayer => _isUsingA ? _playerA : _playerB;

        public double Duration => ActivePlayer.GetPropertyDouble("duration");
        public double Position => ActivePlayer.GetPropertyDouble("time-pos");
        public void Seek(double targetSeconds, bool allowFast = false)
        {
            if (allowFast)
            {
                ActivePlayer.DoCommand("seek", targetSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), "absolute+keyframes");
            }
            else
            {
                ActivePlayer.DoCommand("seek", targetSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), "absolute");
            }
        }

        public void SetVolume(double volume)
        {
            if (_playerA != null) _playerA.SetVolume(volume);
            if (_playerB != null) _playerB.SetVolume(volume);
        }
        public bool IsPaused()
        {
            return ActivePlayer.GetPropertyDouble("pause") == 1.0;
        }

        public bool IsPlaying()
        {
            return Duration > 0;
        }
        public void Stop()
        {
            ShowWallpaper();
        }

        public async Task<List<StreamVariant>> GetAvailableVariants()
        {
            await Task.Yield();
            if (ActivePlayer.Handle == IntPtr.Zero) return new List<StreamVariant>();

            string trackListJson = ActivePlayer.GetPropertyJson("track-list");
            List<StreamVariant> variants = new List<StreamVariant>();
            long currentVideoId = ActivePlayer.GetPropertyLong("vid");

            try
            {
                using (JsonDocument document = JsonDocument.Parse(trackListJson))
                {
                    if (document.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var track in document.RootElement.EnumerateArray())
                        {
                            string type = track.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : "";
                            if (type != "video") continue;
                            if (track.TryGetProperty("albumart", out var art) && art.GetBoolean()) continue;
                            long id = -1;
                            if (track.TryGetProperty("id", out var idProp)) id = idProp.GetInt64();
                            int w = 0, h = 0;
                            if (track.TryGetProperty("demux-w", out var dw)) w = dw.GetInt32();
                            if (w == 0 && track.TryGetProperty("w", out var rw)) w = rw.GetInt32();

                            if (track.TryGetProperty("demux-h", out var dh)) h = dh.GetInt32();
                            if (h == 0 && track.TryGetProperty("h", out var rh)) h = rh.GetInt32();
                            string codec = track.TryGetProperty("codec", out var c) ? c.GetString()?.ToUpper() : "UNKNOWN";
                            double fps = track.TryGetProperty("demux-fps", out var f) ? f.GetDouble() : 0;
                            if (fps > 59) fps = 60; else if (fps > 29) fps = 30;
                            string qualityLabel = "SD";
                            if (h >= 2160) qualityLabel = "4K UHD";
                            else if (h >= 1440) qualityLabel = "2K QHD";
                            else if (h >= 1080) qualityLabel = "FHD";
                            else if (h >= 720) qualityLabel = "HD";

                            string name = "";
                            if (w > 0 && h > 0)
                            {
                                name = $"{qualityLabel} {h}p {w}x{h} ({codec})";
                            }
                            else
                            {
                                name = $"Stream #{id} ({codec})";
                            }

                            if (fps > 0) name += $" {fps:F0}fps";
                            if (id == currentVideoId) name += " (Đang phát)";

                            variants.Add(new StreamVariant
                            {
                                Name = name,
                                Id = id,
                                IsCurrent = (id == currentVideoId)
                            });
                        }
                    }
                }
            }
            catch { }
            variants.Insert(0, new StreamVariant
            {
                Name = "Auto (Tự động chọn tốt nhất)",
                Id = -1,
                IsCurrent = false
            });

            return variants;
        }
        public string GetCurrentVideoInfo()
        {
            if (ActivePlayer.Handle == IntPtr.Zero) return "Không có thông tin";
            long w = ActivePlayer.GetPropertyLong("video-params/w");
            long h = ActivePlayer.GetPropertyLong("video-params/h");
            string codec = ActivePlayer.GetPropertyJson("video-format").Replace("\"", "").ToUpper();
            if (w <= 0 || h <= 0) return "Đang tải hoặc Audio only...";

            string qualityLabel = "SD";
            if (h >= 2160) qualityLabel = "4K UHD";
            else if (h >= 1440) qualityLabel = "2K QHD";
            else if (h >= 1080) qualityLabel = "FHD";
            else if (h >= 720) qualityLabel = "HD";

            return $"{qualityLabel} {h}p {w}x{h} ({codec})";
        }
        public string CurrentYoutubeQuality { get; private set; } = "Auto";
        private string _cachedUrl = "";
        private List<OnlineVideoFormat> _cachedFormats = new List<OnlineVideoFormat>();

        private string GetQualityFormat(string targetRes)
        {
            if (targetRes == "audio_only") return "bestaudio/best";
            if (string.IsNullOrEmpty(targetRes) || targetRes == "Auto" || targetRes == "MAX")
            {
                return "bestvideo[vcodec^=avc1]+bestaudio/bestvideo[vcodec^=vp9]+bestaudio/bestvideo+bestaudio/best";
            }

            int h = 0;
            int.TryParse(targetRes, out h);
            if (h <= 0) return "bestvideo+bestaudio/best";
            if (h >= 1440)
            {
                return
                    $"bestvideo[height={h}][vcodec^=vp9]+bestaudio/" +
                    $"bestvideo[height={h}]+bestaudio/" +
                    $"bestvideo[height<={h}][vcodec^=avc1]+bestaudio/" +
                    "best";
            }
            else
            {
                return
                    $"bestvideo[height={h}][vcodec^=avc1]+bestaudio/" +
                    $"bestvideo[height={h}][vcodec^=vp9]+bestaudio/" +
                    $"bestvideo[height={h}]+bestaudio/" +
                    $"bestvideo[height<={h}][vcodec^=avc1]+bestaudio/" +
                    "best";
            }
        }

        public async void ChangeOnlineResolution(string formatId, string displayName)
        {
            if (ActivePlayer.Handle == IntPtr.Zero) return;
            IsChangingQuality = true;

            try
            {
                // 1. Lưu lại vị trí hiện tại và URL
                double currentPos = Position;
                string currentUrl = ActivePlayer.GetPropertyJson("path").Replace("\"", "");

                if (string.IsNullOrEmpty(currentUrl))
                {
                    IsChangingQuality = false;
                    return;
                }

                // 2. QUAN TRỌNG: Dọn dẹp cache cũ để tránh xung đột
                // Xóa URL cache để buộc các hàm scan định dạng phải chạy lại nếu cần
                _cachedUrl = "";
                lock (_cachedFormats)
                {
                    _cachedFormats.Clear();
                }

                ShowOsdText($"⚙️ Đang chuyển sang: {displayName}...");

                // 3. Xử lý logic định dạng (giữ nguyên logic của bạn)
                string finalFormat = "";
                bool isDailymotion = currentUrl.Contains("dailymotion.com") || currentUrl.Contains("dai.ly");

                if (formatId == "Auto")
                {
                    finalFormat = "bestvideo+bestaudio/best";
                }
                else
                {
                    finalFormat = isDailymotion ? $"{formatId}/best" : $"{formatId}+bestaudio/best";
                }

                // 4. Thiết lập lại thuộc tính cho MPV
                ActivePlayer.SetPropertyString("vid", "auto");
                ActivePlayer.SetPropertyString("aid", "auto");
                ActivePlayer.SetPropertyString("ytdl-format", finalFormat);

                // 5. Tải lại file để áp dụng format mới
                ActivePlayer.DoCommand("loadfile", currentUrl, "replace");

                // 6. Khôi phục lại thời gian đang xem
                if (currentPos > 0)
                {
                    // Đợi 1 chút để stream mới kịp kết nối trước khi Seek
                    await Task.Delay(1500);
                    Seek(currentPos);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ChangeRes Error: " + ex.Message);
            }
            finally
            {
                IsChangingQuality = false;
            }
        }

        public async Task<List<OnlineVideoFormat>> GetRealOnlineFormats(string url)
        {
            if (url == _cachedUrl && _cachedFormats != null && _cachedFormats.Count > 0)
            {
                return new List<OnlineVideoFormat>(_cachedFormats);
            }

            var results = new List<OnlineVideoFormat>();

            await Task.Run(() =>
            {
                try
                {
                    string exePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yt-dlp.exe");
                    if (!System.IO.File.Exists(exePath))
                    {
                        string debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..\\..\\..\\yt-dlp.exe");
                        if (System.IO.File.Exists(debugPath)) exePath = debugPath;
                        else return;
                    }
                    var psi = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = $"--dump-json --no-playlist \"{url}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = System.Text.Encoding.UTF8
                    };

                    using (var process = Process.Start(psi))
                    {
                        if (process == null) return;
                        string jsonOutput = process.StandardOutput.ReadToEnd();
                        process.WaitForExit();

                        if (string.IsNullOrEmpty(jsonOutput)) return;

                        var root = System.Text.Json.Nodes.JsonNode.Parse(jsonOutput);
                        var formats = root?["formats"]?.AsArray();

                        if (formats != null)
                        {
                            foreach (var f in formats)
                            {
                                string id = f?["format_id"]?.ToString() ?? "";
                                int h = f?["height"]?.GetValue<int>() ?? 0;
                                string vcodec = f?["vcodec"]?.ToString() ?? "none";
                                double tbr = f?["tbr"]?.GetValue<double>() ?? 0;

                                if (h >= 144 && vcodec != "none")
                                {
                                    string codecDisplay = "Unknown";
                                    if (vcodec.Contains("avc1")) codecDisplay = "H.264";
                                    else if (vcodec.Contains("vp9")) codecDisplay = "VP9";
                                    else if (vcodec.Contains("av01")) codecDisplay = "AV1";
                                    else codecDisplay = vcodec;

                                    results.Add(new OnlineVideoFormat
                                    {
                                        Id = id,
                                        Height = h,
                                        Codec = codecDisplay,
                                        Label = $"{h}p {codecDisplay}"
                                    });
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("YTDL Scan Error: " + ex.Message);
                }
            });

            var cleanList = results
                .GroupBy(x => x.Label).Select(g => g.First()).OrderByDescending(x => x.Height).ThenBy(x => x.Codec == "H.264" ? 0 : x.Codec == "VP9" ? 1 : 2).ToList();
            if (cleanList.Count > 0)
            {
                _cachedUrl = url;
                _cachedFormats = cleanList;
            }

            return cleanList;
        }

        public string GetPropertyJson(string name)
        {
            if (ActivePlayer != null)
            {
                return ActivePlayer.GetPropertyJson(name);
            }
            return "";
        }
        public void ApplyYoutubeQuality(string heightLabel)
        {
            if (ActivePlayer.Handle == IntPtr.Zero) return;
            CurrentYoutubeQuality = heightLabel;
            string formatString = "bestvideo+bestaudio/best";

            if (heightLabel != "Auto")
            {
                formatString = $"bestvideo[height<={heightLabel}]+bestaudio/best[height<={heightLabel}]/best";
            }
            ActivePlayer.SetPropertyString("ytdl-format", formatString);
            double currentPos = Position;
            string currentPath = ActivePlayer.GetPropertyJson("path").Replace("\"", "");

            if (!string.IsNullOrEmpty(currentPath) && (currentPath.StartsWith("http") || currentPath.StartsWith("ytdl")))
            {
                ShowOsdText($"Đang chuyển sang {heightLabel}p...");

                ActivePlayer.Play(currentPath);
                Task.Delay(1000).ContinueWith(_ =>
                {
                    this.Seek(currentPos);
                });
            }
        }
        public string TargetResolution { get; set; } = "720";
        public void ShowVideoInfoOSD()
        {
            string info = GetCurrentVideoInfo(); ActivePlayer.ShowOsdText(info, 4000);
        }

        public void ShowOsdText(string text, int durationMs = 3000)
        {
            if (ActivePlayer != null)
            {
                ActivePlayer.ShowOsdText(text, durationMs);
            }
        }
        public bool IsOnlineMedia()
        {
            string path = ActivePlayer.GetPropertyJson("path").Replace("\"", "");
            return path.StartsWith("http") || path.StartsWith("rtmp") || path.StartsWith("rtsp");
        }
        public void ToggleVideoVisibility(bool isVisible)
        {
            if (_hostHwnd != IntPtr.Zero)
            {
                ShowWindow(_hostHwnd, isVisible ? 5 : 0);
            }
        }

        public void Pause() => ActivePlayer.Pause();
        public void Resume() => ActivePlayer.Resume();

        private System.Threading.SemaphoreSlim _logoLock = new System.Threading.SemaphoreSlim(1, 1);
        private System.Threading.SemaphoreSlim _tickerLock = new System.Threading.SemaphoreSlim(1, 1);

        private List<LogoLayer> _cachedLogos = new List<LogoLayer>();
        private List<string> _lastLogoPaths = new List<string>();
        public async Task UpdateLogoLayers(List<LogoLayer> logos)
        {
            _cachedLogos = logos ?? new List<LogoLayer>();
            await Task.Run(() => RebuildMasterFilter());
        }

        public async Task HideLogo()
        {
            await _logoLock.WaitAsync();
            try
            {
                _playerA.DoCommand("vf", "remove", "@logo_layer");
                _playerB.DoCommand("vf", "remove", "@logo_layer");
            }
            finally
            {
                _logoLock.Release();
            }
        }


        private string FormatAssTime(double seconds)
        {
            TimeSpan t = TimeSpan.FromSeconds(seconds);
            return string.Format("{0}:{1:00}:{2:00}.{3:00}", t.Hours, t.Minutes, t.Seconds, t.Milliseconds / 10);
        }
        private string ConvertAssToFfmpegColor(string assColor)
        {
            try
            {
                if (assColor.StartsWith("&H"))
                {
                    string clean = assColor.Substring(2); if (clean.Length >= 8) clean = clean.Substring(2); string b = clean.Substring(0, 2);
                    string g = clean.Substring(2, 2);
                    string r = clean.Substring(4, 2);

                    return $"0x{r}{g}{b}@{0.8}";
                }
            }
            catch { }
            return "black@0.5";
        }

        private string EscapeFfmpegPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string safe = path.Replace("\\", "/");
            safe = safe.Replace(":", "\\:");
            safe = safe.Replace("'", "'\\''");

            return safe;
        }


        public async Task ShowTicker(string content, double speed, string textColorHex, int yPos, double startTime, bool useBg, string bgColorHex, int bgHeight, string fontName, bool isFullWidth, int loopCount, bool isBold, bool isItalic)
        {
            if (IsCurrentContentImage || IsShowingWallpaper) return;

            if (!_isTickerActive)
            {
                _isTickerActive = true;
                await Task.Run(() => RebuildMasterFilter());
            }

            await _tickerLock.WaitAsync();
            try
            {
                double power = (speed - 1) / 99.0;
                double factor = Math.Pow(0.01, power);
                int delay = (int)(100 * factor);
                if (delay < 1) delay = 1;
                int fontSize = 50;
                double textWidth = 0;

                using (Bitmap bmp = new Bitmap(1, 1))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    FontStyle style = FontStyle.Regular;
                    if (isBold) style |= FontStyle.Bold;
                    if (isItalic) style |= FontStyle.Italic;

                    Font f = null;
                    try { f = new Font(fontName, fontSize, style); }
                    catch { f = new Font("Arial", fontSize, style); }

                    SizeF size = g.MeasureString(content, f, new PointF(0, 0), StringFormat.GenericTypographic);
                    textWidth = size.Width * 0.75; f.Dispose();
                }
                double totalDistance = 1920 + textWidth;
                double durationPerLoop = (totalDistance * delay) / 1000.0;
                int actualLoops = loopCount;
                if (loopCount <= 0) actualLoops = 500;
                string assBgColor = bgColorHex;
                string assTextColor = textColorHex;

                int assBold = isBold ? -1 : 0;
                int assItalic = isItalic ? -1 : 0;

                int padding = bgHeight; int totalBoxHeight = 50 + (padding * 2);
                int yTop = yPos;
                int yCenter = yTop + (totalBoxHeight / 2) - 5; string assHeader = "[Script Info]\nScriptType: v4.00+\nPlayResX: 1920\nPlayResY: 1080\n\n[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n" +
                                   $"Style: TickerText,{fontName},{fontSize},{assTextColor},&H00000000,&H00000000,&H00000000,{assBold},{assItalic},0,0,100,100,0,0,1,0,0,5,20,20,0,1\n" +
                                   $"Style: TickerBgBox,{fontName},{fontSize},{assTextColor},&H00000000,&H00000000,{assBgColor},{assBold},{assItalic},0,0,100,100,0,0,3,0,0,5,20,20,0,1\n\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n";

                System.Text.StringBuilder sbEvents = new System.Text.StringBuilder();
                double currentStart = startTime;

                for (int i = 0; i < actualLoops; i++)
                {
                    string sStart = FormatAssTime(currentStart);
                    string sEnd = FormatAssTime(currentStart + durationPerLoop);

                    if (useBg)
                    {
                        if (isFullWidth)
                        {
                            string drawCode = $"m 0 0 l 1920 0 l 1920 {totalBoxHeight} l 0 {totalBoxHeight}";
                            sbEvents.Append($"Dialogue: 0,{sStart},{sEnd},TickerText,,0,0,0,,{{\\an7\\pos(0,{yTop})\\1c{assBgColor}\\bord0\\shad0\\p1}}{drawCode}{{\\p0}}\n");
                        }
                        else
                        {
                            sbEvents.Append($"Dialogue: 0,{sStart},{sEnd},TickerBgBox,,0,0,0,Banner;{delay};0;50,{{\\an5\\pos(960,{yCenter})\\3c{assBgColor}\\bord{padding}\\shad0\\1c&HFF000000}}{content}\n");
                        }
                    }
                    sbEvents.Append($"Dialogue: 1,{sStart},{sEnd},TickerText,,0,0,0,Banner;{delay};0;50,{{\\an5\\pos(960,{yCenter})}}{content}\n");

                    currentStart += durationPerLoop;
                }

                ActivePlayer.DoCommand("sub-remove");
                ActivePlayer.AddSubtitlesFromString(assHeader + sbEvents.ToString());

                if (startTime > Position + 1) ShowOsdText($"Ticker ({fontName}) hẹn giờ chạy {actualLoops} lần.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Ticker Error: " + ex.Message);
            }
            finally
            {
                _tickerLock.Release();
            }
        }
        public async Task HideTicker()
        {
            _cachedTicker = null;
            if (_isTickerActive)
            {
                _isTickerActive = false;
                await Task.Run(() => RebuildMasterFilter());
            }

            await _tickerLock.WaitAsync();
            try
            {
                ActivePlayer.DoCommand("sub-remove");
            }
            finally { _tickerLock.Release(); }
        }

        public void CancelDownload()
        {
            var proc = _activeProcess;

            if (proc != null)
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        proc.Kill(true);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Kill Error: " + ex.Message);
                }
                finally
                {
                    _activeProcess = null;
                }
            }
        }

        public async Task DownloadMediaAsync(string url, string formatId, string outputPath, Action<string> onProgress)
        {
            await Task.Run(() =>
            {
                try
                {
                    string exePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yt-dlp.exe");
                    if (!System.IO.File.Exists(exePath))
                    {
                        exePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..\\..\\..\\yt-dlp.exe");
                        if (!System.IO.File.Exists(exePath)) { onProgress?.Invoke("❌ Lỗi: Thiếu yt-dlp.exe"); return; }
                    }
                    string args = "";
                    if (formatId == "audio_only")
                    {
                        args = $"-x --audio-format mp3 -o \"{outputPath}\" --no-part --progress --force-overwrites \"{url}\"";
                    }
                    else
                    {
                        string formatArg = "bestvideo+bestaudio/best";
                        if (formatId != "best" && formatId != "MAX" && formatId != "Auto")
                        {
                            formatArg = $"bestvideo[height<={formatId}]+bestaudio/best[height<={formatId}]";
                        }
                        args = $"-f \"{formatArg}\" --merge-output-format mp4 -o \"{outputPath}\" --no-part --progress --force-overwrites \"{url}\"";
                    }

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = args,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = System.Text.Encoding.UTF8
                    };

                    using (var process = new Process { StartInfo = startInfo })
                    {
                        _activeProcess = process;
                        process.OutputDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                if (e.Data.Contains("%"))
                                {
                                    var match = System.Text.RegularExpressions.Regex.Match(e.Data, @"(\d+\.\d+)%");
                                    if (match.Success) onProgress?.Invoke($"⬇ Đang tải: {match.Groups[1].Value}%");
                                }
                                else if (e.Data.Contains("Destination") || e.Data.Contains("Extracting"))
                                {
                                    onProgress?.Invoke("⚙ Đang xử lý file...");
                                }
                                else if (e.Data.ToLower().Contains("recording"))
                                {
                                    onProgress?.Invoke("🔴 Đang ghi hình (Bấm menu để dừng)...");
                                }
                            }
                        };

                        process.Start();
                        process.BeginOutputReadLine();
                        process.WaitForExit();

                        if (process.ExitCode == 0) onProgress?.Invoke("✅ Tải xuống hoàn tất!");
                        else onProgress?.Invoke("⏹ Đã dừng hoặc lỗi.");
                    }
                }
                catch (Exception ex)
                {
                    onProgress?.Invoke($"❌ Lỗi: {ex.Message}");
                }
                finally
                {
                    _activeProcess = null;
                }
            });
        }
        public void SetVideoAdjustment(string property, double value)
        {
            _playerA?.SetPropertyDouble(property, value);
            _playerB?.SetPropertyDouble(property, value);
        }
        private int _currentRotation = 0;
        public void RotateVideo()
        {
            _currentRotation += 90;
            if (_currentRotation >= 360) _currentRotation = 0;

            _playerA?.SetPropertyLong("video-rotate", _currentRotation);
            _playerB?.SetPropertyLong("video-rotate", _currentRotation);
        }
        public void SetViewMode(string mode)
        {
            ActivePlayer.SetPropertyString("video-unscaled", "no"); ActivePlayer.SetPropertyString("keepaspect", "yes"); ActivePlayer.SetPropertyDouble("panscan", 0.0); ActivePlayer.SetPropertyLong("video-rotate", 0);
            switch (mode)
            {
                case "Fit":
                    ShowOsdText("Chế độ: Vừa khít (Fit)");
                    break;

                case "Fill":
                    ActivePlayer.SetPropertyDouble("panscan", 1.0);
                    ShowOsdText("Chế độ: Lấp đầy (Fill / Crop)");
                    break;

                case "Stretch":
                    ActivePlayer.SetPropertyString("keepaspect", "no");
                    ShowOsdText("Chế độ: Kéo giãn (Stretch)");
                    break;

                case "Original":
                    ActivePlayer.SetPropertyString("video-unscaled", "yes");
                    ShowOsdText("Chế độ: Nguyên bản (1:1)");
                    break;
            }
        }
        public void SetScaleX(double val)
        {
            ActivePlayer.SetPropertyDouble("video-scale-x", val);
        }

        public void SetScaleY(double val)
        {
            ActivePlayer.SetPropertyDouble("video-scale-y", val);
        }
        public void ResetScale()
        {
            ActivePlayer.SetPropertyDouble("video-scale-x", 1.0);
            ActivePlayer.SetPropertyDouble("video-scale-y", 1.0);
        }
        public void SetManualZoom(double level)
        {
            ActivePlayer.SetPropertyDouble("video-zoom", level);
            if (level != 0)
            {
                int percent = (int)(Math.Pow(2, level) * 100);
                ShowOsdText($"Zoom: {percent}%", 1000);
            }
        }
        public void PanVideo(double x, double y)
        {
            ActivePlayer.SetPropertyDouble("video-pan-x", x);
            ActivePlayer.SetPropertyDouble("video-pan-y", y);
        }

        public void FlipVideo(string mode)
        {
            _playerA?.DoCommand("vf", "toggle", mode);
            _playerB?.DoCommand("vf", "toggle", mode);
        }
        private double[] _eqGains = new double[] { 0, 0, 0, 0, 0 };

        public void SetEqualizer(int bandIndex, double gain)
        {
            if (bandIndex < 0 || bandIndex >= 5) return;
            _eqGains[bandIndex] = gain;
            string eqString = $"equalizer={string.Join(":", _eqGains)}";
            _playerA?.DoCommand("no-osd", "af", "set", eqString);
            _playerB?.DoCommand("no-osd", "af", "set", eqString);
        }
        private readonly Dictionary<string, double[]> _eqPresets = new Dictionary<string, double[]>()
        {
            { "Mặc định (Flat)",   new double[] { 0, 0, 0, 0, 0 } },
            { "Rock (Mạnh mẽ)",    new double[] { 5, 3, -2, 4, 6 } },            { "Pop (Giọng hát)",   new double[] { 2, 4, 5, 3, 1 } },            { "Classic (Cổ điển)", new double[] { 4, 2, 0, 2, 4 } },            { "Dance/Club",        new double[] { 6, 4, 0, 2, 5 } },            { "Treble Boost",      new double[] { -2, 0, 2, 5, 8 } },            { "Bass Boost",        new double[] { 8, 5, 2, 0, -2 } }        };

        public double[] GetPresetValues(string name)
        {
            if (_eqPresets.ContainsKey(name)) return _eqPresets[name];
            return new double[] { 0, 0, 0, 0, 0 };
        }
        public void SetEqualizer(double[] gains)
        {
            if (gains.Length != 5) return;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            string eqString = $"equalizer=gains={string.Join(":", gains.Select(g => g.ToString(culture)))}";
            _playerA?.DoCommand("af", "set", eqString);
            _playerB?.DoCommand("af", "set", eqString);
        }
        public void ResetVideoSettings()
        {
            SetVideoAdjustment("brightness", 0);
            SetVideoAdjustment("contrast", 0);
            SetVideoAdjustment("saturation", 0);
            SetVideoAdjustment("hue", 0);

            _currentRotation = 0;
            _playerA?.SetPropertyLong("video-rotate", 0);
            _playerB?.SetPropertyLong("video-rotate", 0);

            _playerA?.DoCommand("vf", "clr", ""); _playerB?.DoCommand("vf", "clr", "");
        }
        public void SetPostProcessing(string type, bool enable)
        {
            ApplyToPlayer(_playerA, type, enable);
            ApplyToPlayer(_playerB, type, enable);
        }

        private void ApplyToPlayer(MpvController player, string type, bool enable)
        {
            if (player == null || player.Handle == IntPtr.Zero) return;

            switch (type)
            {
                case "hdr":
                    player.SetPropertyString("tone-mapping", enable ? "reinhard" : "auto");
                    player.SetPropertyString("tone-mapping-mode", enable ? "hybrid" : "auto");
                    break;

                case "upscale":
                    string scaler = enable ? "ewa_lanczossharp" : "bilinear";
                    player.SetPropertyString("scale", scaler);
                    player.SetPropertyString("cscale", scaler); player.SetPropertyString("dscale", "mitchell"); break;

                case "deband":
                    player.SetPropertyString("deband", enable ? "yes" : "no");
                    if (enable)
                    {
                        player.SetPropertyString("deband-iterations", "2");
                        player.SetPropertyString("deband-threshold", "35");
                    }
                    break;

                case "deinterlace":
                    player.SetPropertyString("deinterlace", enable ? "yes" : "no");
                    break;

                case "shader":
                    if (enable)
                    {
                        player.DoCommand("vf", "add", "@myshader:sharpen=1.0");
                    }
                    else
                    {
                        player.DoCommand("vf", "remove", "@myshader");
                    }
                    break;
            }
        }
        public void SetColorManagement(bool autoIcc, string targetPrim)
        {

            _playerA?.SetPropertyString("icc-profile-auto", autoIcc ? "yes" : "no");
            _playerB?.SetPropertyString("icc-profile-auto", autoIcc ? "yes" : "no");

            if (!string.IsNullOrEmpty(targetPrim) && targetPrim != "auto")
            {
                _playerA?.SetPropertyString("target-prim", targetPrim);
                _playerB?.SetPropertyString("target-prim", targetPrim);
            }
            else
            {
                _playerA?.SetPropertyString("target-prim", "auto");
                _playerB?.SetPropertyString("target-prim", "auto");
            }
        }
        public void SetSharpen(double value)
        {
            _playerA?.SetPropertyDouble("sharpen", value);
            _playerB?.SetPropertyDouble("sharpen", value);
        }

        public void SetDenoise(double strength)
        {
            _playerA?.RemoveVideoFilter("denoise_hq");
            _playerB?.RemoveVideoFilter("denoise_hq");

            if (strength > 0)
            {
                double val = strength / 10.0;
                string filter = $"hqdn3d={val}:{val}:{val}:{val}";

                _playerA?.AddVideoFilter("denoise_hq", filter);
                _playerB?.AddVideoFilter("denoise_hq", filter);
            }
        }

        public void SetColorEffect(string effectType)
        {
            _playerA?.RemoveVideoFilter("fx_color");
            _playerB?.RemoveVideoFilter("fx_color");

            if (effectType == "None") return;

            string filter = "";

            switch (effectType)
            {
                case "Gray":
                    filter = "hue=s=0";
                    break;
                case "Sepia":
                    filter = "colorchannelmixer=.393:.769:.189:0:.349:.686:.168:0:.272:.534:.131";
                    break;
                case "Negative":
                    filter = "negate";
                    break;
                case "Warm":
                    filter = "eq=gamma_r=1.1:gamma_b=0.9:saturation=1.2";
                    break;
                case "Cold":
                    filter = "eq=gamma_r=0.9:gamma_b=1.1:saturation=1.1";
                    break;
                case "Underwater":
                    filter = "eq=gamma_r=0.5:gamma_g=0.8:gamma_b=1.2:saturation=1.3";
                    break;
                case "OldTV":
                    filter = "noise=alls=20:allf=t+u,eq=saturation=0.5";
                    break;
                case "Pixelate":
                    filter = "scale=iw/32:ih/32,scale=iw*32:ih*32:flags=neighbor";
                    break;
                default:
                    return;
            }
            _playerA?.AddVideoFilter("fx_color", filter);
            _playerB?.AddVideoFilter("fx_color", filter);
        }
        public void SetMotionInterpolation(bool enable)
        {
            if (enable)
            {
                _playerA?.SetPropertyString("video-sync", "display-resample");
                _playerB?.SetPropertyString("video-sync", "display-resample");
                _playerA?.SetPropertyString("interpolation", "yes");
                _playerB?.SetPropertyString("interpolation", "yes");
                _playerA?.SetPropertyString("tscale", "oversample");
                _playerB?.SetPropertyString("tscale", "oversample");
            }
            else
            {
                _playerA?.SetPropertyString("video-sync", "audio"); _playerA?.SetPropertyString("interpolation", "no");
                _playerA?.SetPropertyString("tscale", "mitchell");
                _playerB?.SetPropertyString("video-sync", "audio");
                _playerB?.SetPropertyString("interpolation", "no");
                _playerB?.SetPropertyString("tscale", "mitchell");
            }
        }
        public void SetBlur(double strength)
        {
            _playerA?.RemoveVideoFilter("fx_blur");
            _playerB?.RemoveVideoFilter("fx_blur");

            if (strength > 0)
            {
                string filter = $"boxblur={strength}:1";
                _playerA?.AddVideoFilter("fx_blur", filter);
                _playerB?.AddVideoFilter("fx_blur", filter);
            }
        }
        public void SetTechnicolorEffect(bool enable)
        {
            _playerA?.RemoveVideoFilter("fx_techni");
            _playerB?.RemoveVideoFilter("fx_techni");

            if (enable)
            {

                string filter = "eq=saturation=0.55:contrast=1.1:gamma_r=1.4:gamma_g=0.9:gamma_b=0.85";

                _playerA?.AddVideoFilter("fx_techni", filter);
                _playerB?.AddVideoFilter("fx_techni", filter);
            }
        }
        public void SetOldMovieProjector(bool enable)
        {
            _playerA?.RemoveVideoFilter("fx_projector");
            _playerB?.RemoveVideoFilter("fx_projector");

            if (enable)
            {

                string sepiaMatrix = ".393:.769:.189:0:.349:.686:.168:0:.272:.534:.131";

                string filter = $"colorchannelmixer={sepiaMatrix}," +
                                "noise=alls=50:allf=t+u," + "vignette=PI/3," + "fps=16";
                _playerA?.AddVideoFilter("fx_projector", filter);
                _playerB?.AddVideoFilter("fx_projector", filter);
            }
        }
        public void SetFilmGrain(bool enable)
        {
            _playerA?.RemoveVideoFilter("fx_grain");
            _playerB?.RemoveVideoFilter("fx_grain");

            if (enable)
            {
                string filter = "noise=c0s=7:allf=t";

                _playerA?.AddVideoFilter("fx_grain", filter);
                _playerB?.AddVideoFilter("fx_grain", filter);
            }
        }
        public void SetVignette(bool enable)
        {
            if (enable)
            {
                _playerA?.AddVideoFilter("fx_vig", "vignette=PI/4");
                _playerB?.AddVideoFilter("fx_vig", "vignette=PI/4");
            }
            else
            {
                _playerA?.RemoveVideoFilter("fx_vig");
                _playerB?.RemoveVideoFilter("fx_vig");
            }
        }
        public void ApplyRawFFmpegFilter(string filterString)
        {
            _playerA?.RemoveVideoFilter("user_custom");
            _playerB?.RemoveVideoFilter("user_custom");

            if (!string.IsNullOrWhiteSpace(filterString))
            {
                _playerA?.AddVideoFilter("user_custom", filterString);
                _playerB?.AddVideoFilter("user_custom", filterString);
            }
        }

        public List<SubtitleTrack> GetSubtitleTracks()
        {
            var list = new List<SubtitleTrack>();
            if (ActivePlayer.Handle == IntPtr.Zero) return list;
            list.Add(new SubtitleTrack { Id = 0, Title = "Tắt (Off)", IsSelected = false });

            string json = ActivePlayer.GetPropertyJson("track-list");
            long currentSid = ActivePlayer.GetPropertyLong("sid");

            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var track in doc.RootElement.EnumerateArray())
                        {
                            string type = track.TryGetProperty("type", out var t) ? t.GetString() : "";
                            if (type != "sub") continue;

                            string lang = track.TryGetProperty("lang", out var l) ? l.GetString() : "";
                            bool isKeep = string.IsNullOrEmpty(lang) || lang.Contains("vi") || lang.Contains("en");
                            if (!isKeep) continue;

                            long id = track.TryGetProperty("id", out var i) ? i.GetInt64() : -1;
                            string title = track.TryGetProperty("title", out var tit) ? tit.GetString() : "";
                            bool isExt = track.TryGetProperty("external", out var ext) && ext.GetBoolean();
                            string displayName = "";
                            if (!string.IsNullOrEmpty(title)) displayName = title;
                            else displayName = !string.IsNullOrEmpty(lang) ? lang.ToUpper() : $"Track {id}";
                            if (!string.IsNullOrEmpty(lang)) displayName += $" [{lang.ToUpper()}]";
                            if (isExt) displayName += " (File rời)";
                            if ((title ?? "").ToLower().Contains("generated") || (title ?? "").ToLower().Contains("tự động"))
                            {
                                displayName += " [Auto]";
                            }

                            list.Add(new SubtitleTrack
                            {
                                Id = id,
                                Title = displayName,
                                Lang = lang,
                                IsSelected = (id == currentSid),
                                IsExternal = isExt
                            });
                        }
                    }
                }
            }
            catch { }

            if (currentSid <= 0) list[0].IsSelected = true;
            return list;
        }

        public void SelectSubtitle(long id)
        {
            ActivePlayer.SetSubtitleId(id);
            if (id > 0)
            {
                var tracks = GetSubtitleTracks();
                var selected = tracks.FirstOrDefault(t => t.Id == id);
                if (selected != null)
                {
                    ShowOsdText($"💬 Sub: {selected.Title}");
                }
            }
            else
            {
                ShowOsdText("💬 Sub: Tắt (Off)");
            }
        }

        public void LoadExternalSubtitle(string path)
        {
            ActivePlayer.AddSubtitle(path);
        }

        public void UpdateSubtitleSettings(int size, string colorHex, int marginV)
        {
            string colorCmd = $"#{colorHex}";

            void Apply(MpvController p)
            {
                if (p == null) return;

                p.SetPropertyString("sub-ass-override", "force");
                p.SetPropertyString("sub-font-size", size.ToString());
                p.SetPropertyString("sub-margin-y", marginV.ToString());
                p.SetPropertyString("sub-color", colorCmd);
                p.SetPropertyString("sub-border-color", "#000000");
                p.SetPropertyString("sub-border-size", "2.5");
                p.SetPropertyString("sub-shadow-offset", "1");
                p.SetPropertyString("sub-ass-force-style", "WrapStyle=1,Alignment=2");
            }

            Apply(_playerA);
            Apply(_playerB);
        }
        private double _valGamma = 1.0;
        private double _valRed = 1.0;
        private double _valGreen = 1.0;
        private double _valBlue = 1.0;

        public void SetAdvancedColor(string channel, double sliderValue)
        {
            double multiplier = 1.0 + (sliderValue / 100.0);

            switch (channel)
            {
                case "gamma": _valGamma = multiplier; break;
                case "red": _valRed = multiplier; break;
                case "green": _valGreen = multiplier; break;
                case "blue": _valBlue = multiplier; break;
            }

            ApplyColorFilter();
        }

        private void ApplyColorFilter()
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;

            string filter = $"eq=gamma={_valGamma.ToString(culture)}:" +
                            $"gamma_r={_valRed.ToString(culture)}:" +
                            $"gamma_g={_valGreen.ToString(culture)}:" +
                            $"gamma_b={_valBlue.ToString(culture)}";
            _playerA?.RemoveVideoFilter("adv_color");
            _playerB?.RemoveVideoFilter("adv_color");

            _playerA?.AddVideoFilter("adv_color", filter);
            _playerB?.AddVideoFilter("adv_color", filter);
        }
        public void SetHttpHeaders(string userAgent, string referrer)
        {
            string commonUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
            string finalUA = string.IsNullOrEmpty(userAgent) ? commonUA : userAgent;
            ActivePlayer.SetPropertyString("user-agent", finalUA);
            ActivePlayer.SetPropertyString("referrer", referrer ?? "");
            string headers = "";
            if (!string.IsNullOrEmpty(referrer)) headers += $"Referer: {referrer},";
            headers += $"User-Agent: {finalUA}";

            ActivePlayer.SetPropertyString("http-header-fields", headers);
        }
        public void ResetAdvancedColor()
        {
            _valGamma = 1.0; _valRed = 1.0; _valGreen = 1.0; _valBlue = 1.0;

            _playerA?.RemoveVideoFilter("adv_color");
            _playerB?.RemoveVideoFilter("adv_color");
            ResetVideoSettings();
        }
        public void SetAudioDelay(double seconds)
        {
            _playerA?.SetPropertyDouble("audio-delay", seconds);
            _playerB?.SetPropertyDouble("audio-delay", seconds);
            ShowOsdText($"🔊 Sync: {seconds:+0.0;-0.0;0.0}s");
        }
        public void SetSubtitleDelay(double seconds)
        {
            _playerA?.SetPropertyDouble("sub-delay", seconds);
            _playerB?.SetPropertyDouble("sub-delay", seconds);
            ShowOsdText($"💬 Sub Delay: {seconds:+0.0;-0.0;0.0}s");
        }

        public List<SponsorSegment> GetCurrentSponsors()
        {
            return new List<SponsorSegment>(_currentSponsors);
        }
    }
}
