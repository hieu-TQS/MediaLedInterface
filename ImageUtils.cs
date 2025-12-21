using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MediaLedInterfaceNew
{
    public static class ImageUtils
    {
        private static string CacheFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediaLedInterface", "ImageCache");

        private static string GetCacheFileName(string inputPath)
        {
            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(inputPath));
                return BitConverter.ToString(hash).Replace("-", "").ToLower() + ".jpg";
            }
        }

        public static string GetCachedPath(string inputPath)
        {
            try
            {
                if (!File.Exists(inputPath)) return inputPath;

                // BƯỚC 1: KIỂM TRA KÍCH THƯỚC TRƯỚC (QUAN TRỌNG)
                // Dùng FileStream để chỉ đọc header ảnh, không load cả file vào RAM -> Siêu nhanh
                using (var fs = new FileStream(inputPath, FileMode.Open, FileAccess.Read))
                {
                    using (var img = Image.FromStream(fs, false, false))
                    {
                        // Nếu ảnh nhỏ hơn hoặc bằng 4K (3840x2160) -> TRẢ VỀ FILE GỐC NGAY LẬP TỨC
                        // Không tạo cache, không nén, giữ nguyên chất lượng.
                        if (img.Width <= 3840 && img.Height <= 2160)
                        {
                            return inputPath;
                        }
                    }
                }

                // ========================================================
                // NẾU CODE CHẠY ĐẾN ĐÂY NGHĨA LÀ ẢNH > 4K -> BẮT ĐẦU NÉN
                // ========================================================

                if (!Directory.Exists(CacheFolder)) Directory.CreateDirectory(CacheFolder);
                string cachePath = Path.Combine(CacheFolder, GetCacheFileName(inputPath));

                // Nếu đã có cache từ lần trước rồi thì dùng luôn
                if (File.Exists(cachePath)) return cachePath;

                // Bắt đầu Resize
                using (var image = Image.FromFile(inputPath))
                {
                    int maxW = 3840;
                    int maxH = 2160;
                    double ratio = Math.Min((double)maxW / image.Width, (double)maxH / image.Height);
                    int newW = (int)(image.Width * ratio);
                    int newH = (int)(image.Height * ratio);

                    using (var destImage = new Bitmap(newW, newH))
                    {
                        using (var graphics = Graphics.FromImage(destImage))
                        {
                            graphics.CompositingMode = CompositingMode.SourceCopy;
                            graphics.CompositingQuality = CompositingQuality.HighSpeed;
                            graphics.InterpolationMode = InterpolationMode.Bicubic;
                            graphics.SmoothingMode = SmoothingMode.HighSpeed;
                            graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;

                            using (var wrapMode = new ImageAttributes())
                            {
                                wrapMode.SetWrapMode(WrapMode.TileFlipXY);
                                graphics.DrawImage(image, new Rectangle(0, 0, newW, newH), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrapMode);
                            }
                        }

                        // Lưu JPEG Quality 90
                        var encoder = GetEncoder(ImageFormat.Jpeg);
                        var encoderParams = new EncoderParameters(1);
                        encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
                        destImage.Save(cachePath, encoder, encoderParams);
                    }
                }

                return cachePath;
            }
            catch
            {
                return inputPath;
            }
        }

        private static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid) return codec;
            }
            return null;
        }

        public static void ClearCache()
        {
            try { if (Directory.Exists(CacheFolder)) Directory.Delete(CacheFolder, true); } catch { }
        }
    }
}