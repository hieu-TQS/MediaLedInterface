using System;
using System.Runtime.InteropServices;

namespace MediaLedInterfaceNew
{
    // Class này gọi trực tiếp hàm mở file cổ điển của Windows (comdlg32.dll)
    public class Win32Helper
    {
        [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetOpenFileName(ref OPENFILENAME ofn);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct OPENFILENAME
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string lpstrFilter;
            public string lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public IntPtr lpstrFile;
            public int nMaxFile;
            public string lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }

        public static string[] ShowOpenFileDialog(IntPtr ownerHwnd, string title, string filter)
        {
            var ofn = new OPENFILENAME();
            ofn.lStructSize = Marshal.SizeOf(ofn);
            ofn.hwndOwner = ownerHwnd;
            ofn.lpstrTitle = title;

            // Chuyển format filter từ | sang \0 để Windows hiểu
            // Ví dụ input: "Video|*.mp4" -> "Video\0*.mp4\0"
            ofn.lpstrFilter = filter.Replace('|', '\0') + "\0";

            // Bộ đệm chứa đường dẫn file trả về (Tăng lên để chứa nhiều file)
            ofn.nMaxFile = 32000;
            ofn.lpstrFile = Marshal.AllocHGlobal(32000); // Cấp phát bộ nhớ thủ công

            // Xóa rác trong bộ nhớ
            byte[] empty = new byte[32000];
            Marshal.Copy(empty, 0, ofn.lpstrFile, 32000);

            // Cờ cấu hình: 
            // 0x00080000 (OFN_EXPLORER): Giao diện Explorer mới
            // 0x00000200 (OFN_ALLOWMULTISELECT): Cho chọn nhiều file
            // 0x00000800 (OFN_PATHMUSTEXIST): File phải tồn tại
            // 0x00000008 (OFN_NOCHANGEDIR): Không đổi thư mục gốc app
            ofn.Flags = 0x00080000 | 0x00000200 | 0x00000800 | 0x00000008;

            if (GetOpenFileName(ref ofn))
            {
                // Xử lý kết quả trả về (Hơi phức tạp vì nó là chuỗi null-terminated)
                string rawStr = Marshal.PtrToStringAuto(ofn.lpstrFile);

                // Nếu chọn nhiều file, Windows trả về: "Folder\0File1\0File2\0File3\0\0"
                // Nếu chọn 1 file: "FullPath\0"

                // Vì Marshal.PtrToStringAuto chỉ đọc đến ký tự \0 đầu tiên,
                // ta phải đọc thủ công cả khối nhớ

                // Cách đơn giản hơn: Dùng buffer managed
                // Nhưng để an toàn với Win32, ta dùng cách tách chuỗi thủ công từ Pointer:

                IntPtr ptr = ofn.lpstrFile;
                string folder = Marshal.PtrToStringAuto(ptr);
                ptr = (IntPtr)((long)ptr + (folder.Length + 1) * Marshal.SystemDefaultCharSize);

                string nextStr = Marshal.PtrToStringAuto(ptr);

                if (string.IsNullOrEmpty(nextStr))
                {
                    // Trường hợp 1 file duy nhất
                    Marshal.FreeHGlobal(ofn.lpstrFile);
                    return new string[] { folder };
                }
                else
                {
                    // Trường hợp nhiều file
                    var resultList = new System.Collections.Generic.List<string>();
                    while (!string.IsNullOrEmpty(nextStr))
                    {
                        resultList.Add(System.IO.Path.Combine(folder, nextStr));

                        ptr = (IntPtr)((long)ptr + (nextStr.Length + 1) * Marshal.SystemDefaultCharSize);
                        nextStr = Marshal.PtrToStringAuto(ptr);
                    }
                    Marshal.FreeHGlobal(ofn.lpstrFile);
                    return resultList.ToArray();
                }
            }

            Marshal.FreeHGlobal(ofn.lpstrFile);
            return new string[0]; // Không chọn gì
        }
    }
}