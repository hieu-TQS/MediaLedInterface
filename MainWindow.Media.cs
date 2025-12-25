using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MediaLedInterfaceNew
{
    public sealed partial class MainWindow : Window
    {

        private ObservableCollection<MediaItem> _listLibrary = new ObservableCollection<MediaItem>();
        private ObservableCollection<MediaItem> _listLocal = new ObservableCollection<MediaItem>();
        private ObservableCollection<MediaItem> _listStream = new ObservableCollection<MediaItem>();
        private ObservableCollection<MediaItem> _listTv = new ObservableCollection<MediaItem>();
        private ObservableCollection<MediaItem> _listSearch = new ObservableCollection<MediaItem>();

        private List<MediaItem> _backupLibrary = new List<MediaItem>();
        private List<MediaItem> _backupLocal = new List<MediaItem>();
        private List<MediaItem> _backupStream = new List<MediaItem>();
        private List<MediaItem> _backupTv = new List<MediaItem>();

        private const string FILE_LOCAL = "data_local.json";
        private const string FILE_ONLINE = "data_online.json";
        private const string FILE_TV = "data_tv.json";
        private const string SETTING_WATCH_FOLDER = "WatchFolderPath";

        private MediaItem? _playingItem = null;
        private Random _rng = new Random();
        private bool _isUserActionStop = false;
        private readonly HttpClient _httpClient = new HttpClient();
        private Dictionary<int, string> _youtubePageTokens = new Dictionary<int, string>();

        private static System.Threading.SemaphoreSlim _metadataSemaphore = new System.Threading.SemaphoreSlim(3, 3);
        private static System.Threading.SemaphoreSlim _logoSemaphore = new System.Threading.SemaphoreSlim(5, 5);

        private const string MT_USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

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

        public ObservableCollection<MediaItem> CurrentList
        {
            get
            {
                if (lstMedia.ItemsSource is ObservableCollection<MediaItem> list) return list;
                return _listLocal;
            }
        }

        private void SaveListToJson(string fileName, object dataList)
        {
            try
            {
                string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediaLedInterfaceNew");
                if (!System.IO.Directory.Exists(folder)) System.IO.Directory.CreateDirectory(folder);

                string filePath = System.IO.Path.Combine(folder, fileName);

                var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                string json = System.Text.Json.JsonSerializer.Serialize(dataList, options);

                System.IO.File.WriteAllText(filePath, json);
                UpdateStatus($"✅ Đã lưu vào {fileName}", false);
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ Lỗi lưu file {fileName}: {ex.Message}", false, true);
            }
        }

        private List<MediaItem> LoadListFromJson(string fileName)
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediaLedInterfaceNew");
                string filePath = Path.Combine(folder, fileName);

                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    var items = System.Text.Json.JsonSerializer.Deserialize<List<MediaItem>>(json);
                    return items ?? new List<MediaItem>();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Lỗi đọc {fileName}: {ex.Message}");
            }
            return new List<MediaItem>();
        }

        private async void OnBtnSaveSession_Click(object sender, RoutedEventArgs e)
        {
            if (lstMedia.ItemsSource == _listLocal) await Task.Run(() => SaveListToJson(FILE_LOCAL, _listLocal));
            else if (lstMedia.ItemsSource == _listStream) await Task.Run(() => SaveListToJson(FILE_ONLINE, _listStream));
            else if (lstMedia.ItemsSource == _listTv) await Task.Run(() => SaveListToJson(FILE_TV, _listTv));
            else UpdateStatus("⚠️ Tab này không hỗ trợ lưu.", false, true);
        }

        private async void OnBtnRestoreSession_Click(object sender, RoutedEventArgs e)
        {
            List<MediaItem> loadedItems = null;
            ObservableCollection<MediaItem> targetList = null;
            List<MediaItem> targetBackup = null;
            if (lstMedia.ItemsSource == _listLocal)
            {
                loadedItems = await Task.Run(() => LoadListFromJson(FILE_LOCAL));
                targetList = _listLocal;
                targetBackup = _backupLocal;
            }
            else if (lstMedia.ItemsSource == _listStream)
            {
                loadedItems = await Task.Run(() => LoadListFromJson(FILE_ONLINE));
                targetList = _listStream;
                targetBackup = _backupStream;
            }
            else if (lstMedia.ItemsSource == _listTv)
            {
                loadedItems = await Task.Run(() => LoadListFromJson(FILE_TV));
                targetList = _listTv;
                targetBackup = _backupTv;
            }
            if (loadedItems != null && targetList != null)
            {

                int countAdded = 0;

                foreach (var item in loadedItems)
                {
                    bool isExist = targetList.Any(x => x.FullPath == item.FullPath);
                    if (isExist) continue;

                    targetList.Add(item);
                    targetBackup.Add(item);
                    countAdded++;
                    if (targetList == _listLocal && System.IO.File.Exists(item.FullPath))
                    {
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
                    else if ((targetList == _listStream || targetList == _listTv) && !string.IsNullOrEmpty(item.PosterUrl))
                    {
                        _ = Task.Run(async () =>
                        {
                            var bmp = await LoadImageSecurelyAsync(item.PosterUrl);
                            if (bmp != null)
                            {
                                this.DispatcherQueue.TryEnqueue(() => item.Poster = bmp);
                            }
                        });
                    }
                }

                if (countAdded > 0)
                {
                    UpdateStatus($"✅ Đã gộp thêm {countAdded} mục vào danh sách.", false);
                    UpdateListStats();
                }
                else
                {
                    UpdateStatus("⚠ Không có mục mới nào (tất cả đã tồn tại).", false);
                }
            }
            else
            {
                UpdateStatus("⚠️ Không tìm thấy file dữ liệu đã lưu cho tab này.", false, true);
            }
        }

        private void PlaySelectedMedia()
        {
            ClearAllSponsorMarks();
            if (_engine == null) return;

            if (_playingItem != null)
            {
                _playingItem.IsPlaying = false;
                _playingItem.IsPaused = false;
            }

            if (lstMedia.SelectedItem is MediaItem selectedItem)
            {
                _playingItem = selectedItem;
                _playingItem.IsPlaying = true;
                _playingItem.IsPaused = false;

                _engine.SetHttpHeaders(selectedItem.UserAgent, selectedItem.Referrer);
                _engine.PlayTransition(selectedItem.FullPath);
                _engine.Resume();

                UpdatePlayingTabIndicator();
                UpdateMpvLayout();

                btnPlay.Visibility = Visibility.Collapsed;
                btnPause.Visibility = Visibility.Visible;
                UpdateStatus($"▶ Đang phát: {selectedItem.FileName}", true);
                if (lstMedia.ItemsSource != _listTv) StartVisualizer();
            }
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

        private void StopComplete()
        {
            if (_reconnectTimer != null) _reconnectTimer.Stop();
            _engine?.Stop();

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
            if (cvsSponsor != null) cvsSponsor.Children.Clear();
        }

        private void btnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (_engine == null) return;
            var targetItem = lstMedia.SelectedItem as MediaItem;
            if (targetItem == null) { UpdateStatus("⚠️ Vui lòng chọn một mục!", false, true); return; }

            if (_playingItem == targetItem)
            {
                _engine.Resume();
                targetItem.IsPaused = false;
                btnPlay.Visibility = Visibility.Collapsed;
                btnPause.Visibility = Visibility.Visible;
                UpdatePlayingTabIndicator();
                UpdateStatus($"▶ Tiếp tục: {targetItem.FileName}");
            }
            else PlaySelectedMedia();
        }

        private void btnPause_Click(object sender, RoutedEventArgs e)
        {
            _engine?.Pause();
            if (_playingItem != null) _playingItem.IsPaused = true;
            btnPause.Visibility = Visibility.Collapsed;
            btnPlay.Visibility = Visibility.Visible;
            StopVisualizer();
            UpdatePlayingTabIndicator();
            UpdateStatus("⏸ Đã tạm dừng.", true);
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            _isUserActionStop = true;
            StopComplete();
            ClearAllSponsorMarks();
            UpdateStatus("⏹ Đã dừng hẳn.", true);
        }

        private void btnNext_Click(object sender, RoutedEventArgs e)
        {
            PlayNextVideo(false);
            UpdateStatus("⏭ Đang chuyển bài tiếp theo...");
        }

        private void btnPrev_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentList.Count == 0) return;
            int newIndex = -1;
            int currentIndex = lstMedia.SelectedIndex;

            if (_currentMode == PlayerMode.Shuffle) newIndex = _rng.Next(CurrentList.Count);
            else newIndex = (currentIndex > 0) ? currentIndex - 1 : CurrentList.Count - 1;

            if (newIndex >= 0)
            {
                lstMedia.SelectedIndex = newIndex;
                lstMedia.ScrollIntoView(lstMedia.SelectedItem);
                PlaySelectedMedia();
            }
            UpdateStatus("⏮ Đang quay lại bài trước...");
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

        private FileSystemWatcher? _folderWatcher;
        private void StartWatching(string path)
        {
            if (_folderWatcher != null) { try { _folderWatcher.Dispose(); } catch { } _folderWatcher = null; }
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

            try
            {
                _folderWatcher = new FileSystemWatcher(path);
                _folderWatcher.IncludeSubdirectories = true;
                _folderWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size;
                _folderWatcher.Filter = "*.*";
                _folderWatcher.Created += OnFileChanged;
                _folderWatcher.Deleted += OnFileChanged;
                _folderWatcher.Renamed += OnFileChanged;
                _folderWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) { UpdateStatus($"⚠️ Lỗi Watcher: {ex.Message}", false, true); }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            this.DispatcherQueue.TryEnqueue(() => { _debounceTimer.Stop(); _debounceTimer.Start(); });
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
        private async Task SearchYoutubeAsync(string keyword, int pageIndex)
        {
            string pageToken = (pageIndex > 0 && _youtubePageTokens.ContainsKey(pageIndex)) ? $"&pageToken={_youtubePageTokens[pageIndex]}" : "";
            string url = $"https://www.googleapis.com/youtube/v3/search?part=snippet&maxResults=50&q={Uri.EscapeDataString(keyword)}&type=video&key={KeyManager.GetYoutubeKey()}{pageToken}";

            UpdateStatus($"⏳ YouTube: Đang tải trang {pageIndex + 1}...", false);

            try
            {
                string json = await _httpClient.GetStringAsync(url);
                var root = JsonNode.Parse(json);
                var nextToken = root?["nextPageToken"]?.ToString();
                if (!string.IsNullOrEmpty(nextToken)) _youtubePageTokens[pageIndex + 1] = nextToken;

                var items = root?["items"]?.AsArray();
                if (items != null && items.Count > 0)
                {
                    foreach (var item in items)
                    {
                        string vid = item?["id"]?["videoId"]?.ToString();
                        if (!string.IsNullOrEmpty(vid))
                        {
                            var snippet = item?["snippet"];
                            _listSearch.Add(new MediaItem
                            {
                                FileName = snippet["title"]?.ToString(),
                                FullPath = $"https://www.youtube.com/watch?v={vid}",
                                Type = "YOUTUBE",
                                ChannelName = snippet["channelTitle"]?.ToString(),
                                Duration = "YouTube",
                                Poster = new BitmapImage(new Uri($"https://img.youtube.com/vi/{vid}/mqdefault.jpg"))
                            });
                        }
                    }
                    UpdatePaginationUI(true, pageIndex, !string.IsNullOrEmpty(nextToken));
                    UpdateStatus($"✅ YouTube: Hiển thị trang {pageIndex + 1}.");
                    UpdateListStats();
                }
                else { UpdateStatus("⚠️ Không tìm thấy video nào.", false); UpdatePaginationUI(true, pageIndex, false); }
            }
            catch (Exception ex) { UpdateStatus("❌ Lỗi YouTube API: " + ex.Message, false, true); }
        }

        private async Task SearchDailymotionAsync(string keyword, int pageIndex)
        {
            int apiPage = pageIndex + 1;
            string apiUrl = $"https://api.dailymotion.com/videos?fields=id,title,thumbnail_240_url,owner.username&search={Uri.EscapeDataString(keyword)}&limit=50&page={apiPage}";
            UpdateStatus($"⏳ Dailymotion: Đang tải trang {apiPage}...");

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
                            _listSearch.Add(new MediaItem
                            {
                                FileName = item?["title"]?.ToString(),
                                FullPath = $"https://www.dailymotion.com/video/{vid}",
                                Type = "DAILYMOTION",
                                ChannelName = item?["owner.username"]?.ToString(),
                                Duration = "Dailymotion",
                                Poster = new BitmapImage(new Uri(item?["thumbnail_240_url"]?.ToString()))
                            });
                        }
                    }
                    UpdatePaginationUI(true, pageIndex, apiHasMore || list.Count == 50);
                    UpdateStatus($"✅ Dailymotion: Hiển thị trang {apiPage}.");
                    UpdateListStats();
                }
                else { UpdateStatus("⚠️ Đã hết kết quả.", false); UpdatePaginationUI(true, pageIndex, false); }
            }
            catch (Exception ex) { UpdateStatus("❌ Lỗi Dailymotion: " + ex.Message, false, true); }
        }

        private async Task SearchWindowsIndexAsync(string keyword, int pageIndex, int filterMode)
        {
            string normalizedKeyword = keyword.Normalize(System.Text.NormalizationForm.FormC).Replace("'", "''");

            UpdateStatus($"⏳ Đang tìm kiếm '{keyword}'...", false);

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

        private async Task TriggerSearchNavigation()
        {
            _listSearch.Clear();
            switch (_currentSourceMode)
            {
                case 0: await SearchYoutubeAsync(_currentSearchQuery, _currentSearchPage); break;
                case 1: await SearchDailymotionAsync(_currentSearchQuery, _currentSearchPage); break;
                case 2: await SearchWindowsIndexAsync(_currentSearchQuery, _currentSearchPage, _currentPcFilterMode); break;
            }
            UpdateListStats();
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

        private void btnDelete_Click(object sender, RoutedEventArgs e) => DeleteSelectedItems();

        private void DeleteSelectedItems()
        {
            var selectedItems = lstMedia.SelectedItems.Cast<MediaItem>().ToList();
            var activeList = CurrentList;
            foreach (var item in selectedItems)
            {
                activeList.Remove(item);
                if (activeList == _listLocal) _backupLocal.Remove(item);
                else if (activeList == _listStream) _backupStream.Remove(item);
                else if (activeList == _listTv) _backupTv.Remove(item);
            }
            UpdateListStats();
        }

        public async void HandleExternalPlaylist(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            BringToFront();
            if (rbTV != null) { rbTV.IsChecked = true; OnNavTabClick(rbTV, null); }

            UpdateStatus($"📂 Đang đọc danh sách kênh từ: {Path.GetFileName(filePath)}...", false);
            try
            {
                string content = await File.ReadAllTextAsync(filePath);
                ParseM3UContent(content);
                UpdateStatus(_listTv.Count > 0 ? $"✅ Đã nhập M3U ({_listTv.Count} kênh)." : "⚠ File M3U rỗng.", false, _listTv.Count == 0);
            }
            catch (Exception ex) { UpdateStatus($"❌ Lỗi đọc file M3U: {ex.Message}", false, true); }
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
                    currentLogo = "";
                    currentName = "Channel";
                    currentGroup = "Chung";
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
                    string capturedLogo = currentLogo;

                    var item = new MediaItem
                    {
                        FileName = currentName,
                        FullPath = l,
                        Type = "TV CHANNEL",
                        ChannelName = currentGroup,
                        Duration = "LIVE",
                        UserAgent = currentUa,
                        Referrer = currentRef,
                        Poster = null,
                        PosterUrl = capturedLogo
                    };

                    if (!string.IsNullOrEmpty(capturedLogo))
                    {
                        _ = Task.Run(async () =>
                        {
                            await _logoSemaphore.WaitAsync();
                            try
                            {
                                var img = await LoadImageSecurelyAsync(capturedLogo);
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
                    else
                    {
                        FetchMetadataInBackground(item);
                    }

                    _listTv.Add(item);
                    _backupTv.Add(item);
                }
            }
            UpdateListStats();
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
                                        Duration = "LIVE",
                                        PosterUrl = logo
                                    };

                                    if (!string.IsNullOrEmpty(logo))
                                    {
                                        try
                                        {
                                            mediaItem.Poster = new BitmapImage(new Uri(logo));
                                        }
                                        catch { }
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
                    PosterUrl = data.Thumb,
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
                        string debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..\\..\\..\\yt-dlp.exe");
                        if (System.IO.File.Exists(debugPath)) exePath = debugPath;
                        else return;
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
                                    string uploader = node["uploader"]?.ToString() ?? node["channel"]?.ToString();
                                    this.DispatcherQueue.TryEnqueue(() =>
                                    {
                                        if (!string.IsNullOrEmpty(title) && item.FileName == "Unknown Channel")
                                            item.FileName = title;

                                        if (!string.IsNullOrEmpty(uploader))
                                        {
                                            item.ChannelName = uploader;
                                        }
                                        if (!string.IsNullOrEmpty(thumbnail))
                                        {
                                            item.PosterUrl = thumbnail;
                                            if (item.Poster == null)
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
                                        }
                                    });
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Metadata fetch error: " + ex.Message);
                }
                finally
                {
                    _metadataSemaphore.Release();
                }
            });
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
    }
}