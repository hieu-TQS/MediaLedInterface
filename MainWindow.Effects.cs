using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MediaLedInterfaceNew
{
    public sealed partial class MainWindow : Window
    {
        private FontIcon? _fsVolIcon = null;
        private FontIcon? _fsRepeatIcon = null;
        private WasapiLoopbackCapture? _audioCapture;
        private const int FftLength = 1024;
        private float[] _fftBuffer = new float[FftLength];
        private int _fftPos = 0;
        private double[] _lastLevels = new double[9];
        private double[] _currentPeaks = new double[9];
        private int[] _peakHoldTimers = new int[9];
        private const int PEAK_HOLD_FRAMES = 20;
        private double _currentMaxLevel = 0.01;
        private const double NOISE_GATE = 0.00025;
        private string _currentAudioDeviceId = "";
        private DispatcherTimer _audioDeviceCheckTimer;
        private string _tickerTextColor = "&H00FFFFFF";
        private string _tickerBgColor = "&H000000FF";
        private List<string> _allSystemFonts = new List<string>();
        private bool _isDragging = false;
        private bool _isEffectResizing = false;
        private Windows.Foundation.Point _startPoint;
        private double _orgLeft, _orgTop, _orgWidth, _orgHeight;
        private double _orgAspectRatio;
        private FrameworkElement? _selectedElement = null;
        private string _currentResizeMode = "";
        private bool _isInternalFullscreen = false;
        private Slider? _fsTimeSlider = null;
        private Slider? _fsVolSlider = null;
        private TextBlock? _fsCurrentTime = null;
        private TextBlock? _fsTotalTime = null;
        private FontIcon? _fsPlayIcon = null;
        private Microsoft.UI.Xaml.Shapes.Rectangle? _fsBufferRect = null;
        private Canvas? _fsSponsorCanvas = null;
        private Border? _fsPreviewTip = null;
        private TextBlock? _fsPreviewText = null;
        private Border? _fsSponsorTip = null;
        private TextBlock? _fsSponsorText = null;
        private bool _isZenMode = false;
        private DispatcherTimer _netTimer;
        private NetworkInterface? _activeNic;
        private long _lastBytesRecv = 0;
        private long _lastBytesSent = 0;
        private long _sessionDownloaded = 0;
        private string _currentSsid = "";
        private MediaEngine.MonitorInfo? _selectedMonitor = null;
        private IntPtr _lastMonitorHandle = IntPtr.Zero;
        private bool _isLedOn = false;
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;


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

        private void CheckAudioDeviceChanged(object sender, object e)
        {
            try
            {
                bool shouldBeRunning = _engine != null && _engine.IsPlaying() && !_engine.IsPaused();
                using (var enumerator = new MMDeviceEnumerator())
                {
                    var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    if (_currentAudioDeviceId != defaultDevice.ID)
                    {
                        if (_audioCapture != null) { StopVisualizer(); StartVisualizer(); }
                        else if (shouldBeRunning) { StartVisualizer(); }
                    }
                }
            }
            catch { }
        }

        private void OnAudioDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0) return;
            for (int i = 0; i < e.BytesRecorded; i += 4)
            {
                float sample = BitConverter.ToSingle(e.Buffer, i);
                _fftBuffer[_fftPos] = sample;
                _fftPos++;
                if (_fftPos >= FftLength)
                {
                    _fftPos = 0;
                    CalculateFFT();
                }
            }
        }

        private void CalculateFFT()
        {
            NAudio.Dsp.Complex[] fftComplex = new NAudio.Dsp.Complex[FftLength];
            for (int i = 0; i < FftLength; i++)
            {
                double window = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (FftLength - 1)));
                fftComplex[i].X = (float)(_fftBuffer[i] * window);
                fftComplex[i].Y = 0;
            }
            FastFourierTransform.FFT(true, (int)Math.Log(FftLength, 2.0), fftComplex);
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
            double frameMax = 0;
            for (int i = 0; i < 9; i++) { if (bands[i] > frameMax) frameMax = bands[i]; }

            if (frameMax > _currentMaxLevel)
            {
                _currentMaxLevel = frameMax;
            }
            else
            {
                _currentMaxLevel *= 0.90;
                if (_currentMaxLevel < 0.005) _currentMaxLevel = 0.005;
            }

            double agcFactor = 0.65 / _currentMaxLevel;
            bool isSilence = frameMax < NOISE_GATE;
            double[] boosted = new double[9];

            for (int i = 0; i < 9; i++)
            {
                if (isSilence)
                {
                    boosted[i] = 0;
                }
                else
                {
                    double val = bands[i] * agcFactor;
                    if (i == 7) val *= 50.0;
                    else if (i == 6) val *= 8.0;
                    else if (i == 8) val *= 7.0;
                    else if (i == 5) val *= 8.0;
                    else if (i == 0) val *= 1.0;
                    else if (i == 1 || i == 2) val *= 1.0;
                    else if (i == 3 || i == 4) val *= 1.0;

                    boosted[i] = val;
                }
            }
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
            double containerHeight = 100;
            if (pnlAudioViz.Parent is FrameworkElement parent)
            {
                containerHeight = parent.ActualHeight;
                if (containerHeight < 20) containerHeight = 20;
            }
            double maxHeight = containerHeight * 0.95;
            double targetHeight = signalValue * maxHeight;
            if (targetHeight > maxHeight) targetHeight = maxHeight;
            if (targetHeight < 4) targetHeight = 4;

            double smoothFactorUp = 0.2;
            double smoothFactorDown = 0.1;

            if (targetHeight > _lastLevels[index])
            {
                _lastLevels[index] += (targetHeight - _lastLevels[index]) * smoothFactorUp;
            }
            else
            {
                _lastLevels[index] += (targetHeight - _lastLevels[index]) * smoothFactorDown;
            }

            if (_lastLevels[index] > maxHeight) _lastLevels[index] = maxHeight;
            if (_lastLevels[index] < 4) _lastLevels[index] = 4;

            bar.Height = _lastLevels[index];

            if (_lastLevels[index] > _currentPeaks[index])
            {
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
                    _currentPeaks[index] -= 0.3;
                }
            }

            if (_currentPeaks[index] > maxHeight) _currentPeaks[index] = maxHeight;
            if (_currentPeaks[index] < 4) _currentPeaks[index] = 4;

            peakBar.Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, _currentPeaks[index]);
        }

        private double GetBandAverage(NAudio.Dsp.Complex[] data, int startIdx, int endIdx)
        {
            double sum = 0;
            for (int i = startIdx; i <= endIdx && i < data.Length; i++)
            {
                double magnitude = Math.Sqrt(data[i].X * data[i].X + data[i].Y * data[i].Y);
                sum += magnitude;
            }
            return sum / (endIdx - startIdx + 1);
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
                        int w = (int)g.ActualWidth; int h = (int)g.ActualHeight;
                        if (w <= 0) w = 100; if (h <= 0) h = 100;
                        string optimizedPath = await Task.Run(() => CreateResizedLogoCache(originalPath, w, h));

                        logoList.Add(new LogoLayer
                        {
                            Path = optimizedPath,
                            X = (int)Canvas.GetLeft(g),
                            Y = (int)Canvas.GetTop(g),
                            Width = w,
                            Height = h
                        });
                    }
                }
                await _engine.UpdateLogoLayers(logoList);
                UpdateStatus($"✅ Đã cập nhật {logoList.Count} logo.");
            }
            else
            {
                await _engine.UpdateLogoLayers(null);
                UpdateStatus("Đã tắt Logo.");
            }
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
                AreaControls.Visibility = Visibility.Visible;
                if (RowTitleBar != null) RowTitleBar.Height = new GridLength(32);
                UpdateUIVisibility();
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
        private void ToggleControlBar()
        {
            if (_isInternalFullscreen)
            {
                if (FullscreenPopup.IsOpen) CloseFsWindow();
                else ShowFsWindow();
            }
        }

        private void ShowFsWindow()
        {
            if (PopupContainer.Children.Count == 0) PopupContainer.Children.Add(CreateFullscreenControls());
            if (this.Content is FrameworkElement root) { PopupContainer.Width = root.ActualWidth; PopupContainer.Height = root.ActualHeight; }
            FullscreenPopup.IsOpen = true;
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
                    if (targetElement == null || targetElement.ActualWidth <= 0 || !targetElement.IsLoaded) return;

                    var rootElement = this.Content as UIElement;
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
                        return;
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
        private async void ToggleZenMode()
        {
            _isZenMode = !_isZenMode;
            UpdateUIVisibility();
            if (_isZenMode)
                UpdateStatus("Đã BẬT chế độ toàn cảnh (Zen Mode)", false);
            else
                UpdateStatus("Đã TẮT chế độ toàn cảnh", false);
            RootGrid.UpdateLayout();
            await Task.Delay(20);
            UpdateMpvLayout();
        }

        private void UpdateUIVisibility()
        {
            if (_isZenMode)
            {
                AreaNav.Visibility = Visibility.Collapsed;
                AreaSidebar.Visibility = Visibility.Collapsed;
                if (SidebarSplitter != null) SidebarSplitter.Visibility = Visibility.Collapsed;
                if (StatusBarGrid != null) StatusBarGrid.Visibility = Visibility.Collapsed;
                ColSidebar.Width = new GridLength(0);
                if (ColSplitter != null) ColSplitter.Width = new GridLength(0);
                if (ColNav != null) ColNav.Width = new GridLength(0);
                if (RootGrid.RowDefinitions.Count > 1) RootGrid.RowDefinitions[1].Height = new GridLength(0);
            }
            else
            {
                AreaNav.Visibility = Visibility.Visible;
                AreaSidebar.Visibility = Visibility.Visible;
                if (SidebarSplitter != null) SidebarSplitter.Visibility = Visibility.Visible;
                if (StatusBarGrid != null) StatusBarGrid.Visibility = Visibility.Visible;
                ColSidebar.Width = new GridLength(300);
                if (ColSplitter != null) ColSplitter.Width = new GridLength(4);
                if (ColNav != null) ColNav.Width = GridLength.Auto;
                if (RootGrid.RowDefinitions.Count > 1) RootGrid.RowDefinitions[1].Height = new GridLength(24);
            }
        }

        private void btnToggleLed_Click(object sender, RoutedEventArgs e)
        {
            if (_engine == null) return;
            if (_isPlayerMode) return;
            bool isManual = swLedMode != null && swLedMode.IsOn;

            if (!isManual && _selectedMonitor == null)
            {
                UpdateStatus("⛔ Chưa chọn màn hình xuất! Vui lòng kiểm tra dây cáp hoặc chọn chế độ Thủ công.", false, true);
                RefreshMonitors();
                return;
            }

            if (!_engine.IsPlaying() && !_engine.IsShowingWallpaper)
            {
                _engine.ShowWallpaper();
            }

            _isLedOn = !_isLedOn;

            if (_isLedOn)
            {
                ApplyLedConfig();

                if (btnToggleLed.Content is FontIcon icon)
                {
                    icon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 140, 0));
                    if (iconLed != null) iconLed.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 140, 0));
                }

                if (isManual)
                    UpdateStatus($"🚀 Đã xuất LED (Thủ công): {(int)nbLedWidth.Value}x{(int)nbLedHeight.Value}");
                else
                    UpdateStatus($"🚀 Đã xuất hình ra: {_selectedMonitor.Name}");
            }
            else
            {
                _engine.SetLedScreen(false, new MediaEngine.RECT());

                if (btnToggleLed.Content is FontIcon icon)
                {
                    icon.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray);
                    if (iconLed != null) iconLed.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray);
                }
                UpdateStatus("Đã ngắt kết nối màn hình LED.");
            }

            UpdateMpvLayout();
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
    }

}