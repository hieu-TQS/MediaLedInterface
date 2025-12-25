using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;
using Exception = System.Exception;
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
        public string PosterUrl { get; set; } = "";

        private Microsoft.UI.Xaml.Media.ImageSource? _poster;
        [JsonIgnore]
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
        private bool _isLocalLoaded = false;
        private bool _isStreamLoaded = false;
        private bool _isTvLoaded = false;
        private const string SETTING_VOLUME = "SavedVolume";
        private DispatcherTimer _reconnectTimer;
        private Microsoft.UI.Windowing.AppWindow? _appWindow;
        private MediaEngine? _engine;
        private bool _isResizing = false;
        private bool _isInitialized = false;
        private bool _isNavExpanded = false;
        private bool _isPlayerMode = false;
        private PlayerMode _currentMode = PlayerMode.Off;
        private const string SETTING_SPONSOR = "EnableSponsorBlock";
        private const string SETTING_WAKELOCK = "EnableWakeLock";
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
        private DispatcherTimer _statusTimer;

        private string _tickerColor = "&H00FFFFFF";
        private string _savedStatus = "Sẵn sàng.";
        private string _persistentStatus = "Sẵn sàng.";
        private string _inputMode = "";
        private DispatcherTimer _debounceTimer;
        private const string SETTING_APP_MODE = "AppViewMode";
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

        private Random _fakeRnd = new Random();

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


        private double GetScheduledTime()
        {
            double.TryParse(txtH.Text, out double h);
            double.TryParse(txtM.Text, out double m);
            double.TryParse(txtS.Text, out double s);
            return (h * 3600) + (m * 60) + s;
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
                _engine.OnStatusMessage += (msg) =>
                {
                    UpdateStatus(msg, false);
                };
                _engine.SetPropertyString("hr-seek", "yes");
                _engine.SetPropertyString("hwdec", "auto-copy");
                _engine.SetPropertyString("gpu-context", "d3d11");

                _engine.SetPropertyString("vd-lavc-dr", "no");


                _engine.SetLedScreen(false, new MediaEngine.RECT());
                LoadSystemSettings();
                LoadBackgroundSetting();
                KeyManager.LoadKeys();
                string savedVolStr = AppSettings.Get(SETTING_VOLUME);
                double startVol = 60;
                if (double.TryParse(savedVolStr, out double v))
                {
                    startVol = v;
                }
                sliderVolume.Value = startVol;
                _engine.SetVolume(startVol);
                UpdateVolumeIcon(startVol);
                string savedMode = AppSettings.Get(SETTING_APP_MODE);
                bool isPlayerStart = savedMode == "True";
                btnModeSwitch.IsChecked = isPlayerStart;
                _isPlayerMode = isPlayerStart;
                if (isPlayerStart)
                {
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
            if (rbMedia != null)
            {
                rbMedia.IsChecked = true;
                OnNavTabClick(rbMedia, null);
            }
            UpdateStatus("Hệ thống đã sẵn sàng. Chào mừng bạn!");
            RefreshMonitors();
            UpdateListStats();
            StartVisualizer();
        }
        private void btnEditYtKey_Click(object sender, RoutedEventArgs e) => KeyManager.OpenFileToEdit("yt");

        private void btnReloadKeys_Click(object sender, RoutedEventArgs e)
        {
            KeyManager.LoadKeys();
            UpdateStatus("✅ Đã cập nhật danh sách API Key mới!");
        }

        private void lstMedia_DragOver(object sender, DragEventArgs e)
        {

            bool isTabLocal = lstMedia.ItemsSource == _listLocal;
            bool isTabTv = lstMedia.ItemsSource == _listTv;

            if (!isTabLocal)
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

                        UpdateStatus("✅ Đã bật khởi động cùng Windows.");
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
            if (sliderVolume != null)
            {
                AppSettings.Save(SETTING_VOLUME, sliderVolume.Value.ToString());
            }
            if (_folderWatcher != null)
            {
                _folderWatcher.EnableRaisingEvents = false;
                _folderWatcher.Dispose();
                _folderWatcher = null;
            }

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
            System.Environment.Exit(0);
        }
        [DllImport("user32.dll")]
        static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);
        delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RectStruct lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential)]
        public struct RectStruct { public int Left; public int Top; public int Right; public int Bottom; }
        public class MyMonitorInfo
        {
            public string Name { get; set; }
            public MediaEngine.RECT Rect { get; set; }
            public IntPtr Handle { get; set; }
        }

        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
        private List<MyMonitorInfo> GetSystemMonitors()
        {
            var allMonitors = new List<MyMonitorInfo>();
            int count = 0;
            IntPtr appHwnd = WindowNative.GetWindowHandle(this);
            IntPtr appMonitorHandle = MonitorFromWindow(appHwnd, MONITOR_DEFAULTTONEAREST);

            try
            {
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                    delegate (IntPtr hMonitor, IntPtr hdcMonitor, ref RectStruct lprcMonitor, IntPtr dwData)
                    {
                        count++;
                        int w = lprcMonitor.Right - lprcMonitor.Left;
                        int h = lprcMonitor.Bottom - lprcMonitor.Top;
                        var info = new MyMonitorInfo
                        {
                            Handle = hMonitor,
                            Name = $"Màn hình {count} ({w}x{h})",
                            Rect = new MediaEngine.RECT
                            {
                                left = lprcMonitor.Left,
                                top = lprcMonitor.Top,
                                right = lprcMonitor.Right,
                                bottom = lprcMonitor.Bottom
                            }
                        };
                        allMonitors.Add(info);
                        return true;
                    }, IntPtr.Zero);
            }
            catch { }
            var externalMonitors = allMonitors.Where(m => m.Handle != appMonitorHandle).ToList();
            if (externalMonitors.Count > 0)
            {
                return externalMonitors;
            }
            return allMonitors;
        }
        private void swLedMode_Toggled(object sender, RoutedEventArgs e)
        {
            if (swLedMode.IsOn)
            {
                EnableManualControls(true);
            }
            else
            {
                EnableManualControls(false);
            }
            ApplyLedConfig();
        }
        private void EnableManualControls(bool isManual)
        {
            if (pnlManualLed == null || cboMonitorOutput == null) return;

            if (isManual)
            {
                pnlManualLed.Visibility = Visibility.Visible;

                cboMonitorOutput.IsEnabled = false;
                if (btnRefreshMonitors != null) btnRefreshMonitors.IsEnabled = false;
                if (lblMonitorSelect != null) lblMonitorSelect.Opacity = 0.5;
            }
            else
            {
                pnlManualLed.Visibility = Visibility.Collapsed;

                cboMonitorOutput.IsEnabled = true;
                if (btnRefreshMonitors != null) btnRefreshMonitors.IsEnabled = true;
                if (lblMonitorSelect != null) lblMonitorSelect.Opacity = 1.0;
            }
        }
        private void btnLedMove_Click(object sender, RoutedEventArgs e)
        {
            if (nbLedX == null || nbLedY == null || sldLedStep == null) return;

            if (sender is Button btn && btn.Tag is string direction)
            {
                double step = sldLedStep.Value;
                double currentX = nbLedX.Value;
                double currentY = nbLedY.Value;
                switch (direction)
                {
                    case "Up": currentY -= step; break;
                    case "Down": currentY += step; break;
                    case "Left": currentX -= step; break;
                    case "Right": currentX += step; break;
                }
                nbLedX.Value = currentX;
                nbLedY.Value = currentY;
                if (swLedMode != null && swLedMode.IsOn)
                {
                    ApplyLedConfig();
                }
            }
        }
        private void RefreshMonitors()
        {
            _isRefreshing = true;
            try
            {
                cboMonitorOutput.ItemsSource = null;
                var list = GetSystemMonitors();

                if (list.Count > 0)
                {
                    cboMonitorOutput.ItemsSource = list;
                    cboMonitorOutput.DisplayMemberPath = "Name";
                    cboMonitorOutput.SelectedIndex = 0;

                    txtMonitorStatus.Text = $"Đã tìm thấy {list.Count} màn hình xuất.";
                }
                else
                {
                    txtMonitorStatus.Text = "Không tìm thấy màn hình nào.";
                }
            }
            catch (Exception ex)
            {
                txtMonitorStatus.Text = "Lỗi quét màn hình: " + ex.Message;
            }
            finally
            {
                _isRefreshing = false;
                if (cboMonitorOutput.Items.Count > 0)
                    cboMonitorOutput_SelectionChanged(null, null);
            }
        }

        private void cboMonitorOutput_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshing) return;

            if (cboMonitorOutput.SelectedItem is MyMonitorInfo monitor)
            {
                _selectedMonitor = new MediaEngine.MonitorInfo
                {
                    Name = monitor.Name,
                    Rect = monitor.Rect
                };
                if (nbLedX != null) nbLedX.Value = monitor.Rect.left;
                if (nbLedY != null) nbLedY.Value = monitor.Rect.top;
                if (nbLedWidth != null) nbLedWidth.Value = monitor.Rect.right - monitor.Rect.left;
                if (nbLedHeight != null) nbLedHeight.Value = monitor.Rect.bottom - monitor.Rect.top;
                if (!swLedMode.IsOn)
                {
                    if (_isLedOn && _engine != null)
                    {
                        _engine.SetLedScreen(true, monitor.Rect);
                        txtMonitorStatus.Text = $"✅ Đã cập nhật vùng phát: {monitor.Name}";
                    }
                    else
                    {
                        txtMonitorStatus.Text = $"Đã chọn màn hình: {monitor.Name}. (Chờ bật LED)";
                    }
                }
            }
        }
        private bool _isRefreshing = false;
        private void btnRefreshMonitors_Click(object sender, RoutedEventArgs e)
        {
            RefreshMonitors();
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
                    UpdateStatus("⏳ Đang xử lý và nạp ảnh nền...");
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
        private void RemoveDuplicates_Click(object sender, RoutedEventArgs e)
        {
            int removedCount = 0;
            if (lstMedia.ItemsSource == _listLocal)
            {
                var uniqueItems = new List<MediaItem>();
                var seenKeys = new HashSet<string>();

                foreach (var item in _listLocal)
                {
                    long fileSize = -1;
                    try
                    {
                        if (System.IO.File.Exists(item.FullPath))
                            fileSize = new System.IO.FileInfo(item.FullPath).Length;
                    }
                    catch { }
                    string key = $"{item.FileName}_{fileSize}";

                    if (!seenKeys.Contains(key))
                    {
                        seenKeys.Add(key);
                        uniqueItems.Add(item);
                    }
                }
                removedCount = _listLocal.Count - uniqueItems.Count;

                if (removedCount > 0)
                {
                    _listLocal.Clear();
                    _backupLocal.Clear();
                    foreach (var item in uniqueItems)
                    {
                        _listLocal.Add(item);
                        _backupLocal.Add(item);
                    }
                }
            }
            else if (lstMedia.ItemsSource == _listStream || lstMedia.ItemsSource == _listTv)
            {
                ObservableCollection<MediaItem> currentList = (lstMedia.ItemsSource == _listStream) ? _listStream : _listTv;
                List<MediaItem> backupList = (lstMedia.ItemsSource == _listStream) ? _backupStream : _backupTv;
                var uniqueItems = currentList.GroupBy(x => new { x.FileName, x.FullPath })
                                             .Select(g => g.First())
                                             .ToList();

                removedCount = currentList.Count - uniqueItems.Count;

                if (removedCount > 0)
                {
                    currentList.Clear();
                    backupList.Clear();
                    foreach (var item in uniqueItems)
                    {
                        currentList.Add(item);
                        backupList.Add(item);
                    }
                }
            }
            else
            {
                UpdateStatus("⚠️ Tính năng này không hỗ trợ Tab hiện tại.", false, true);
                return;
            }
            if (removedCount > 0)
            {
                UpdateListStats();
                UpdateStatus($"✅ Đã dọn dẹp {removedCount} mục trùng lặp.", false);
            }
            else
            {
                UpdateStatus("Danh sách sạch sẽ, không có mục trùng!", false);
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
        private void chkManualLed_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (pnlManualLed == null || cboMonitorOutput == null) return;

            bool isManual = swLedMode.IsOn == true;
            pnlManualLed.Visibility = isManual ? Visibility.Visible : Visibility.Collapsed;
            cboMonitorOutput.IsEnabled = true;
            if (_isLedOn)
            {
                ApplyLedConfig();
            }
        }


        private void btnApplyManualLed_Click(object sender, RoutedEventArgs e)
        {
            ApplyLedConfig();
            txtMonitorStatus.Text = "Đã áp dụng cấu hình thủ công.";
        }
        private void ApplyLedConfig()
        {
            if (_engine == null) return;
            if (!_isLedOn) return;

            if (swLedMode.IsOn == true)
            {
                int x = (int)nbLedX.Value;
                int y = (int)nbLedY.Value;
                int w = (int)nbLedWidth.Value;
                int h = (int)nbLedHeight.Value;

                var customRect = new MediaEngine.RECT { left = x, top = y, right = x + w, bottom = y + h };
                _engine.SetLedScreen(true, customRect);
            }
            else
            {
                if (_selectedMonitor != null)
                {
                    _engine.SetLedScreen(true, _selectedMonitor.Rect);
                }
            }
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
                    else
                    {
                        dlSubItem.Items.Add(new MenuFlyoutSeparator());
                        var rawDl = new MenuFlyoutItem { Text = "🎬 Tải Luồng Gốc (Stream/Raw)" };
                        rawDl.Click += (s, a) => HandleDownload("best");
                        dlSubItem.Items.Add(rawDl);
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
                UpdateStatus($"⏳ Đang chuẩn bị tải {label}...");

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

        private void swSponsorBlock_Toggled(object sender, RoutedEventArgs e)
        {
            if (_engine == null) return;
            bool isOn = swSponsorBlock.IsOn;
            _engine.IsSponsorBlockEnabled = isOn;
            AppSettings.Save(SETTING_SPONSOR, isOn.ToString());

            UpdateStatus(isOn ? "Đã bật SponsorBlock (Tự động lưu)." : "Đã tắt SponsorBlock.");
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
        private void swAudioBoost_Toggled(object sender, RoutedEventArgs e)
        {
            if (_engine == null) return;

            bool isBoostOn = swAudioBoost.IsOn;
            double boostLevel = sldBoostLevel != null ? sldBoostLevel.Value : 5;

            if (lblBoostValue != null)
            {
                lblBoostValue.Text = $"+{boostLevel} dB";
            }
            _engine.SetAudioBoost(isBoostOn, boostLevel);
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
            _audioDeviceCheckTimer.Interval = TimeSpan.FromSeconds(2);
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
            var properties = e.GetCurrentPoint(RootGrid).Properties;
            if (properties.IsLeftButtonPressed)
            {
                if (e.OriginalSource is FrameworkElement source &&
                   (source is Button || source is Slider || source is TextBox || source is ToggleSwitch || source is Thumb))
                {
                    return;
                }
                ToggleControlBar();
            }
            else if (properties.IsRightButtonPressed && _isInternalFullscreen)
            {
                ToggleControlBar();
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
            RootGrid.KeyboardAccelerators.Clear();
            var accF12 = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.F12 };
            accF12.Invoked += (s, e) =>
            {
                if (!_isPlayerMode) return;
                ToggleZenMode();
                e.Handled = true;
            };
            RootGrid.KeyboardAccelerators.Add(accF12);
            var accAltEnter = new Microsoft.UI.Xaml.Input.KeyboardAccelerator
            {
                Key = Windows.System.VirtualKey.Enter,
                Modifiers = Windows.System.VirtualKeyModifiers.Menu
            };
            accAltEnter.Invoked += (s, e) =>
            {
                if (!_isPlayerMode) return;

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
                ToggleControlBar();
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
        private async void OnNavTabClick(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Content != null)
            {
                if (grpInlineInput != null)
                {
                    grpInlineInput.Visibility = Visibility.Collapsed;
                    txtInlineUrl.Text = "";
                }
                string tabName = rb.Content.ToString().ToUpper();
                if (btnRemoveDuplicates != null)
                {
                    if (tabName == "LOCAL" || tabName == "ONLINE" || tabName == "IPTV")
                    {
                        btnRemoveDuplicates.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        btnRemoveDuplicates.Visibility = Visibility.Collapsed;
                    }
                }
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
                if (grpBottomActions != null) grpBottomActions.Visibility = Visibility.Visible;
                if (btnSaveSession != null) btnSaveSession.Visibility = Visibility.Visible;
                if (btnRestoreSession != null) btnRestoreSession.Visibility = Visibility.Visible;
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
                        txtSidebarHeader.Text = "TÌM KIẾM";
                        lstMedia.ItemsSource = _listSearch;
                        btnSearchSource.Visibility = Visibility.Visible;
                        if (grpBottomActions != null) grpBottomActions.Visibility = Visibility.Visible;
                        if (btnSaveSession != null) btnSaveSession.Visibility = Visibility.Collapsed;
                        if (btnRestoreSession != null) btnRestoreSession.Visibility = Visibility.Collapsed;
                        if (_listSearch.Count > 0 && _currentSourceMode >= 3)
                            grpSearchPagination.Visibility = Visibility.Visible;
                        break;

                    case "SETTING":
                        txtSidebarHeader.Text = "CÀI ĐẶT";
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
                if (tabName == "LOCAL") UpdateStatus($"Đang xem: Danh sách File nội bộ ({_listLocal.Count})");
                else if (tabName == "ONLINE") UpdateStatus($"Đang xem: Danh sách Online ({_listStream.Count})");
                else if (tabName == "IPTV") UpdateStatus($"Đang xem: Kênh IPTV ({_listTv.Count})");
                else if (tabName == "ABOUTS") UpdateStatus("Đang xem: Thông tin phần mềm");

                if (txtSearch != null && btnSearchSource.Visibility != Visibility.Visible)
                {
                    txtSearch.Text = "";
                }
                UpdateListStats();
                if (_playingItem != null && CurrentList.Contains(_playingItem))
                {
                    this.DispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            lstMedia.SelectedItem = _playingItem;
                            lstMedia.ScrollIntoView(_playingItem);
                        }
                        catch { }
                    });
                }
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
            TimeSpan tHover = TimeSpan.FromSeconds(hoverTime);
            txtSeekPreviewTime.Text = (tHover.TotalHours >= 1) ? tHover.ToString(@"hh\:mm\:ss") : tHover.ToString(@"mm\:ss");
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
            bool shouldShowGif = _playingItem != null && !_playingItem.IsPaused;

            foreach (var kvp in tabMap)
            {
                RadioButton rb = kvp.Key;
                System.Collections.IList list = kvp.Value;
                bool isTabContainingItem = (_playingItem != null && list.Contains(_playingItem));

                var icon = FindChildElement<Microsoft.UI.Xaml.Controls.Image>(rb, "PART_PlayingIcon");
                if (icon != null)
                {
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
            TimeSpan tHover = TimeSpan.FromSeconds(hoverTime);
            _fsPreviewText.Text = (tHover.TotalHours >= 1) ? tHover.ToString(@"hh\:mm\:ss") : tHover.ToString(@"mm\:ss");
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
            AppSettings.Save(SETTING_VOLUME, sliderVolume.Value.ToString());
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
            if (_isZenMode)
            {
                UpdateUIVisibility();
                UpdateMpvLayout();
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
        private void btnZoomIn_Click(object sender, RoutedEventArgs e)
        {
            if (sldZoom.Value < sldZoom.Maximum) sldZoom.Value += 0.1;
        }

        private void btnZoomOut_Click(object sender, RoutedEventArgs e)
        {
            if (sldZoom.Value > sldZoom.Minimum) sldZoom.Value -= 0.1;
        }
        private void btnScaleXUp_Click(object sender, RoutedEventArgs e)
        {
            if (sldScaleX.Value < sldScaleX.Maximum) sldScaleX.Value += 0.01;
        }

        private void btnScaleXDown_Click(object sender, RoutedEventArgs e)
        {
            if (sldScaleX.Value > sldScaleX.Minimum) sldScaleX.Value -= 0.01;
        }
        private void btnScaleYUp_Click(object sender, RoutedEventArgs e)
        {
            if (sldScaleY.Value < sldScaleY.Maximum) sldScaleY.Value += 0.01;
        }

        private void btnScaleYDown_Click(object sender, RoutedEventArgs e)
        {
            if (sldScaleY.Value > sldScaleY.Minimum) sldScaleY.Value -= 0.01;
        }
        void sldZoom_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_engine == null) return;

            double val = e.NewValue;
            _engine.SetManualZoom(val);

            if (lblZoomLevel != null)
            {
                int percent = (int)(Math.Pow(2, val) * 100);

                lblZoomLevel.Text = $"{percent}%";
            }
        }
        private void sldScaleX_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_engine == null) return;
            double val = e.NewValue;

            _engine.SetScaleX(val);

            if (lblScaleX != null) lblScaleX.Text = val.ToString("0.00");
        }

        private void sldScaleY_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_engine == null) return;
            double val = e.NewValue;

            _engine.SetScaleY(val);

            if (lblScaleY != null) lblScaleY.Text = val.ToString("0.00");
        }

        private void btnResetScaleX_Click(object sender, RoutedEventArgs e)
        {
            sldScaleX.Value = 1.0;
        }

        private void btnResetScaleY_Click(object sender, RoutedEventArgs e)
        {
            sldScaleY.Value = 1.0;
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

                UpdateStatus($"⏳ Đang đọc file links.txt...");
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

                UpdateStatus($"⏳ Đang quét thư mục Playlists...");
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
            _isPlayerMode = newMode;
            await _engine.SetMode(newMode);
            StopComplete();
            UpdateModeUI(newMode);
            UpdateStatus(newMode ? "Đã chuyển sang chế độ PLAYER." : "Đã chuyển sang chế độ STUDIO.");
            AppSettings.Save(SETTING_APP_MODE, newMode.ToString());

            RootGrid.UpdateLayout();
            await Task.Delay(50);
            if (newMode)
            {
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
                    UpdateStatus($"⏳ Đang đọc file: {file.Name}...");
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
                    UpdateStatus($"⏳ Đang quét thư mục: {folder.Name}...");
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
                string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediaLedInterfaceNew");
                if (System.IO.Directory.Exists(folder))
                {
                    string[] filesToDelete = { FILE_LOCAL, FILE_ONLINE, FILE_TV };
                    foreach (var fileName in filesToDelete)
                    {
                        string filePath = System.IO.Path.Combine(folder, fileName);
                        if (System.IO.File.Exists(filePath))
                        {
                            System.IO.File.Delete(filePath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Lỗi xóa file cấu hình: " + ex.Message);
            }
            swStartup.IsOn = false;
            swWakeLock.IsOn = false;
            swSponsorBlock.IsOn = true;

            sliderVolume.Value = 60;
            sliderSpeedSetting.Value = 1.0;
            sliderSeekSetting.Value = 5;

            txtWatchFolder.Text = "";
            txtBgPath.Text = "";
            txtLibraryStatus.Text = "Trạng thái: Chưa chọn thư mục.";
            _listLibrary.Clear();
            _backupLibrary.Clear();

            _listLocal.Clear();
            _backupLocal.Clear();

            _listStream.Clear();
            _backupStream.Clear();

            _listTv.Clear();
            _backupTv.Clear();

            _listSearch.Clear();
            sldBright.Value = 0; sldContrast.Value = 0; sldSat.Value = 0; sldHue.Value = 0;
            sldGamma.Value = 0; sldRed.Value = 0; sldGreen.Value = 0; sldBlue.Value = 0;

            sldSubSize.Value = 55; sldSubMargin.Value = 50; cboSubColor.SelectedIndex = 0;
            sldSubDelay.Value = 0; sldAudioDelay.Value = 0;
            if (_engine != null)
            {
                _engine.IsSponsorBlockEnabled = true;
                _engine.PreventSleep(false);
                _engine.SetVolume(60);
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
                _engine.Stop();
            }
            UpdateListStats();
            UpdateStatus("♻️ Đã khôi phục cài đặt gốc và xóa toàn bộ dữ liệu lưu trữ!");
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
            txtInlineUrl.Text = "https://";
            txtInlineUrl.Select(txtInlineUrl.Text.Length, 0);
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
            if (string.IsNullOrEmpty(url) || url == "https://") return;
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

        private void lstMedia_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            PlaySelectedMedia();
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

            UpdateStatus($"📂 Đã thêm {countAdded} file mới.");
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


