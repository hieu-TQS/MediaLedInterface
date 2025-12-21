using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle; // Cần thiết cho Single Instance
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.Activation;
using AppInstance = Microsoft.Windows.AppLifecycle.AppInstance;

namespace MediaLedInterfaceNew
{
    public partial class App : Application
    {
        private Window? m_window;

        public App()
        {
            this.InitializeComponent();
        }

        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // 1. --- SINGLE INSTANCE (CHỈ CHO PHÉP 1 APP CHẠY) ---
            var mainInstance = AppInstance.FindOrRegisterForKey("MediaLedInterface_Unique_ID_v2");

            if (!mainInstance.IsCurrent)
            {
                // Nếu App đã mở: Gửi tín hiệu sang App chính rồi tắt App phụ này
                var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
                await mainInstance.RedirectActivationToAsync(activatedArgs);
                System.Diagnostics.Process.GetCurrentProcess().Kill();
                return;
            }

            // Nếu là App chính: Đăng ký nhận tin nhắn
            mainInstance.Activated += OnAppActivated;

            // 2. --- KHỞI TẠO CỬA SỔ ---
            m_window = new MainWindow();
            m_window.Activate();

            // 3. --- XỬ LÝ FILE LẦN ĐẦU (Lúc App đang tắt) ---
            var currentArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            HandleActivation(currentArgs);
        }

        // Sự kiện: Nhận file mới khi App ĐANG CHẠY
        private void OnAppActivated(object sender, AppActivationArguments args)
        {
            m_window?.DispatcherQueue.TryEnqueue(() =>
            {
                if (m_window is MainWindow w)
                {
                    w.BringToFront();
                    HandleActivation(args); // Gọi hàm xử lý với args mới
                }
            });
        }

        // --- [KHU VỰC SỬA LỖI] ---
        private void HandleActivation(AppActivationArguments args)
        {
            if (m_window is MainWindow w)
            {
                string[] filesToProcess = Array.Empty<string>();

                // TH1: Mở bằng cách Double Click file (Phổ biến nhất)
                // Windows gửi file qua FileActivatedEventArgs. Đây là dữ liệu MỚI NHẤT.
                if (args.Kind == ExtendedActivationKind.File)
                {
                    var fileArgs = args.Data as IFileActivatedEventArgs;
                    if (fileArgs != null && fileArgs.Files.Count > 0)
                    {
                        filesToProcess = fileArgs.Files.Select(f => f.Path).ToArray();
                    }
                }
                else if (args.Kind == ExtendedActivationKind.Launch)
                {
                    var launchArgs = args.Data as ILaunchActivatedEventArgs;
                    if (launchArgs != null)
                    {
                        string rawArgs = launchArgs.Arguments;

                        if (!string.IsNullOrEmpty(rawArgs))
                        {
                            // [FIX LỖI QUAN TRỌNG] 
                            // Dùng Regex để tách các tham số (xử lý đúng cả đường dẫn có khoảng trắng nằm trong ngoặc kép)
                            // Pattern này sẽ bắt: "Nội dung trong ngoặc" HOẶC Các_ký_tự_liền_nhau_không_có_khoảng_trắng
                            var matches = System.Text.RegularExpressions.Regex.Matches(rawArgs, "(\"[^\"]+\"|\\S+)");

                            var validFiles = new List<string>();

                            foreach (System.Text.RegularExpressions.Match match in matches)
                            {
                                // Xóa ngoặc kép bao quanh (nếu có) của TỪNG tham số
                                string cleanPath = match.Value.Trim('"');

                                // LỌC RÁC:
                                // 1. Bỏ qua chính file .exe của app (nguyên nhân gây lỗi lù lù tên app)
                                if (cleanPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

                                // 2. Bỏ qua tham số autostart
                                if (cleanPath.Contains("--autostart")) continue;

                                // 3. (Tuỳ chọn) Chỉ lấy nếu file tồn tại để tránh lỗi đường dẫn ảo
                                // if (System.IO.File.Exists(cleanPath)) 

                                validFiles.Add(cleanPath);
                            }

                            filesToProcess = validFiles.ToArray();
                        }
                    }
                }

                // --- GỬI SANG MAINWINDOW ---
                if (filesToProcess.Length > 0)
                {
                    // Lọc file playlist
                    var playlist = filesToProcess.FirstOrDefault(f => f.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) ||
                                                                      f.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase));

                    // Lọc file video
                    var videos = filesToProcess.Where(f => !f.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) &&
                                                           !f.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)).ToArray();

                    if (!string.IsNullOrEmpty(playlist))
                    {
                        w.HandleExternalPlaylist(playlist);
                    }
                    else if (videos.Length > 0)
                    {
                        w.HandleExternalFiles(videos);
                    }
                }
            }
        }
    }
}
