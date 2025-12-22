using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;
using static MediaLedInterfaceNew.MediaEngine;
using Exception = System.Exception;
using HttpClient = System.Net.Http.HttpClient;
using HttpMethod = System.Net.Http.HttpMethod;
using HttpRequestMessage = System.Net.Http.HttpRequestMessage;
using TimeSpan = System.TimeSpan;
using Type = System.Type;
using Uri = System.Uri;

namespace MediaLedInterfaceNew
{
    public class MediaItem : INotifyPropertyChanged
    {
        public string FileName { get; set; } = "";
        public string FullPath { get; set; } = "";
        public string Duration { get; set; } = "";
        public string Type { get; set; } = "";
        public string ChannelName { get; set; } = "";
        public string UserAgent { get; set; } = "";
        public string Referrer { get; set; } = "";

        private Microsoft.UI.Xaml.Media.ImageSource? _poster;
        public Microsoft.UI.Xaml.Media.ImageSource? Poster
        {
            get => _poster;
            set { _poster = value; OnPropertyChanged(nameof(Poster)); }
        }
        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            set { _isPlaying = value; OnPropertyChanged(nameof(IsPlaying)); }
        }
        private bool _isPaused;
        public bool IsPaused
        {
            get => _isPaused;
            set { _isPaused = value; OnPropertyChanged(nameof(IsPaused)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed partial class MainWindow : Window
    {
        private DispatcherTimer _reconnectTimer;
        private Microsoft.UI.Windowing.AppWindow? _appWindow;
        private MediaEngine? _engine;
        private bool _isResizing = false;
        private bool _isInitialized = false;
        private bool _isLedOn = false;
        private bool _isNavExpanded = false;
        private bool _isPlayerMode = false;
        private PlayerMode _currentMode = PlayerMode.Off;
        private readonly HttpClient _httpClient = new HttpClient();
        private static System.Threading.SemaphoreSlim _metadataSemaphore = new System.Threading.SemaphoreSlim(3, 3);
        private static System.Threading.SemaphoreSlim _logoSemaphore = new System.Threading.SemaphoreSlim(5, 5);
        private const string MT_USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
        private ObservableCollection<MediaItem> _listLibrary = new ObservableCollection<MediaItem>();
        private List<MediaItem> _backupLibrary = new List<MediaItem>();
        private const string SETTING_SPONSOR = "EnableSponsorBlock";
        private const string SETTING_WAKELOCK = "EnableWakeLock";
        private const string SETTING_WATCH_FOLDER = "WatchFolderPath";
        private ObservableCollection<MediaItem> _listLocal = new ObservableCollection<MediaItem>();
        private ObservableCollection<MediaItem> _listStream = new ObservableCollection<MediaItem>();
        private ObservableCollection<MediaItem> _listTv = new ObservableCollection<MediaItem>();
        private ObservableCollection<MediaItem> _listSearch = new ObservableCollection<MediaItem>();
        private List<MediaItem> _backupLocal = new List<MediaItem>();
        private List<MediaItem> _backupStream = new List<MediaItem>();
        private List<MediaItem> _backupTv = new List<MediaItem>();
        private Random _rng = new Random();
        private MediaItem? _playingItem = null;
        private string _currentAudioDeviceId = "";
        private DispatcherTimer _audioDeviceCheckTimer;
        private Dictionary<int, string> _youtubePageTokens = new Dictionary<int, string>();
        public static Visibility IsVisibleIf(bool val) => val ? Visibility.Visible : Visibility.Collapsed;
        public static Visibility IsHiddenIf(bool val) => val ? Visibility.Collapsed : Visibility.Visible;
        private DispatcherTimer _progressTimer;
        private bool _isUserDragging = false;
        private bool _isUserDraggingTimeline = false;
        private bool _isInternalUpdate = false;
        private double _lastVolume = 80;
        private double _savedVolume = 80;
        private double _seekStep = 5.0;
        private const string SETTING_BG_PATH = "MpvBackgroundPath";
        private bool _isUserActionStop = false;
        private DispatcherTimer _statusTimer;
        private bool _isDragging = false;
        private bool _isEffectResizing = false;
        private Windows.Foundation.Point _startPoint;
        private double _orgLeft, _orgTop, _orgWidth, _orgHeight;
        private Microsoft.UI.Xaml.FrameworkElement? _selectedElement = null;
        private string _tickerColor = "&H00FFFFFF";
        private string _savedStatus = "Sẵn sàng.";
        private string _persistentStatus = "Sẵn sàng.";
        private string _inputMode = "";
        private DispatcherTimer _netTimer;
        private NetworkInterface? _activeNic;
        private long _lastBytesRecv = 0;
        private long _lastBytesSent = 0;
        private long _sessionDownloaded = 0;
        private string _currentSsid = "";
        private MonitorInfo? _selectedMonitor = null;
        private FileSystemWatcher? _folderWatcher;
        private DispatcherTimer _debounceTimer;
        private double _orgAspectRatio;
        private const string SETTING_APP_MODE = "AppViewMode";
        private string _currentResizeMode = "";
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const long WS_CAPTION = 0x00C00000L;
        private const long WS_THICKFRAME = 0x00040000L;
        private const long WS_BORDER = 0x00800000L;
        private const long WS_POPUP = 0x80000000L;
        private const long WS_EX_TOOLWINDOW = 0x00000080L;
        private const long WS_DLGFRAME = 0x00400000L;
        private const long WS_SYSMENU = 0x00080000L;
        private const long WS_MINIMIZEBOX = 0x00020000L;
        private const long WS_MAXIMIZEBOX = 0x00010000L;

        private const long WS_EX_DLGMODALFRAME = 0x00000001L;
        private const long WS_EX_CLIENTEDGE = 0x00000200L;
        private const long WS_EX_STATICEDGE = 0x00020000L;
        private const long WS_EX_WINDOWEDGE = 0x00000100L;
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;
        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS
        {
            public int cxLeftWidth;
            public int cxRightWidth;
            public int cyTopHeight;
            public int cyBottomHeight;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
        public ObservableCollection<MediaItem> CurrentList
        {
            get
            {
                if (lstMedia.ItemsSource is ObservableCollection<MediaItem> list) return list;
                return _listLocal;
            }
        }
        private void LoadSystemFonts()
        {
            _allSystemFonts.Clear();
            try
            {
                using (var collection = new System.Drawing.Text.InstalledFontCollection())
                {
                    var families = collection.Families;
                    foreach (var family in families)
                    {
                        _allSystemFonts.Add(family.Name);
                    }
                }
            }
            catch
            {
                _allSystemFonts.AddRange(new[] { "Arial", "Segoe UI", "Times New Roman" });
            }

            _allSystemFonts.Sort();
            cboTickerFont.ItemsSource = _allSystemFonts;
            if (_allSystemFonts.Contains("Arial")) cboTickerFont.SelectedItem = "Arial";
            else if (_allSystemFonts.Count > 0) cboTickerFont.SelectedIndex = 0;
        }

        private void cboTickerFont_KeyUp(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            var box = sender as ComboBox;
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                string textToFind = box.Text.ToLower();
                var filtered = string.IsNullOrEmpty(textToFind)
                    ? _allSystemFonts
                    : _allSystemFonts.Where(f => f.ToLower().Contains(textToFind)).ToList();
                box.ItemsSource = filtered;
                box.IsDropDownOpen = true;
                e.Handled = true;
                return;
            }
            if (e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.Down)
            {
                return;
            }

            if (string.IsNullOrEmpty(box.Text))
            {
                box.ItemsSource = _allSystemFonts;
            }
        }
        private List<string> _allSystemFonts = new List<string>();
        private async void btnAddMoreLogo_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".bmp");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                AddLogoToCanvas(file.Path);
            }
        }
        private WasapiLoopbackCapture? _audioCapture;
        private const int FftLength = 1024;
        private float[] _fftBuffer = new float[FftLength];
        private int _fftPos = 0;
        private double[] _lastLevels = new double[9];

        private double[] _currentPeaks = new double[9];
        private int[] _peakHoldTimers = new int[9];
        private const int PEAK_HOLD_FRAMES = 20;
        private const double PEAK_DROP_SPEED = 0.5;
        private void StartVisualizer()
        {
            if (_audioCapture != null && _audioCapture.CaptureState == NAudio.CoreAudioApi.CaptureState.Capturing)
                return;

            try
            {
                using (var enumerator = new MMDeviceEnumerator())
                {
                    var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    _currentAudioDeviceId = defaultDevice.ID;
                }
                _audioCapture = new WasapiLoopbackCapture();
                _audioCapture.DataAvailable += OnAudioDataAvailable;
                _audioCapture.StartRecording();

                if (pnlAudioViz != null) pnlAudioViz.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Lỗi khởi động Audio Viz: " + ex.Message);
            }
        }
        private void CheckAudioDeviceChanged(object sender, object e)
        {
            try
            {
                bool shouldBeRunning = _engine != null && _engine.IsPlaying() && !_engine.IsPaused();

                using (var enumerator = new MMDeviceEnumerator())
                {
                    var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    string newDeviceId = defaultDevice.ID;
                    if (_currentAudioDeviceId != newDeviceId)
                    {
                        System.Diagnostics.Debug.WriteLine($"Audio Device Changed: {newDeviceId}");

                        if (_audioCapture != null && _audioCapture.CaptureState == NAudio.CoreAudioApi.CaptureState.Capturing)
                        {
                            StopVisualizer();
                            StartVisualizer();
                        }
                        else if (shouldBeRunning)
                        {
                            StartVisualizer();
                        }
                    }
                }
            }
            catch
            {
            }
        }
        private void StopVisualizer()
        {
            if (_audioCapture != null)
            {
                _audioCapture.StopRecording();
                _audioCapture.DataAvailable -= OnAudioDataAvailable;
                _audioCapture.Dispose();
                _audioCapture = null;
            }
            if (pnlAudioViz != null) pnlAudioViz.Visibility = Visibility.Collapsed;
        }

        // Biến cho AGC (Tự động cân bằng âm lượng)
        private double _currentMaxLevel = 0.01; // Mức tín hiệu lớn nhất hiện tại (tránh chia cho 0)
        private const double NOISE_GATE = 0.0005; // Ngưỡng lọc nhiễu (dưới mức này coi là im lặng)
        private Random _fakeRnd = new Random(); // Random cho chế độ giả lập khi Mute
        private void OnAudioDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0) return;

            // Chuyển đổi byte sang float (PCM 32-bit IEEE float)
            for (int i = 0; i < e.BytesRecorded; i += 4)
            {
                // Lấy mẫu âm thanh
                float sample = BitConverter.ToSingle(e.Buffer, i);

                // Đưa vào buffer
                _fftBuffer[_fftPos] = sample;
                _fftPos++;

                // Khi đủ dữ liệu thì tính toán FFT
                if (_fftPos >= FftLength)
                {
                    _fftPos = 0;
                    CalculateFFT();
                }
            }
        }

        private void CalculateFFT()
        {
            // 1. Xử lý FFT
            NAudio.Dsp.Complex[] fftComplex = new NAudio.Dsp.Complex[FftLength];
            for (int i = 0; i < FftLength; i++)
            {
                double window = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (FftLength - 1)));
                fftComplex[i].X = (float)(_fftBuffer[i] * window);
                fftComplex[i].Y = 0;
            }
            FastFourierTransform.FFT(true, (int)Math.Log(FftLength, 2.0), fftComplex);

            // 2. Chia dải tần
            double[] bands = new double[9];
            bands[0] = GetBandAverage(fftComplex, 0, 2);
            bands[1] = GetBandAverage(fftComplex, 2, 5);
            bands[2] = GetBandAverage(fftComplex, 5, 10);
            bands[3] = GetBandAverage(fftComplex, 10, 20);
            bands[4] = GetBandAverage(fftComplex, 20, 40);
            bands[5] = GetBandAverage(fftComplex, 40, 80);
            bands[6] = GetBandAverage(fftComplex, 80, 150);
            bands[7] = GetBandAverage(fftComplex, 150, 300);
            bands[8] = GetBandAverage(fftComplex, 300, 511);

            // --- BƯỚC 3: AGC (TỰ ĐỘNG CÂN BẰNG) ---

            // Tìm giá trị lớn nhất trong khung hình hiện tại
            double frameMax = 0;
            for (int i = 0; i < 9; i++)
            {
                if (bands[i] > frameMax) frameMax = bands[i];
            }

            // Cập nhật mức trần (Dynamic Ceiling)
            // Nếu tín hiệu hiện tại lớn hơn mức trần đã biết -> Đẩy trần lên
            if (frameMax > _currentMaxLevel)
            {
                _currentMaxLevel = frameMax;
            }
            else
            {
                // Nếu tín hiệu nhỏ hơn, từ từ hạ trần xuống (để thích nghi khi bạn vặn nhỏ volume)
                _currentMaxLevel -= 0.0001;
                if (_currentMaxLevel < 0.001) _currentMaxLevel = 0.001; // Không được xuống quá thấp
            }

            // Tính hệ số phóng đại (Scale Factor)
            // Mục tiêu: Luôn đưa mức tín hiệu cao nhất về khoảng 0.8 (80% cột)
            double agcFactor = 0.6 / _currentMaxLevel;

            // --- BƯỚC 4: XỬ LÝ KHI MUTE HOẶC IM LẶNG ---
            bool isSilence = frameMax < NOISE_GATE;

            double[] boosted = new double[9];
            for (int i = 0; i < 9; i++)
            {
                if (isSilence)
                {
                    // CHẾ ĐỘ GIẢ LẬP (Fake Viz): Khi Mute hoặc hết nhạc
                    // Tạo sóng ngẫu nhiên nhẹ nhàng để màn hình không bị chết
                    // Tần số thấp (Bass) dao động chậm, Tần số cao dao động nhanh
                    boosted[i] = _fakeRnd.NextDouble() * 0.2;
                }
                else
                {
                    // CHẾ ĐỘ THẬT: Áp dụng AGC
                    double val = bands[i] * agcFactor;

                    // Vẫn giữ lại các tinh chỉnh Bass/Treble cũ
                    if (i == 0) val *= 0.8;      // Giảm Sub-Bass
                    else if (i >= 7) val *= 2.0; // Tăng Treble

                    boosted[i] = val;
                }
            }

            // 5. Cập nhật UI
            this.DispatcherQueue.TryEnqueue(() =>
            {
                UpdateBar(bar5, peak5, boosted[0], 4);

                UpdateBar(bar4, peak4, boosted[1], 3);
                UpdateBar(bar6, peak6, boosted[2], 5);

                UpdateBar(bar3, peak3, boosted[3], 2);
                UpdateBar(bar7, peak7, boosted[4], 6);

                UpdateBar(bar2, peak2, boosted[5], 1);
                UpdateBar(bar8, peak8, boosted[6], 7);

                UpdateBar(bar1, peak1, boosted[7], 0);
                UpdateBar(bar9, peak9, boosted[8], 8);
            });
        }
        private void UpdateBar(Microsoft.UI.Xaml.Shapes.Rectangle? bar, Microsoft.UI.Xaml.Shapes.Rectangle? peakBar, double signalValue, int index)
        {
            if (bar == null || peakBar == null) return;

            // 1. Lấy chiều cao khung chứa
            double containerHeight = 100;
            if (pnlAudioViz.Parent is FrameworkElement parent)
            {
                containerHeight = parent.ActualHeight;
                if (containerHeight < 20) containerHeight = 20;
            }

            // Giới hạn 95%
            double maxHeight = containerHeight * 0.95;

            // 2. Tính chiều cao mục tiêu (Target)
            double targetHeight = signalValue * maxHeight;

            // Cắt ngọn & đáy
            if (targetHeight > maxHeight) targetHeight = maxHeight;
            if (targetHeight < 4) targetHeight = 4;

            // --- THUẬT TOÁN SIÊU MƯỢT (ULTRA SMOOTH) ---

            // Hệ số làm mượt (Càng nhỏ càng mượt nhưng càng trễ)
            // 0.15 = Rất mượt (Slow)
            // 0.3 = Vừa phải (Normal)
            // 0.5 = Nhanh (Fast)
            double smoothFactorUp = 0.2;   // Tốc độ đi lên (Giảm từ 0.4 xuống 0.2)
            double smoothFactorDown = 0.1; // Tốc độ đi xuống (Dùng nội suy thay vì trừ thẳng)

            if (targetHeight > _lastLevels[index])
            {
                // Đi lên: Nhích từ từ 20% khoảng cách mỗi lần
                _lastLevels[index] += (targetHeight - _lastLevels[index]) * smoothFactorUp;
            }
            else
            {
                // Đi xuống: Cũng dùng nội suy để rơi "mềm" hơn (Exponential Decay)
                // Thay vì rơi bộp một cái (Linear), nó sẽ giảm tốc dần khi gần đáy
                _lastLevels[index] += (targetHeight - _lastLevels[index]) * smoothFactorDown;
            }

            // Ràng buộc giá trị
            if (_lastLevels[index] > maxHeight) _lastLevels[index] = maxHeight;
            if (_lastLevels[index] < 4) _lastLevels[index] = 4;

            bar.Height = _lastLevels[index];

            // 3. Xử lý Vạch Peak (Cũng làm mượt nhẹ)
            if (_lastLevels[index] > _currentPeaks[index])
            {
                // Đẩy Peak lên nhanh hơn thanh chính một chút để tạo cảm giác lực
                _currentPeaks[index] = _lastLevels[index];
                _peakHoldTimers[index] = PEAK_HOLD_FRAMES;
            }
            else
            {
                if (_peakHoldTimers[index] > 0)
                {
                    _peakHoldTimers[index]--;
                }
                else
                {
                    // Rơi chậm hơn nữa (Giảm từ 0.5 xuống 0.3)
                    _currentPeaks[index] -= 0.3;
                }
            }

            // Kéo vạch Peak xuống nếu vượt trần (Fix lỗi resize)
            if (_currentPeaks[index] > maxHeight) _currentPeaks[index] = maxHeight;
            if (_currentPeaks[index] < 4) _currentPeaks[index] = 4;

            // Cập nhật vị trí
            peakBar.Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, _currentPeaks[index]);
        }
        private double GetBandAverage(NAudio.Dsp.Complex[] data, int startIdx, int endIdx)
        {
            double sum = 0;
            for (int i = startIdx; i <= endIdx && i < data.Length; i++)
            {
                // Tính độ lớn (Magnitude)
                double magnitude = Math.Sqrt(data[i].X * data[i].X + data[i].Y * data[i].Y);
                sum += magnitude;
            }
            return sum / (endIdx - startIdx + 1);
        }

        private async Task ApplyLogoLogic()
        {
            if (_engine == null) return;

            if (swLogo.IsOn)
            {
                var logoList = new List<LogoLayer>();
                foreach (var child in OverlayCanvas.Children)
                {
                    if (child is Grid g && g.Tag is string originalPath)
                    {

                        int w = (int)g.ActualWidth;
                        int h = (int)g.ActualHeight;
                        if (w <= 0) w = 100;
                        if (h <= 0) h = 100;

                        double l = Canvas.GetLeft(g);
                        double t = Canvas.GetTop(g);

                        string optimizedPath = await Task.Run(() => CreateResizedLogoCache(originalPath, w, h));

                        logoList.Add(new LogoLayer
                        {
                            Path = optimizedPath,
                            X = (int)l,
                            Y = (int)t,
                            Width = w,
                            Height = h
                        });
                    }
                }
                await _engine.UpdateLogoLayers(logoList);
                UpdateStatus($"✅ Đã cập nhật {logoList.Count} logo (Đã tối ưu hóa).");
            }
            else
            {
                await _engine.UpdateLogoLayers(null);
                UpdateStatus("Đã tắt Logo.");
            }
        }
        private string CreateResizedLogoCache(string sourcePath, int targetWidth, int targetHeight)
        {
            try
            {
                if (!System.IO.File.Exists(sourcePath)) return sourcePath;

                string tempFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MediaLed_Cache");
                if (!System.IO.Directory.Exists(tempFolder))
                {
                    System.IO.Directory.CreateDirectory(tempFolder);
                }

                string fileName = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
                string ext = System.IO.Path.GetExtension(sourcePath);
                string cacheFileName = $"{fileName}_{targetWidth}x{targetHeight}{ext}";
                string destPath = System.IO.Path.Combine(tempFolder, cacheFileName);

                if (System.IO.File.Exists(destPath)) return destPath;

                using (var originalImage = System.Drawing.Image.FromFile(sourcePath))
                {
                    using (var resizedBitmap = new System.Drawing.Bitmap(targetWidth, targetHeight))
                    {
                        using (var graphics = System.Drawing.Graphics.FromImage(resizedBitmap))
                        {
                            graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                            graphics.DrawImage(originalImage, 0, 0, targetWidth, targetHeight);
                        }
                        resizedBitmap.Save(destPath, originalImage.RawFormat);
                    }
                }

                return destPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Resize Logo Error: {ex.Message}");
                return sourcePath;
            }
        }
        private string GetMpvColor(string inputHex)
        {
            try
            {
                if (string.IsNullOrEmpty(inputHex)) return "&H00000000";
                if (inputHex.StartsWith("&H", StringComparison.OrdinalIgnoreCase))
                {
                    return inputHex;
                }

                string cleanHex = inputHex.Replace("#", "").Trim();

                byte a = 255;
                byte r = 0, g = 0, b = 0;

                if (cleanHex.Length == 8)
                {
                    a = Convert.ToByte(cleanHex.Substring(0, 2), 16);
                    r = Convert.ToByte(cleanHex.Substring(2, 2), 16);
                    g = Convert.ToByte(cleanHex.Substring(4, 2), 16);
                    b = Convert.ToByte(cleanHex.Substring(6, 2), 16);
                }
                else if (cleanHex.Length == 6)
                {
                    r = Convert.ToByte(cleanHex.Substring(0, 2), 16);
                    g = Convert.ToByte(cleanHex.Substring(2, 2), 16);
                    b = Convert.ToByte(cleanHex.Substring(4, 2), 16);
                }


                byte mpvAlpha = (byte)(255 - a);

                return $"&H{mpvAlpha:X2}{b:X2}{g:X2}{r:X2}";
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine($"Lỗi parse màu: {inputHex}");
                return "&H00000000";
            }
        }
        private async Task ApplyTickerLogic()
        {
            if (_engine == null) return;
            if (swTicker.IsOn && (_engine.IsShowingWallpaper || _engine.IsCurrentContentImage))
            {
                swTicker.IsOn = false;
                UpdateStatus("⚠️ Chữ chạy chỉ hoạt động trên Video.", false, true);
                if (DraggableText != null) DraggableText.Visibility = Visibility.Collapsed;
                return;
            }

            if (swTicker.IsOn && !string.IsNullOrEmpty(txtTickerInput.Text))
            {
                string finalTextColorMpv = GetMpvColor(_tickerTextColor);
                string finalBgColorMpv = GetMpvColor(_tickerBgColor);
                bool useBg = swTickerBg.IsOn;


                if (finalBgColorMpv.StartsWith("&HFF"))
                {
                    useBg = false;
                }
                double uiTop = Microsoft.UI.Xaml.Controls.Canvas.GetTop(DraggableText);
                int exactY = Math.Clamp((int)uiTop, 0, 2000);

                double startTime = (radTickerTime.SelectedIndex == 1) ?
                    GetScheduledTime() : _engine.Position;
                await _engine.ShowTicker(
            txtTickerInput.Text,
            sldTickerSpeed.Value,
            finalTextColorMpv,
            exactY,
            startTime,
            useBg,
            finalBgColorMpv,
            (int)sldTickerBgSize.Value,
            cboTickerFont.SelectedItem?.ToString() ?? "Arial",
            chkFullWidth.IsChecked == true,
            (int)nbLoopCount.Value,
            btnBold.IsChecked == true,
            btnItalic.IsChecked == true
                );

                UpdateStatus($"✅ Đã chạy chữ (Màu nền: {_tickerBgColor ?? "Mặc định"})");
            }
            else
            {
                await _engine.HideTicker();
                UpdateStatus("Đã tắt chữ chạy.");
            }
        }

        private double GetScheduledTime()
        {
            double.TryParse(txtH.Text, out double h);
            double.TryParse(txtM.Text, out double m);
            double.TryParse(txtS.Text, out double s);
            return (h * 3600) + (m * 60) + s;
        }
        private void AddLogoToCanvas(string path)
        {
            if (swLogo.IsOn == false)
            {
                swLogo.IsOn = true;

            }

            var grid = new Grid();

            grid.Width = 200;
            grid.Height = 200;
            grid.Tag = path;
            grid.IsHitTestVisible = true;

            var menu = new MenuFlyout();
            var deleteItem = new MenuFlyoutItem { Text = "Xóa Logo", Icon = new FontIcon { Glyph = "\uE74D" } };
            deleteItem.Click += (s, e) =>
            {
                OverlayCanvas.Children.Remove(grid);
                _selectedElement = null;
            };
            menu.Items.Add(deleteItem);
            grid.ContextFlyout = menu;
            grid.PointerPressed += Element_PointerPressed;
            grid.PointerMoved += Element_PointerMoved;
            grid.PointerReleased += Element_PointerReleased;
            grid.KeyDown += (s, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Delete)
                {
                    OverlayCanvas.Children.Remove(grid);
                    _selectedElement = null;
                }
            };
            var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(path));
            var img = new Microsoft.UI.Xaml.Controls.Image();
            img.Source = bmp;
            img.Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill;

            bmp.ImageOpened += (s, e) =>
            {
                double w = bmp.PixelWidth;
                double h = bmp.PixelHeight;

                if (w > 0 && h > 0)
                {
                    double ratio = w / h;
                    grid.Width = 200;
                    grid.Height = 200 / ratio;
                }
            };

            grid.Children.Add(img);
            var border = new Border();
            border.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Cyan);
            border.BorderThickness = new Thickness(1);

            var rectDash = new Microsoft.UI.Xaml.Shapes.Rectangle();
            rectDash.Stroke = new SolidColorBrush(Microsoft.UI.Colors.Cyan);
            rectDash.StrokeThickness = 2;
            rectDash.StrokeDashArray = new DoubleCollection { 4, 2 };

            grid.Children.Add(border);
            grid.Children.Add(rectDash);
            var btnDelete = new Button();
            btnDelete.Width = 24; btnDelete.Height = 24;
            btnDelete.Padding = new Thickness(0);
            btnDelete.CornerRadius = new CornerRadius(12);
            btnDelete.Background = new SolidColorBrush(Microsoft.UI.Colors.Red);
            btnDelete.HorizontalAlignment = HorizontalAlignment.Right;
            btnDelete.VerticalAlignment = VerticalAlignment.Top;
            btnDelete.Margin = new Thickness(0, -12, -12, 0);
            btnDelete.Content = new FontIcon { Glyph = "\uE711", FontSize = 12, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) };
            btnDelete.Click += (s, e) => { OverlayCanvas.Children.Remove(grid); _selectedElement = null; };
            btnDelete.PointerPressed += (s, e) => e.Handled = true;
            grid.Children.Add(btnDelete);
            Microsoft.UI.Xaml.Shapes.Ellipse CreateHandle(string tag, HorizontalAlignment h, VerticalAlignment v, string cursorIcon)
            {
                var el = new Microsoft.UI.Xaml.Shapes.Ellipse();
                el.Width = 16; el.Height = 16;
                el.Fill = new SolidColorBrush(Microsoft.UI.Colors.White);
                el.Stroke = new SolidColorBrush(Microsoft.UI.Colors.Cyan);
                el.StrokeThickness = 2;
                el.HorizontalAlignment = h;
                el.VerticalAlignment = v;
                el.Tag = tag;
                double mRight = (h == HorizontalAlignment.Right) ? -8 : 0;
                double mBottom = (v == VerticalAlignment.Bottom) ? -8 : 0;
                el.Margin = new Thickness(0, 0, mRight, mBottom);

                el.PointerPressed += Resize_PointerPressed;
                el.PointerMoved += Resize_PointerMoved;
                el.PointerReleased += Resize_PointerReleased;
                el.PointerEntered += (s, e) =>
                {
                    if (cursorIcon == "NWSE") SetCursor(el, Microsoft.UI.Input.InputSystemCursorShape.SizeNorthwestSoutheast);
                    else if (cursorIcon == "WE") SetCursor(el, Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);
                    else if (cursorIcon == "NS") SetCursor(el, Microsoft.UI.Input.InputSystemCursorShape.SizeNorthSouth);
                };
                el.PointerExited += (s, e) => ResetCursor(el);
                return el;
            }

            grid.Children.Add(CreateHandle("Right", HorizontalAlignment.Right, VerticalAlignment.Center, "WE"));
            grid.Children.Add(CreateHandle("Bottom", HorizontalAlignment.Center, VerticalAlignment.Bottom, "NS"));
            grid.Children.Add(CreateHandle("Corner", HorizontalAlignment.Right, VerticalAlignment.Bottom, "NWSE"));
            Canvas.SetLeft(grid, 100);
            Canvas.SetTop(grid, 100);
            OverlayCanvas.Children.Add(grid);

            UpdateStatus("Đã thêm logo (Tỷ lệ gốc).");
        }
        private async void MainWindow_Activated(object sender, WindowActivatedEventArgs e)
        {
            if (_isInitialized) return;
            _isInitialized = true;

            UpdateStatus("Hệ thống đang khởi động engine...");

            await Task.Delay(100);
            if (_engine == null)
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                _engine = new MediaEngine(hwnd, this.DispatcherQueue);
                _engine.SetPropertyString("hr-seek", "yes");
                _engine.SetPropertyString("hwdec", "auto-copy");
                _engine.SetPropertyString("gpu-context", "d3d11");

                _engine.SetPropertyString("vd-lavc-dr", "no");


                _engine.SetLedScreen(false, 0);
                LoadSystemSettings();
                LoadBackgroundSetting();
                KeyManager.LoadKeys();
                string savedMode = AppSettings.Get(SETTING_APP_MODE);
                bool isPlayerStart = savedMode == "True";
                btnModeSwitch.IsChecked = isPlayerStart;
                if (isPlayerStart)
                {
                    // Đợi UI + window ổn định hoàn toàn
                    await Task.Delay(300);

                    await _engine.SetMode(true);
                    UpdateModeUI(true);
                }
                UpdateMpvLayout();
            }

            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    if (swStartup != null)
                    {
                        swStartup.IsOn = key?.GetValue("MediaLedInterface") != null;
                    }
                }
            }
            catch { }

            if (swWakeLock != null)
            {
                swWakeLock.IsOn = true;
                _engine?.PreventSleep(true);
            }

            LoadWatchFolder();

            UpdateStatus("Hệ thống đã sẵn sàng. Chào mừng bạn!");
            RefreshMonitors();
            UpdateListStats();
        }
        private void btnEditYtKey_Click(object sender, RoutedEventArgs e) => KeyManager.OpenFileToEdit("yt");

        private void btnReloadKeys_Click(object sender, RoutedEventArgs e)
        {
            KeyManager.LoadKeys();
            UpdateStatus("✅ Đã cập nhật danh sách API Key mới!", true);
        }

        private void lstMedia_DragOver(object sender, DragEventArgs e)
        {

            bool isTabLocal = lstMedia.ItemsSource == _listLocal;
            bool isTabTv = lstMedia.ItemsSource == _listTv;

            if (!isTabLocal && !isTabTv)
            {
                e.AcceptedOperation = DataPackageOperation.None;
                e.DragUIOverride.Caption = "Không thể thả vào Tab này";
                e.DragUIOverride.IsCaptionVisible = true;
                return;
            }
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "Thêm vào danh sách";
                e.DragUIOverride.IsCaptionVisible = true;
            }
            else
            {
                e.AcceptedOperation = DataPackageOperation.Move;
                e.DragUIOverride.IsCaptionVisible = false;
            }
        }

        private async void lstMedia_Drop(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                if (items.Count > 0)
                {
                    if (lstMedia.ItemsSource != _listLocal)
                    {
                        if (rbMedia != null)
                        {
                            rbMedia.IsChecked = true;
                            OnNavTabClick(rbMedia, null);
                        }
                    }

                    int countAdded = 0;
                    int countSkipped = 0;

                    foreach (var item in items)
                    {
                        if (item is StorageFile file)
                        {
                            string ext = file.FileType;

                            if (_allowedExtensions.Contains(ext))
                            {
                                if (ext.ToLower().Contains("m3u"))
                                {
                                    HandleExternalPlaylist(file.Path);
                                    return;
                                }
                                var newItem = new MediaItem
                                {
                                    FileName = file.Name,
                                    FullPath = file.Path,
                                    Type = ext.ToUpper().Replace(".", ""),
                                    Poster = null
                                };

                                _listLocal.Add(newItem);
                                _backupLocal.Add(newItem);
                                countAdded++;
                                _ = Task.Run(async () =>
                                {
                                    var stream = await FastThumbnail.GetImageStreamAsync(file.Path);
                                    if (stream != null)
                                    {
                                        this.DispatcherQueue.TryEnqueue(async () =>
                                        {
                                            try
                                            {
                                                var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                                                await bmp.SetSourceAsync(stream.AsRandomAccessStream());
                                                newItem.Poster = bmp;
                                            }
                                            catch { }
                                        });
                                    }
                                });
                            }
                            else
                            {
                                countSkipped++;
                            }
                        }
                    }
                    if (countAdded > 0)
                    {
                        if (countSkipped > 0)
                            UpdateStatus($"✅ Đã thêm {countAdded} file (Bỏ qua {countSkipped} file không hỗ trợ).");
                        else
                            UpdateStatus($"✅ Đã thêm {countAdded} file.");

                        UpdateListStats();
                    }
                    else if (countSkipped > 0)
                    {
                        UpdateStatus("⛔ Các file bạn kéo vào không được hỗ trợ!", false, true);
                    }
                }
            }
        }

        private const string SETTING_SPONSOR_CATS = "SponsorCategories";
        private void LoadSystemSettings()
        {
            string savedSponsor = AppSettings.Get(SETTING_SPONSOR);
            if (savedSponsor != null)
            {
                bool isOn = savedSponsor == "True";
                swSponsorBlock.IsOn = isOn;
                if (_engine != null) _engine.IsSponsorBlockEnabled = isOn;
            }
            string savedCats = AppSettings.Get(SETTING_SPONSOR_CATS);
            if (string.IsNullOrEmpty(savedCats)) savedCats = "sponsor";

            var cats = new HashSet<string>(savedCats.Split(','));
            chkSbSponsor.IsChecked = cats.Contains("sponsor");
            chkSbSelfPromo.IsChecked = cats.Contains("selfpromo");
            chkSbInteraction.IsChecked = cats.Contains("interaction");
            chkSbIntro.IsChecked = cats.Contains("intro");
            chkSbOutro.IsChecked = cats.Contains("outro");
            chkSbPreview.IsChecked = cats.Contains("preview");
            chkSbMusic.IsChecked = cats.Contains("music_offtopic");
            chkSbFiller.IsChecked = cats.Contains("filler");
            if (_engine != null) _engine.AllowedSponsorCategories = cats;
            string savedWakeLock = AppSettings.Get(SETTING_WAKELOCK);
            if (savedWakeLock != null)
            {
                bool isOn = savedWakeLock == "True";
                swWakeLock.IsOn = isOn;
            }
        }
        private void OnSponsorCategoryChanged(object sender, RoutedEventArgs e)
        {
            if (_engine == null) return;
            var selectedCats = new List<string>();

            if (chkSbSponsor.IsChecked == true) selectedCats.Add("sponsor");
            if (chkSbSelfPromo.IsChecked == true) selectedCats.Add("selfpromo");
            if (chkSbInteraction.IsChecked == true) selectedCats.Add("interaction");
            if (chkSbIntro.IsChecked == true) selectedCats.Add("intro");
            if (chkSbOutro.IsChecked == true) selectedCats.Add("outro");
            if (chkSbPreview.IsChecked == true) selectedCats.Add("preview");
            if (chkSbMusic.IsChecked == true) selectedCats.Add("music_offtopic");
            if (chkSbFiller.IsChecked == true) selectedCats.Add("filler");
            string saveString = string.Join(",", selectedCats);
            AppSettings.Save(SETTING_SPONSOR_CATS, saveString);
            _engine.AllowedSponsorCategories = new HashSet<string>(selectedCats);
            if (_playingItem != null && _playingItem.FullPath.Contains("youtube"))
            {
                _engine.LoadSponsorBlock(_playingItem.FullPath);
            }
        }
        private void swStartup_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            string appName = "MediaLedInterface";
            string appPath = Environment.ProcessPath;

            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    if (swStartup.IsOn)
                    {

                        key.SetValue(appName, $"\"{appPath}\" --autostart");

                        UpdateStatus("✅ Đã bật khởi động cùng Windows (Chế độ thu nhỏ).");
                    }
                    else
                    {
                        key.DeleteValue(appName, false);
                        UpdateStatus("⛔ Đã tắt khởi động cùng Windows.");
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus("Lỗi Registry: " + ex.Message, false, true);
            }
        }

        private void swWakeLock_Toggled(object sender, RoutedEventArgs e)
        {
            if (_engine == null) return;
            bool enable = swWakeLock.IsOn;
            _engine.PreventSleep(enable);
            AppSettings.Save(SETTING_WAKELOCK, enable.ToString());

            UpdateStatus(enable ? "💡 Màn hình sẽ luôn sáng (Đã lưu)." : "💤 Màn hình sẽ tắt theo cài đặt Windows.");
        }


        private async void btnRegisterAssoc_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string packageFamilyName = Package.Current.Id.FamilyName;
                var uri = new Uri($"ms-settings:defaultapps?pname={packageFamilyName}");
                await Launcher.LaunchUriAsync(uri);

                UpdateStatus("✅ Đã mở cài đặt Windows. Hãy chọn 'Set default' (Đặt mặc định) cho các loại file!");
            }
            catch (Exception ex)
            {
                await Launcher.LaunchUriAsync(new Uri("ms-settings:defaultapps"));
                UpdateStatus($"⚠️ Đã mở cài đặt chung (Lỗi: {ex.Message})", false, true);
            }
        }

        private void SetupCloseBehavior()
        {
            if (_appWindow != null)
            {
                _appWindow.Closing += AppWindow_Closing;
            }
        }

        private void AppWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
        {
            CloseFsWindow();

            if (_isInternalFullscreen)
            {

                _appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Default);
            }
            args.Cancel = false;
            if (_engine != null)
            {
                _engine.Dispose();
                _engine = null;
            }

            System.Environment.Exit(0);
        }
        private void RefreshMonitors()
        {
            if (_engine == null) return;


            var currentMonitorInfo = _engine.GetCurrentAppMonitor();
            _lastMonitorHandle = currentMonitorInfo.Handle;
            var secondaryMonitors = _engine.GetSecondaryMonitors();
            cboMonitorOutput.ItemsSource = secondaryMonitors;

            if (secondaryMonitors.Count > 0)
            {
                cboMonitorOutput.SelectedIndex = 0;
                _selectedMonitor = secondaryMonitors[0];

                btnToggleLed.IsEnabled = true;
                btnToggleLed.Opacity = 1.0;
                string msg = $"✅ Smart Detect:\n" +
                             $"• App đang ở: {currentMonitorInfo.Name}\n" +
                             $"• Auto Output: {_selectedMonitor.Name}";

                txtMonitorStatus.Text = msg;
                txtMonitorStatus.Foreground = new SolidColorBrush(Microsoft.UI.Colors.LimeGreen);

                UpdateStatus($"Đã phát hiện {_selectedMonitor.Name}. Sẵn sàng xuất hình.");
            }
            else
            {
                cboMonitorOutput.ItemsSource = null;
                cboMonitorOutput.PlaceholderText = "Không có màn hình phụ";
                _selectedMonitor = null;

                if (_isLedOn) btnToggleLed_Click(null, null);

                btnToggleLed.IsEnabled = false;
                btnToggleLed.Opacity = 0.3;
                txtMonitorStatus.Text = $"⚠️ Chỉ phát hiện 1 màn hình ({currentMonitorInfo.Name}).\nVui lòng cắm dây HDMI/DP.";
                txtMonitorStatus.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Orange);
            }
        }
        private IntPtr _lastMonitorHandle = IntPtr.Zero;

        private void cboMonitorOutput_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cboMonitorOutput.SelectedItem is MonitorInfo monitor)
            {
                _selectedMonitor = monitor;
                if (_isLedOn && _engine != null)
                {
                    _engine.SetLedScreen(false, 0);
                    _engine.SetLedScreen(true, _selectedMonitor.Index);
                }
            }
        }
        private void btnRefreshMonitors_Click(object sender, RoutedEventArgs e)
        {
            RefreshMonitors();
            UpdateStatus("Đã quét lại danh sách màn hình.");
        }

        private void LoadBackgroundSetting()
        {
            string path = AppSettings.Get(SETTING_BG_PATH) ?? "";

            if (!string.IsNullOrEmpty(path))
            {
                txtBgPath.Text = path;
                if (_engine != null)
                {
                    _ = _engine.SetBackgroundImage(path).ContinueWith(t =>
                    {
                        this.DispatcherQueue.TryEnqueue(() =>
                        {
                            if (_engine != null && !_engine.IsPlaying())
                            {
                                _engine.ShowWallpaper();
                            }
                        });
                    });
                }
            }
        }

        private async void btnBrowseBg_Click(object sender, RoutedEventArgs e)
        {
            string imageExts = "*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tiff;*.tif;*.svg";
            string filter = $"Image Files|{imageExts}|All Files (*.*)|*.*";
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            string[] files = Win32Helper.ShowOpenFileDialog(hWnd, "Chọn ảnh nền chờ", filter);
            if (files.Length > 0)
            {
                string path = files[0];
                txtBgPath.Text = path;
                AppSettings.Save(SETTING_BG_PATH, path);
                if (_engine != null)
                {
                    UpdateStatus("⏳ Đang xử lý và nạp ảnh nền...", true);
                    await _engine.SetBackgroundImage(path);
                    if (_playingItem == null)
                    {
                        _engine.ShowWallpaper();
                    }

                    UpdateStatus("✅ Đã cập nhật ảnh nền.");
                }
            }
        }

        private void btnClearBg_Click(object sender, RoutedEventArgs e)
        {
            txtBgPath.Text = "";
            AppSettings.Remove(SETTING_BG_PATH);

            if (_engine != null)
            {
                _engine.SetBackgroundImage("");
                if (_playingItem == null) _engine.Stop();
            }
        }

        public enum PlayerMode
        {
            Off,
            Shuffle,
            RepeatAll,
            RepeatOne
        }
        private void btnMute_Click(object sender, RoutedEventArgs e)
        {
            if (sliderVolume.Value > 0)
            {
                _savedVolume = sliderVolume.Value;
                sliderVolume.Value = 0;
            }
            else
            {
                if (_savedVolume <= 0) _savedVolume = 50;

                sliderVolume.Value = _savedVolume;
            }
        }
        private void btnRewind_Click(object sender, RoutedEventArgs e)
        {
            if (_engine == null) return;
            double currentPos = _engine.Position;
            double newPos = currentPos - _seekStep;
            if (newPos < 0) newPos = 0;


            _isInternalUpdate = true;

            double totalDuration = _engine.Duration;
            if (totalDuration > 0)
            {
                double percent = (newPos / totalDuration) * 100;
                timelineSlider.Value = percent;
                if (_fsTimeSlider != null) _fsTimeSlider.Value = percent;
                TimeSpan tCurrent = TimeSpan.FromSeconds(newPos);
                string sCurrent = (tCurrent.TotalHours >= 1) ? tCurrent.ToString(@"hh\:mm\:ss") : tCurrent.ToString(@"mm\:ss");
                txtCurrentTime.Text = sCurrent;
                if (_fsCurrentTime != null) _fsCurrentTime.Text = sCurrent;
            }
            _isInternalUpdate = false;


            _engine.Seek(newPos);
            UpdateStatus($"⏪ Lùi {_seekStep}s", false);
        }
        private void btnForward_Click(object sender, RoutedEventArgs e)
        {
            if (_engine == null) return;

            double currentPos = _engine.Position;
            double totalDuration = _engine.Duration;

            double newPos = currentPos + _seekStep;
            if (totalDuration > 0 && newPos > totalDuration) newPos = totalDuration;


            _isInternalUpdate = true;
            if (totalDuration > 0)
            {
                double percent = (newPos / totalDuration) * 100;
                timelineSlider.Value = percent;

                if (_fsTimeSlider != null) _fsTimeSlider.Value = percent;

                TimeSpan tCurrent = TimeSpan.FromSeconds(newPos);
                string sCurrent = (tCurrent.TotalHours >= 1) ? tCurrent.ToString(@"hh\:mm\:ss") : tCurrent.ToString(@"mm\:ss");
                txtCurrentTime.Text = sCurrent;
                if (_fsCurrentTime != null) _fsCurrentTime.Text = sCurrent;
            }
            _isInternalUpdate = false;
            _engine.Seek(newPos, false);

            UpdateStatus($"⏩ Tiến {_seekStep}s", false);
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            _isUserActionStop = true;
            StopComplete();
            ClearAllSponsorMarks();
            UpdateStatus("⏹ Đã dừng hẳn.", true);
        }

        private bool _isInternalFullscreen = false;
        private RectInt32 _lastWindowRect;
        private bool _isDownloading = false;

        private async void btnQuickSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_engine == null) return;

            MenuFlyout menu = new MenuFlyout();
            var qualitySubItem = new MenuFlyoutSubItem { Text = "Chất lượng Video", Icon = new FontIcon { Glyph = "\uE9E9" } };
            string currentInfo = _engine.GetCurrentVideoInfo();
            qualitySubItem.Items.Add(new MenuFlyoutItem { Text = $"Đang phát: {currentInfo}", IsEnabled = false });
            qualitySubItem.Items.Add(new MenuFlyoutSeparator());
            var internalVariants = await _engine.GetAvailableVariants();
            var cachedFormatsForDownload = _engine.GetCachedFormatsSafe();
            if (internalVariants.Count > 2)
            {
                foreach (var v in internalVariants)
                {
                    var item = new RadioMenuFlyoutItem
                    {
                        Text = v.Name,
                        IsChecked = v.IsCurrent,
                        GroupName = "QualityGroup"
                    };
                    item.Click += (s, a) =>
                    {
                        _engine.SwitchVariant(v);
                        UpdateStatus($"⚙ Đã chuyển kênh: {v.Name}", false);
                    };
                    qualitySubItem.Items.Add(item);
                }
            }
            else if (_engine.IsOnlineMedia())
            {
                var autoItem = new RadioMenuFlyoutItem
                {
                    Text = "Auto (Mặc định)",
                    IsChecked = (_engine.CurrentYoutubeQuality == "Auto"),
                    GroupName = "QualityGroup"
                };
                autoItem.Click += (s, a) =>
                {
                    _engine.TargetResolution = "Auto";
                    _engine.ChangeOnlineResolution("Auto", "Auto");
                    UpdateStatus("⚙ Đã đặt về Auto.", false);
                };
                qualitySubItem.Items.Add(autoItem);
                qualitySubItem.Items.Add(new MenuFlyoutSeparator());
                var loadingItem = new MenuFlyoutItem { Text = "⏳ Đang quét độ phân giải...", IsEnabled = false };
                qualitySubItem.Items.Add(loadingItem);
                string pathToCheck = _playingItem?.FullPath ?? "";

                _ = Task.Run(async () =>
                {
                    var formats = await _engine.GetRealOnlineFormats(pathToCheck);
                    this.DispatcherQueue.TryEnqueue(() =>
                    {
                        if (qualitySubItem.Items.Contains(loadingItem))
                        {
                            qualitySubItem.Items.Remove(loadingItem);
                        }

                        if (formats.Count > 0)
                        {
                            foreach (var fmt in formats)
                            {
                                var item = new RadioMenuFlyoutItem
                                {
                                    Text = fmt.Label,
                                    GroupName = "QualityGroup",
                                    Tag = fmt.Id
                                };
                                if (_engine.CurrentYoutubeQuality == fmt.Height.ToString())
                                    item.IsChecked = true;

                                item.Click += (s, a) =>
                                {
                                    if (s is RadioMenuFlyoutItem r && r.Tag != null)
                                    {
                                        string id = r.Tag.ToString();
                                        string label = r.Text;
                                        _engine.TargetResolution = fmt.Height.ToString();
                                        _engine.ChangeOnlineResolution(id, label);
                                        if (_playingItem != null)
                                        {
                                            double currentPos = _engine.Position;
                                            if (lstMedia.SelectedItem != _playingItem && CurrentList.Contains(_playingItem))
                                            {
                                                lstMedia.SelectedItem = _playingItem;
                                            }
                                            PlaySelectedMedia();
                                            DispatcherTimer restoreTimer = new DispatcherTimer();
                                            restoreTimer.Interval = TimeSpan.FromMilliseconds(500);
                                            restoreTimer.Tick += (ts, te) =>
                                            {
                                                if (_engine.Duration > 0)
                                                {
                                                    _engine.Seek(currentPos);
                                                    restoreTimer.Stop();
                                                }
                                            };
                                            restoreTimer.Start();
                                        }

                                        UpdateStatus($"⚙ Đã chuyển: {label} (Đang tải lại...)", false);
                                    }
                                };
                                qualitySubItem.Items.Add(item);
                            }
                        }
                        else
                        {
                            qualitySubItem.Items.Add(new MenuFlyoutItem { Text = "⚠️ Không tìm thấy định dạng nào", IsEnabled = false });
                        }
                    });
                });
            }
            else
            {
                qualitySubItem.Items.Add(new MenuFlyoutItem { Text = "File nội bộ (Gốc)", IsEnabled = false });
            }

            menu.Items.Add(qualitySubItem);
            var subSubItem = new MenuFlyoutSubItem { Text = "Phụ đề (Captions)", Icon = new FontIcon { Glyph = "\ue190" } };
            var itemLoadSub = new MenuFlyoutItem { Text = "📂 Tải file phụ đề...", Icon = new FontIcon { Glyph = "\uE8E5" } };
            itemLoadSub.Click += async (s, a) =>
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                picker.FileTypeFilter.Add(".srt"); picker.FileTypeFilter.Add(".ass");
                var file = await picker.PickSingleFileAsync();
                if (file != null) { _engine.LoadExternalSubtitle(file.Path); UpdateStatus($"Đã nạp sub: {file.Name}"); }
            };
            subSubItem.Items.Add(itemLoadSub);
            subSubItem.Items.Add(new MenuFlyoutSeparator());

            var tracks = _engine.GetSubtitleTracks();
            if (tracks.Count > 0)
            {
                foreach (var t in tracks)
                {
                    var itemSub = new RadioMenuFlyoutItem { Text = t.Title, IsChecked = t.IsSelected, GroupName = "SubGroup", Tag = t.Id };
                    itemSub.Click += (s, a) => { if (s is RadioMenuFlyoutItem i && i.Tag is long sid) _engine.SelectSubtitle(sid); };
                    subSubItem.Items.Add(itemSub);
                }
            }
            else subSubItem.Items.Add(new MenuFlyoutItem { Text = "Không có track phụ đề", IsEnabled = false });

            subSubItem.Items.Add(new MenuFlyoutSeparator());
            var itemSubSettings = new MenuFlyoutItem { Text = "Cài đặt Phụ đề nâng cao", Icon = new FontIcon { Glyph = "\uE713" } };
            itemSubSettings.Click += btnSubSettings_Click;
            subSubItem.Items.Add(itemSubSettings);
            menu.Items.Add(subSubItem);
            menu.Items.Add(new MenuFlyoutSeparator());
            if (_isDownloading)
            {
                var stopItem = new MenuFlyoutItem { Text = "⏹ DỪNG TẢI NGAY", Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red) };
                stopItem.Click += (s, a) => { _engine.CancelDownload(); UpdateStatus("✋ Đang dừng tải...", true); };
                menu.Items.Add(stopItem);
            }
            else
            {
                var dlSubItem = new MenuFlyoutSubItem { Text = "Tải xuống / Ghi hình", Icon = new FontIcon { Glyph = "\uE896" } };

                if (_engine.IsOnlineMedia())
                {
                    var audioDl = new MenuFlyoutItem { Text = "🎵 Tải Âm thanh (MP3)", Icon = new FontIcon { Glyph = "\uE189" } };
                    audioDl.Click += (s, a) => HandleDownload("audio_only");
                    dlSubItem.Items.Add(audioDl);

                    var cachedFormats = _engine.GetCurrentCachedFormats();

                    if (cachedFormats != null && cachedFormats.Count > 0)
                    {
                        dlSubItem.Items.Add(new MenuFlyoutSeparator());
                        foreach (var fmt in cachedFormats)
                        {
                            var vidDl = new MenuFlyoutItem { Text = $"🎬 Tải Video MP4 ({fmt.Label})" };
                            vidDl.Click += (s, a) => HandleDownload(fmt.Height.ToString());

                            dlSubItem.Items.Add(vidDl);
                        }
                    }
                }
                else dlSubItem.Items.Add(new MenuFlyoutItem { Text = "Không khả dụng cho file nội bộ", IsEnabled = false });
                menu.Items.Add(dlSubItem);
            }
            if (sender is FrameworkElement senderElement) menu.ShowAt(senderElement);
        }

        private void swMotion_Toggled(object sender, RoutedEventArgs e)
        {
            if (_engine == null) return;

            bool isEnabled = swMotion.IsOn;
            _engine.SetMotionInterpolation(isEnabled);

            UpdateStatus(isEnabled ? "🚀 Đã bật Motion Boost (Làm mượt)." : "🛑 Đã tắt Motion Boost.");
        }
        private async void HandleDownload(string quality)
        {
            if (_engine == null || _playingItem == null) return;
            if (_isDownloading) return;

            string url = _playingItem.FullPath;
            string defaultFileName = $"{_playingItem.FileName}";
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) defaultFileName = defaultFileName.Replace(c, '_');
            string ext = "mp4";
            string label = quality;

            if (quality == "audio_only")
            {
                ext = "mp3";
                label = "Audio";
            }
            else if (_engine.Duration <= 0)
            {
                ext = "ts";
                label = "LiveRec";
            }

            var savePicker = new Windows.Storage.Pickers.FileSavePicker();
            InitializeWithWindow.Initialize(savePicker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            if (ext == "mp3")
                savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary;
            else
                savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.VideosLibrary;

            savePicker.FileTypeChoices.Add(label, new List<string>() { $".{ext}" });
            savePicker.SuggestedFileName = $"{defaultFileName}_{label}";

            var file = await savePicker.PickSaveFileAsync();
            if (file != null)
            {
                string savePath = file.Path;

                _isDownloading = true;
                UpdateStatus($"⏳ Đang chuẩn bị tải {label}...", true);

                _ = _engine.DownloadMediaAsync(url, quality, savePath, (status) =>
                {
                    this.DispatcherQueue.TryEnqueue(() =>
                    {
                        UpdateStatus(status, true);
                        if (status.StartsWith("✅") || status.StartsWith("❌") || status.StartsWith("⏹"))
                        {
                            _isDownloading = false;
                            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                            t.Tick += (s, e) => { t.Stop(); UpdateStatus($"▶ Đang phát: {_playingItem?.FileName}", true); };
                            t.Start();
                        }
                    });
                });
            }
        }

        private void StopComplete()
        {
            if (_reconnectTimer != null) _reconnectTimer.Stop();
            if (_engine != null)
            {

                _engine.Stop();

            }
            btnPlay.Visibility = Visibility.Visible;
            btnPause.Visibility = Visibility.Collapsed;
            timelineSlider.Value = 0;
            txtCurrentTime.Text = "00:00";
            if (!_isUserActionStop && lstMedia.ItemsSource == _listTv && lstMedia.SelectedItem != null)
            {
                UpdateStatus("⚠️ Mất tín hiệu. Thử lại sau 3s...", false, true);
                _reconnectTimer.Start();
            }
            if (_playingItem != null)
            {
                _playingItem.IsPlaying = false;
                _playingItem.IsPaused = false;
                _playingItem = null;
            }
            UpdatePlayingTabIndicator();
            if (CurrentList != null)
            {
                foreach (var item in CurrentList)
                {
                    item.IsPlaying = false;
                    item.IsPaused = false;
                }
            }

            if (cvsSponsor != null) cvsSponsor.Children.Clear();
        }
        private void btnPrev_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentList.Count == 0) return;

            int newIndex = -1;
            int currentIndex = lstMedia.SelectedIndex;
            if (_currentMode == PlayerMode.Shuffle)
            {
                newIndex = _rng.Next(CurrentList.Count);
            }
            else
            {
                if (currentIndex > 0)
                {
                    newIndex = currentIndex - 1;
                }
                else
                {
                    newIndex = CurrentList.Count - 1;
                }
            }

            if (newIndex >= 0)
            {
                lstMedia.SelectedIndex = newIndex;
                lstMedia.ScrollIntoView(lstMedia.SelectedItem);
                PlaySelectedMedia();
            }
            UpdateStatus("⏮ Đang quay lại bài trước...");
        }
        private void swSponsorBlock_Toggled(object sender, RoutedEventArgs e)
        {
            if (_engine == null) return;
            bool isOn = swSponsorBlock.IsOn;
            _engine.IsSponsorBlockEnabled = isOn;
            AppSettings.Save(SETTING_SPONSOR, isOn.ToString());

            UpdateStatus(isOn ? "Đã bật SponsorBlock (Tự động lưu)." : "Đã tắt SponsorBlock.");
        }
        private void btnNext_Click(object sender, RoutedEventArgs e)
        {
            PlayNextVideo(false);
            UpdateStatus("⏭ Đang chuyển bài tiếp theo...");
        }

        private void PlayNextVideo(bool isAuto)
        {
            if (CurrentList == null || CurrentList.Count == 0) return;

            int newIndex = -1;

            int currentIndex = -1;
            if (_playingItem != null)
            {
                currentIndex = CurrentList.IndexOf(_playingItem);
            }

            if (_currentMode == PlayerMode.Shuffle)
            {
                do
                {
                    newIndex = _rng.Next(CurrentList.Count);
                }
                while (CurrentList.Count > 1 && newIndex == currentIndex);
            }
            else
            {
                if (currentIndex < CurrentList.Count - 1)
                {
                    newIndex = currentIndex + 1;
                }

                else
                {
                    if (isAuto && _currentMode == PlayerMode.Off)
                    {
                        StopComplete();
                        return;
                    }
                    newIndex = 0;
                }
            }
            if (newIndex >= 0 && newIndex < CurrentList.Count)
            {
                lstMedia.SelectedIndex = newIndex;
                lstMedia.ScrollIntoView(lstMedia.SelectedItem);
                PlaySelectedMedia();
            }
        }
        private void PlayNextVideo()
        {
            if (CurrentList.Count == 0) return;

            int newIndex = -1;
            int currentIndex = lstMedia.SelectedIndex;

            if (_currentMode == PlayerMode.Shuffle)
            {
                do
                {
                    newIndex = _rng.Next(CurrentList.Count);
                }
                while (CurrentList.Count > 1 && newIndex == currentIndex);
            }
            else
            {
                if (currentIndex < CurrentList.Count - 1)
                {
                    newIndex = currentIndex + 1;
                }
                else
                {
                    if (_currentMode == PlayerMode.RepeatAll || _currentMode == PlayerMode.RepeatOne)
                    {
                        newIndex = 0;
                    }
                    else
                    {
                        return;
                    }
                }
            }
            if (newIndex >= 0)
            {
                lstMedia.SelectedIndex = newIndex;
                lstMedia.ScrollIntoView(lstMedia.SelectedItem);
                PlaySelectedMedia();
            }
        }

        private void UpdateVolumeIcon(double vol)
        {
            if (iconVolume == null) return;
            if (vol <= 0)
            {
                iconVolume.Glyph = "\uE74F";
                iconVolume.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
                if (btnMute != null) ToolTipService.SetToolTip(btnMute, "Bật tiếng");
            }
            else
            {
                iconVolume.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Orange);
                if (btnMute != null) ToolTipService.SetToolTip(btnMute, "Tắt tiếng");
                if (vol < 30)
                {
                    iconVolume.Glyph = "\uE993";
                }
                else if (vol >= 30 && vol <= 70)
                {
                    iconVolume.Glyph = "\uE994";
                }
                else
                {
                    iconVolume.Glyph = "\uE995";
                }
            }
        }
        private void btnModeCycle_Click(object sender, RoutedEventArgs e)
        {
            _currentMode = (PlayerMode)(((int)_currentMode + 1) % 4);


            Application.Current.Resources.TryGetValue("AccentBrush", out object? activeResource);
            SolidColorBrush activeBrush = activeResource as SolidColorBrush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 224, 112, 0));
            SolidColorBrush grayBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray);

            string modeName = "";
            switch (_currentMode)
            {
                case PlayerMode.Shuffle:
                    iconCycle.Glyph = "\uE14B";
                    iconCycle.Foreground = activeBrush;
                    ToolTipService.SetToolTip(btnModeCycle, "Trộn bài: BẬT");
                    modeName = "Trộn bài ngẫu nhiên (Shuffle)";
                    break;

                case PlayerMode.RepeatAll:
                    iconCycle.Glyph = "\uE8EE";
                    iconCycle.Foreground = activeBrush;
                    ToolTipService.SetToolTip(btnModeCycle, "Lặp lại: TẤT CẢ");
                    modeName = "Lặp lại toàn bộ danh sách";
                    break;

                case PlayerMode.RepeatOne:
                    iconCycle.Glyph = "\uE1CC";
                    iconCycle.Foreground = activeBrush;
                    ToolTipService.SetToolTip(btnModeCycle, "Lặp lại: MỘT BÀI");
                    modeName = "Lặp lại một bài duy nhất";
                    break;

                case PlayerMode.Off:
                default:
                    iconCycle.Glyph = "\uF5E7";
                    iconCycle.Foreground = grayBrush;
                    ToolTipService.SetToolTip(btnModeCycle, "Lặp/Trộn: TẮT");
                    modeName = "Phát tuần tự (Không lặp)";
                    break;
            }
            UpdateStatus($"🔀 Đã đổi chế độ: {modeName}");
        }
        private async Task SearchYoutubeAsync(string keyword, int pageIndex)
        {
            string pageToken = "";
            if (pageIndex > 0 && _youtubePageTokens.ContainsKey(pageIndex))
            {
                pageToken = $"&pageToken={_youtubePageTokens[pageIndex]}";
            }
            string url = $"https://www.googleapis.com/youtube/v3/search?part=snippet&maxResults=50&q={Uri.EscapeDataString(keyword)}&type=video&key={KeyManager.GetYoutubeKey()}{pageToken}";

            UpdateStatus($"⏳ YouTube: Đang tải trang {pageIndex + 1}...", true);

            try
            {
                string json = await _httpClient.GetStringAsync(url);
                var root = JsonNode.Parse(json);

                var nextToken = root?["nextPageToken"]?.ToString();
                if (!string.IsNullOrEmpty(nextToken))
                {
                    if (!_youtubePageTokens.ContainsKey(pageIndex + 1))
                        _youtubePageTokens[pageIndex + 1] = nextToken;
                }

                var items = root?["items"]?.AsArray();
                if (items != null && items.Count > 0)
                {
                    foreach (var item in items)
                    {
                        var snippet = item?["snippet"];
                        string vid = item?["id"]?["videoId"]?.ToString();
                        if (!string.IsNullOrEmpty(vid))
                        {
                            string channelTitle = snippet["channelTitle"]?.ToString() ?? "YouTube";
                            _listSearch.Add(new MediaItem
                            {
                                FileName = snippet["title"]?.ToString(),
                                FullPath = $"https://www.youtube.com/watch?v={vid}",
                                Type = "YOUTUBE",
                                ChannelName = snippet["channelTitle"]?.ToString(),
                                Duration = channelTitle,
                                Poster = new BitmapImage(new Uri($"https://img.youtube.com/vi/{vid}/mqdefault.jpg"))
                            });
                        }
                    }

                    bool hasMore = !string.IsNullOrEmpty(nextToken);

                    UpdatePaginationUI(true, pageIndex, hasMore);
                    UpdateStatus($"✅ YouTube: Hiển thị trang {pageIndex + 1} ({items.Count} video).");
                    UpdateListStats();
                }
                else
                {
                    UpdateStatus("⚠️ Không tìm thấy video nào.", false);
                    UpdatePaginationUI(true, pageIndex, false);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus("❌ Lỗi YouTube API: " + ex.Message, false, true);
            }
        }
        private async Task SearchDailymotionAsync(string keyword, int pageIndex)
        {
            int apiPage = pageIndex + 1;
            string apiUrl = $"https://api.dailymotion.com/videos?fields=id,title,thumbnail_240_url,owner.username&search={Uri.EscapeDataString(keyword)}&limit=50&page={apiPage}";

            UpdateStatus($"⏳ Dailymotion: Đang tải trang {apiPage}...", true);

            try
            {
                string json = await _httpClient.GetStringAsync(apiUrl);
                var root = JsonNode.Parse(json);

                bool apiHasMore = root?["has_more"]?.GetValue<bool>() ?? false;
                var list = root?["list"]?.AsArray();

                if (list != null && list.Count > 0)
                {
                    foreach (var item in list)
                    {
                        string vid = item?["id"]?.ToString();
                        if (!string.IsNullOrEmpty(vid))
                        {
                            string ownerName = item?["owner.username"]?.ToString() ?? "Dailymotion";
                            _listSearch.Add(new MediaItem
                            {
                                FileName = item?["title"]?.ToString(),
                                FullPath = $"https://www.dailymotion.com/video/{vid}",
                                Type = "DAILYMOTION",
                                ChannelName = item?["owner.username"]?.ToString(),
                                Duration = ownerName,
                                Poster = new BitmapImage(new Uri(item?["thumbnail_240_url"]?.ToString()))
                            });
                        }
                    }
                    bool hasMore = apiHasMore || list.Count == 50;

                    UpdatePaginationUI(true, pageIndex, hasMore);
                    UpdateStatus($"✅ Dailymotion: Hiển thị trang {apiPage} ({list.Count} video).");
                    UpdateListStats();
                }
                else
                {
                    UpdateStatus("⚠️ Đã hết kết quả.", false);
                    UpdatePaginationUI(true, pageIndex, false);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus("❌ Lỗi Dailymotion: " + ex.Message, false, true);
            }
        }

        private string DetectSourceType(string url)
        {
            if (string.IsNullOrEmpty(url)) return "ONLINE";

            string u = url.ToLower();

            if (u.Contains("youtube.com") || u.Contains("youtu.be")) return "YOUTUBE";
            if (u.Contains("dailymotion.com") || u.Contains("dai.ly")) return "DAILYMOTION";
            if (u.Contains("facebook.com") || u.Contains("fb.watch")) return "FACEBOOK";
            if (u.Contains("tiktok.com")) return "TIKTOK";
            if (u.Contains("vimeo.com")) return "VIMEO";
            if (u.Contains("soundcloud.com")) return "SOUNDCLOUD";
            if (u.Contains("twitch.tv")) return "TWITCH";
            if (u.Contains("Vkvideo.ru")) return "VKVideo";
            if (u.Contains("OK.ru")) return "OK.RU";

            return "ONLINE STREAM";
        }
        private async Task SearchWindowsIndexAsync(string keyword, int pageIndex, int filterMode)
        {
            string normalizedKeyword = keyword.Normalize(System.Text.NormalizationForm.FormC).Replace("'", "''");

            UpdateStatus($"⏳ Đang tìm kiếm '{keyword}'...", true);

            await Task.Run(() =>
            {
                var results = new List<MediaItem>();
                int limit = 50;
                string kindFilter = "";
                switch (filterMode)
                {
                    case 1:
                        kindFilter = "AND System.Kind = 'video'";
                        break;
                    case 2:
                        kindFilter = "AND System.Kind = 'music'";
                        break;
                    case 3:
                        kindFilter = "AND System.Kind = 'picture'";
                        break;
                    default:
                        kindFilter = "AND (System.Kind = 'video' OR System.Kind = 'music' OR System.Kind = 'picture' OR System.Size > 100000)";
                        break;
                }
                string sqlQuery = $@"SELECT TOP {limit} System.ItemName, System.ItemPathDisplay, System.ItemType 
                             FROM SystemIndex 
                             WHERE System.ItemName LIKE '%{normalizedKeyword}%' 
                             {kindFilter}
                             AND System.Size > 0 
                             AND System.FileAttributes <> 2
                             ORDER BY System.DateModified DESC";

                try
                {
                    using (var connection = new System.Data.OleDb.OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';"))
                    {
                        connection.Open();
                        using (var command = new System.Data.OleDb.OleDbCommand(sqlQuery, connection))
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string fileName = reader[0]?.ToString() ?? "Unknown";
                                string fullPath = reader[1]?.ToString() ?? "";
                                string itemType = reader[2]?.ToString() ?? "";
                                if (!string.IsNullOrEmpty(fullPath) && System.IO.File.Exists(fullPath))
                                {
                                    string ext = System.IO.Path.GetExtension(fullPath).ToLower();
                                    if (_allowedExtensions.Contains(ext))
                                    {
                                        results.Add(new MediaItem
                                        {
                                            FileName = fileName,
                                            FullPath = fullPath,
                                            Type = itemType.ToUpper(),
                                            Duration = "Local",
                                            Poster = null
                                        });
                                    }
                                }
                            }
                        }
                    }

                    this.DispatcherQueue.TryEnqueue(() =>
                    {
                        if (results.Count == 0)
                        {
                            UpdateStatus($"⚠️ Không tìm thấy kết quả. (Kiểm tra lại Indexing Options!)", false);
                        }
                        else
                        {
                            foreach (var item in results)
                            {
                                _listSearch.Add(item);
                                _ = Task.Run(async () =>
                                {
                                    var stream = await FastThumbnail.GetImageStreamAsync(item.FullPath);
                                    if (stream != null)
                                    {
                                        this.DispatcherQueue.TryEnqueue(async () =>
                                        {
                                            try
                                            {
                                                var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                                                await bmp.SetSourceAsync(stream.AsRandomAccessStream());
                                                item.Poster = bmp;
                                            }
                                            catch { }
                                        });
                                    }
                                });
                            }

                            UpdateStatus($"✅ Đã tìm thấy {results.Count} file media.");
                            UpdateListStats();
                        }
                    });
                }
                catch (Exception ex)
                {
                    this.DispatcherQueue.TryEnqueue(() => UpdateStatus($"❌ Lỗi SQL: {ex.Message}", false, true));
                }
            });
        }
        private int _currentSourceMode = 0;
        private int _currentPcFilterMode = 0;
        private async void OnSearchSourceClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is string tag)
            {
                txtCurrentSource.Text = item.Text;
                if (item.Icon is FontIcon fontIcon)
                {
                    iconCurrentSource.Glyph = fontIcon.Glyph;
                    iconCurrentSource.Foreground = fontIcon.Foreground;
                }
                switch (tag)
                {
                    case "YOUTUBE":
                        _currentSourceMode = 0;
                        break;
                    case "DAILYMOTION":
                        _currentSourceMode = 1;
                        break;
                    case "PC_ALL":
                        _currentSourceMode = 2;
                        _currentPcFilterMode = 0;
                        txtCurrentSource.Text = "PC (Tất cả)";
                        break;
                    case "PC_VIDEO":
                        _currentSourceMode = 2;
                        _currentPcFilterMode = 1;
                        txtCurrentSource.Text = "PC (Video)";
                        break;
                    case "PC_AUDIO":
                        _currentSourceMode = 2;
                        _currentPcFilterMode = 2;
                        txtCurrentSource.Text = "PC (Nhạc)";
                        break;
                    case "PC_IMAGE":
                        _currentSourceMode = 2;
                        _currentPcFilterMode = 3;
                        txtCurrentSource.Text = "PC (Ảnh)";
                        break;
                }
                if (!string.IsNullOrWhiteSpace(txtSearch.Text))
                {
                    _currentSearchPage = 0;
                    _listSearch.Clear();
                    await TriggerSearchNavigation();
                }
            }
        }
        private async void cboFileType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_currentSourceMode == 2 && !string.IsNullOrWhiteSpace(txtSearch.Text))
            {
                _listSearch.Clear();
                _currentSearchPage = 0;
                _currentSearchQuery = txtSearch.Text;
                await SearchWindowsIndexAsync(_currentSearchQuery, _currentSearchPage, _currentPcFilterMode);
            }
        }
        private string GetExtensionQuery(int typeIndex)
        {

            var videoExts = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".ts" };
            var audioExts = new[] { ".mp3", ".flac", ".wav", ".m4a", ".aac", ".wma", ".ogg" };
            var imageExts = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tiff" };

            List<string> selectedExts = new List<string>();

            switch (typeIndex)
            {
                case 1:
                    selectedExts.AddRange(videoExts);
                    break;
                case 2:
                    selectedExts.AddRange(audioExts);
                    break;
                case 3:
                    selectedExts.AddRange(imageExts);
                    break;
                default:
                    selectedExts.AddRange(videoExts);
                    selectedExts.AddRange(audioExts);
                    selectedExts.AddRange(imageExts);
                    break;
            }
            string clause = "AND (";
            for (int i = 0; i < selectedExts.Count; i++)
            {
                clause += $"System.FileExtension = '{selectedExts[i]}'";
                if (i < selectedExts.Count - 1) clause += " OR ";
            }
            clause += ")";

            return clause;
        }
        private InputSystemCursor? ProtectedCursor
        {
            get
            {
                var targetElement = this.Content as UIElement;
                if (targetElement == null) return null;

                var property = typeof(UIElement).GetProperty("ProtectedCursor", BindingFlags.Instance | BindingFlags.NonPublic);
                return (InputSystemCursor?)property?.GetValue(targetElement);
            }
            set
            {
                var targetElement = this.Content as UIElement;
                if (targetElement == null) return;

                var property = typeof(UIElement).GetProperty("ProtectedCursor", BindingFlags.Instance | BindingFlags.NonPublic);
                property?.SetValue(targetElement, value);
            }
        }

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        private DispatcherTimer _resizeTimer;
        private DispatcherTimer _syncDebounceTimer;
        private DispatcherTimer _resizeDebounceTimer;
        public MainWindow()
        {
            this.InitializeComponent();
            if (!Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
            {
                if (RootGrid.Resources.TryGetValue("SurfaceBrush", out object brush))
                {
                    if (brush is Microsoft.UI.Xaml.Media.SolidColorBrush solidBrush)
                    {
                        solidBrush.Color = Windows.UI.Color.FromArgb(255, 24, 24, 24);
                    }
                }
            }
            timelineSlider.SizeChanged += (s, e) => DrawSponsorMarks();
            InitializeAppWindow();
            ExtendTitleBar();
            CustomizeTitleBarColors();

            lstMedia.ItemsSource = _listLocal;

            this.Activated += MainWindow_Activated;
            this.Closed += MainWindow_Closed;
            this.SizeChanged += MainWindow_SizeChanged;
            _progressTimer = new DispatcherTimer();
            _progressTimer.Interval = TimeSpan.FromMilliseconds(250);
            _progressTimer.Tick += ProgressTimer_Tick;
            _progressTimer.Start();

            UpdateVolumeIcon(sliderVolume.Value);
            InitializeNetworkMonitor();
            _statusTimer = new DispatcherTimer();
            _statusTimer.Interval = TimeSpan.FromSeconds(3);
            _statusTimer.Tick += (s, e) =>
            {
                _statusTimer.Stop();
                UpdateStatus(_persistentStatus, true);
            };
            _resizeTimer = new DispatcherTimer();
            _resizeTimer.Interval = TimeSpan.FromMilliseconds(200);
            _resizeTimer.Tick += (s, e) =>
            {
                _resizeTimer.Stop();
                UpdateMpvLayout();
            };
            RootGrid.PointerMoved += RootGrid_PointerMoved;
            RootGrid.KeyDown += RootGrid_KeyDown;
            _tickerTextColor = "&H00FFFFFF";
            _tickerBgColor = "&H000000FF";
            _debounceTimer = new DispatcherTimer();
            _debounceTimer.Interval = TimeSpan.FromSeconds(2);
            _debounceTimer.Tick += (s, e) =>
            {
                _debounceTimer.Stop();
                string path = txtWatchFolder.Text;
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    UpdateStatus("♻️ Phát hiện thay đổi file. Đang tự động cập nhật...", false);
                    ScanLibraryAsync(path);
                }
            };
            _syncDebounceTimer = new DispatcherTimer();
            _syncDebounceTimer.Interval = TimeSpan.FromMilliseconds(300);
            _syncDebounceTimer.Tick += SyncDebounceTimer_Tick;
            SetupGlobalHotkeys();
            UpdateListStats();
            RootGrid.Loaded += (s, e) =>
            {
                RootGrid.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            };
            _resizeDebounceTimer = new DispatcherTimer();
            _resizeDebounceTimer.Interval = TimeSpan.FromMilliseconds(15);
            _resizeDebounceTimer.Tick += (s, e) =>
            {
                _resizeDebounceTimer.Stop();
                UpdateMpvLayout();
            };
            LoadSystemFonts();
            var monitorCheckTimer = new DispatcherTimer();
            monitorCheckTimer.Interval = TimeSpan.FromSeconds(2);
            monitorCheckTimer.Tick += (s, e) =>
            {
                if (_engine == null) return;

                if (_isLedOn) return;

                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                IntPtr currentMonitor = MediaEngine.MonitorFromWindow(hwnd, 2);

                if (currentMonitor != _lastMonitorHandle)
                {
                    _lastMonitorHandle = currentMonitor;
                    RefreshMonitors();
                    System.Diagnostics.Debug.WriteLine("App đã chuyển màn hình -> Auto Refresh");
                }
            };
            _audioDeviceCheckTimer = new DispatcherTimer();
            _audioDeviceCheckTimer.Interval = TimeSpan.FromSeconds(2); // Kiểm tra mỗi 2 giây
            _audioDeviceCheckTimer.Tick += CheckAudioDeviceChanged;
            _audioDeviceCheckTimer.Start();
            monitorCheckTimer.Start();
            _reconnectTimer = new DispatcherTimer();
            _reconnectTimer.Interval = TimeSpan.FromSeconds(3);
            _reconnectTimer.Tick += (s, e) =>
            {
                _reconnectTimer.Stop();

                if (_playingItem == null && lstMedia.ItemsSource == _listTv && !_isUserActionStop)
                {
                    if (lstMedia.SelectedItem is MediaItem item)
                    {
                        PlaySelectedMedia();
                        UpdateStatus("🔄 Đang thử kết nối lại...", true);
                    }
                }
            };
        }

        private void SyncDebounceTimer_Tick(object sender, object e)
        {
            _syncDebounceTimer.Stop();

            if (_engine == null) return;

            if (sldSubDelay != null) _engine.SetSubtitleDelay(sldSubDelay.Value);
            if (sldAudioDelay != null) _engine.SetAudioDelay(sldAudioDelay.Value);
        }
        private void timelineSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_isInternalUpdate) return;

            if (_engine != null && _engine.Duration > 0)
            {
                double seekTo = (timelineSlider.Value / 100.0) * _engine.Duration;

                _engine.Seek(seekTo, _isUserDragging);

                if (_fsTimeSlider != null)
                {
                    bool oldState = _isInternalUpdate;
                    _isInternalUpdate = true;
                    _fsTimeSlider.Value = timelineSlider.Value;
                    _isInternalUpdate = oldState;
                }
            }
        }

        private void StartWatching(string path)
        {
            if (_folderWatcher != null)
            {
                try
                {
                    _folderWatcher.EnableRaisingEvents = false;
                    _folderWatcher.Dispose();
                }
                catch { }
                finally { _folderWatcher = null; }
            }

            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

            try
            {
                _folderWatcher = new FileSystemWatcher(path);

                _folderWatcher.IncludeSubdirectories = true;
                _folderWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                                              NotifyFilters.LastWrite | NotifyFilters.Size;

                _folderWatcher.Filter = "*.*";
                _folderWatcher.Created += OnFileChanged;
                _folderWatcher.Deleted += OnFileChanged;
                _folderWatcher.Renamed += OnFileChanged;
                _folderWatcher.EnableRaisingEvents = true;

                System.Diagnostics.Debug.WriteLine($"[Watcher] Đã bắt đầu theo dõi: {path}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"⚠️ Lỗi Watcher: {ex.Message}", false, true);
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                _debounceTimer.Stop();
                _debounceTimer.Start();
            });
        }
        private void MoveToContainer(FrameworkElement control, Panel newParent)
        {
            if (control.Parent == newParent) return;
            if (control.Parent is Panel oldParent) oldParent.Children.Remove(control);
            if (control.Parent != null) return;
            if (!newParent.Children.Contains(control))
            {
                try { newParent.Children.Add(control); } catch { }
            }
        }

        private void SafeReparent(FrameworkElement target, Panel newParent)
        {
            if (target.Parent == newParent) return;

            var oldParent = target.Parent;
            if (oldParent != null)
            {
                if (oldParent is Panel p) p.Children.Remove(target);
                else if (oldParent is ContentControl c) c.Content = null;
                else if (oldParent is Border b) b.Child = null;
                else if (oldParent is Popup pop) pop.Child = null;
            }

            if (target.Parent != null) return;
            if (!newParent.Children.Contains(target))
            {
                try { newParent.Children.Add(target); } catch { }
            }
        }
        private Slider? _fullscreenSlider = null;
        private TextBlock? _fullscreenTimeText = null;
        private Slider? _fsTimeSlider = null;
        private Slider? _fsVolSlider = null;
        private Border? _fsPreviewTip = null;
        private TextBlock? _fsPreviewText = null;
        private Border? _fsSponsorTip = null;
        private TextBlock? _fsSponsorText = null;
        private Microsoft.UI.Xaml.Shapes.Rectangle? _fsBufferRect = null;
        private Canvas? _fsSponsorCanvas = null;
        private TextBlock? _fsCurrentTime = null;
        private TextBlock? _fsTotalTime = null;
        private FontIcon? _fsPlayIcon = null;
        private FontIcon? _fsVolIcon = null;
        private FontIcon? _fsRepeatIcon = null;


        private void ToggleInternalFullscreen()
        {
            if (_appWindow == null) return;
            _isInternalFullscreen = !_isInternalFullscreen;
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            IntPtr HWND_NOTOPMOST = new IntPtr(-2);
            uint flags = 0x0002 | 0x0001 | 0x0010 | 0x0020;
            if (_isInternalFullscreen)
            {
                AreaNav.Visibility = Visibility.Collapsed;
                AreaSidebar.Visibility = Visibility.Collapsed;
                AreaControls.Visibility = Visibility.Collapsed;
                if (RowTitleBar != null) RowTitleBar.Height = new GridLength(0);
                _appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
                SetWindowPos(hWnd, HWND_NOTOPMOST, 0, 0, 0, 0, flags);
                this.SystemBackdrop = null;
                RootGrid.Background = new SolidColorBrush(Microsoft.UI.Colors.Black);
                UpdateMpvLayout();
                UpdateStatus("⛶ Fullscreen (Chế độ trình chiếu)", false);
            }
            else
            {
                _appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Default);
                SetWindowPos(hWnd, HWND_NOTOPMOST, 0, 0, 0, 0, flags);
                CloseFsWindow();
                if (btnFullScreen != null) btnFullScreen.IsChecked = false;
                AreaNav.Visibility = Visibility.Visible;
                AreaSidebar.Visibility = Visibility.Visible;
                AreaControls.Visibility = Visibility.Visible;
                AreaNav.Width = _isNavExpanded ? 125 : 50;
                ColSidebar.Width = new GridLength(300);
                if (RowTitleBar != null) RowTitleBar.Height = new GridLength(32);
                if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
                {
                    this.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
                    RootGrid.Background = null;
                }
                else
                {
                    this.SystemBackdrop = null;
                    if (Application.Current.Resources.TryGetValue("SurfaceBrush", out object brush))
                        RootGrid.Background = (SolidColorBrush)brush;
                }
                _fsTimeSlider = null;
                _fsVolSlider = null;
                _fsBufferRect = null;
                _fsSponsorCanvas = null;
                _fsCurrentTime = null;
                _fsTotalTime = null;
                _fsPlayIcon = null;
                this.DispatcherQueue.TryEnqueue(async () =>
                {
                    await Task.Delay(50);
                    UpdateMpvLayout();
                });
            }
        }

        private void AnimateControlsOpacity(double targetOpacity)
        {
            if (AreaControls.Opacity == targetOpacity) return;
            var storyboard = new Storyboard();
            var animation = new DoubleAnimation
            {
                To = targetOpacity,
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(animation, AreaControls);
            Storyboard.SetTargetProperty(animation, "Opacity");
            storyboard.Children.Add(animation);
            storyboard.Begin();
            AreaControls.IsHitTestVisible = (targetOpacity > 0);
        }

        private void RootGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_isInternalFullscreen)
            {
                if (ProtectedCursor == null)
                    ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
            }
        }

        private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!_isInternalFullscreen) return;
            var properties = e.GetCurrentPoint(RootGrid).Properties;
            if (properties.IsLeftButtonPressed || properties.IsRightButtonPressed)
            {
                if (_fsWindow == null)
                {
                    ShowFsWindow();
                }
                else
                {
                    CloseFsWindow();
                }

                e.Handled = true;
            }
        }
        private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
        {

        }

        private void ToggleSubtitleSmart()
        {
            if (_engine == null) return;
            var tracks = _engine.GetSubtitleTracks();
            if (tracks.Count <= 1)
            {
                UpdateStatus("⚠️ Video này không có phụ đề.", false, true);
                return;
            }
            var currentTrack = tracks.FirstOrDefault(t => t.IsSelected);

            if (currentTrack != null && currentTrack.Id > 0)
            {
                _engine.SelectSubtitle(0);
                UpdateStatus("Đã tắt phụ đề.", false);
            }
            else
            {
                var firstSub = tracks.FirstOrDefault(t => t.Id > 0);
                if (firstSub != null)
                {
                    _engine.SelectSubtitle(firstSub.Id);
                    UpdateStatus($"Đã bật phụ đề: {firstSub.Title}", false);
                }
            }
        }
        private bool IsInputActive()
        {
            var focusObj = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(this.Content.XamlRoot) as FrameworkElement;

            if (focusObj is TextBox ||
                focusObj is PasswordBox ||
                focusObj is RichEditBox ||
                focusObj is AutoSuggestBox ||
                focusObj is ComboBox)
            {
                return true;
            }
            return false;
        }

        private void SetupGlobalHotkeys()
        {
            var accAltEnter = new Microsoft.UI.Xaml.Input.KeyboardAccelerator
            {
                Key = Windows.System.VirtualKey.Enter,
                Modifiers = Windows.System.VirtualKeyModifiers.Menu
            };
            accAltEnter.Invoked += (s, e) =>
            {
                ToggleInternalFullscreen();
                e.Handled = true;
            };
            RootGrid.KeyboardAccelerators.Add(accAltEnter);
            var accSpace = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.Space };
            accSpace.Invoked += (s, e) =>
            {
                if (IsInputActive()) return;

                if (btnPlay.Visibility == Visibility.Visible) btnPlay_Click(null, null);
                else btnPause_Click(null, null);

                e.Handled = true;
            };
            RootGrid.KeyboardAccelerators.Add(accSpace);
            var accStop = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.S };
            accStop.Invoked += (s, e) =>
            {
                if (IsInputActive()) return;
                btnStop_Click(null, null);
                e.Handled = true;
            };
            RootGrid.KeyboardAccelerators.Add(accStop);

            var accF11 = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.F11 };
            accF11.Invoked += (s, e) =>
            {
                if (!_isPlayerMode)
                {
                    btnToggleLed_Click(null, null);
                    e.Handled = true;
                }
            };
            RootGrid.KeyboardAccelerators.Add(accF11);
            var accEsc = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape };
            accEsc.Invoked += (s, e) =>
            {
                if (_isInternalFullscreen)
                {
                    ToggleInternalFullscreen();
                    if (btnFullScreen != null) btnFullScreen.IsChecked = false;
                    e.Handled = true;
                }
            };
            RootGrid.KeyboardAccelerators.Add(accEsc);

            var accH = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.H };
            accH.Invoked += (s, e) =>
            {
                if (_isInternalFullscreen)
                {
                    if (FullscreenPopup.IsOpen) CloseFsWindow();
                    else ShowFsWindow();
                }
                else if (_isPlayerMode)
                {
                    bool isHidden = AreaControls.Visibility == Visibility.Collapsed;
                    AreaControls.Visibility = isHidden ? Visibility.Visible : Visibility.Collapsed;
                    if (isHidden && ProtectedCursor == null)
                        ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
                    UpdateStatus(isHidden ? "Đã hiện thanh điều khiển" : "Đã ẩn thanh điều khiển", false);
                }
                e.Handled = true;
            };
            RootGrid.KeyboardAccelerators.Add(accH);
            var accSub = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.P };
            accSub.Invoked += (s, e) =>
            {
                if (IsInputActive()) return;
                if (_engine != null)
                {
                    _engine.SelectSubtitle(0);
                    UpdateStatus("Đã tắt phụ đề (Subtitle Off).", false);
                }
                e.Handled = true;
            };
            RootGrid.KeyboardAccelerators.Add(accSub);
            var accMute = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.M };
            accMute.Invoked += (s, e) =>
            {
                if (IsInputActive()) return;
                btnMute_Click(null, null);
                e.Handled = true;
            };
            RootGrid.KeyboardAccelerators.Add(accMute);
            var accRight = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.Right };
            accRight.Invoked += (s, e) =>
            {
                if (IsInputActive()) return;
                btnForward_Click(null, null);
                e.Handled = true;
            };
            RootGrid.KeyboardAccelerators.Add(accRight);

            var accLeft = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.Left };
            accLeft.Invoked += (s, e) =>
            {
                if (IsInputActive()) return;
                btnRewind_Click(null, null);
                e.Handled = true;
            };
            RootGrid.KeyboardAccelerators.Add(accLeft);
            var accNext = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.Right, Modifiers = Windows.System.VirtualKeyModifiers.Menu };
            accNext.Invoked += (s, e) => { btnNext_Click(null, null); e.Handled = true; };
            RootGrid.KeyboardAccelerators.Add(accNext);
            var accPrev = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.Left, Modifiers = Windows.System.VirtualKeyModifiers.Menu };
            accPrev.Invoked += (s, e) => { btnPrev_Click(null, null); e.Handled = true; };
            RootGrid.KeyboardAccelerators.Add(accPrev);
            var accVolUp = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.Up };
            accVolUp.Invoked += (s, e) =>
            {
                if (IsInputActive()) return;
                sliderVolume.Value += 5;
                e.Handled = true;
            };
            RootGrid.KeyboardAccelerators.Add(accVolUp);

            var accVolDown = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.Down };
            accVolDown.Invoked += (s, e) =>
            {
                if (IsInputActive()) return;
                sliderVolume.Value -= 5;
                e.Handled = true;
            };
            RootGrid.KeyboardAccelerators.Add(accVolDown);
        }

        private void sldSubDelay_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (lblSubDelay != null)
            {
                lblSubDelay.Text = $"{e.NewValue:+0.0;-0.0;0.0}s";
            }

            _syncDebounceTimer.Stop();
            _syncDebounceTimer.Start();
        }

        private void btnResetSubDelay_Click(object sender, RoutedEventArgs e)
        {
            if (sldSubDelay != null) sldSubDelay.Value = 0;
        }
        private void sldAudioDelay_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (lblAudioDelay != null)
            {
                lblAudioDelay.Text = $"{e.NewValue:+0.0;-0.0;0.0}s";
            }

            _syncDebounceTimer.Stop();
            _syncDebounceTimer.Start();
        }

        private void btnResetAudioDelay_Click(object sender, RoutedEventArgs e)
        {
            if (sldAudioDelay != null) sldAudioDelay.Value = 0;
        }

        private RadioButton _lastMediaTab = null;
        private void OnNavTabClick(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Content != null)
            {
                string tabName = rb.Content.ToString().ToUpper();
                if (lstMedia != null) lstMedia.Visibility = Visibility.Visible;
                if (grpSearchBar != null) grpSearchBar.Visibility = Visibility.Visible;
                if (grpBottomActions != null) grpBottomActions.Visibility = Visibility.Visible;
                if (grpSettings != null) grpSettings.Visibility = Visibility.Collapsed;
                if (grpEffects != null) grpEffects.Visibility = Visibility.Collapsed;
                if (grpInfo != null) grpInfo.Visibility = Visibility.Collapsed;
                if (btnSearchSource != null) btnSearchSource.Visibility = Visibility.Collapsed;
                if (grpSearchPagination != null) grpSearchPagination.Visibility = Visibility.Collapsed;
                if (pnlPreview != null) pnlPreview.Visibility = Visibility.Visible;
                if (DesignSurface != null) DesignSurface.Visibility = Visibility.Collapsed;
                if (_engine != null) _engine.TogglePreview(true);

                switch (tabName)
                {
                    case "LIBRARY":
                        txtSidebarHeader.Text = "THƯ VIỆN (WATCH FOLDER)";
                        lstMedia.ItemsSource = _listLibrary;
                        _lastMediaTab = rb;
                        grpBottomActions.Visibility = Visibility.Collapsed;
                        UpdateStatus($"Đang xem: Thư mục giám sát ({_listLibrary.Count} file)");
                        break;

                    case "LOCAL":
                        txtSidebarHeader.Text = "DANH SÁCH FILES NỘI BỘ";
                        lstMedia.ItemsSource = _listLocal;
                        _lastMediaTab = rb;
                        break;

                    case "ONLINE":
                        txtSidebarHeader.Text = "DANH SÁCH PHÁT LINKS ONLINE";
                        lstMedia.ItemsSource = _listStream;
                        _lastMediaTab = rb;
                        break;

                    case "IPTV":
                        txtSidebarHeader.Text = "DANH SÁCH KÊNH TRUYỀN HÌNH";
                        lstMedia.ItemsSource = _listTv;
                        _lastMediaTab = rb;
                        break;

                    case "SEARCH":
                        txtSidebarHeader.Text = "TÌM KIẾM TRÊN INTERNET";
                        lstMedia.ItemsSource = _listSearch;
                        btnSearchSource.Visibility = Visibility.Visible;
                        if (_listSearch.Count > 0 && _currentSourceMode >= 3)
                            grpSearchPagination.Visibility = Visibility.Visible;
                        break;

                    case "SETTING":
                        txtSidebarHeader.Text = "CẤU HÌNH HỆ THỐNG";
                        lstMedia.Visibility = Visibility.Collapsed;
                        grpSearchBar.Visibility = Visibility.Collapsed;
                        grpBottomActions.Visibility = Visibility.Collapsed;
                        grpSettings.Visibility = Visibility.Visible;
                        break;

                    case "EFFECTS":
                        txtSidebarHeader.Text = "THIẾT KẾ HIỆU ỨNG (CG)";
                        lstMedia.Visibility = Visibility.Collapsed;
                        grpSearchBar.Visibility = Visibility.Collapsed;
                        grpBottomActions.Visibility = Visibility.Collapsed;

                        grpEffects.Visibility = Visibility.Visible;

                        if (_engine != null) _engine.TogglePreview(false);
                        pnlPreview.Visibility = Visibility.Collapsed;
                        DesignSurface.Visibility = Visibility.Visible;
                        break;
                    case "ABOUTS":
                        txtSidebarHeader.Text = "THÔNG TIN PHẦN MỀM";
                        lstMedia.Visibility = Visibility.Collapsed;
                        grpSearchBar.Visibility = Visibility.Collapsed;
                        grpBottomActions.Visibility = Visibility.Collapsed;
                        grpSettings.Visibility = Visibility.Collapsed;
                        grpEffects.Visibility = Visibility.Collapsed;
                        grpInfo.Visibility = Visibility.Visible;
                        break;
                }
                if (tabName == "LOCAL") UpdateStatus("Đang xem: Danh sách File nội bộ");
                else if (tabName == "ONLINE") UpdateStatus("Đang xem: Danh sách Online");
                else if (tabName == "IPTV") UpdateStatus("Đang xem: Kênh truyền hình IPTV");
                if (tabName == "ABOUTS") UpdateStatus("Đang xem: Thông tin phần mềm");
                else UpdateStatus($"Đang chuyển sang chế độ: {tabName}");
                if (txtSearch != null && btnSearchSource.Visibility != Visibility.Visible)
                {
                    txtSearch.Text = "";
                }
                UpdateListStats();
                if (_playingItem != null && CurrentList.Contains(_playingItem))
                {
                    this.DispatcherQueue.TryEnqueue(() =>
                    {
                        lstMedia.SelectedItem = _playingItem;
                        lstMedia.ScrollIntoView(_playingItem);
                    });
                }
            }
        }

        private void LoadWatchFolder()
        {
            string path = AppSettings.Get(SETTING_WATCH_FOLDER) ?? "";

            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                txtWatchFolder.Text = path;

                ScanLibraryAsync(path);

                StartWatching(path);
            }
        }
        private async void btnSelectWatchFolder_Click(object sender, RoutedEventArgs e)
        {
            var folderPicker = new FolderPicker();
            InitializeWithWindow.Initialize(folderPicker, WindowNative.GetWindowHandle(this));
            folderPicker.FileTypeFilter.Add("*");

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                string path = folder.Path;
                txtWatchFolder.Text = path;
                AppSettings.Save(SETTING_WATCH_FOLDER, path);
                ScanLibraryAsync(path);

                StartWatching(path);
            }
        }

        private void btnRefreshLibrary_Click(object sender, RoutedEventArgs e)
        {
            string path = txtWatchFolder.Text;
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                ScanLibraryAsync(path);
            }
            else
            {
                UpdateStatus("⚠ Chưa chọn thư mục hoặc thư mục không tồn tại.", false, true);
            }
        }
        private async void ScanLibraryAsync(string folderPath)
        {
            txtLibraryStatus.Text = "Trạng thái: Đang quét file...";
            UpdateStatus("⏳ Đang cập nhật thư viện từ: " + folderPath);

            await Task.Run(() =>
            {
                try
                {
                    var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm",
                        ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".wma", ".aac"
                    };
                    var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
                                         .Where(f => extensions.Contains(Path.GetExtension(f)));

                    var newItems = new List<MediaItem>();

                    foreach (var file in files)
                    {
                        newItems.Add(new MediaItem
                        {
                            FileName = Path.GetFileName(file),
                            FullPath = file,
                            Type = "LIBRARY",
                            Poster = null
                        });
                    }
                    this.DispatcherQueue.TryEnqueue(() =>
                    {
                        _listLibrary.Clear();
                        _backupLibrary.Clear();

                        foreach (var item in newItems)
                        {
                            _listLibrary.Add(item);
                            _backupLibrary.Add(item);
                            _ = Task.Run(async () =>
                            {

                                var stream = await FastThumbnail.GetImageStreamAsync(item.FullPath);

                                if (stream != null)
                                {
                                    this.DispatcherQueue.TryEnqueue(async () =>
                                    {
                                        try
                                        {
                                            var bmp = new BitmapImage();
                                            await bmp.SetSourceAsync(stream.AsRandomAccessStream());
                                            item.Poster = bmp;
                                        }
                                        catch { }
                                    });
                                }
                            });
                        }

                        txtLibraryStatus.Text = $"Trạng thái: Đã xong ({newItems.Count} file).";
                        UpdateStatus($"✅ Đã cập nhật Thư viện: {newItems.Count} file.");
                        UpdateListStats();
                    });
                }
                catch (Exception ex)
                {
                    this.DispatcherQueue.TryEnqueue(() =>
                    {
                        txtLibraryStatus.Text = "Lỗi: " + ex.Message;
                        UpdateStatus("❌ Lỗi quét thư viện.", false, true);
                    });
                }
            });
        }
        private string _tickerTextColor = "#FFFFFFFF";
        private string _tickerBgColor = "#FFFF0000";
        private void btnTextColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null)
            {
                _tickerTextColor = btn.Tag.ToString();
                if (txtTickerPreview != null) txtTickerPreview.Foreground = btn.Background;
            }
        }

        private void btnBgColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null)
            {
                _tickerBgColor = btn.Tag.ToString();

                if (DraggableText != null)
                {
                    foreach (var item in DraggableText.Children)
                    {
                        if (item is Border b)
                        {
                            b.Background = btn.Background;
                            break;
                        }
                    }
                }
            }
        }

        private async void btnApplyLogo_Click(object sender, RoutedEventArgs e)
        {
            await ApplyLogoLogic();
        }

        private async void btnApplyTicker_Click(object sender, RoutedEventArgs e)
        {
            await ApplyTickerLogic();
        }
        private void cboSysFonts_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private double _lastPreviewTime = -1;
        private bool _isFetchingPreview = false;

        private void timelineSlider_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_engine == null || _engine.Duration <= 0) return;
            if (popPreview.XamlRoot == null)
            {
                if (this.Content != null && this.Content.XamlRoot != null)
                {
                    popPreview.XamlRoot = this.Content.XamlRoot;
                }
            }

            popPreview.IsOpen = true;
        }
        private void timelineSlider_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_engine == null || _engine.Duration <= 0) return;

            var slider = sender as Slider;
            var point = e.GetCurrentPoint(slider);
            double mouseX = point.Position.X;
            double sliderWidth = slider.ActualWidth;
            double percent = mouseX / sliderWidth;
            double hoverTime = percent * _engine.Duration;
            if (hoverTime < 0) hoverTime = 0;
            if (hoverTime > _engine.Duration) hoverTime = _engine.Duration;
            txtSeekPreviewTime.Text = TimeSpan.FromSeconds(hoverTime).ToString(@"mm\:ss");
            popPreview.HorizontalOffset = mouseX - (grpPreviewBox.Width / 2);
            if (!popPreview.IsOpen) popPreview.IsOpen = true;
            var matchingSegments = new System.Collections.Generic.List<dynamic>();
            var segments = _engine.GetCurrentSponsors();

            foreach (var seg in segments)
            {
                double startRatio = seg.Segment[0] / _engine.Duration;
                double endRatio = seg.Segment[1] / _engine.Duration;
                double startPixel = startRatio * sliderWidth;
                double endPixel = endRatio * sliderWidth;
                double width = endPixel - startPixel;
                if (width < 6)
                {
                    double center = (startPixel + endPixel) / 2;
                    startPixel = center - 3;
                    endPixel = center + 3;
                }
                if (mouseX >= startPixel && mouseX <= endPixel)
                {
                    matchingSegments.Add(seg);
                }
            }
            if (matchingSegments.Count > 0)
            {
                var bestMatch = matchingSegments.OrderByDescending(s => GetCategoryPriority(s.Category)).First();

                string category = bestMatch.Category;
                string sponsorName = "";
                Windows.UI.Color sponsorColor = Microsoft.UI.Colors.Gray;

                switch (category.ToLower())
                {
                    case "sponsor":
                        sponsorName = "Tài trợ (Sponsor)";
                        sponsorColor = Microsoft.UI.Colors.LimeGreen;
                        break;
                    case "intro":
                        sponsorName = "Đoạn mở đầu (Intro)";
                        sponsorColor = Microsoft.UI.Colors.MediumPurple;
                        break;
                    case "outro":
                        sponsorName = "Đoạn kết (Outro)";
                        sponsorColor = Microsoft.UI.Colors.Red;
                        break;
                    case "selfpromo":
                        sponsorName = "Tự quảng cáo (Self-promo)";
                        sponsorColor = Microsoft.UI.Colors.DeepPink;
                        break;
                    case "interaction":
                        sponsorName = "Kêu gọi tương tác";
                        sponsorColor = Microsoft.UI.Colors.Cyan;
                        break;
                    case "preview":
                        sponsorName = "Xem trước tập sau (Preview)";
                        sponsorColor = Microsoft.UI.Colors.DodgerBlue;
                        break;
                    case "music_offtopic":
                        sponsorName = "Không phải nhạc (Offtopic)";
                        sponsorColor = Microsoft.UI.Colors.Pink;
                        break;
                    case "filler":
                        sponsorName = "Nội dung đệm (Filler)";
                        sponsorColor = Microsoft.UI.Colors.Yellow;
                        break;
                    default:
                        sponsorName = category;
                        sponsorColor = Microsoft.UI.Colors.Gray;
                        break;
                }
                txtSponsorPreview.Text = sponsorName;
                if (popSponsor.Child is Border b)
                {
                    b.Background = new SolidColorBrush(sponsorColor);
                    if (category.ToLower() == "filler" ||
                        category.ToLower() == "interaction" ||
                        category.ToLower() == "music_offtopic" ||
                        category.ToLower() == "sponsor")
                    {
                        txtSponsorPreview.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black);
                    }
                    else
                    {
                        txtSponsorPreview.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
                    }
                }

                popSponsor.HorizontalOffset = mouseX - 40;
                if (!popSponsor.IsOpen) popSponsor.IsOpen = true;
            }
            else
            {
                popSponsor.IsOpen = false;
            }
        }
        private int GetCategoryPriority(string category)
        {
            switch (category.ToLower())
            {
                case "sponsor": return 100;
                case "selfpromo": return 90;
                case "interaction": return 80;

                case "intro": return 60;
                case "outro": return 60;
                case "preview": return 60;

                case "filler": return 20;
                case "music_offtopic": return 10;

                default: return 0;
            }
        }

        private void timelineSlider_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            popPreview.IsOpen = false;
            popSponsor.IsOpen = false;
        }
        private string ColorToAssHex(Windows.UI.Color c)
        {

            return $"&H{00:X2}{c.B:X2}{c.G:X2}{c.R:X2}";
        }
        private void cpTextColor_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
        {
            rectTextPreview.Background = new SolidColorBrush(args.NewColor);
            _tickerTextColor = $"#{args.NewColor.A:X2}{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}";

            if (txtTickerPreview != null)
                txtTickerPreview.Foreground = new SolidColorBrush(args.NewColor);
        }

        private void cpBgColor_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
        {
            rectBgPreview.Background = new SolidColorBrush(args.NewColor);
            _tickerBgColor = $"#{args.NewColor.A:X2}{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}";

            if (borderTickerBg != null)
                borderTickerBg.Background = new SolidColorBrush(args.NewColor);
        }
        private void UpdateTickerDesignPreview()
        {
            if (txtTickerPreview == null || borderTickerBg == null) return;

            try
            {
                if (cboTickerFont.SelectedItem != null)
                {
                    string fontName = cboTickerFont.SelectedItem.ToString();
                    txtTickerPreview.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(fontName);
                }

                if (nbFontSize != null)
                {
                    double size = nbFontSize.Value;
                    if (size > 0) txtTickerPreview.FontSize = size;
                }
                if (btnBold != null)
                {
                    txtTickerPreview.FontWeight = (btnBold.IsChecked == true) ? FontWeights.Bold : FontWeights.Normal;
                }
                if (btnItalic != null)
                {
                    txtTickerPreview.FontStyle = (btnItalic.IsChecked == true) ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal;
                }

                if (cpTextColor != null)
                {
                    txtTickerPreview.Foreground = new SolidColorBrush(cpTextColor.Color);
                }

                if (cpBgColor != null && swTickerBg != null)
                {
                    if (swTickerBg.IsOn)
                    {
                        borderTickerBg.Background = new SolidColorBrush(cpBgColor.Color);
                    }
                    else
                    {
                        borderTickerBg.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    }
                }
            }
            catch { }
            if (sldTickerBgSize != null && borderTickerBg != null)
            {
                borderTickerBg.Padding = new Thickness(sldTickerBgSize.Value);
            }
            if (chkFullWidth != null && borderTickerBg != null)
            {
                if (chkFullWidth.IsChecked == true)
                {
                    borderTickerBg.HorizontalAlignment = HorizontalAlignment.Stretch;
                }
                else
                {
                    borderTickerBg.HorizontalAlignment = HorizontalAlignment.Center;
                }
            }
        }

        private void OnTickerSettingChanged(object sender, RoutedEventArgs e)
        {
            UpdateTickerDesignPreview();
        }

        private void OnTickerSettingChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            UpdateTickerDesignPreview();
        }

        private void OnTickerSettingChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateTickerDesignPreview();
        }
        private string ColorToHex(Windows.UI.Color color)
        {
            return $"{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        private void sliderSeekSetting_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            _seekStep = e.NewValue;
            if (txtSeekValue != null)
            {
                txtSeekValue.Text = $"{_seekStep}s";
            }
            if (btnRewind != null)
                ToolTipService.SetToolTip(btnRewind, $"Lùi {_seekStep} giây");

            if (btnForward != null)
                ToolTipService.SetToolTip(btnForward, $"Tiến {_seekStep} giây");
        }

        private void sliderSpeedSetting_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_engine != null)
            {
                _engine.SetSpeed(e.NewValue);
            }

            if (txtSpeedValue != null)
            {
                txtSpeedValue.Text = $"{e.NewValue:F2}x";
            }
            UpdateStatus($"⚡ Tốc độ phát: {e.NewValue:F2}x", false);
        }
        private void btnResetSpeed_Click(object sender, RoutedEventArgs e)
        {
            sliderSpeedSetting.Value = 1.0;
            UpdateStatus("⚡ Đã đặt lại tốc độ chuẩn (1.0x)", false);
        }
        private void btnSpeedPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null)
            {
                if (double.TryParse(btn.Tag.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double speed))
                {
                    sliderSpeedSetting.Value = speed;
                    UpdateStatus($"⚡ Đã chuyển tốc độ: {speed}x", false);
                }
            }
        }
        private void sliderVolume_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var delta = e.GetCurrentPoint(sliderVolume).Properties.MouseWheelDelta;

            double step = 5;
            if (delta > 0)
            {
                sliderVolume.Value += step;
            }
            else
            {
                sliderVolume.Value -= step;
            }

            e.Handled = true;
        }
        private Grid CreateFullscreenControls()
        {
            var accentBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 140, 0));
            var bgBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(180, 20, 20, 20));
            var textGray = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
            var textWhite = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.WhiteSmoke);
            Grid wrapper = new Grid
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                IsHitTestVisible = true
            };
            wrapper.PointerPressed += (s, e) => CloseFsWindow();
            Grid container = new Grid
            {
                Background = bgBrush,
                BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 51, 51)),
                BorderThickness = new Microsoft.UI.Xaml.Thickness(0, 1, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = 120,
                RenderTransform = new TranslateTransform { Y = 120 }
            };
            container.PointerPressed += (s, e) => e.Handled = true;
            container.Loaded += (s, e) =>
            {
                var transform = container.RenderTransform as TranslateTransform;
                if (transform == null) return;
                var sb = new Storyboard();
                var anim = new DoubleAnimation { From = 120, To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(350)), EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut } };
                Storyboard.SetTarget(anim, transform);
                Storyboard.SetTargetProperty(anim, "Y");
                sb.Children.Add(anim);
                sb.Begin();
            };
            container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            container.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid rowTime = new Grid { Margin = new Microsoft.UI.Xaml.Thickness(20, 10, 20, 0) };
            rowTime.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            rowTime.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowTime.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

            _fsCurrentTime = new TextBlock { Text = txtCurrentTime.Text, Foreground = textGray, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            _fsTotalTime = new TextBlock { Text = txtTotalTime.Text, Foreground = textGray, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };

            Grid sliderContainer = new Grid { VerticalAlignment = VerticalAlignment.Center };
            _fsBufferRect = new Microsoft.UI.Xaml.Shapes.Rectangle { Height = 4, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 192, 192, 192)), IsHitTestVisible = false };
            _fsSponsorCanvas = new Canvas { VerticalAlignment = VerticalAlignment.Center, Height = 4, Margin = new Microsoft.UI.Xaml.Thickness(0, 24, 0, 0), IsHitTestVisible = false };
            _fsTimeSlider = new Slider { Minimum = 0, Maximum = 100, Value = timelineSlider.Value, VerticalAlignment = VerticalAlignment.Center, Foreground = accentBrush, IsThumbToolTipEnabled = false };
            _fsTimeSlider.PointerMoved += OnFsSliderPointerMoved;
            _fsTimeSlider.PointerExited += OnFsSliderPointerExited;
            _fsTimeSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnSliderDragStart), true);
            _fsTimeSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnSliderDragEnd), true);
            _fsTimeSlider.ValueChanged += (s, e) => { if (!_isInternalUpdate) { timelineSlider.Value = e.NewValue; if (_engine != null && _engine.Duration > 0) _engine.Seek((e.NewValue / 100.0) * _engine.Duration); } };
            _fsTimeSlider.SizeChanged += (s, e) => { DrawSponsorMarks(); UpdateFullscreenBufferWidth(); };
            _fsTimeSlider.Resources["SliderTrackValueFillPointerOver"] = accentBrush;
            _fsTimeSlider.Resources["SliderTrackValueFillPressed"] = accentBrush;
            _fsTimeSlider.Resources["SliderThumbBackgroundPointerOver"] = accentBrush;
            _fsTimeSlider.Resources["SliderThumbBackgroundPressed"] = accentBrush;
            _fsTimeSlider.Resources["SliderTrackFillPointerOver"] = new SolidColorBrush(Windows.UI.Color.FromArgb(64, 255, 255, 255));
            _fsTimeSlider.Resources["SliderTrackFillPressed"] = new SolidColorBrush(Windows.UI.Color.FromArgb(64, 255, 255, 255));

            sliderContainer.Children.Add(_fsBufferRect);
            sliderContainer.Children.Add(_fsTimeSlider);
            sliderContainer.Children.Add(_fsSponsorCanvas);

            Grid.SetColumn(_fsCurrentTime, 0);
            Grid.SetColumn(sliderContainer, 1);
            Grid.SetColumn(_fsTotalTime, 2);
            rowTime.Children.Add(_fsCurrentTime);
            rowTime.Children.Add(sliderContainer);
            rowTime.Children.Add(_fsTotalTime);
            Grid.SetRow(rowTime, 0); container.Children.Add(rowTime);
            Grid rowControls = new Grid { Margin = new Microsoft.UI.Xaml.Thickness(20, 0, 20, 10) };
            rowControls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            rowControls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowControls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            Button CreateBtn(string glyph, double size, string tooltip, RoutedEventHandler onClick, SolidColorBrush color = null)
            {
                Button b = new Button { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Microsoft.UI.Xaml.Thickness(0), Width = 40, Height = 40, Padding = new Microsoft.UI.Xaml.Thickness(0), CornerRadius = new CornerRadius(4), IsTabStop = false };
                b.Resources["ButtonBackgroundPointerOver"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                b.Resources["ButtonBackgroundPressed"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                ToolTipService.SetToolTip(b, tooltip);
                b.Content = new FontIcon { Glyph = glyph, FontSize = size, Foreground = color ?? textWhite };
                b.Click += onClick;
                return b;
            }
            StackPanel pLeft = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            pLeft.Children.Add(CreateBtn("\uE92C", 18, "Thoát toàn màn hình", (s, e) => ToggleInternalFullscreen()));
            pLeft.Children.Add(CreateBtn("\uE9E9", 16, "Cài đặt", (s, e) => btnQuickSettings_Click(s, e)));
            Grid.SetColumn(pLeft, 0); rowControls.Children.Add(pLeft);
            StackPanel pCenter = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            pCenter.Children.Add(CreateBtn("\uE25E", 16, "Dừng hẳn", (s, e) => btnStop_Click(s, e)));
            pCenter.Children.Add(CreateBtn("\uE622", 16, "Bài trước", (s, e) => btnPrev_Click(s, e)));
            pCenter.Children.Add(CreateBtn("\uE627", 18, "Lùi 5s", (s, e) => btnRewind_Click(s, e)));
            Button btnPlayBig = new Button { Width = 64, Height = 64, CornerRadius = new CornerRadius(32), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Microsoft.UI.Xaml.Thickness(0), Padding = new Microsoft.UI.Xaml.Thickness(0) };
            btnPlayBig.Resources["ButtonBackgroundPointerOver"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            btnPlayBig.Resources["ButtonBackgroundPressed"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            string playIconGlyph = (btnPlay.Visibility == Visibility.Visible) ? "\uF5B0" : "\uF8AE";
            _fsPlayIcon = new FontIcon { Glyph = playIconGlyph, FontSize = 40, Foreground = accentBrush };
            btnPlayBig.Content = _fsPlayIcon;
            btnPlayBig.Click += (s, e) => { if (btnPlay.Visibility == Visibility.Visible) btnPlay_Click(s, e); else btnPause_Click(s, e); };
            pCenter.Children.Add(btnPlayBig);
            pCenter.Children.Add(CreateBtn("\uE628", 18, "Tiến 5s", (s, e) => btnForward_Click(s, e)));
            pCenter.Children.Add(CreateBtn("\uE623", 16, "Bài sau", (s, e) => btnNext_Click(s, e)));
            Button btnModeFs = CreateBtn(iconCycle.Glyph, 16, "Chế độ lặp", (s, e) => btnModeCycle_Click(s, e));
            _fsRepeatIcon = (FontIcon)btnModeFs.Content;
            _fsRepeatIcon.Glyph = iconCycle.Glyph;
            _fsRepeatIcon.Foreground = iconCycle.Foreground;
            pCenter.Children.Add(btnModeFs);
            Grid.SetColumn(pCenter, 1); rowControls.Children.Add(pCenter);
            StackPanel pRight = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            Button btnMuteFs = CreateBtn(iconVolume.Glyph, 20, "Tắt tiếng", (s, e) => btnMute_Click(s, e));
            _fsVolIcon = (FontIcon)btnMuteFs.Content; pRight.Children.Add(btnMuteFs);
            _fsVolSlider = new Slider { Width = 100, Minimum = 0, Maximum = 100, Value = sliderVolume.Value, VerticalAlignment = VerticalAlignment.Center, Foreground = accentBrush, IsThumbToolTipEnabled = true };
            _fsVolSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnSliderDragStart), true);
            _fsVolSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnSliderDragEnd), true);
            _fsVolSlider.ValueChanged += (s, e) => { if (!_isInternalUpdate && Math.Abs(sliderVolume.Value - e.NewValue) > 1) sliderVolume.Value = e.NewValue; };
            pRight.Children.Add(_fsVolSlider);
            Grid.SetColumn(pRight, 2); rowControls.Children.Add(pRight);

            Grid.SetRow(rowControls, 1);
            container.Children.Add(rowControls);
            _fsPreviewText = new TextBlock { Text = "00:00", Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), FontSize = 11, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _fsPreviewTip = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 240, 165, 0)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 4, 6, 4),
                Child = _fsPreviewText,
                Visibility = Visibility.Collapsed,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 130),
                IsHitTestVisible = false
            };
            _fsSponsorText = new TextBlock { Text = "Sponsor", Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), FontSize = 12, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _fsSponsorTip = new Border
            {
                Background = new SolidColorBrush(Microsoft.UI.Colors.LimeGreen),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Child = _fsSponsorText,
                Visibility = Visibility.Collapsed,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 160),
                IsHitTestVisible = false
            };
            wrapper.Children.Add(container);
            wrapper.Children.Add(_fsPreviewTip);
            wrapper.Children.Add(_fsSponsorTip);

            return wrapper;
        }
        private T FindChildElement<T>(DependencyObject parent, string childName) where T : FrameworkElement
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T fe && fe.Name == childName)
                    return fe;

                var result = FindChildElement<T>(child, childName);
                if (result != null)
                    return result;
            }
            return null;
        }

        private void UpdatePlayingTabIndicator()
        {
            var tabMap = new Dictionary<RadioButton, System.Collections.IList>
    {
        { rbMedia, _listLocal },
        { rbStream, _listStream },
        { rbTV, _listTv },
        { rbLibrary, _listLibrary },
        { rbSearch, _listSearch }
    };

            // LOGIC MỚI: Chỉ hiện khi có bài đang chọn VÀ bài đó không bị Pause
            bool shouldShowGif = _playingItem != null && !_playingItem.IsPaused;

            foreach (var kvp in tabMap)
            {
                RadioButton rb = kvp.Key;
                System.Collections.IList list = kvp.Value;

                // Kiểm tra xem Tab này có chứa bài đang phát không
                bool isTabContainingItem = (_playingItem != null && list.Contains(_playingItem));

                var icon = FindChildElement<Microsoft.UI.Xaml.Controls.Image>(rb, "PART_PlayingIcon");
                if (icon != null)
                {
                    // Kết hợp điều kiện: Tab chứa bài + Bài đang phát (không pause)
                    icon.Visibility = (isTabContainingItem && shouldShowGif) ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }
        private void OnFsSliderPointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (_fsPreviewTip != null) _fsPreviewTip.Visibility = Visibility.Collapsed;
            if (_fsSponsorTip != null) _fsSponsorTip.Visibility = Visibility.Collapsed;
            popPreview.IsOpen = false;
            popSponsor.IsOpen = false;
        }

        private void OnFsSliderPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_engine == null || _engine.Duration <= 0) return;
            if (_fsTimeSlider == null || _fsPreviewTip == null || _fsPreviewText == null) return;

            var slider = sender as Slider;
            var point = e.GetCurrentPoint(slider);
            double mouseX = point.Position.X;
            double sliderWidth = slider.ActualWidth;
            double percent = mouseX / sliderWidth;
            double hoverTime = percent * _engine.Duration;
            if (hoverTime < 0) hoverTime = 0;
            if (hoverTime > _engine.Duration) hoverTime = _engine.Duration;
            _fsPreviewText.Text = TimeSpan.FromSeconds(hoverTime).ToString(@"mm\:ss");
            var transform = slider.TransformToVisual(PopupContainer.Children[0] as UIElement);
            var sliderAbsPos = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

            double absoluteX = sliderAbsPos.X + mouseX;
            _fsPreviewTip.Visibility = Visibility.Visible;
            _fsPreviewTip.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            double tipWidth = _fsPreviewTip.ActualWidth > 0 ? _fsPreviewTip.ActualWidth : 50;

            _fsPreviewTip.RenderTransform = new TranslateTransform { X = absoluteX - (tipWidth / 2) };
            var matchingSegments = new System.Collections.Generic.List<dynamic>();
            var segments = _engine.GetCurrentSponsors();

            foreach (var seg in segments)
            {
                double startRatio = seg.Segment[0] / _engine.Duration;
                double endRatio = seg.Segment[1] / _engine.Duration;
                double startPixel = startRatio * sliderWidth;
                double endPixel = endRatio * sliderWidth;

                double width = endPixel - startPixel;
                if (width < 6) { double center = (startPixel + endPixel) / 2; startPixel = center - 3; endPixel = center + 3; }

                if (mouseX >= startPixel && mouseX <= endPixel)
                {
                    matchingSegments.Add(seg);
                }
            }

            if (matchingSegments.Count > 0 && _fsSponsorTip != null && _fsSponsorText != null)
            {
                var bestMatch = matchingSegments.OrderByDescending(s => GetCategoryPriority((string)s.Category)).First();
                string category = bestMatch.Category;
                UpdateFsSponsorUI(category);

                _fsSponsorTip.Visibility = Visibility.Visible;
                _fsSponsorTip.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                double sponsorTipWidth = _fsSponsorTip.ActualWidth > 0 ? _fsSponsorTip.ActualWidth : 60;

                _fsSponsorTip.RenderTransform = new TranslateTransform { X = absoluteX - (sponsorTipWidth / 2) };
            }
            else if (_fsSponsorTip != null)
            {
                _fsSponsorTip.Visibility = Visibility.Collapsed;
            }
        }
        private void UpdateFsSponsorUI(string category)
        {
            if (_fsSponsorTip == null || _fsSponsorText == null) return;

            string sponsorName = "";
            Windows.UI.Color sponsorColor = Microsoft.UI.Colors.Gray;
            Windows.UI.Color textColor = Microsoft.UI.Colors.White;

            switch (category.ToLower())
            {
                case "sponsor": sponsorName = "Tài trợ"; sponsorColor = Microsoft.UI.Colors.LimeGreen; textColor = Microsoft.UI.Colors.Black; break;
                case "intro": sponsorName = "Intro"; sponsorColor = Microsoft.UI.Colors.MediumPurple; break;
                case "outro": sponsorName = "Outro"; sponsorColor = Microsoft.UI.Colors.Red; break;
                case "selfpromo": sponsorName = "Promo"; sponsorColor = Microsoft.UI.Colors.DeepPink; break;
                case "interaction": sponsorName = "Tương tác"; sponsorColor = Microsoft.UI.Colors.Cyan; textColor = Microsoft.UI.Colors.Black; break;
                case "preview": sponsorName = "Preview"; sponsorColor = Microsoft.UI.Colors.DodgerBlue; break;
                case "music_offtopic": sponsorName = "Nhạc nền"; sponsorColor = Microsoft.UI.Colors.Pink; textColor = Microsoft.UI.Colors.Black; break;
                case "filler": sponsorName = "Filler"; sponsorColor = Microsoft.UI.Colors.Yellow; textColor = Microsoft.UI.Colors.Black; break;
                default: sponsorName = category; break;
            }

            _fsSponsorText.Text = sponsorName;
            _fsSponsorText.Foreground = new SolidColorBrush(textColor);
            _fsSponsorTip.Background = new SolidColorBrush(sponsorColor);
        }
        private void ClearAllSponsorMarks()
        {
            if (cvsSponsor != null)
            {
                cvsSponsor.Children.Clear();
            }
            if (_fsSponsorCanvas != null)
            {
                _fsSponsorCanvas.Children.Clear();
            }
            if (_fsSponsorTip != null)
            {
                _fsSponsorTip.Visibility = Visibility.Collapsed;
            }
        }
        private void UpdateSponsorPopupUI(string category)
        {
            string sponsorName = "";
            Windows.UI.Color sponsorColor = Microsoft.UI.Colors.Gray;

            switch (category.ToLower())
            {
                case "sponsor": sponsorName = "Tài trợ"; sponsorColor = Microsoft.UI.Colors.LimeGreen; break;
                case "intro": sponsorName = "Intro"; sponsorColor = Microsoft.UI.Colors.MediumPurple; break;
                case "outro": sponsorName = "Outro"; sponsorColor = Microsoft.UI.Colors.Red; break;
                case "selfpromo": sponsorName = "Promo"; sponsorColor = Microsoft.UI.Colors.DeepPink; break;
                case "interaction": sponsorName = "Tương tác"; sponsorColor = Microsoft.UI.Colors.Cyan; break;
                case "preview": sponsorName = "Preview"; sponsorColor = Microsoft.UI.Colors.DodgerBlue; break;
                case "music_offtopic": sponsorName = "Nhạc nền"; sponsorColor = Microsoft.UI.Colors.Pink; break;
                case "filler": sponsorName = "Filler"; sponsorColor = Microsoft.UI.Colors.Yellow; break;
                default: sponsorName = category; break;
            }
            txtSponsorPreview.Text = sponsorName;
            if (popSponsor.Child is Border b)
            {
                b.Background = new SolidColorBrush(sponsorColor);
                if (category.ToLower() == "filler" || category.ToLower() == "interaction" || category.ToLower() == "sponsor")
                    txtSponsorPreview.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black);
                else
                    txtSponsorPreview.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
            }
        }
        private void UpdateFullscreenBufferWidth()
        {
            if (_engine == null || _fsBufferRect == null || _fsTimeSlider == null) return;

            double total = _engine.Duration;
            double bufferedPos = _engine.GetBufferedPosition();
            double sliderWidth = _fsTimeSlider.ActualWidth;

            if (total > 0 && bufferedPos > 0 && sliderWidth > 0)
            {
                double percent = bufferedPos / total;
                if (percent > 1) percent = 1;
                _fsBufferRect.Width = sliderWidth * percent;
            }
            else
            {
                _fsBufferRect.Width = 0;
            }
        }
        private void UpdateFullscreenBuffer()
        {
            if (_engine == null || _fsBufferRect == null || _fsTimeSlider == null) return;

            double total = _engine.Duration;
            double bufferedPos = _engine.GetBufferedPosition();
            if (bufferedPos > 0)
            {
                double buffPercent = (bufferedPos / total) * 100;
                if (buffPercent > 100) buffPercent = 100;

                if (prgBuffer != null) prgBuffer.Value = buffPercent;
                UpdateFullscreenBufferWidth();
            }
            else
            {
                if (prgBuffer != null) prgBuffer.Value = 0;
                if (_fsBufferRect != null) _fsBufferRect.Width = 0;
            }
        }
        private void SyncFullscreenState()
        {
            if (!FullscreenPopup.IsOpen) return;

            if (_fsPlayIcon != null)
            {
                if (btnPlay.Visibility == Visibility.Visible)
                    _fsPlayIcon.Glyph = "\uF5B0";
                else
                    _fsPlayIcon.Glyph = "\uF8AE";
            }

            if (_fsRepeatIcon != null)
            {
                _fsRepeatIcon.Glyph = iconCycle.Glyph;
                _fsRepeatIcon.Foreground = iconCycle.Foreground;
            }
            if (_fsVolSlider != null && Math.Abs(_fsVolSlider.Value - sliderVolume.Value) > 0.5)
            {
                _fsVolSlider.Value = sliderVolume.Value;
            }
            if (_fsVolIcon != null)
            {
                _fsVolIcon.Glyph = iconVolume.Glyph;
                _fsVolIcon.Foreground = iconVolume.Foreground;
            }
            if (!_isUserDragging)
            {
                if (_fsTimeSlider != null) _fsTimeSlider.Value = timelineSlider.Value;
                if (_fsCurrentTime != null) _fsCurrentTime.Text = txtCurrentTime.Text;
                if (_fsTotalTime != null) _fsTotalTime.Text = txtTotalTime.Text;
            }
        }

        private bool _isAutoChanging = false;

        private void ProgressTimer_Tick(object sender, object e)
        {
            if (_engine == null) return;
            if (_engine.IsChangingQuality) return;
            if (_isUserDragging) return;
            if (_isAutoChanging) return;
            _engine.CheckAndSkipSponsor();

            double total = _engine.Duration;
            double current = _engine.Position;
            if (total > 0)
            {
                double bufferedPos = _engine.GetBufferedPosition();
                if (bufferedPos > 0)
                {
                    double buffPercent = (bufferedPos / total) * 100;
                    if (buffPercent > 100) buffPercent = 100;

                    if (prgBuffer != null)
                    {
                        prgBuffer.Value = buffPercent;
                    }
                    UpdateFullscreenBuffer();
                }
                else
                {
                    if (prgBuffer != null) prgBuffer.Value = 0;
                    if (_fsBufferRect != null) _fsBufferRect.Width = 0;
                }
            }
            TimeSpan tTotal = TimeSpan.FromSeconds(total);
            TimeSpan tCurrent = TimeSpan.FromSeconds(current);
            string sTotal = (total > 0) ? ((tTotal.TotalHours >= 1) ? tTotal.ToString(@"hh\:mm\:ss") : tTotal.ToString(@"mm\:ss")) : "00:00";
            string sCurrent = (tCurrent.TotalHours >= 1) ? tCurrent.ToString(@"hh\:mm\:ss") : tCurrent.ToString(@"mm\:ss");

            txtTotalTime.Text = sTotal;
            txtCurrentTime.Text = sCurrent;
            if (_fsCurrentTime != null) _fsCurrentTime.Text = sCurrent;
            if (_fsTotalTime != null) _fsTotalTime.Text = sTotal;
            _isInternalUpdate = true;
            if (total > 0)
            {
                double percent = (current / total) * 100;
                timelineSlider.Value = percent;
                if (_fsTimeSlider != null) _fsTimeSlider.Value = percent;
            }
            else
            {
                timelineSlider.Value = 0;
                if (_fsTimeSlider != null) _fsTimeSlider.Value = 0;
            }
            _isInternalUpdate = false;
            UpdateMediaInfoUI();
            if (_fsVolSlider != null && Math.Abs(_fsVolSlider.Value - sliderVolume.Value) > 1)
                _fsVolSlider.Value = sliderVolume.Value;
            SyncFullscreenState();

            bool isTvMode = (lstMedia.ItemsSource == _listTv);
            bool isLiveStream = _engine.IsLiveStream();
            bool shouldShowViz = false;

            bool isUserPaused = (_playingItem != null && _playingItem.IsPaused);

            if (_engine.IsPlaying() && !_engine.IsPaused() && !isUserPaused)
            {
                var techInfo = _engine.GetTechnicalInfo();
                if (!string.IsNullOrEmpty(techInfo.AudioCodec) &&
                    !techInfo.AudioCodec.Contains("KHÔNG CÓ ÂM THANH"))
                {
                    shouldShowViz = true;
                }
            }
            if (shouldShowViz)
            {
                StartVisualizer();
            }
            else
            {
                StopVisualizer();
            }
            if (isTvMode || isLiveStream)
            {
                return;
            }
            if (total > 0)
            {
                if (current >= total - 0.5)
                {
                    _isAutoChanging = true;

                    if (_currentMode == PlayerMode.RepeatOne)
                    {
                        _engine.Seek(0);
                        _engine.Resume();
                        Task.Delay(500).ContinueWith(_ => this.DispatcherQueue.TryEnqueue(() => _isAutoChanging = false));
                    }
                    else if (_currentMode == PlayerMode.Off)
                    {
                        StopComplete();
                        _isAutoChanging = false;
                    }
                    else
                    {
                        PlayNextVideo(true);
                    }
                }
            }
            if (cvsSponsor != null && cvsSponsor.Children.Count == 0)
            {
                if (_engine.GetCurrentSponsors().Count > 0)
                {
                    DrawSponsorMarks();
                }
            }
        }
        private void OnSliderDragStart(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _isUserDragging = true;
        }

        private void OnSliderDragEnd(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _isUserDragging = false;
            if (sender is Slider sl && _engine != null)
            {
                if (sl == timelineSlider || sl == _fsTimeSlider)
                {
                    double seekTo = (sl.Value / 100.0) * _engine.Duration;
                    _engine.Seek(seekTo);
                }
            }
        }
        private void sliderVolume_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (sliderVolume.Value > 0)
            {
                _savedVolume = sliderVolume.Value;
            }

            double newVolume = sliderVolume.Value;

            if (_engine != null)
            {
                _engine.SetVolume(newVolume);
            }

            UpdateVolumeIcon(newVolume);
            UpdateStatus($"🔊 Âm lượng: {(int)sliderVolume.Value}%");
            if (_fsVolSlider != null && Math.Abs(_fsVolSlider.Value - newVolume) > 1)
            {
                _fsVolSlider.Value = newVolume;
            }
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            CloseFsWindow();
            if (_folderWatcher != null)
            {
                _folderWatcher.EnableRaisingEvents = false;
                _folderWatcher.Dispose();
                _folderWatcher = null;
            }
            if (_engine != null)
            {
                _engine.Dispose();
                _engine = null;
            }
            Task.Run(() =>
            {
                try
                {
                    string tempFolder = Path.Combine(Path.GetTempPath(), "MediaLed_Cache");
                    if (Directory.Exists(tempFolder))
                    {
                        Directory.Delete(tempFolder, true);
                    }
                }
                catch { }
            });
        }

        private long _lastResizeTicks = 0;

        private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs e)
        {
            if (_resizeDebounceTimer != null)
            {
                _resizeDebounceTimer.Stop();
                _resizeDebounceTimer.Start();
            }
            if (_engine != null && _engine.Duration > 0)
            {
                DrawSponsorMarks();
            }
            if (FullscreenPopup.IsOpen)
            {
                PopupContainer.Width = this.Content.XamlRoot.Size.Width;
                PopupContainer.Height = this.Content.XamlRoot.Size.Height;
            }
        }

        private void cboViewMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_engine == null || !_isInitialized) return;

            if (cboViewMode.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                string mode = item.Tag.ToString();
                _engine.SetViewMode(mode);

                if (sldZoom != null) sldZoom.Value = 0;
            }
        }
        private void sldZoom_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_engine == null) return;

            double val = e.NewValue;
            _engine.SetManualZoom(val);

            if (lblZoomLevel != null)
            {
                int percent = 100 + (int)(val * 100);
                lblZoomLevel.Text = $"{percent}%";
            }
        }

        private void btnResetZoom_Click(object sender, RoutedEventArgs e)
        {
            if (sldZoom != null) sldZoom.Value = 0;
        }

        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {

            if (btnSearchSource.Visibility == Visibility.Visible) return;

            string keyword = txtSearch.Text.Trim().ToLower();

            ObservableCollection<MediaItem> currentViewList = null;
            List<MediaItem> sourceBackupList = null;

            if (lstMedia.ItemsSource == _listLocal)
            {
                currentViewList = _listLocal;
                sourceBackupList = _backupLocal;
            }
            else if (lstMedia.ItemsSource == _listTv)
            {
                currentViewList = _listTv;
                sourceBackupList = _backupTv;
            }
            else if (lstMedia.ItemsSource == _listStream)
            {
                currentViewList = _listStream;
                sourceBackupList = _backupStream;
            }
            else if (lstMedia.ItemsSource == _listLibrary)
            {
                currentViewList = _listLibrary;
                sourceBackupList = _backupLibrary;
            }
            if (currentViewList != null && sourceBackupList != null)
            {
                currentViewList.Clear();

                if (string.IsNullOrEmpty(keyword))
                {
                    foreach (var item in sourceBackupList) currentViewList.Add(item);
                }
                else
                {
                    foreach (var item in sourceBackupList)
                    {
                        if (item.FileName.ToLower().Contains(keyword))
                        {
                            currentViewList.Add(item);
                        }
                    }
                }
            }
            UpdateListStats();
        }
        private async Task AutoLoadLinksAsync()
        {
            try
            {
                string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
                string FilePath = System.IO.Path.Combine(BaseDir, "IPTV_Data", "links.txt");

                if (!System.IO.File.Exists(FilePath))
                {
                    UpdateStatus($"⚠️ Không tìm thấy file: {FilePath}", false, true);
                    return;
                }

                UpdateStatus($"⏳ Đang đọc file links.txt...", true);
                string[] lines = await System.IO.File.ReadAllLinesAsync(FilePath);
                int count = 0;

                foreach (string line in lines)
                {
                    string cleanLine = line.Trim();
                    if (string.IsNullOrEmpty(cleanLine) || cleanLine.StartsWith("#")) continue;
                    if (cleanLine.StartsWith("http") || cleanLine.StartsWith("rtmp") || cleanLine.StartsWith("udp"))
                    {
                        var newItem = new MediaItem
                        {
                            FileName = $"Link Auto {count + 1}",
                            FullPath = cleanLine,
                            Type = "IPTV",
                            Duration = "Live",
                            ChannelName = "Auto Import"
                        };
                        _listTv.Add(newItem);
                        _backupTv.Add(newItem);
                        count++;
                    }
                }

                if (count > 0)
                {
                    UpdateStatus($"✅ Đã nạp {count} link từ file cấu hình.");
                    UpdateListStats();
                }
                else
                {
                    UpdateStatus("⚠️ File links.txt không có link hợp lệ.", false, true);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ Lỗi đọc file cấu hình: {ex.Message}", false, true);
            }
        }
        private async Task AutoScanM3uFolderAsync()
        {
            try
            {
                string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
                string FolderPath = System.IO.Path.Combine(BaseDir, "IPTV_Data", "Playlists");

                if (!System.IO.Directory.Exists(FolderPath))
                {
                    UpdateStatus($"⚠️ Không tìm thấy thư mục: {FolderPath}", false, true);
                    return;
                }

                UpdateStatus($"⏳ Đang quét thư mục Playlists...", true);
                var files = System.IO.Directory.GetFiles(FolderPath, "*.*")
                            .Where(f => f.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase));

                int countFile = 0;
                foreach (string file in files)
                {
                    HandleExternalPlaylist(file);
                    countFile++;
                }

                if (countFile > 0)
                {
                    UpdateStatus($"✅ Đã nạp {countFile} file playlist từ thư mục sẵn.");
                    UpdateListStats();
                }
                else
                {
                    UpdateStatus("⚠️ Thư mục Playlists rỗng.", false, true);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ Lỗi quét thư mục cấu hình: {ex.Message}", false, true);
            }
        }
        private async void txtSearch_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            if (btnSearchSource.Visibility != Visibility.Visible) return;

            string keyword = txtSearch.Text.Trim();
            if (string.IsNullOrEmpty(keyword)) return;
            _currentSearchQuery = keyword;
            _currentSearchPage = 0;
            _youtubePageTokens.Clear();
            _listSearch.Clear();
            lstMedia.ItemsSource = _listSearch;

            await TriggerSearchNavigation();

            UpdateListStats();
        }

        private void UpdateListStats()
        {
            if (txtListStats == null || lstMedia == null || grpListStats == null) return;

            if (lstMedia.Visibility != Visibility.Visible)
            {
                grpListStats.Visibility = Visibility.Collapsed;
                return;
            }
            grpListStats.Visibility = Visibility.Visible;
            var currentList = lstMedia.ItemsSource as System.Collections.IList;
            int currentCount = currentList != null ? currentList.Count : 0;
            string label = "file";

            if (lstMedia.ItemsSource == _listStream) label = "link";
            else if (lstMedia.ItemsSource == _listTv) label = "kênh";
            else if (lstMedia.ItemsSource == _listSearch) label = "kết quả tìm kiếm";
            else if (lstMedia.ItemsSource == _listLibrary) label = "file thư viện";

            if (btnSearchSource.Visibility == Visibility.Visible)
            {
                txtListStats.Text = $"Tổng số {currentCount} {label}";
            }
            else if (!string.IsNullOrWhiteSpace(txtSearch.Text))
            {
                int totalCount = 0;
                if (lstMedia.ItemsSource == _listLocal) totalCount = _backupLocal.Count;
                else if (lstMedia.ItemsSource == _listStream) totalCount = _backupStream.Count;
                else if (lstMedia.ItemsSource == _listTv) totalCount = _backupTv.Count;
                else if (lstMedia.ItemsSource == _listLibrary) totalCount = _backupLibrary.Count;

                txtListStats.Text = $"Tìm thấy {currentCount}/{totalCount} {label}";
            }
            else
            {
                txtListStats.Text = $"Tổng số {currentCount} {label}";
            }
        }
        private void UpdatePaginationUI(bool isVisible, int pageIndex, bool canNext)
        {
            if (grpSearchPagination == null) return;

            if (isVisible)
            {
                grpSearchPagination.Visibility = Visibility.Visible;
                txtSearchPageInfo.Text = $"Trang {pageIndex + 1}";
                btnSearchPrev.IsEnabled = (pageIndex > 0);
                btnSearchPrev.Opacity = (pageIndex > 0) ? 1.0 : 0.5;
                btnSearchNext.IsEnabled = canNext;
                btnSearchNext.Opacity = canNext ? 1.0 : 0.5;
            }
            else
            {
                grpSearchPagination.Visibility = Visibility.Collapsed;
            }
        }

        private async void btnSearchPrev_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSearchPage > 0)
            {
                _currentSearchPage--;
                await TriggerSearchNavigation();
            }
        }

        private async void btnSearchNext_Click(object sender, RoutedEventArgs e)
        {
            _currentSearchPage++;
            await TriggerSearchNavigation();
        }
        private async Task TriggerSearchNavigation()
        {
            _listSearch.Clear();
            switch (_currentSourceMode)
            {
                case 0:
                    await SearchYoutubeAsync(_currentSearchQuery, _currentSearchPage);
                    break;

                case 1:
                    await SearchDailymotionAsync(_currentSearchQuery, _currentSearchPage);
                    break;

                case 2:
                    await SearchWindowsIndexAsync(_currentSearchQuery, _currentSearchPage, _currentPcFilterMode);
                    break;
            }

            UpdateListStats();
        }

        private int _currentSearchPage = 0;
        private string _currentSearchQuery = "";

        private bool IsFacebookVideoLink(string url)
        {
            string link = url.ToLower();
            if (link.Contains("/groups/") ||
                link.Contains("/posts/") ||
                link.Contains("/photos/") ||
                link.Contains("/events/") ||
                link.Contains("/permalink/"))
            {
                return false;
            }

            if (link.Contains("/videos/") ||
                link.Contains("/watch") ||
                link.Contains("/reel/") ||
                link.Contains("fb.watch"))
            {
                return true;
            }
            return false;
        }
        private void btnOpenIndexing_Click(object sender, RoutedEventArgs e)
        {
            OpenWindowsIndexingOptions();
        }
        private void OpenWindowsIndexingOptions()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "control.exe",
                    Arguments = "srchadmin.dll",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                UpdateStatus("Không thể mở Indexing Options: " + ex.Message, false, true);
            }
        }
        private void DrawSponsorMarks()
        {
            ClearAllSponsorMarks();

            if (_engine == null || _engine.Duration <= 0) return;

            var segments = _engine.GetCurrentSponsors();
            if (segments == null || segments.Count == 0) return;

            double totalSeconds = _engine.Duration;

            foreach (var seg in segments)
            {
                double startVal = seg.Segment[0];
                double endVal = seg.Segment[1];

                double startPercent = startVal / totalSeconds;
                double endPercent = endVal / totalSeconds;
                double widthPercent = endPercent - startPercent;
                var color = GetColorByCategory(seg.Category);
                if (cvsSponsor != null && timelineSlider != null && timelineSlider.ActualWidth > 0)
                {
                    double canvasW = timelineSlider.ActualWidth;

                    var rect = new Microsoft.UI.Xaml.Shapes.Rectangle
                    {
                        Height = cvsSponsor.ActualHeight > 0 ? cvsSponsor.ActualHeight : 4,
                        Width = widthPercent * canvasW,
                        Fill = new SolidColorBrush(color),
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(rect, startPercent * canvasW);
                    cvsSponsor.Children.Add(rect);
                }
                if (_fsSponsorCanvas != null && _fsTimeSlider != null)
                {
                    double fsWidth = _fsTimeSlider.ActualWidth;
                    if (fsWidth <= 0) fsWidth = 800;

                    var fsRect = new Microsoft.UI.Xaml.Shapes.Rectangle
                    {
                        Height = 4,
                        Width = widthPercent * fsWidth,
                        Fill = new SolidColorBrush(color),
                        IsHitTestVisible = false
                    };

                    Canvas.SetLeft(fsRect, startPercent * fsWidth);
                    _fsSponsorCanvas.Children.Add(fsRect);
                }
            }
        }
        private Windows.UI.Color GetColorByCategory(string category)
        {
            switch (category.ToLower())
            {
                case "sponsor": return Microsoft.UI.Colors.LimeGreen;
                case "intro": return Microsoft.UI.Colors.MediumPurple;
                case "outro": return Microsoft.UI.Colors.Red;
                case "selfpromo": return Microsoft.UI.Colors.DeepPink;
                case "interaction": return Microsoft.UI.Colors.Cyan;
                case "preview": return Microsoft.UI.Colors.DodgerBlue;
                case "music_offtopic": return Microsoft.UI.Colors.Pink;
                case "filler": return Microsoft.UI.Colors.Yellow;
                default: return Microsoft.UI.Colors.Gray;
            }
        }
        private void DrawMarksOnCanvas(Canvas targetCanvas, double drawWidth, double totalDuration, List<SponsorSegment> segments)
        {
            foreach (var seg in segments)
            {
                double start = seg.Segment[0];
                double end = seg.Segment[1];

                double left = (start / totalDuration) * drawWidth;
                double width = ((end - start) / totalDuration) * drawWidth;

                if (width < 4) width = 4;

                var colorBrush = new SolidColorBrush(Microsoft.UI.Colors.LimeGreen);
                string cat = seg.Category.ToLower();
                if (cat == "sponsor") colorBrush = new SolidColorBrush(Microsoft.UI.Colors.LimeGreen);
                else if (cat == "intro") colorBrush = new SolidColorBrush(Microsoft.UI.Colors.MediumPurple);
                else if (cat == "outro") colorBrush = new SolidColorBrush(Microsoft.UI.Colors.Red);
                else if (cat == "selfpromo") colorBrush = new SolidColorBrush(Microsoft.UI.Colors.DeepPink);
                else if (cat == "interaction") colorBrush = new SolidColorBrush(Microsoft.UI.Colors.Cyan);
                else if (cat == "preview") colorBrush = new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue);
                else if (cat == "music_offtopic") colorBrush = new SolidColorBrush(Microsoft.UI.Colors.Pink);
                else if (cat == "filler") colorBrush = new SolidColorBrush(Microsoft.UI.Colors.Yellow);
                else colorBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray);

                var rect = new Microsoft.UI.Xaml.Shapes.Rectangle
                {
                    Width = width,
                    Height = targetCanvas.ActualHeight > 0 ? targetCanvas.ActualHeight : 4,
                    Fill = colorBrush,
                    Opacity = 1.0,
                    IsHitTestVisible = false,
                    RadiusX = 2,
                    RadiusY = 2
                };

                Canvas.SetLeft(rect, left);
                targetCanvas.Children.Add(rect);
            }
        }

        private void btnFullScreen_Click(object sender, RoutedEventArgs e)
        {
            ToggleInternalFullscreen();
        }

        private void AnimateControls(double targetY)
        {
            var transform = AreaControls.RenderTransform as TranslateTransform;
            if (transform == null) return;
            double targetValue = targetY;
            var storyboard = new Storyboard();

            var animation = new DoubleAnimation
            {
                To = targetValue,
                Duration = new Duration(TimeSpan.FromMilliseconds(300)),
                EasingFunction = new SineEase() { EasingMode = (targetY == 0 ? EasingMode.EaseOut : EasingMode.EaseIn) }
            };

            Storyboard.SetTarget(animation, transform);
            Storyboard.SetTargetProperty(animation, "Y");

            storyboard.Children.Add(animation);
            storyboard.Begin();
        }
        private IntPtr _fsControlHwnd = IntPtr.Zero;
        private IntPtr _fsControlWinProcPtr = IntPtr.Zero;
        private const int SW_SHOWNOACTIVATE = 4;
        private const uint SWP_NOACTIVATE = 0x0010;


        private const int WS_EX_TOPMOST = 0x00000008;
        private void ShowFsWindow()
        {
            if (PopupContainer.Children.Count == 0)
            {
                var content = CreateFullscreenControls();
                PopupContainer.Children.Add(content);
            }

            if (this.Content is FrameworkElement root)
            {
                PopupContainer.Width = root.ActualWidth;
                PopupContainer.Height = root.ActualHeight;
            }
            FullscreenPopup.IsOpen = true;
        }

        private async void CloseFsWindow()
        {
            if (FullscreenPopup.IsOpen && PopupContainer.Children.Count > 0)
            {
                if (PopupContainer.Children[0] is Grid wrapper && wrapper.Children.Count > 0)
                {
                    if (wrapper.Children[0] is FrameworkElement container &&
                        container.RenderTransform is TranslateTransform transform)
                    {
                        var sb = new Storyboard();
                        var anim = new DoubleAnimation
                        {
                            To = 120,
                            Duration = new Duration(TimeSpan.FromMilliseconds(250)),
                            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseIn }
                        };
                        Storyboard.SetTarget(anim, transform);
                        Storyboard.SetTargetProperty(anim, "Y");
                        sb.Children.Add(anim);
                        sb.Begin();
                        await Task.Delay(250);
                    }
                }
                FullscreenPopup.IsOpen = false;
                PopupContainer.Children.Clear();
            }
        }
        private Window _fsWindow = null;
        private IntPtr _fsWindowHandle = IntPtr.Zero;
        private IntPtr CustomControlWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == MediaEngine.WM_CLOSE) return IntPtr.Zero;
            return MediaEngine.DefWindowProc(hWnd, msg, wParam, lParam);
        }
        private void UpdateModeUI(bool isPlayerMode)
        {
            var studioColor = Windows.UI.Color.FromArgb(255, 255, 140, 0);
            var playerColor = Windows.UI.Color.FromArgb(255, 255, 68, 68);
            var grayColor = Microsoft.UI.Colors.Gray;

            if (isPlayerMode)
            {
                btnToggleLed.Visibility = Visibility.Collapsed;
                btnFullScreen.Visibility = Visibility.Visible;

                if (rbEffects != null) rbEffects.Visibility = Visibility.Collapsed;
                if (grpEffects.Visibility == Visibility.Visible) OnNavTabClick(rbStream, null);

                txtMode.Text = "PLAYER";
                txtMode.Foreground = new SolidColorBrush(playerColor);
                if (iconStudio != null) iconStudio.Foreground = new SolidColorBrush(playerColor);

                btnToggleLed.IsEnabled = false;
                btnToggleLed.Opacity = 0.3;
                _isLedOn = false;
                if (iconLed != null) iconLed.Foreground = new SolidColorBrush(grayColor);
            }
            else
            {
                btnToggleLed.Visibility = Visibility.Visible;
                btnFullScreen.Visibility = Visibility.Collapsed;
                if (rbEffects != null) rbEffects.Visibility = Visibility.Visible;

                txtMode.Text = "STUDIO";
                txtMode.Foreground = new SolidColorBrush(studioColor);
                if (iconStudio != null) iconStudio.Foreground = new SolidColorBrush(studioColor);

                btnToggleLed.IsEnabled = true;
                btnToggleLed.Opacity = 1.0;
                _isLedOn = false;
                if (iconLed != null) iconLed.Foreground = new SolidColorBrush(grayColor);
            }
        }
        private async void btnModeSwitch_Click(object sender, RoutedEventArgs e)
        {
            if (_engine == null) return;
            _isUserActionStop = true;
            StopComplete();

            await Task.Delay(100);
            pnlPreview.SizeChanged -= PnlPreview_SizeChanged;
            bool newMode = btnModeSwitch.IsChecked == true;
            await _engine.SetMode(newMode);
            StopComplete();
            UpdateModeUI(newMode);
            UpdateStatus(newMode ? "Đã chuyển sang chế độ PLAYER." : "Đã chuyển sang chế độ STUDIO.");
            AppSettings.Save(SETTING_APP_MODE, newMode.ToString());

            RootGrid.UpdateLayout();
            await Task.Delay(50); // Đợi UI render xong

            // Ép buộc cập nhật vị trí video
            if (newMode) // Nếu là Player Mode
            {
                // Gọi 2 lần để chắc chắn kích thước đã ăn khớp
                UpdateMpvLayout();
                await Task.Delay(50);
                UpdateMpvLayout();
            }
            else
            {
                UpdateMpvLayout();
            }
        }
        private void btnNavToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_isAnimatingNav) CompositionTarget.Rendering -= OnNavAnimating;
            bool isCurrentlyExpanded = AreaNav.ActualWidth > 80;
            _isNavExpanded = !isCurrentlyExpanded;

            if (_isNavExpanded)
            {
                iconNav.Glyph = "\uE72B";
                pnlAppTitle.Visibility = Visibility.Visible;
            }
            else
            {
                iconNav.Glyph = "\uE700";
                pnlAppTitle.Visibility = Visibility.Collapsed;
            }

            double currentNavW = AreaNav.ActualWidth;
            double currentListW = AreaSidebar.ActualWidth;
            _animTotalLeftW = currentNavW + currentListW;

            _animTargetNavW = _isNavExpanded ? 125 : 50;
            _animStartNavW = currentNavW;

            _animStartTime = DateTime.Now;
            _isAnimatingNav = true;
            CompositionTarget.Rendering += OnNavAnimating;
        }

        private void btnToggleLed_Click(object sender, RoutedEventArgs e)
        {
            if (_engine == null) return;
            if (_isPlayerMode) return;

            if (_selectedMonitor == null)
            {
                UpdateStatus("⛔ Không thể bật LED: Chưa chọn màn hình xuất!", false, true);
                RefreshMonitors();
                return;
            }

            _isLedOn = !_isLedOn;

            if (_isLedOn)
            {
                _engine.SetLedScreen(true, _selectedMonitor.Index);
                if (btnToggleLed.Content is FontIcon icon)
                {
                    icon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 140, 0));
                    if (iconLed != null) iconLed.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 140, 0));
                }
            }
            else
            {
                _engine.SetLedScreen(false, 0);
                if (btnToggleLed.Content is FontIcon icon)
                {
                    icon.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray);
                    if (iconLed != null) iconLed.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray);
                }
            }

            UpdateMpvLayout();
        }

        private void UpdateMpvLayout()
        {
            if (_engine == null || this.Content == null || this.Content.XamlRoot == null) return;

            bool shouldShowVideo = true;
            if (grpEffects.Visibility == Visibility.Visible)
            {
                shouldShowVideo = false;
            }

            try
            {
                double scale = this.Content.XamlRoot.RasterizationScale;
                int x, y, w, h;

                if (_isInternalFullscreen)
                {
                    x = 0; y = 0;
                    w = (int)(RootGrid.ActualWidth * scale);
                    h = (int)(RootGrid.ActualHeight * scale);
                    shouldShowVideo = true;
                }
                else
                {
                    FrameworkElement targetElement = (grpEffects.Visibility == Visibility.Visible)
                        ? (FrameworkElement)DesignSurface
                        : (FrameworkElement)pnlPreview;

                    // --- THÊM KIỂM TRA IsLoaded ---
                    if (targetElement == null || targetElement.ActualWidth <= 0 || !targetElement.IsLoaded) return;

                    var rootElement = this.Content as UIElement;

                    // TransformToVisual có thể gây lỗi nếu element đang biến mất, bọc try-catch riêng
                    try
                    {
                        var transform = targetElement.TransformToVisual(rootElement);
                        var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

                        x = (int)(point.X * scale);
                        y = (int)(point.Y * scale);
                        w = (int)(targetElement.ActualWidth * scale);
                        h = (int)(targetElement.ActualHeight * scale);
                    }
                    catch
                    {
                        return; // Nếu lỗi tính toán vị trí thì bỏ qua frame này
                    }
                }

                if (w > 0 && h > 0)
                {
                    _engine.UpdateLayout(x, y, w, h, shouldShowVideo);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Layout Error: " + ex.Message);
            }
        }

        private double _savedNavWidth = 50;
        private double _savedSidebarWidth = 300;
        private double _savedControlsHeight = 100;

        private void AnimateToSize(FrameworkElement element, string property, double toVal, int durationMs, Action onCompleted = null)
        {
            var sb = new Storyboard();
            var anim = new DoubleAnimation
            {
                To = toVal,
                Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                EnableDependentAnimation = true
            };

            Storyboard.SetTarget(anim, element);
            Storyboard.SetTargetProperty(anim, property);

            if (onCompleted != null)
            {
                sb.Completed += (s, e) => onCompleted();
            }

            sb.Children.Add(anim);
            sb.Begin();
        }
        private void PnlPreview_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_resizeDebounceTimer != null)
            {
                _resizeDebounceTimer.Stop();
                _resizeDebounceTimer.Start();
            }
        }
        private async void btnAdd_Click(object sender, RoutedEventArgs e)
        {
            if (lstMedia.ItemsSource == _listSearch)
            {
                if (lstMedia.SelectedItems.Count == 0)
                {
                    UpdateStatus("⚠ Vui lòng chọn ít nhất một mục từ kết quả tìm kiếm để thêm.", false, true);
                    return;
                }
                var selectedItems = lstMedia.SelectedItems.Cast<MediaItem>().ToList();
                int countStream = 0;
                int countLocal = 0;

                foreach (var item in selectedItems)
                {
                    var newItem = new MediaItem
                    {
                        FileName = item.FileName,
                        FullPath = item.FullPath,
                        Duration = item.Duration,
                        ChannelName = item.ChannelName,
                        Poster = item.Poster,
                        Type = item.Type
                    };

                    if (item.FullPath.StartsWith("http") || item.Type.Contains("YOUTUBE") || item.Type.Contains("DAILY"))
                    {
                        newItem.Type = "ONLINE";
                        _listStream.Add(newItem);
                        _backupStream.Add(newItem);
                        countStream++;
                    }
                    else
                    {
                        if (newItem.Poster == null)
                        {
                            try
                            {
                                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(newItem.FullPath);
                                newItem.Poster = await GetFileThumbnailAsync(file);
                            }
                            catch { }
                        }

                        _listLocal.Add(newItem);
                        _backupLocal.Add(newItem);
                        countLocal++;
                    }
                }
                string msg = "✅ Đã thêm: ";
                if (countStream > 0) msg += $"{countStream} vào Online ";
                if (countLocal > 0) msg += $"{countLocal} vào Local";
                UpdateStatus(msg);
                lstMedia.SelectedItems.Clear();
                UpdateListStats();
                return;
            }

            if (lstMedia.ItemsSource == _listTv)
            {
                MenuFlyout menu = new MenuFlyout();
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var subLink = new MenuFlyoutSubItem
                {
                    Text = "Thêm Link Stream",
                    Icon = new FontIcon { Glyph = "\uE71B" }
                };

                var itemLinkManual = new MenuFlyoutItem
                {
                    Text = "Nhập thủ công (Trống)...",
                    Icon = new FontIcon { Glyph = "\uE70F" }
                };
                itemLinkManual.Click += (s, args) =>
                {
                    ShowInlineInput("IPTV");
                    if (txtInlineUrl != null)
                    {
                        txtInlineUrl.Text = "";
                        txtInlineUrl.Focus(FocusState.Programmatic);
                    }
                };
                subLink.Items.Add(itemLinkManual);
                subLink.Items.Add(new MenuFlyoutSeparator());
                string linkFilePath = System.IO.Path.Combine(baseDir, "IPTV_Data", "links.txt");
                if (System.IO.File.Exists(linkFilePath))
                {
                    try
                    {
                        var linesOfLink = System.IO.File.ReadAllLines(linkFilePath);
                        int linkCount = 0;
                        foreach (var line in linesOfLink)
                        {
                            if (string.IsNullOrWhiteSpace(line) || line.Trim().StartsWith("#")) continue;

                            string urlRaw = line.Trim();
                            string displayName = urlRaw;

                            if (urlRaw.Contains("|"))
                            {
                                var parts = urlRaw.Split('|');
                                urlRaw = parts[0].Trim();
                                if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                                    displayName = parts[1].Trim();
                            }

                            if (urlRaw.StartsWith("http") || urlRaw.StartsWith("rtmp") || urlRaw.StartsWith("udp"))
                            {
                                var dynamicItem = new MenuFlyoutItem
                                {
                                    Text = displayName,
                                    Icon = new FontIcon { Glyph = "\uE968" },
                                };
                                ToolTipService.SetToolTip(dynamicItem, urlRaw);

                                string finalUrl = urlRaw;
                                dynamicItem.Click += async (s, e) =>
                                {
                                    this.Activate();
                                    ShowInlineInput("IPTV");

                                    if (txtInlineUrl != null)
                                    {
                                        txtInlineUrl.Text = finalUrl;
                                        btnInlineAdd_Click(btnInlineAdd, new RoutedEventArgs());
                                        await Task.Delay(1500);
                                        UpdateListStats();
                                    }
                                };

                                subLink.Items.Add(dynamicItem);
                                linkCount++;
                            }
                        }
                        if (linkCount == 0) subLink.Items.Add(new MenuFlyoutItem { Text = "(File trống)", IsEnabled = false });
                    }
                    catch { subLink.Items.Add(new MenuFlyoutItem { Text = "(Lỗi đọc file)", IsEnabled = false }); }
                }
                else
                {
                    subLink.Items.Add(new MenuFlyoutItem { Text = "(Chưa có file links.txt)", IsEnabled = false });
                }
                menu.Items.Add(subLink);
                var subM3u = new MenuFlyoutSubItem { Text = "Thêm Playlist (M3U)", Icon = new FontIcon { Glyph = "\uE8FD" } };
                var itemM3uManual = new MenuFlyoutItem { Text = "Chọn file từ máy...", Icon = new FontIcon { Glyph = "\uE8E5" } };
                itemM3uManual.Click += async (s, args) => await AddTvFromFileAsync();
                subM3u.Items.Add(itemM3uManual);
                subM3u.Items.Add(new MenuFlyoutSeparator());

                string playlistFolder = System.IO.Path.Combine(baseDir, "IPTV_Data", "Playlists");
                if (System.IO.Directory.Exists(playlistFolder))
                {
                    var filesOfM3u = System.IO.Directory.GetFiles(playlistFolder);
                    int m3uCount = 0;
                    foreach (var file in filesOfM3u)
                    {
                        string ext = System.IO.Path.GetExtension(file).ToLower();
                        if (ext == ".m3u" || ext == ".m3u8")
                        {
                            string fileName = System.IO.Path.GetFileNameWithoutExtension(file);
                            var dynamicItem = new MenuFlyoutItem { Text = fileName, Icon = new FontIcon { Glyph = "\uE8B7" } };
                            string filePathCapture = file;

                            dynamicItem.Click += (s, e) =>
                            {
                                this.Activate();
                                this.DispatcherQueue.TryEnqueue(() => HandleExternalPlaylist(filePathCapture));
                            };
                            subM3u.Items.Add(dynamicItem);
                            m3uCount++;
                        }
                    }
                    if (m3uCount == 0) subM3u.Items.Add(new MenuFlyoutItem { Text = "(Folder trống)", IsEnabled = false });
                }
                else { subM3u.Items.Add(new MenuFlyoutItem { Text = "(Chưa có folder Playlists)", IsEnabled = false }); }
                menu.Items.Add(subM3u);

                if (this.Content != null && this.Content.XamlRoot != null) menu.XamlRoot = this.Content.XamlRoot;
                menu.ShowAt(sender as FrameworkElement);
                return;
            }
            if (lstMedia.ItemsSource == _listStream)
            {
                ShowInlineInput("ONLINE");
                return;
            }

            string filter =
                "Video Files|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.m4v;*.3gp;*.ts;*.m2ts;*.mts;*.vob;*.iso;*.mpg;*.mpeg;*.m3u8|" +
                "Audio Files|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.wma;*.opus;*.ape;*.alac|" +
                "Pictures|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|" +
                "All Files (*.*)|*.*";
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            string[] files = Win32Helper.ShowOpenFileDialog(hWnd, "Chọn tệp Media", filter);

            if (files.Length > 0)
            {
                if (lstMedia.ItemsSource != _listLocal)
                {
                    rbMedia.IsChecked = true;
                    OnNavTabClick(rbMedia, null);
                }

                int countAdded = 0;
                foreach (string filePath in files)
                {
                    var item = new MediaItem
                    {
                        FileName = System.IO.Path.GetFileName(filePath),
                        FullPath = filePath,
                        Type = System.IO.Path.GetExtension(filePath).Replace(".", "").ToUpper(),
                        Poster = null
                    };

                    _listLocal.Add(item);
                    _backupLocal.Add(item);
                    countAdded++;
                    _ = Task.Run(async () =>
                    {
                        var stream = await FastThumbnail.GetImageStreamAsync(item.FullPath);
                        if (stream != null)
                        {
                            this.DispatcherQueue.TryEnqueue(async () =>
                            {
                                try
                                {
                                    var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                                    await bmp.SetSourceAsync(stream.AsRandomAccessStream());
                                    item.Poster = bmp;
                                }
                                catch { }
                            });
                        }
                    });
                }

                if (countAdded > 0)
                {
                    UpdateStatus($"✅ Đã thêm {countAdded} file vào danh sách.");
                    UpdateListStats();
                }
            }
        }

        private void AddSingleLinkToTv(string url, string name)
        {
            var newItem = new MediaItem
            {
                FileName = name,
                FullPath = url,
                Type = "IPTV",
                Duration = "Live",
                ChannelName = "Quick Link"
            };

            _listTv.Add(newItem);
            _backupTv.Add(newItem);
            UpdateStatus($"✅ Đã thêm kênh: {name}");
            UpdateListStats();
        }

        private async Task AddTvLinkFromTextFileAsync()
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

                picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
                picker.FileTypeFilter.Add(".txt");
                picker.FileTypeFilter.Add(".list");

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    UpdateStatus($"⏳ Đang đọc file: {file.Name}...", true);
                    var lines = await Windows.Storage.FileIO.ReadLinesAsync(file);
                    int count = 0;

                    foreach (string line in lines)
                    {
                        string cleanLine = line.Trim();
                        if (string.IsNullOrEmpty(cleanLine) || cleanLine.StartsWith("#")) continue;
                        if (cleanLine.StartsWith("http") || cleanLine.StartsWith("rtmp") || cleanLine.StartsWith("udp"))
                        {
                            var newItem = new MediaItem
                            {
                                FileName = $"Link {count + 1} ({file.Name})",
                                FullPath = cleanLine,
                                Type = "IPTV",
                                Duration = "Live",
                                ChannelName = "Imported Text"
                            };

                            _listTv.Add(newItem);
                            _backupTv.Add(newItem);
                            count++;
                        }
                    }

                    if (count > 0)
                    {
                        UpdateStatus($"✅ Đã nhập {count} link từ file text.");
                        UpdateListStats();
                    }
                    else
                    {
                        UpdateStatus("⚠️ Không tìm thấy link hợp lệ trong file.", false, true);
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ Lỗi đọc file Text: {ex.Message}", false, true);
            }
        }
        private async Task AddTvFromFolderAsync()
        {
            try
            {
                var folderPicker = new Windows.Storage.Pickers.FolderPicker();
                WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                folderPicker.FileTypeFilter.Add("*");

                var folder = await folderPicker.PickSingleFolderAsync();
                if (folder != null)
                {
                    UpdateStatus($"⏳ Đang quét thư mục: {folder.Name}...", true);
                    var files = await folder.GetFilesAsync();
                    int countM3u = 0;

                    foreach (var file in files)
                    {
                        string ext = file.FileType.ToLower();
                        if (ext == ".m3u" || ext == ".m3u8")
                        {
                            HandleExternalPlaylist(file.Path);
                            countM3u++;
                        }
                    }

                    if (countM3u > 0)
                    {
                        UpdateStatus($"✅ Đã quét và nạp {countM3u} file M3U từ thư mục.");
                        UpdateListStats();
                    }
                    else
                    {
                        UpdateStatus("⚠️ Không tìm thấy file M3U nào trong thư mục này.", false, true);
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ Lỗi quét thư mục: {ex.Message}", false, true);
            }
        }

        private readonly HashSet<string> _allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v",
            ".3gp", ".3g2", ".ts", ".m2ts", ".mts", ".vob", ".iso", ".mpeg",
            ".mpg", ".mpe", ".ps", ".f4v", ".swf", ".rm", ".rmvb", ".asf",
            ".ogv", ".ogm", ".divx", ".xvid", ".mxf", ".dnxhd", ".prores",
            ".yuv", ".rgb", ".ivf", ".nut", ".m3u8", ".mpd",
            ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".wma", ".opus",
            ".ape", ".alac", ".aiff", ".dsf", ".dff",
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tiff", ".tif", ".svg"
        };
        private void btnCancelReset_Click(object sender, RoutedEventArgs e)
        {
            if (btnFactoryReset.Flyout != null)
            {
                btnFactoryReset.Flyout.Hide();
            }
        }

        private void btnConfirmReset_Click(object sender, RoutedEventArgs e)
        {
            if (btnFactoryReset.Flyout != null)
            {
                btnFactoryReset.Flyout.Hide();
            }

            try
            {
                AppSettings.ClearAll();
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null) key.DeleteValue("MediaLedInterface", false);
                }
            }
            catch { }
            swStartup.IsOn = false;
            swWakeLock.IsOn = false;
            swSponsorBlock.IsOn = true;

            sliderVolume.Value = 80;
            sliderSpeedSetting.Value = 1.0;
            sliderSeekSetting.Value = 5;

            txtWatchFolder.Text = "";
            txtBgPath.Text = "";
            _listLibrary.Clear();
            _backupLibrary.Clear();
            txtLibraryStatus.Text = "Trạng thái: Chưa chọn thư mục.";

            sldBright.Value = 0; sldContrast.Value = 0; sldSat.Value = 0; sldHue.Value = 0;
            sldGamma.Value = 0; sldRed.Value = 0; sldGreen.Value = 0; sldBlue.Value = 0;

            sldSubSize.Value = 55; sldSubMargin.Value = 50; cboSubColor.SelectedIndex = 0;
            sldSubDelay.Value = 0; sldAudioDelay.Value = 0;
            if (_engine != null)
            {
                _engine.IsSponsorBlockEnabled = true;
                _engine.PreventSleep(false);
                _engine.SetVolume(80);
                _engine.SetSpeed(1.0);
                _engine.SetBackgroundImage("");
                _engine.ResetVideoSettings();
                _engine.ResetAdvancedColor();
                cboEqPresets.SelectedIndex = 0;
                sldEq1.Value = 0; sldEq2.Value = 0; sldEq3.Value = 0; sldEq4.Value = 0; sldEq5.Value = 0;
                _engine.SetEqualizer(new double[] { 0, 0, 0, 0, 0 });
                if (swLogo != null) swLogo.IsOn = false;
                if (swTicker != null) swTicker.IsOn = false;
                cboColorFx.SelectedIndex = 0;
                swMotion.IsOn = false; swHdr.IsOn = false; swUpscale.IsOn = false;
                swDeband.IsOn = false; swDeinterlace.IsOn = false; swShader.IsOn = false;
            }
            UpdateStatus("♻️ Đã khôi phục cài đặt gốc thành công!", true);
            UpdateListStats();
        }
        private void btnDelete_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelectedItems();
        }

        private void lstMedia_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Delete)
            {
                DeleteSelectedItems();
            }
            else if (e.Key == Windows.System.VirtualKey.A)
            {
                var ctrlState = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
                if ((ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down)
                {
                    lstMedia.SelectAll();
                    e.Handled = true;
                }
            }
        }
        private void ShowInlineInput(string mode)
        {
            _inputMode = mode;
            grpInlineInput.Visibility = Visibility.Visible;
            txtInlineUrl.Text = "";
            txtInlineUrl.Focus(FocusState.Programmatic);
            if (mode == "IPTV")
                lblInlinePrompt.Text = "Nhập đường dẫn IPTV (M3U/JSON):";
            else
                lblInlinePrompt.Text = "Nhập Link Video (YouTube/Dailymotion/Facebook...):";
            UpdateStatus($"Đang chờ nhập link cho {mode}...");
        }

        private void btnInlineCancel_Click(object sender, RoutedEventArgs e)
        {
            grpInlineInput.Visibility = Visibility.Collapsed;
            txtInlineUrl.Text = "";
            UpdateStatus("Đã hủy nhập link.");
        }

        private async void btnInlineAdd_Click(object sender, RoutedEventArgs e)
        {
            string url = txtInlineUrl.Text.Trim();
            if (string.IsNullOrEmpty(url)) return;

            grpInlineInput.Visibility = Visibility.Collapsed;
            txtInlineUrl.Text = "";


            if (url.Contains("facebook.com") && !url.Contains("/videos/") && !url.Contains("/watch") && !url.Contains("/reel"))
            {
                if (url.EndsWith("/")) url = url.Substring(0, url.Length - 1);
                url += "/reels/";
                UpdateStatus("🔀 Chuyển sang chế độ Facebook Reels.");
            }
            bool isDailyPlaylist = url.Contains("/playlist/") || (url.Contains("dailymotion.com") && url.Contains("playlist="));
            bool isYtPlaylist = url.Contains("list=") || url.Contains("/playlist/") || url.Contains("/reels/") || url.Contains("tiktok.com/@");
            if (url.Contains("tiktok.com/@")) isYtPlaylist = true;

            int maxItems = 50;

            UpdateStatus($"⏳ Đang phân tích: {url}...");

            if (_inputMode == "IPTV")
            {
                if (rbTV.IsChecked != true)
                {
                    rbTV.IsChecked = true;
                    OnNavTabClick(rbTV, null);
                }

                UpdateStatus($"⏳ Đang phân tích danh sách IPTV: {url}...");

                try
                {
                    string content = await _httpClient.GetStringAsync(url);
                    content = content.Trim();
                    if (content.StartsWith("#EXTM3U") || content.StartsWith("#EXTINF"))
                    {
                        ParseM3UContent(content);
                        UpdateStatus($"✅ Đã nhập danh sách M3U thành công.");
                    }
                    else if (content.StartsWith("[") || content.StartsWith("{"))
                    {
                        ParseJsonTvContent(content);
                        UpdateStatus($"✅ Đã nhập danh sách JSON thành công.");
                    }
                    else
                    {
                        var singleItem = new MediaItem
                        {
                            FileName = "Kênh thủ công",
                            FullPath = url,
                            Type = "TV SINGLE",
                            ChannelName = "User Added",
                            Duration = "LIVE",
                            Poster = null
                        };

                        _listTv.Add(singleItem);
                        _backupTv.Add(singleItem);
                        UpdateStatus($"✅ Đã thêm kênh lẻ: {url}");
                        UpdateListStats();
                    }
                }
                catch (Exception ex)
                {

                    _listTv.Add(new MediaItem { FileName = "Link IPTV (Lỗi tải)", FullPath = url, Type = "TV RAW" });
                    UpdateStatus($"⚠️ Không đọc được nội dung playlist, đã thêm link thô.", false, true);
                }
                return;
            }
            else if (isDailyPlaylist)
            {
                var newItems = await ParseDailymotionPlaylistAsync(url, maxItems);

                if (newItems.Count > 0)
                {
                    foreach (var item in newItems) { _listStream.Add(item); _backupStream.Add(item); }
                    UpdateStatus($"✅ Đã tải {newItems.Count} video từ Playlist.");
                    return;
                }
                else
                {

                    UpdateStatus("⚠️ Không đọc được Playlist, đang thử tải video lẻ...");
                    var singleVideoItems = await ParseOnlineVideoAsync(url, false, 1);

                    if (singleVideoItems.Count > 0)
                    {
                        foreach (var item in singleVideoItems) { _listStream.Add(item); _backupStream.Add(item); }
                        UpdateStatus("✅ Đã thêm video lẻ (Playlist lỗi/riêng tư).");
                        return;
                    }
                    else
                    {
                        _listStream.Add(new MediaItem { FileName = "Link: " + url, FullPath = url, Type = "ONLINE RAW", Duration = "..." });
                        UpdateStatus("⚠️ Link không xác định.");
                        return;
                    }
                }
            }
            else
            {
                var newItems = await ParseOnlineVideoAsync(url, isYtPlaylist, maxItems);

                if (newItems.Count > 0)
                {
                    foreach (var item in newItems)
                    {
                        _listStream.Add(item);
                        _backupStream.Add(item);
                        FetchMetadataInBackground(item);
                    }

                    UpdateStatus($"✅ Đã tải nhanh {newItems.Count} video (Đang cập nhật chi tiết ngầm...).");
                    UpdateListStats();
                }
                else
                {
                    goto Fallback;
                }
                return;
            }

        Fallback:
            _listStream.Add(new MediaItem { FileName = "Link: " + url, FullPath = url, Type = "ONLINE RAW", Duration = "..." });
            UpdateStatus("⚠️ Link thô (Không lấy được Metadata).");
            UpdateListStats();
        }

        private void txtInlineUrl_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                btnInlineAdd_Click(sender, new RoutedEventArgs());
            }
        }
        private void DeleteSelectedItems()
        {
            var selectedItems = lstMedia.SelectedItems.Cast<MediaItem>().ToList();
            var activeList = CurrentList;
            foreach (var item in selectedItems)
            {
                activeList.Remove(item);
            }
            foreach (var item in selectedItems)
            {
                activeList.Remove(item);
                if (activeList == _listLocal) _backupLocal.Remove(item);
                else if (activeList == _listStream) _backupStream.Remove(item);
                else if (activeList == _listTv) _backupTv.Remove(item);
            }
            UpdateListStats();
        }

        private void lstMedia_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            PlaySelectedMedia();
        }

        private void btnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (_engine == null) return;

            var targetItem = lstMedia.SelectedItem as MediaItem;
            if (targetItem == null)
            {
                UpdateStatus("⚠️ Vui lòng chọn một file hoặc link để phát!", false, true);
                return;
            }

            if (_playingItem == targetItem)
            {
                _engine.Resume();
                targetItem.IsPaused = false;
                btnPlay.Visibility = Visibility.Collapsed;
                btnPause.Visibility = Visibility.Visible;

                // --- THÊM CODE MỚI ---
                UpdatePlayingTabIndicator(); // Hiện lại icon playing ở Tab
                                             // ---------------------

                UpdateStatus($"▶ Tiếp tục: {targetItem.FileName}", true);
            }
            else
            {
                PlaySelectedMedia();
            }
        }
        private void btnPause_Click(object sender, RoutedEventArgs e)
        {
            if (_engine != null) _engine.Pause();

            // Cập nhật trạng thái item
            if (lstMedia.SelectedItem is MediaItem item)
            {
                item.IsPaused = true;
            }
            else if (_playingItem != null)
            {
                _playingItem.IsPaused = true;
            }

            btnPause.Visibility = Visibility.Collapsed;
            btnPlay.Visibility = Visibility.Visible;

            StopVisualizer();

            UpdatePlayingTabIndicator();

            UpdateStatus("⏸ Đã tạm dừng.", true);
        }

        private void InitializeAppWindow()
        {
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            Microsoft.UI.WindowId wndId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(wndId);

            if (_appWindow != null)
            {
                _appWindow.Resize(new Windows.Graphics.SizeInt32(1367, 720));
                _appWindow.Title = "MediaLed Interface";

                string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");

                if (System.IO.File.Exists(iconPath))
                {
                    _appWindow.SetIcon(iconPath);
                }

                var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(wndId, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
                if (displayArea != null)
                {
                    var centeredX = (displayArea.WorkArea.Width - 1080) / 2;
                    var centeredY = (displayArea.WorkArea.Height - 720) / 2;
                    _appWindow.Move(new Windows.Graphics.PointInt32(centeredX, centeredY));
                }
            }

            if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
            {
                this.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
            }
            SetupCloseBehavior();
        }

        private void ExtendTitleBar()
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(null);
        }

        private void PlaySelectedMedia()
        {
            ClearAllSponsorMarks();
            if (_engine == null) return;

            // Reset trạng thái bài cũ (nếu có)
            if (_playingItem != null)
            {
                _playingItem.IsPlaying = false;
                _playingItem.IsPaused = false;
            }

            if (lstMedia.SelectedItem is MediaItem selectedItem)
            {
                _playingItem = selectedItem;
                _playingItem.IsPlaying = true;

                // QUAN TRỌNG: Đảm bảo IsPaused là false khi bắt đầu phát
                _playingItem.IsPaused = false;

                _engine.SetHttpHeaders(selectedItem.UserAgent, selectedItem.Referrer);
                _engine.PlayTransition(selectedItem.FullPath);
                _engine.Resume();

                // Cập nhật giao diện (Lúc này IsPaused = false nên GIF sẽ hiện)
                UpdatePlayingTabIndicator();
                UpdateMpvLayout();

                btnPlay.Visibility = Visibility.Collapsed;
                btnPause.Visibility = Visibility.Visible;
                UpdateStatus($"▶ Đang phát: {selectedItem.FileName}", true);
                _isAutoChanging = false;

                bool isTvMode = (lstMedia.ItemsSource == _listTv);
                if (!isTvMode)
                {
                    StartVisualizer(); // Gọi hàm bắt âm thanh thật
                }
            }
        }
        public void BringToFront()
        {
            if (_appWindow != null)
            {
                if (_appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter op)
                {
                    if (op.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)
                    {
                        op.Restore();
                    }
                }
            }
            this.Activate();
        }

        public async void HandleExternalPlaylist(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            int retries = 0;
            while (!_isInitialized)
            {
                await Task.Delay(200);
                retries++;
                if (retries > 20) return;
            }
            BringToFront();
            if (rbTV != null)
            {
                rbTV.IsChecked = true;
                OnNavTabClick(rbTV, null);
            }

            UpdateStatus($"📂 Đang đọc danh sách kênh từ: {System.IO.Path.GetFileName(filePath)}...", true);

            try
            {
                string content = await System.IO.File.ReadAllTextAsync(filePath);
                ParseM3UContent(content);

                if (_listTv.Count > 0)
                {
                    lstMedia.SelectedIndex = 0;
                    if (_playingItem != null)
                    {
                        _playingItem.IsPlaying = false;
                        _playingItem = null;
                    }

                    PlaySelectedMedia();

                    UpdateStatus($"✅ Đã nhập M3U và đang phát kênh đầu tiên!", true);
                }
                else
                {
                    UpdateStatus($"⚠ File M3U rỗng hoặc không đọc được.", false, true);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ Lỗi đọc file M3U: {ex.Message}", false, true);
            }
        }
        public async void HandleExternalFiles(IReadOnlyList<string> filePaths)
        {
            if (filePaths == null || filePaths.Count == 0) return;
            int retries = 0;
            while (!_isInitialized || _engine == null)
            {
                await Task.Delay(200);
                retries++;
                if (retries > 25) return;
            }

            if (rbMedia != null && rbMedia.IsChecked != true)
            {
                rbMedia.IsChecked = true;
                OnNavTabClick(rbMedia, null);
            }


            MediaItem? itemToPlay = null;
            int countAdded = 0;

            foreach (string path in filePaths)
            {

                string cleanPath = path.Replace("\"", "").Trim();


                string ext = System.IO.Path.GetExtension(cleanPath).ToLower();

                if (ext == ".exe" || ext == ".dll" || ext == ".pdb" || ext == ".config") continue;

                if (cleanPath.Contains("--autostart")) continue;
                if (!_allowedExtensions.Contains(ext)) continue;

                var item = new MediaItem
                {
                    FileName = System.IO.Path.GetFileName(cleanPath),
                    FullPath = cleanPath,
                    Type = ext.Replace(".", "").ToUpper(),
                    Duration = "File ngoài",
                    Poster = null
                };

                _listLocal.Add(item);
                _backupLocal.Add(item);

                if (itemToPlay == null) itemToPlay = item;
                countAdded++;
                _ = Task.Run(async () =>
                {
                    var stream = await FastThumbnail.GetImageStreamAsync(item.FullPath);
                    if (stream != null)
                    {
                        this.DispatcherQueue.TryEnqueue(async () =>
                        {
                            try
                            {
                                var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                                await bmp.SetSourceAsync(stream.AsRandomAccessStream());
                                item.Poster = bmp;
                            }
                            catch { }
                        });
                    }
                });
            }

            if (countAdded == 0) return;

            UpdateStatus($"📂 Đã thêm {countAdded} file mới.", true);
            UpdateListStats();
            if (itemToPlay != null)
            {
                lstMedia.SelectedItem = itemToPlay;
                lstMedia.ScrollIntoView(itemToPlay);

                if (_playingItem != null)
                {
                    _playingItem.IsPlaying = false;
                    _playingItem.IsPaused = false;
                    _playingItem = null;
                }

                PlaySelectedMedia();
                BringToFront();
            }
        }

        private void UpdateMediaInfoUI()
        {
            if (_engine == null || expMediaInfo == null || !expMediaInfo.IsExpanded) return;

            var info = _engine.GetTechnicalInfo();

            if (!string.IsNullOrEmpty(info.Filename)) infFileName.Text = info.Filename;
            infResolution.Text = info.Resolution;
            infFps.Text = info.Fps;
            infVideoCodec.Text = info.VideoCodec;
            infAudioCodec.Text = info.AudioCodec;
            infBitrate.Text = info.FileSize;
            infDuration.Text = info.Duration;
        }
        private void CustomizeTitleBarColors()
        {
            if (_appWindow != null && Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
            {
                var titleBar = _appWindow.TitleBar;
                titleBar.ButtonForegroundColor = Colors.White;
                titleBar.ButtonBackgroundColor = Colors.Transparent;

                titleBar.ButtonHoverForegroundColor = Colors.White;
                titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(50, 255, 255, 255);

                titleBar.ButtonPressedForegroundColor = Colors.White;
                titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(100, 255, 255, 255);

                titleBar.ButtonInactiveForegroundColor = Colors.Gray;
                titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            }
        }

        private void SetCursor(object element, InputSystemCursorShape shape)
        {
            var uiElement = element as UIElement;
            if (uiElement != null)
            {
                var propertyInfo = typeof(UIElement).GetProperty("ProtectedCursor", BindingFlags.Instance | BindingFlags.NonPublic);
                if (propertyInfo != null)
                {
                    var cursor = InputSystemCursor.Create(shape);
                    propertyInfo.SetValue(uiElement, cursor);
                }
            }
        }
        private double _animStartNavW;
        private double _animTargetNavW;
        private double _animTotalLeftW;
        private DateTime _animStartTime;
        private bool _isAnimatingNav = false;
        private const double ANIM_DURATION_MS = 300;
        private void OnNavAnimating(object sender, object e)
        {
            double elapsed = (DateTime.Now - _animStartTime).TotalMilliseconds;
            double progress = elapsed / ANIM_DURATION_MS;
            if (progress >= 1.0)
            {
                progress = 1.0;
                CompositionTarget.Rendering -= OnNavAnimating;
                _isAnimatingNav = false;
                if (_resizeTimer != null) { _resizeTimer.Stop(); _resizeTimer.Start(); }
            }

            double ease = 1 - Math.Pow(1 - progress, 3);
            double currentNavW = _animStartNavW + (_animTargetNavW - _animStartNavW) * ease;
            double currentListW = _animTotalLeftW - currentNavW;
            if (currentListW < 0) currentListW = 0;
            AreaNav.Width = currentNavW;
            ColSidebar.Width = new GridLength(currentListW);
        }
        private void ResetCursor(object element)
        {
            var uiElement = element as UIElement;
            if (uiElement != null)
            {
                var propertyInfo = typeof(UIElement).GetProperty("ProtectedCursor", BindingFlags.Instance | BindingFlags.NonPublic);
                if (propertyInfo != null)
                {
                    propertyInfo.SetValue(uiElement, null);
                }
            }
        }

        private void GridSplitter_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            SetCursor(sender, InputSystemCursorShape.SizeWestEast);
        }

        private void GridSplitter_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (!_isResizing)
            {
                ResetCursor(sender);
            }
        }

        private void GridSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isResizing = true;
            (sender as UIElement)?.CapturePointer(e.Pointer);
        }

        private void GridSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isResizing = false;
            if (sender is UIElement splitter)
            {
                splitter.ReleasePointerCapture(e.Pointer);
                ResetCursor(sender);
            }
        }

        private void GridSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_isResizing)
            {
                try
                {
                    if (this.Content is UIElement rootElement)
                    {
                        var point = e.GetCurrentPoint(rootElement).Position;
                        double navWidth = AreaNav.ActualWidth;
                        double newWidth = point.X - navWidth;
                        if (newWidth > 200 && newWidth < 800)
                        {
                            ColSidebar.Width = new GridLength(newWidth);
                            if (_resizeDebounceTimer != null)
                            {
                                _resizeDebounceTimer.Stop();
                                _resizeDebounceTimer.Start();
                            }
                        }
                    }
                }
                catch { }
            }
        }

        private async System.Threading.Tasks.Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?> GetFileThumbnailAsync(Windows.Storage.StorageFile file)
        {
            try
            {
                var thumbnail = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem, 256);

                if (thumbnail != null)
                {
                    var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                    await bitmap.SetSourceAsync(thumbnail);
                    return bitmap;
                }
            }
            catch
            {
            }
            return null;
        }

        private async Task<List<MediaItem>> ParseOnlineVideoAsync(string url, bool isPlaylist, int maxLimit = 30)
        {
            var finalResults = new List<MediaItem>();

            string exePath = System.IO.Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe");
            if (!System.IO.File.Exists(exePath))
            {
                string debugPath = System.IO.Path.Combine(AppContext.BaseDirectory, "..\\..\\..\\yt-dlp.exe");
                if (System.IO.File.Exists(debugPath)) exePath = debugPath;
                else return finalResults;
            }
            var rawData = new List<(string Title, string Url, string Thumb, string Uploader, string Duration, string ID, string Extractor)>();

            await Task.Run(() =>
            {
                try
                {
                    string limitArg = isPlaylist ? $"--playlist-end {maxLimit}" : "";
                    string playlistArg = isPlaylist ? "--yes-playlist" : "--no-playlist";

                    string scanMode = "--flat-playlist";
                    if (url.Contains("facebook.com") || url.Contains("fb.watch"))
                    {
                        scanMode = "";
                        url = url.Replace("www.facebook.com", "m.facebook.com");
                    }
                    string args = $"--no-config --encoding utf-8 --user-agent \"{MT_USER_AGENT}\" {playlistArg} {limitArg} {scanMode} --skip-download -j --no-warnings --ignore-errors \"{url}\"";

                    var psi = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = args,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8
                    };

                    using (var process = new Process { StartInfo = psi })
                    {
                        process.Start();
                        _ = Task.Run(() => { try { process.StandardError.ReadToEnd(); } catch { } });

                        while (!process.StandardOutput.EndOfStream)
                        {
                            string jsonLine = process.StandardOutput.ReadLine();
                            if (string.IsNullOrWhiteSpace(jsonLine)) continue;

                            try
                            {

                                var node = System.Text.Json.Nodes.JsonNode.Parse(jsonLine);
                                if (node == null) continue;


                                string id = node["id"]?.ToString() ?? "";
                                string title = node["title"]?.ToString() ?? $"Video {id}";
                                string webUrl = node["webpage_url"]?.ToString() ?? node["url"]?.ToString() ?? "";
                                string uploader = node["uploader"]?.ToString() ?? node["channel"]?.ToString() ?? "Online";
                                string extractor = node["extractor_key"]?.ToString() ?? "Generic";

                                string durationStr = node["duration"]?.ToString() ?? "0";

                                string thumb = node["thumbnail"]?.ToString() ?? "";

                                if (string.IsNullOrEmpty(thumb))
                                {
                                    var thumbsArr = node["thumbnails"]?.AsArray();
                                    if (thumbsArr != null && thumbsArr.Count > 0)
                                    {
                                        thumb = thumbsArr[thumbsArr.Count - 1]?["url"]?.ToString() ?? "";
                                    }
                                }
                                if (title == "[Deleted video]" || title == "[Private video]") continue;

                                rawData.Add((title, webUrl, thumb, uploader, durationStr, id, extractor));
                            }
                            catch { /* Bỏ qua dòng lỗi JSON */ }
                        }
                        process.WaitForExit();
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.Message); }
            });
            foreach (var data in rawData)
            {
                string durDisplay = data.Uploader;
                if (string.IsNullOrEmpty(durDisplay)) durDisplay = "Unknown Channel";

                string smartType = data.Extractor.ToUpper();
                if (smartType.Contains("YOUTUBE")) smartType = "YOUTUBE";
                else if (smartType.Contains("FACEBOOK")) smartType = "FACEBOOK";
                else if (smartType.Contains("TIKTOK")) smartType = "TIKTOK";
                else if (smartType.Contains("DAILYMOTION")) smartType = "DAILYMOTION";

                var item = new MediaItem
                {
                    FileName = data.Title,
                    FullPath = data.Url,
                    Type = smartType,
                    ChannelName = data.Uploader,
                    Duration = durDisplay,
                    Poster = null
                };
                _ = Task.Run(async () =>
                {
                    string bestThumbUrl = data.Thumb;
                    if (smartType == "YOUTUBE" && (string.IsNullOrEmpty(bestThumbUrl) || bestThumbUrl.Contains(".webp")))
                    {
                        if (!string.IsNullOrEmpty(data.ID)) bestThumbUrl = $"https://i.ytimg.com/vi/{data.ID}/hqdefault.jpg";
                    }

                    var bitmap = await LoadImageSecurelyAsync(bestThumbUrl);
                    if (bitmap == null && smartType == "YOUTUBE" && !string.IsNullOrEmpty(data.ID))
                    {
                        bitmap = await LoadImageSecurelyAsync($"https://i.ytimg.com/vi/{data.ID}/mqdefault.jpg");
                    }

                    if (bitmap != null)
                    {
                        this.DispatcherQueue.TryEnqueue(() => item.Poster = bitmap);
                    }
                });

                finalResults.Add(item);
            }

            return finalResults;
        }
        private async Task<List<MediaItem>> ParseDailymotionPlaylistAsync(string playlistUrl, int limit = 30)
        {
            var results = new List<MediaItem>();

            string playlistId = "";

            var match = System.Text.RegularExpressions.Regex.Match(playlistUrl, @"(?:playlist\/|playlist=)([a-zA-Z0-9]+)");
            if (match.Success)
            {
                playlistId = match.Groups[1].Value;
            }

            if (string.IsNullOrEmpty(playlistId)) return results;

            try
            {
                string apiUrl = $"https://api.dailymotion.com/playlist/{playlistId}/videos?fields=id,title,thumbnail_720_url,thumbnail_480_url,thumbnail_url,owner.username,duration,url&limit={limit}";

                using (var request = new HttpRequestMessage(HttpMethod.Get, apiUrl))
                {
                    request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                    using (var response = await _httpClient.SendAsync(request))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            string json = await response.Content.ReadAsStringAsync();
                            var root = System.Text.Json.Nodes.JsonNode.Parse(json);
                            var list = root?["list"]?.AsArray();

                            if (list != null)
                            {
                                foreach (var item in list)
                                {
                                    string title = item?["title"]?.ToString() ?? "Unknown Video";
                                    string videoUrl = item?["url"]?.ToString() ?? "";
                                    string thumbUrl = item?["thumbnail_720_url"]?.ToString();
                                    if (string.IsNullOrEmpty(thumbUrl)) thumbUrl = item?["thumbnail_480_url"]?.ToString();
                                    if (string.IsNullOrEmpty(thumbUrl)) thumbUrl = item?["thumbnail_url"]?.ToString();

                                    string owner = item?["owner.username"]?.ToString() ?? "Dailymotion";
                                    double durationSec = item?["duration"]?.GetValue<double>() ?? 0;
                                    TimeSpan t = TimeSpan.FromSeconds(durationSec);
                                    string durDisplay = (t.TotalHours >= 1) ? t.ToString(@"hh\:mm\:ss") : t.ToString(@"mm\:ss");

                                    var mediaItem = new MediaItem
                                    {
                                        FileName = title,
                                        FullPath = videoUrl,
                                        Type = "DAILYMOTION",
                                        ChannelName = owner,
                                        Duration = owner,
                                        Poster = null
                                    };
                                    if (!string.IsNullOrEmpty(thumbUrl))
                                    {
                                        _ = Task.Run(async () =>
                                        {
                                            var bitmap = await LoadImageSecurelyAsync(thumbUrl);
                                            if (bitmap != null) this.DispatcherQueue.TryEnqueue(() => mediaItem.Poster = bitmap);
                                        });
                                    }

                                    results.Add(mediaItem);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Daily API Error: " + ex.Message);
            }

            return results;
        }

        private async Task<Microsoft.UI.Xaml.Media.ImageSource?> LoadImageSecurelyAsync(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;

            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                    using (var response = await client.GetAsync(url))
                    {
                        if (!response.IsSuccessStatusCode) return null;

                        byte[] data = await response.Content.ReadAsByteArrayAsync();

                        if (data != null && data.Length > 0)
                        {
                            var tcs = new TaskCompletionSource<Microsoft.UI.Xaml.Media.ImageSource?>();

                            this.DispatcherQueue.TryEnqueue(async () =>
                            {
                                try
                                {
                                    var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();

                                    using (var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                                    {
                                        await stream.WriteAsync(data.AsBuffer());
                                        stream.Seek(0);
                                        await bitmap.SetSourceAsync(stream);
                                    }
                                    tcs.SetResult(bitmap);
                                }
                                catch
                                {
                                    tcs.SetResult(null);
                                }
                            });

                            return await tcs.Task;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Lỗi tải ảnh: {ex.Message}");
            }
            return null;
        }
        private void FetchMetadataInBackground(MediaItem item)
        {
            Task.Run(async () =>
            {
                await _metadataSemaphore.WaitAsync();

                try
                {
                    if (item == null) return;

                    string exePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yt-dlp.exe");
                    if (!System.IO.File.Exists(exePath))
                    {
                        exePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..\\..\\..\\yt-dlp.exe");
                        if (!System.IO.File.Exists(exePath)) return;
                    }

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = $"\"{item.FullPath}\" --dump-json --no-playlist --skip-download --no-warnings --socket-timeout 5",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8
                    };

                    using (var process = new Process { StartInfo = startInfo })
                    {
                        process.Start();
                        string jsonOutput = await process.StandardOutput.ReadToEndAsync();
                        await process.WaitForExitAsync();

                        if (!string.IsNullOrEmpty(jsonOutput))
                        {
                            try
                            {
                                var node = JsonNode.Parse(jsonOutput);
                                if (node != null)
                                {
                                    string title = node["title"]?.ToString();
                                    string thumbnail = node["thumbnail"]?.ToString();

                                    // --- SỬA ĐOẠN NÀY ---
                                    // Lấy tên người đăng (uploader) thay vì lấy duration
                                    string uploader = node["uploader"]?.ToString() ?? node["channel"]?.ToString();

                                    this.DispatcherQueue.TryEnqueue(() =>
                                    {
                                        if (!string.IsNullOrEmpty(title)) item.FileName = title;

                                        // Cập nhật lại Tên kênh vào vị trí Duration để hiển thị đồng bộ
                                        if (!string.IsNullOrEmpty(uploader))
                                        {
                                            item.Duration = uploader;
                                            item.ChannelName = uploader;
                                        }

                                        // (Đã xóa dòng cập nhật thời gian: item.Duration = TimeSpan...)

                                        if (!string.IsNullOrEmpty(thumbnail))
                                        {
                                            _ = Task.Run(async () =>
                                            {
                                                var bmp = await LoadImageSecurelyAsync(thumbnail);
                                                if (bmp != null)
                                                {
                                                    this.DispatcherQueue.TryEnqueue(() => item.Poster = bmp);
                                                }
                                            });
                                        }
                                    });
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.Message); }
                finally
                {
                    _metadataSemaphore.Release();
                }
            });
        }
        private async Task AddTvFromUrlAsync()
        {
            var inputTextBox = new TextBox { Height = 35, PlaceholderText = "Ví dụ: https://xem.hoiquan.click/" };
            var dialog = new ContentDialog
            {
                XamlRoot = this.Content.XamlRoot,
                Title = "Nhập đường dẫn IPTV",
                PrimaryButtonText = "Tải danh sách",
                CloseButtonText = "Hủy",
                DefaultButton = ContentDialogButton.Primary,
                Content = inputTextBox
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                string url = inputTextBox.Text.Trim();
                if (string.IsNullOrEmpty(url)) return;

                UpdateStatus($"⏳ Đang tải playlist từ: {url}...");

                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        request.Headers.Add("User-Agent", MT_USER_AGENT);

                        var response = await _httpClient.SendAsync(request);
                        response.EnsureSuccessStatusCode();

                        string content = await response.Content.ReadAsStringAsync();
                        content = content.Trim();
                        if (content.StartsWith("[") || content.StartsWith("{"))
                        {
                            ParseJsonTvContent(content);
                            UpdateStatus("✅ Đã nhập danh sách dạng JSON.");
                        }
                        else
                        {
                            ParseM3UContent(content);
                            UpdateStatus("✅ Đã nhập danh sách dạng M3U.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _listTv.Add(new MediaItem
                    {
                        FileName = "Link Lỗi/Trực tiếp",
                        FullPath = url,
                        Type = "TV RAW",
                        Poster = null
                    });
                    UpdateStatus($"⚠️ Lỗi tải playlist: {ex.Message}", false, true);
                }
            }
            UpdateListStats();
        }
        private async Task AddTvFromFileAsync()
        {
            var picker = new FileOpenPicker();
            var hWnd = WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(picker, hWnd);
            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeFilter.Add(".m3u");
            picker.FileTypeFilter.Add(".m3u8");
            picker.FileTypeFilter.Add(".txt");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                string content = await Windows.Storage.FileIO.ReadTextAsync(file);
                ParseM3UContent(content);
            }
        }
        private void ParseM3UContent(string content)
        {
            if (lstMedia.ItemsSource != _listTv)
            {
                rbTV.IsChecked = true;
                OnNavTabClick(rbTV, null);
            }
            var lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            string currentName = "Unknown Channel";
            string currentGroup = "IPTV";
            string currentLogo = "";
            string currentUa = "";
            string currentRef = "";

            foreach (var line in lines)
            {
                string l = line.Trim();
                if (string.IsNullOrEmpty(l)) continue;
                if (l.StartsWith("#EXTINF"))
                {
                    currentName = "Channel";
                    currentGroup = "Chung";
                    currentLogo = "";
                    currentUa = "";
                    currentRef = "";

                    int lastComma = l.LastIndexOf(',');
                    if (lastComma != -1 && lastComma < l.Length - 1)
                    {
                        currentName = l.Substring(lastComma + 1).Trim();
                    }

                    var mGroup = System.Text.RegularExpressions.Regex.Match(l, "group-title=\"([^\"]+)\"");
                    if (mGroup.Success) currentGroup = mGroup.Groups[1].Value.Trim();

                    var mTvgLogo = System.Text.RegularExpressions.Regex.Match(l, "tvg-logo=\"([^\"]+)\"");
                    if (mTvgLogo.Success)
                    {
                        currentLogo = mTvgLogo.Groups[1].Value.Trim();
                    }
                    else
                    {
                        var mLogo = System.Text.RegularExpressions.Regex.Match(l, "\\blogo=\"([^\"]+)\"");
                        if (mLogo.Success)
                        {
                            currentLogo = mLogo.Groups[1].Value.Trim();
                        }
                    }

                }
                else if (l.StartsWith("#EXTVLCOPT") || l.StartsWith("#EXTHTTP"))
                {
                    if (l.Contains("http-user-agent="))
                        currentUa = l.Substring(l.IndexOf("http-user-agent=") + 16).Trim();
                    else if (l.Contains("http-referrer="))
                        currentRef = l.Substring(l.IndexOf("http-referrer=") + 14).Trim();
                }
                else if (!l.StartsWith("#"))
                {
                    if (l.Length < 5) continue;

                    var item = new MediaItem
                    {
                        FileName = currentName,
                        FullPath = l,
                        Type = "TV CHANNEL",
                        ChannelName = currentGroup,
                        Duration = "LIVE",
                        UserAgent = currentUa,
                        Referrer = currentRef,
                        Poster = null
                    };

                    if (!string.IsNullOrEmpty(currentLogo))
                    {
                        string logoUrl = currentLogo;
                        _ = Task.Run(async () =>
                        {
                            await _logoSemaphore.WaitAsync();
                            try
                            {
                                var img = await LoadImageSecurelyAsync(logoUrl);
                                if (img != null)
                                {
                                    this.DispatcherQueue.TryEnqueue(() => item.Poster = img);
                                }
                            }
                            catch { }
                            finally
                            {
                                _logoSemaphore.Release();
                            }
                        });
                    }

                    _listTv.Add(item);
                    _backupTv.Add(item);
                }
            }
            UpdateListStats();
        }
        private void InitializeNetworkMonitor()
        {
            Task.Run(() =>
            {
                FindActiveInterface();
                GetWifiSsidAsync();

                this.DispatcherQueue.TryEnqueue(() =>
                {
                    _netTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    _netTimer.Tick += (s, e) => UpdateNetworkStats();
                    _netTimer.Start();
                });
            });

            NetworkChange.NetworkAddressChanged += (s, e) =>
            {
                Task.Run(() =>
                {
                    FindActiveInterface();
                    if (_activeNic != null && _activeNic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    {
                        GetWifiSsidAsync();
                    }
                });
            };
        }

        private void FindActiveInterface()
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                _activeNic = null;
                return;
            }
            NetworkInterface? foundNic = null;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus == OperationalStatus.Up &&
                   (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet || nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
                {
                    var props = nic.GetIPProperties();
                    if (props.GatewayAddresses.Count > 0)
                    {
                        foundNic = nic;
                        break;
                    }
                }
            }
            if (foundNic != null)
            {
                _activeNic = foundNic;
                _lastBytesRecv = foundNic.GetIPStatistics().BytesReceived;
                _lastBytesSent = foundNic.GetIPStatistics().BytesSent;
            }
            else
            {
                _activeNic = null;
            }
        }

        private async void GetWifiSsidAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    var proc = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "netsh",
                            Arguments = "wlan show interfaces",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true,
                            StandardOutputEncoding = Encoding.UTF8
                        }

                    };

                    proc.Start();
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();

                    var match = Regex.Match(output, @"^\s*SSID\s*:\s*(.+)$", RegexOptions.Multiline);
                    if (match.Success)
                    {
                        _currentSsid = match.Groups[1].Value.Trim();
                    }
                    else
                    {
                        _currentSsid = "Wi-Fi";
                    }
                }
                catch
                {
                    _currentSsid = "Wi-Fi";
                }
            });
        }

        private void UpdateNetworkStats()
        {
            if (_activeNic == null)
            {

                FindActiveInterface();
                if (_activeNic == null)
                {
                    UpdateNetLabel("\uf140 Không có kết nối Internet", 0, 0);
                    return;
                }
            }

            try
            {
                long recv = _activeNic.GetIPStatistics().BytesReceived;
                long sent = _activeNic.GetIPStatistics().BytesSent;

                long down = recv - _lastBytesRecv;
                long up = sent - _lastBytesSent;
                if (down < 0) down = 0;
                if (up < 0) up = 0;

                _sessionDownloaded += down;
                _lastBytesRecv = recv;
                _lastBytesSent = sent;

                string name = "\uE839 Ethernet";

                if (_activeNic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                {
                    string ssidDisplay = !string.IsNullOrEmpty(_currentSsid) ? _currentSsid : "Wi-Fi";
                    name = $"\uE701 {ssidDisplay}";
                }

                UpdateNetLabel(name, down, up);
            }
            catch
            {
                _activeNic = null;
                UpdateNetLabel("\uf140 Không có kết nối Internet", 0, 0);
            }
        }

        private void UpdateNetLabel(string name, long down, long up)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                if (txtNetworkStatus == null) return;

                if (name.Contains("No Internet"))
                {
                    txtNetworkStatus.Text = $"{name}";
                    txtNetworkStatus.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
                }
                else
                {
                    txtNetworkStatus.Text = $"{name}   \uE64F {FormatBytes(down)}/s   \uE650 {FormatBytes(up)}/s   \uE74E {FormatBytes(_sessionDownloaded)}";
                    txtNetworkStatus.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
    Windows.UI.Color.FromArgb(255, 255, 102, 0)
);
                }
            });
        }
        private void UpdateStatus(string message, bool isPersistent = false, bool isError = false)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                if (txtAppStatus == null || iconStatus == null) return;

                txtAppStatus.Text = message;
                if (isError)
                {
                    txtAppStatus.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
                    iconStatus.Glyph = "\uE783";
                    iconStatus.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
                }
                else
                {
                    txtAppStatus.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gainsboro);
                    iconStatus.Glyph = "\uE946";
                    iconStatus.Foreground = new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue);
                }

                if (isPersistent)
                {
                    _savedStatus = message;
                    _persistentStatus = message;
                    _statusTimer.Stop();
                }
                else
                {
                    _statusTimer.Stop();
                    _statusTimer.Start();
                }
            });
        }
        private string GetNextSongName()
        {
            if (CurrentList == null || CurrentList.Count == 0) return "(Hết danh sách)";

            int currentIndex = lstMedia.SelectedIndex;
            int nextIndex = -1;
            switch (_currentMode)
            {
                case PlayerMode.RepeatOne:
                    return "(Lặp lại bài này)";

                case PlayerMode.Shuffle:
                    return "(Bài ngẫu nhiên)";

                case PlayerMode.RepeatAll:
                    nextIndex = (currentIndex >= CurrentList.Count - 1) ? 0 : currentIndex + 1;
                    break;

                case PlayerMode.Off:
                default:
                    if (currentIndex >= CurrentList.Count - 1) return "(Dừng phát)";
                    nextIndex = currentIndex + 1;
                    break;
            }

            if (nextIndex != -1 && nextIndex < CurrentList.Count)
            {
                return CurrentList[nextIndex].FileName;
            }
            return "(Không xác định)";
        }
        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = (decimal)bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number = number / 1024;
                counter++;
            }
            return string.Format("{0:n1} {1}", number, suffixes[counter]);
        }
        private void ParseJsonTvContent(string jsonContent)
        {
            lstMedia.ItemsSource = _listTv;

            try
            {
                var root = JsonNode.Parse(jsonContent);
                if (root == null) return;
                var groups = root["groups"]?.AsArray();
                if (groups != null && groups.Count > 0)
                {
                    foreach (var group in groups)
                    {
                        string groupName = group?["name"]?.ToString() ?? "Nổi Bật";
                        var channels = group?["channels"]?.AsArray();

                        if (channels != null)
                        {
                            foreach (var channel in channels)
                            {
                                string name = channel?["name"]?.ToString() ?? "Unknown";
                                string logo = channel?["image"]?["url"]?.ToString() ?? "";
                                string streamUrl = GetUrlFromNestedJson(channel);
                                if (!string.IsNullOrEmpty(streamUrl))
                                {
                                    var mediaItem = new MediaItem
                                    {
                                        FileName = name,
                                        FullPath = streamUrl,
                                        Type = "TV ONLINE",
                                        ChannelName = groupName,
                                        Duration = "LIVE"
                                    };

                                    if (!string.IsNullOrEmpty(logo))
                                    {
                                        try { mediaItem.Poster = new BitmapImage(new Uri(logo)); } catch { }
                                    }
                                    _listTv.Add(mediaItem);
                                }
                            }
                        }
                    }
                    return;
                }
                JsonArray? items = null;
                if (root is JsonArray arr) items = arr;
                else if (root is JsonObject obj)
                {
                    foreach (var kvp in obj)
                    {
                        if (kvp.Value is JsonArray a && a.Count > 0)
                        {
                            items = a;
                            break;
                        }
                    }
                }

                if (items != null)
                {
                    foreach (var item in items)
                    {
                        string name = item?["name"]?.ToString() ?? item?["title"]?.ToString() ?? "Unknown";
                        string url = item?["url"]?.ToString() ?? item?["link"]?.ToString() ?? item?["file"]?.ToString() ?? "";
                        string logo = item?["logo"]?.ToString() ?? item?["image"]?.ToString() ?? "";

                        if (!string.IsNullOrEmpty(url))
                        {
                            var mediaItem = new MediaItem
                            {
                                FileName = name,
                                FullPath = url,
                                Type = "TV JSON",
                                ChannelName = "IPTV",
                                Duration = "LIVE"
                            };

                            if (!string.IsNullOrEmpty(logo))
                            {
                                try { mediaItem.Poster = new BitmapImage(new Uri(logo)); } catch { }
                            }
                            _listTv.Add(mediaItem);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("JSON TV Error: " + ex.Message);
            }
            UpdateListStats();
        }

        private void sldVideoColor_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_engine == null) return;
            if (sender is Slider sld && sld.Tag != null)
            {
                string prop = sld.Tag.ToString();
                double val = e.NewValue;
                _engine.SetVideoAdjustment(prop, val);
                string textVal = (val > 0 ? "+" : "") + val.ToString("0");
                if (prop == "brightness" && lblBright != null) lblBright.Text = textVal;
                if (prop == "contrast" && lblContrast != null) lblContrast.Text = textVal;
                if (prop == "saturation" && lblSat != null) lblSat.Text = textVal;
                if (prop == "hue" && lblHue != null) lblHue.Text = textVal;
            }
        }

        private void btnResetVideo_Click(object sender, RoutedEventArgs e)
        {
            if (_engine == null) return;

            _engine.ResetVideoSettings();

            if (sldBright != null) sldBright.Value = 0;
            if (sldContrast != null) sldContrast.Value = 0;
            if (sldSat != null) sldSat.Value = 0;
            if (sldHue != null) sldHue.Value = 0;

            UpdateStatus("Đã đặt lại thiết lập hình ảnh.");
        }
        private void btnRotate_Click(object sender, RoutedEventArgs e)
        {
            if (_engine != null)
            {
                _engine.RotateVideo();
                UpdateStatus("🔄 Đã xoay video 90 độ.", false);
            }
        }

        private void btnFlipH_Click(object sender, RoutedEventArgs e)
        {
            if (_engine != null)
            {
                _engine.FlipVideo("hflip");
                UpdateStatus("↔️ Đã lật ngang video.", false);
            }
        }

        private void btnFlipV_Click(object sender, RoutedEventArgs e)
        {
            if (_engine != null)
            {
                _engine.FlipVideo("vflip");
                UpdateStatus("↕️ Đã lật dọc video.", false);
            }
        }

        private bool _isUpdatingEqUi = false;
        private void cboEqPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_engine == null || _isUpdatingEqUi) return;

            if (cboEqPresets.SelectedItem is ComboBoxItem item)
            {
                if (item.Tag != null && item.Tag.ToString() == "Custom") return;

                string presetName = item.Content.ToString();
                double[] vals = _engine.GetPresetValues(presetName);
                _isUpdatingEqUi = true;
                sldEq1.Value = vals[0];
                sldEq2.Value = vals[1];
                sldEq3.Value = vals[2];
                sldEq4.Value = vals[3];
                sldEq5.Value = vals[4];
                _engine.SetEqualizer(vals);
                _isUpdatingEqUi = false;

                UpdateStatus($"🎚 Đã áp dụng EQ: {presetName}", false);
            }
        }
        private void swPostProc_Toggled(object sender, RoutedEventArgs e)
        {
            if (_engine == null) return;

            if (sender is ToggleSwitch sw && sw.Tag != null)
            {
                string feature = sw.Tag.ToString();
                bool isEnabled = sw.IsOn;

                _engine.SetPostProcessing(feature, isEnabled);

                string statusText = "";
                switch (feature)
                {
                    case "hdr": statusText = "HDR Tone-Mapping"; break;
                    case "upscale": statusText = "Upscale chất lượng cao"; break;
                    case "deband": statusText = "Khử loang màu (Deband)"; break;
                    case "deinterlace": statusText = "Khử sọc (Deinterlace)"; break;
                    case "shader": statusText = "Shader làm nét"; break;
                }

                UpdateStatus($"{(isEnabled ? "✅ Đã bật" : "⛔ Đã tắt")}: {statusText}", false);
            }
        }

        private void cboColorFx_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_engine == null) return;
            if (cboColorFx.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                _engine.SetColorEffect(item.Tag.ToString());
            }
        }

        private void sldBlur_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_engine == null) return;
            _engine.SetBlur(e.NewValue);
        }

        private void chkFx_Changed(object sender, RoutedEventArgs e)
        {
            if (_engine == null) return;

            if (sender is CheckBox chk && chk.Tag != null)
            {
                string tag = chk.Tag.ToString();
                bool isChecked = chk.IsChecked == true;

                switch (tag)
                {
                    case "vig":
                        _engine.SetVignette(isChecked);
                        break;
                    case "grain":
                        _engine.SetFilmGrain(isChecked);
                        break;
                    case "technicolor":
                        _engine.SetTechnicolorEffect(isChecked);
                        break;
                }

                if (isChecked) UpdateStatus($"✨ Đã bật hiệu ứng: {chk.Content}", false);
            }
        }

        private void sldAdvancedColor_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_engine == null) return;

            if (sender is Slider sld && sld.Tag != null)
            {
                string channel = sld.Tag.ToString();
                double val = e.NewValue;

                _engine.SetAdvancedColor(channel, val);

                string txt = (val > 0 ? "+" : "") + val.ToString("0");

                switch (channel)
                {
                    case "gamma": if (lblGamma != null) lblGamma.Text = txt; break;
                    case "red": if (lblRed != null) lblRed.Text = txt; break;
                    case "green": if (lblGreen != null) lblGreen.Text = txt; break;
                    case "blue": if (lblBlue != null) lblBlue.Text = txt; break;
                }
            }
        }

        private void btnResetAdvanced_Click(object sender, RoutedEventArgs e)
        {
            if (_engine == null) return;
            _engine.ResetAdvancedColor();
            if (sldBright != null) sldBright.Value = 0;
            if (sldContrast != null) sldContrast.Value = 0;
            if (sldSat != null) sldSat.Value = 0;
            if (sldHue != null) sldHue.Value = 0;
            if (sldGamma != null) sldGamma.Value = 0;
            if (sldRed != null) sldRed.Value = 0;
            if (sldGreen != null) sldGreen.Value = 0;
            if (sldBlue != null) sldBlue.Value = 0;

            UpdateStatus("♻️ Đã đặt lại toàn bộ màu sắc.");
        }
        private void btnApplyFilter_Click(object sender, RoutedEventArgs e)
        {
            if (_engine == null) return;
            string filter = txtFfmpegFilter.Text.Trim();
            _engine.ApplyRawFFmpegFilter(filter);
            UpdateStatus(string.IsNullOrEmpty(filter) ? "Đã xóa bộ lọc thủ công." : "Đã áp dụng bộ lọc tùy biến.");
        }
        private void ColorMan_Changed(object sender, RoutedEventArgs e)
        {
            if (_engine == null || swIccProfile == null || cboTargetPrim == null) return;

            bool autoIcc = swIccProfile.IsOn;

            string target = "auto";
            if (cboTargetPrim.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                target = item.Tag.ToString();
            }

            _engine.SetColorManagement(autoIcc, target);

            if (_isInitialized)
                UpdateStatus($"🎨 Color: ICC={(autoIcc ? "Auto" : "Off")}, Gamut={target}", false);
        }

        private void sldPro_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_engine == null) return;

            if (sender is Slider s && s == sldSharpen)
            {
                _engine.SetSharpen(sldSharpen.Value);

                if (lblSharpenVal != null)
                    lblSharpenVal.Text = sldSharpen.Value.ToString("F1");
            }
            else if (sender == sldDenoise)
            {
                _engine.SetDenoise(sldDenoise.Value);
                if (lblDenoiseVal != null)
                    lblDenoiseVal.Text = $"{(int)sldDenoise.Value}%";
            }
        }

        private void btnSubtitle_Click(object sender, RoutedEventArgs e)
        {
            if (_engine == null) return;

            MenuFlyout menu = new MenuFlyout();
            menu.Items.Add(new MenuFlyoutItem { Text = "QUẢN LÝ PHỤ ĐỀ", IsEnabled = false, FontWeight = Microsoft.UI.Text.FontWeights.Bold });
            menu.Items.Add(new MenuFlyoutSeparator());
            var itemLoad = new MenuFlyoutItem { Text = "📂 Tải file phụ đề (.srt, .ass)...", Icon = new FontIcon { Glyph = "\uE8E5" } };
            itemLoad.Click += async (s, args) =>
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
                picker.FileTypeFilter.Add(".srt");
                picker.FileTypeFilter.Add(".ass");
                picker.FileTypeFilter.Add(".vtt");

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    _engine.LoadExternalSubtitle(file.Path);
                    UpdateStatus($"Đã nạp phụ đề: {file.Name}");
                }
            };
            menu.Items.Add(itemLoad);
            menu.Items.Add(new MenuFlyoutSeparator());
            var tracks = _engine.GetSubtitleTracks();
            if (tracks.Count > 0)
            {
                foreach (var t in tracks)
                {
                    var toggle = new ToggleMenuFlyoutItem
                    {
                        Text = t.Title,
                        IsChecked = t.IsSelected,
                        Tag = t.Id
                    };
                    toggle.Click += (s, args) =>
                    {
                        if (s is ToggleMenuFlyoutItem item && item.Tag is long sid)
                        {
                            _engine.SelectSubtitle(sid);
                            UpdateStatus(sid > 0 ? $"Đã chọn phụ đề: {item.Text}" : "Đã tắt phụ đề.");
                        }
                    };
                    menu.Items.Add(toggle);
                }
            }
            else
            {
                menu.Items.Add(new MenuFlyoutItem { Text = "(Không có phụ đề)", IsEnabled = false });
            }
            menu.Items.Add(new MenuFlyoutSeparator());
            var itemSettings = new MenuFlyoutItem { Text = "⚙ Cài đặt & Dịch tự động", Icon = new FontIcon { Glyph = "\uE713" } };
            itemSettings.Click += btnSubSettings_Click;
            menu.Items.Add(itemSettings);
            if (sender is FrameworkElement ele)
            {
                menu.ShowAt(ele);
            }
        }

        private void btnSubSettings_Click(object sender, RoutedEventArgs e)
        {

            rbSettings.IsChecked = true;
            OnNavTabClick(rbSettings, null);

            grpSubSettings.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true });

            UpdateStatus("⚙ Đã chuyển đến cài đặt Phụ đề.");
        }

        private void sldSub_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_engine == null) return;
            int size = (int)sldSubSize.Value;
            int margin = (int)sldSubMargin.Value;
            string color = "FFFFFF";
            if (cboSubColor.SelectedItem is ComboBoxItem cItem && cItem.Tag != null)
                color = cItem.Tag.ToString();

            _engine.UpdateSubtitleSettings(size, color, margin);
        }
        private void cboSubColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            sldSub_ValueChanged(null, null);
        }
        private void sldEq_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_engine == null) return;

            if (_isUpdatingEqUi) return;

            foreach (var item in cboEqPresets.Items)
            {
                if (item is ComboBoxItem cbi && cbi.Tag != null && cbi.Tag.ToString() == "Custom")
                {
                    if (cboEqPresets.SelectedItem != item)
                    {
                        cboEqPresets.SelectedItem = item;
                    }
                    break;
                }
            }

            double[] currentVals = new double[]
            {
        sldEq1.Value,
        sldEq2.Value,
        sldEq3.Value,
        sldEq4.Value,
        sldEq5.Value
            };
            _engine.SetEqualizer(currentVals);
        }
        private string GetUrlFromNestedJson(JsonNode? channelNode)
        {
            try
            {
                var sources = channelNode?["sources"]?.AsArray();
                if (sources == null) return "";

                foreach (var source in sources)
                {
                    var contents = source?["contents"]?.AsArray();
                    if (contents == null) continue;

                    foreach (var content in contents)
                    {
                        var streams = content?["streams"]?.AsArray();
                        if (streams == null) continue;

                        foreach (var stream in streams)
                        {
                            var links = stream?["stream_links"]?.AsArray();
                            if (links != null)
                            {
                                foreach (var linkObj in links)
                                {
                                    string finalUrl = linkObj?["url"]?.ToString() ?? "";
                                    if (!string.IsNullOrEmpty(finalUrl)) return finalUrl;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return "";
        }

        private async void btnBrowseLogo_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".bmp");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {

                AddLogoToCanvas(file.Path);
            }
        }

        private async void swLogo_Toggled(object sender, RoutedEventArgs e)
        {
            bool isOn = swLogo.IsOn;

            if (pnlLogoControls != null)
            {
                pnlLogoControls.Visibility = isOn ? Visibility.Visible : Visibility.Collapsed;
            }

            if (OverlayCanvas != null)
            {
                foreach (var child in OverlayCanvas.Children)
                {
                    if (child is Grid g && g.Tag != null && g != DraggableText)
                    {
                        g.Visibility = isOn ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
            }

            if (_engine != null)
            {
                if (isOn)
                {
                    await ApplyLogoLogic();
                }
                else
                {
                    await _engine.UpdateLogoLayers(null);

                    UpdateStatus("Đã tắt hoàn toàn Logo trên Video.");
                }
            }
        }

        private async void swTicker_Toggled(object sender, RoutedEventArgs e)
        {
            bool isOn = swTicker.IsOn;

            if (pnlTickerControls != null)
            {
                pnlTickerControls.Visibility = isOn ? Visibility.Visible : Visibility.Collapsed;
            }

            if (DraggableText != null)
            {
                DraggableText.Visibility = isOn ? Visibility.Visible : Visibility.Collapsed;
            }

            if (_engine != null)
            {
                if (isOn)
                {

                }
                else
                {
                    await _engine.HideTicker();
                    UpdateStatus("Đã tắt chữ chạy.");
                }
            }
        }

        private void DesignGrid_Loaded(object sender, RoutedEventArgs e)
        {
            DrawDesignGrid();
        }

        private void DrawDesignGrid()
        {
            if (GridPatternCanvas == null) return;

            GridPatternCanvas.Children.Clear();

            var lineBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 53, 53, 53));

            double step = 60;
            for (double x = 0; x <= 1920; x += step)
            {
                var line = new Microsoft.UI.Xaml.Shapes.Line
                {
                    X1 = x,
                    Y1 = 0,
                    X2 = x,
                    Y2 = 1080,
                    Stroke = lineBrush,
                    StrokeThickness = 1
                };
                GridPatternCanvas.Children.Add(line);
            }
            for (double y = 0; y <= 1080; y += step)
            {
                var line = new Microsoft.UI.Xaml.Shapes.Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = 1920,
                    Y2 = y,
                    Stroke = lineBrush,
                    StrokeThickness = 1
                };
                GridPatternCanvas.Children.Add(line);
            }
        }

        private void radTickerTime_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (stkTimerInputs == null) return;

            if (radTickerTime.SelectedIndex == 1)
            {
                stkTimerInputs.Visibility = Visibility.Visible;
            }
            else
            {
                stkTimerInputs.Visibility = Visibility.Collapsed;
            }
        }

        private void txtTickerInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtTickerPreview != null)
            {
                txtTickerPreview.Text = string.IsNullOrEmpty(txtTickerInput.Text)
                                        ? "NỘI DUNG DEMO..."
                                        : txtTickerInput.Text;
            }
            UpdateTickerDesignPreview();
        }

        private void btnColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null)
            {
                _tickerColor = btn.Tag.ToString();
                if (txtTickerPreview != null)
                {
                    if (_tickerColor.Contains("FFFF")) txtTickerPreview.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Yellow);
                    else if (_tickerColor.Contains("FF00")) txtTickerPreview.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Lime);
                    else if (_tickerColor.Contains("00FF")) txtTickerPreview.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
                    else txtTickerPreview.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
                }
            }
        }

        private void Element_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_isEffectResizing) return;

            var element = sender as FrameworkElement;
            if (element == null) return;

            if (element is Control c) c.Focus(FocusState.Programmatic);
            else if (element is Panel p)
            {

            }

            _selectedElement = element;
            _isDragging = true;
            _startPoint = e.GetCurrentPoint(OverlayCanvas).Position;
            _orgLeft = Canvas.GetLeft(_selectedElement);
            _orgTop = Canvas.GetTop(_selectedElement);

            element.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void Element_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_isDragging && _selectedElement != null)
            {
                var currentPos = e.GetCurrentPoint(OverlayCanvas).Position;
                double offsetX = currentPos.X - _startPoint.X;
                double offsetY = currentPos.Y - _startPoint.Y;

                Canvas.SetLeft(_selectedElement, _orgLeft + offsetX);
                Canvas.SetTop(_selectedElement, _orgTop + offsetY);
            }
        }

        private async void Element_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isDragging && _selectedElement != null)
            {
                _isDragging = false;
                _selectedElement.ReleasePointerCapture(e.Pointer);

                if (pnlPreview != null && pnlPreview.Visibility == Visibility.Visible)
                {
                    if (_selectedElement == DraggableText && swTicker.IsOn)
                    {
                        await ApplyTickerLogic();
                    }
                    else if (_selectedElement != DraggableText && swLogo.IsOn)
                    {
                        await ApplyLogoLogic();
                    }
                }

                _selectedElement = null;
            }
        }
        private void Resize_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var handle = sender as Microsoft.UI.Xaml.Shapes.Shape;
            if (handle != null && handle.Parent is Grid parentGrid)
            {
                _selectedElement = parentGrid;
                _isEffectResizing = true;
                _startPoint = e.GetCurrentPoint(OverlayCanvas).Position;

                _orgWidth = _selectedElement.ActualWidth;
                _orgHeight = _selectedElement.ActualHeight;
                if (_orgHeight > 0) _orgAspectRatio = _orgWidth / _orgHeight;
                else _orgAspectRatio = 1;
                _currentResizeMode = handle.Tag?.ToString() ?? "Corner";

                handle.CapturePointer(e.Pointer);
                e.Handled = true;
            }
        }

        private void Resize_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_isEffectResizing && _selectedElement != null)
            {
                var currentPos = e.GetCurrentPoint(OverlayCanvas).Position;
                double diffX = currentPos.X - _startPoint.X;
                double diffY = currentPos.Y - _startPoint.Y;

                double newW = _orgWidth;
                double newH = _orgHeight;

                switch (_currentResizeMode)
                {
                    case "Corner":

                        newW = _orgWidth + diffX;
                        if (newW < 20) newW = 20;
                        newH = newW / _orgAspectRatio;
                        break;

                    case "Right":
                        newW = _orgWidth + diffX;
                        if (newW < 20) newW = 20;
                        newH = _orgHeight;
                        break;

                    case "Bottom":
                        newH = _orgHeight + diffY;
                        if (newH < 20) newH = 20;
                        newW = _orgWidth;
                        break;
                }

                _selectedElement.Width = newW;
                _selectedElement.Height = newH;
            }
        }

        private void Resize_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isEffectResizing)
            {
                _isEffectResizing = false;
                (sender as UIElement)?.ReleasePointerCapture(e.Pointer);
                _selectedElement = null;
                _currentResizeMode = "";
            }
        }
    }

    public static class AppSettings
    {
        private static string FolderPath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediaLedInterface");
        private static string FilePath => System.IO.Path.Combine(FolderPath, "config.json");

        private static Dictionary<string, string> _values = new Dictionary<string, string>();

        static AppSettings()
        {
            Load();
        }
        public static void ClearAll()
        {
            _values.Clear();

            try
            {
                if (System.IO.File.Exists(FilePath))
                {
                    System.IO.File.Delete(FilePath);
                }
            }
            catch { }
        }
        private static void Load()
        {
            try
            {
                if (System.IO.File.Exists(FilePath))
                {
                    string json = System.IO.File.ReadAllText(FilePath);
                    _values = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                }
            }
            catch { }
        }

        public static void Save(string key, string value)
        {
            if (_values.ContainsKey(key)) _values[key] = value;
            else _values.Add(key, value);
            SaveToFile();
        }

        public static string? Get(string key)
        {
            return _values.ContainsKey(key) ? _values[key] : null;
        }

        public static void Remove(string key)
        {
            if (_values.Remove(key)) SaveToFile();
        }

        private static void SaveToFile()
        {
            try
            {
                if (!System.IO.Directory.Exists(FolderPath)) System.IO.Directory.CreateDirectory(FolderPath);
                string json = System.Text.Json.JsonSerializer.Serialize(_values);
                System.IO.File.WriteAllText(FilePath, json);
            }
            catch { }
        }
    }

    public static class FastThumbnail
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItem ppv);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, out IntPtr ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        private interface IShellItemImageFactory
        {
            void GetImage([In, MarshalAs(UnmanagedType.Struct)] SIZE size, [In] int flags, [Out] out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE
        {
            public int cx;
            public int cy;
            public SIZE(int x, int y) { cx = x; cy = y; }
        }

        public static async Task<MemoryStream?> GetImageStreamAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                IntPtr hBitmap = IntPtr.Zero;
                try
                {
                    Guid shellItemGuid = typeof(IShellItem).GUID;
                    Guid imageFactoryGuid = typeof(IShellItemImageFactory).GUID;
                    IShellItem shellItem;
                    int hr = SHCreateItemFromParsingName(filePath, IntPtr.Zero, ref shellItemGuid, out shellItem);
                    if (hr != 0 || shellItem == null) return null;
                    var imageFactory = shellItem as IShellItemImageFactory;
                    if (imageFactory == null) return null;
                    imageFactory.GetImage(new SIZE(256, 256), 0, out hBitmap);

                    if (hBitmap != IntPtr.Zero)
                    {
                        using (var bitmap = System.Drawing.Image.FromHbitmap(hBitmap))
                        {
                            var stream = new MemoryStream();

                            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                            stream.Position = 0;
                            return stream;
                        }
                    }
                }
                catch { }
                finally
                {
                    if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                }
                return null;
            });
        }
    }
    public class BoolToVisibilityConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public bool IsInverted { get; set; } = false;

        public object Convert(object value, System.Type targetType, object parameter, string language)
        {
            bool val = (value is bool b) ? b : false;
            if (IsInverted)
                return val ? Visibility.Collapsed : Visibility.Visible;
            return val ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, System.Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class IndexToVisConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {

            if (value is int index && index == 1)
            {
                return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }

    }
}



