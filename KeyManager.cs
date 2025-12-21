using System;
using System.Collections.Generic;
using System.IO;

namespace MediaLedInterfaceNew
{
    public static class KeyManager
    {
        // Danh sách Key lưu trong RAM
        private static List<string> _youtubeKeys = new List<string>();

        // Đường dẫn file (nằm cùng chỗ với file .exe)
        private static string PathYt => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "keys_youtube.txt");

        // Hàm tải Key từ file
        public static void LoadKeys()
        {
            // Chỉ đọc mỗi YouTube Key
            _youtubeKeys = ReadFileLines(PathYt);
        }

        // Hàm đọc file an toàn (Tự tạo file nếu chưa có)
        private static List<string> ReadFileLines(string path)
        {
            var list = new List<string>();
            try
            {
                if (!File.Exists(path))
                {
                    // Tạo file mẫu nếu chưa có
                    File.WriteAllText(path, "PASTE_KEY_HERE_1\nPASTE_KEY_HERE_2");
                }

                var lines = File.ReadAllLines(path);
                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                    {
                        list.Add(line.Trim());
                    }
                }
            }
            catch { }
            return list;
        }

        // Hàm lấy Key ngẫu nhiên
        private static Random _rng = new Random();

        public static string GetYoutubeKey()
        {
            if (_youtubeKeys.Count == 0) return "";
            return _youtubeKeys[_rng.Next(_youtubeKeys.Count)];
        }

        public static void OpenFileToEdit(string type)
        {
            if (type == "yt")
            {
                string path = PathYt;

                if (!File.Exists(path)) File.WriteAllText(path, "");

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = path,
                    UseShellExecute = true
                });
            }
        }
    }
}