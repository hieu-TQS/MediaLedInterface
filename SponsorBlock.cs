using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MediaLedInterfaceNew
{
    public class SponsorSegment
    {
        [JsonPropertyName("category")]
        public string Category { get; set; } = "";

        [JsonPropertyName("segment")]
        public double[] Segment { get; set; }

        [JsonPropertyName("UUID")]
        public string UUID { get; set; }
    }

    public static class SponsorBlockClient
    {
        private static readonly HttpClient _client;

        // Khởi tạo Static Constructor để setup HttpClient 1 lần duy nhất
        static SponsorBlockClient()
        {
            _client = new HttpClient();
            // [QUAN TRỌNG] Thêm User-Agent để không bị chặn
            _client.DefaultRequestHeaders.Add("User-Agent", "MediaLedInterface/1.0");
            _client.Timeout = TimeSpan.FromSeconds(5); // Timeout nhanh sau 5s nếu mạng lag
        }

        private static readonly string _categories = "[\"sponsor\",\"intro\",\"outro\",\"selfpromo\",\"interaction\",\"music_offtopic\"]";

        public static async Task<List<SponsorSegment>> GetSegmentsAsync(string videoId)
        {
            try
            {
                string url = $"https://sponsor.ajay.app/api/skipSegments?videoID={videoId}&categories={_categories}";
                Debug.WriteLine($"[SponsorBlock] Request: {url}");

                var response = await _client.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var data = JsonSerializer.Deserialize<List<SponsorSegment>>(json, options);
                    Debug.WriteLine($"[SponsorBlock] Success: {data.Count} segments");
                    return data;
                }
                else
                {
                    // Nếu lỗi 404 nghĩa là video không có dữ liệu skip (bình thường)
                    Debug.WriteLine($"[SponsorBlock] Status: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SponsorBlock] Exception: {ex.Message}");
            }

            return new List<SponsorSegment>();
        }
    }
}