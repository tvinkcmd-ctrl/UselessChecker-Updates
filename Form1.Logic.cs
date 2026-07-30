// Внутренняя логика Form1 (partial). Вся «внутрянка»: загрузка тулз, сканер угроз,
// поиск Steam-профилей (ЛОКАЛЬНО, без Steam API), сбор характеристик ПК, мониторинг
// реестра и DNS-кэша. Здесь же объявлены ВСЕ поля формы, чтобы partial-часть UI
// (Form1.UI.cs) не дублировала объявления.
#pragma warning disable CS8618, CS8625, CS8601, CS8602, CS8603, CS8604, CS8600, CS8629
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace UselessChecker
{
    public partial class Form1 : Form
    {
        #region Поля формы (единое место объявления для обеих partial-частей)
        private static readonly string ToolsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UselessChecker", "Tools");

        // HttpClient для загрузки внешних утилит (НЕ для Steam — Steam API удалён).
        private static readonly HttpClient HttpClientInstance = CreateHttpClient();
        private static long _lastProgressReportTicks = 0;

        private int _logoClickCount = 0;
        private int _currentTab = 0;
        private Panel _mainContent;
        private Panel _viewport;
        private Panel _activePanel;
        private bool _isTransitioning = false;
        private Panel _programsPanel, _foldersPanel, _additionalPanel, _steamPanel, _guidePanel, _pcInfoPanel, _flappyPanel, _contactsPanel;
        private CyberFlappyPanel _flappyGameControl;
        private FlowLayoutPanel _steamFlow;
        private readonly List<CyberButton> _sidebarButtons = new List<CyberButton>();
        private DateTime? _pcBootTime;
        private Label _uptimeValueLabel;
        private readonly System.Windows.Forms.Timer _uptimeTimer;
        private bool _isFlappyTabVisible = false;
        private System.Windows.Forms.Timer _regStatusTimer;
        private readonly List<(string Path, Label StatusLabel)> _monitoredRegistryPaths = new List<(string, Label)>();
        private bool _isCheckingRegistry = false;
        private PCInfoData _cachedPCInfo = null;
        private Panel _pnlScanIdle;
        private Panel _pnlScanRunning;
        private Panel _pnlScanResults;
        private CyberProgressBar _embedProgressBar;
        private Label _embedScanPercent;
        private Label _embedScanStatus;
        private Label _embedScanPath;
        private Label _embedFilesCount;
        private Label _embedCheatsCount;
        private Label _embedPathsCount;
        private ListBox _embedResultsListBox;
        private CyberVScrollBar _embedResultsScrollbar;
        private Label _embedResultsHeader;
        private CancellationTokenSource _scanCts;
        private CyberAimGamePanel _gamePanel;
        private CyberChatOverlay _chatOverlay;
        private CyberChatToggleButton _chatToggleBtn;

                // WS_EX_COMPOSITED убран НАВСЕГДА: он собирал окно в offscreen-буфер, куда при
        // сворачивании с открытым чатом попадал мусор (белые дыры + красные линии по старым
        // координатам оверлея). Без буфера артефактам неоткуда браться. Мерцание скролла
        // этот флаг больше не нужен — контейнеры непрозрачные.
        protected override CreateParams CreateParams
        {
            get { return base.CreateParams; }
        }
        #endregion

        #region Конструктор и инициализация сети
        // Ранняя принудительная инициализация TLS 1.2 для загрузки утилит.
        static Form1()
        {
            try
            {
                System.Net.ServicePointManager.SecurityProtocol =
                    System.Net.SecurityProtocolType.Tls12 |
                    System.Net.SecurityProtocolType.Tls11 |
                    System.Net.SecurityProtocolType.Tls;
            }
            catch { }
        }

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                AllowAutoRedirect = true
            };
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8");
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
            client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
            return client;
        }

        public Form1()
        {
            Logger.Info("Запуск ядра визуализации UselessChecker");
            try
            {
                using var currentProcess = Process.GetCurrentProcess();
                string exePath = currentProcess.MainModule.FileName;
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                    this.Icon = Icon.ExtractAssociatedIcon(exePath);
            }
            catch (Exception ex) { Logger.Error("Icon extraction failed", ex); }

            InitializeDesignUI(); // разметка окна — в partial-части UI

            _uptimeTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _uptimeTimer.Tick += (s, e) => UpdateUptimeRealtime();
            _uptimeTimer.Start();

            _regStatusTimer = new System.Windows.Forms.Timer { Interval = 2500 };
            _regStatusTimer.Tick += async (s, e) => await UpdateRegistryStatusesAsync();
            _regStatusTimer.Start();

                           Shown += async (s, e) =>
            {
                // 1) Сначала загрузка и появление главного окна (intro) — чтобы модалка
                //    «обновлено» всплыла поверх уже готового окна, а не над пустотой.
                var diagTask = RetrievePCInfoDataAsync();
                await ShowIntroScreenAsync();
                _cachedPCInfo = await diagTask;
                InitializeChatOverlay();

                // 2) Главное окно видно и развёрнуто — теперь показываем модалку обновления.
                try
                {
                    string markerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UselessChecker", "uc_updated.marker");
                    if (File.Exists(markerPath))
                    {
                        File.Delete(markerPath);
                        this.WindowState = FormWindowState.Normal;
                        this.Activate();
                        await ShowCustomMessageBoxAsync("Чекер был успешно обновлен до актуальной версии!", "Обновление системы", "Success");
                        // После ОК — гарантированно держим главное окно открытым и активным.
                        this.WindowState = FormWindowState.Normal;
                        this.Activate();
                        this.BringToFront();
                    }
                }
                catch (Exception ex) { Logger.Error("Update Marker Check Failed", ex); }
            };
        }
        #endregion

        #region Загрузка внешних утилит (двухэтапная, с обходом блокировок)
        private static async Task<bool> DownloadAndExtractToolAsync(string primaryUrl, string fallbackUrl, string destExePath, bool isZip)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    string dir = Path.GetDirectoryName(destExePath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    byte[] fileBytes = null;
                    bool downloadSuccess = false;
                    Logger.Info($"[DEBUG] ===== НАЧАЛО ЗАГРУЗКИ =====");
                    Logger.Info($"[DEBUG] Целевой путь: {destExePath}");
                    Logger.Info($"[DEBUG] Основной URL: {primaryUrl}");
                    Logger.Info($"[DEBUG] IsZip: {isZip}");

                    if (primaryUrl.Contains("github.com"))
                    {
                        try
                        {
                            Logger.Info($"[DEBUG] Используем WebClient для GitHub...");
                            using (var webClient = new System.Net.WebClient())
                            {
                                webClient.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                                fileBytes = await webClient.DownloadDataTaskAsync(primaryUrl);
                                downloadSuccess = true;
                                Logger.Info($"[DEBUG] WebClient успешно: {fileBytes.Length} байт");
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"[DEBUG] WebClient ошибка", ex);
                            try
                            {
                                Logger.Info($"[DEBUG] Fallback: curl.exe...");
                                string tempFile = Path.Combine(Path.GetTempPath(), $"download_{Guid.NewGuid()}.exe");
                                var psi = new ProcessStartInfo
                                {
                                    FileName = "curl.exe",
                                    Arguments = $"-L -o \"{tempFile}\" \"{primaryUrl}\" --max-time 120 -H \"Accept: application/octet-stream\"",
                                    UseShellExecute = false,
                                    CreateNoWindow = true,
                                    RedirectStandardError = true
                                };
                                using (var process = Process.Start(psi))
                                {
                                    if (process != null)
                                    {
                                        await process.StandardError.ReadToEndAsync();
                                        await Task.Run(() => process.WaitForExit(120000));
                                    }
                                }
                                if (File.Exists(tempFile) && new FileInfo(tempFile).Length > 10000)
                                {
                                    fileBytes = File.ReadAllBytes(tempFile);
                                    downloadSuccess = true;
                                    Logger.Info($"[DEBUG] curl.exe успешно: {fileBytes.Length} байт");
                                }
                                try { File.Delete(tempFile); } catch { }
                            }
                            catch (Exception ex2) { Logger.Error($"[DEBUG] curl.exe fallback ошибка", ex2); }
                        }
                    }
                    else
                    {
                        try
                        {
                            fileBytes = await HttpClientInstance.GetByteArrayAsync(primaryUrl);
                            downloadSuccess = true;
                            Logger.Info($"[DEBUG] HttpClient успешно: {fileBytes.Length} байт");
                        }
                        catch (Exception ex) { Logger.Error($"[DEBUG] HttpClient ошибка", ex); }
                    }

                    if (!downloadSuccess || fileBytes == null)
                    {
                        Logger.Error($"[DEBUG] ===== ЗАГРУЗКА ПРОВАЛЕНА =====", new Exception("All attempts failed"));
                        return false;
                    }
                    Logger.Info($"[DEBUG] Итоговый размер: {fileBytes.Length} байт");

                    if (isZip)
                    {
                        using var ms = new MemoryStream(fileBytes);
                        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
                        string targetFileName = Path.GetFileName(destExePath);
                        string zipSubDir = "";
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            if (entry.FullName.EndsWith(targetFileName, StringComparison.OrdinalIgnoreCase))
                            {
                                zipSubDir = entry.FullName.Substring(0, entry.FullName.Length - targetFileName.Length);
                                break;
                            }
                        }
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            if (entry.FullName.StartsWith(zipSubDir, StringComparison.OrdinalIgnoreCase) && !entry.FullName.EndsWith("/"))
                            {
                                string entryFileName = entry.FullName.Substring(zipSubDir.Length);
                                if (entryFileName.Contains("/") || entryFileName.Contains("\\")) continue;
                                if (entryFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                                    entryFileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                                {
                                    string outPath = Path.Combine(dir, entryFileName);
                                    using var entryStream = entry.Open();
                                    using var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None);
                                    entryStream.CopyTo(fs);
                                    Logger.Info($"[DEBUG] Распаковано: {outPath}");
                                }
                            }
                        }
                    }
                    else
                    {
                        File.WriteAllBytes(destExePath, fileBytes);
                        Logger.Info($"[DEBUG] Файл сохранен: {destExePath}");
                    }

                    bool result = File.Exists(destExePath);
                    Logger.Info($"[DEBUG] Результат: {result}");
                    Logger.Info($"[DEBUG] ===== ЗАГРУЗКА ЗАВЕРШЕНА =====");
                    return result;
                }
                catch (Exception ex)
                {
                    Logger.Error($"[DEBUG] КРИТИЧЕСКИЙ СБОЙ", ex);
                    return false;
                }
            });
        }
        #endregion

        #region Uptime
        private void UpdateUptimeRealtime()
        {
            if (_uptimeValueLabel != null && _pcBootTime.HasValue)
            {
                var uptime = DateTime.Now - _pcBootTime.Value;
                _uptimeValueLabel.Text = $"{uptime.Days}д {uptime.Hours}ч {uptime.Minutes}м {uptime.Seconds}с";
            }
        }
        #endregion

        #region Сканер угроз (читов/эксплойтов)
        private static IEnumerable<string> SafeEnumerateFiles(string rootPath)
        {
            var dirs = new Queue<string>();
            dirs.Enqueue(rootPath);
            while (dirs.Count > 0)
            {
                string currentDir = dirs.Dequeue();
                IEnumerable<string> files = null;
                try { files = Directory.EnumerateFiles(currentDir); } catch { }
                if (files != null) foreach (var file in files) yield return file;
                IEnumerable<string> subDirs = null;
                try { subDirs = Directory.EnumerateDirectories(currentDir); } catch { }
                if (subDirs != null) foreach (var dir in subDirs) dirs.Enqueue(dir);
            }
        }

        private static string GetFolderDisplayName(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            string normalized = path.TrimEnd('\\', '/');
            if (normalized.Length == 2 && normalized[1] == ':') return normalized + "\\";
            if (normalized.StartsWith(@"\\"))
            {
                string fileName = Path.GetFileName(normalized);
                return string.IsNullOrEmpty(fileName) ? normalized : fileName;
            }
            string result = Path.GetFileName(normalized);
            return string.IsNullOrEmpty(result) ? normalized : result;
        }

        private List<string> BuildScanPaths()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var appDataRoaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appDataLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            void AddPath(string p) { if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p)) paths.Add(p); }
            void AddSubdirectoriesOf(string root)
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
                try { foreach (var dir in Directory.GetDirectories(root)) AddPath(dir); } catch { }
            }

            AddSubdirectoriesOf(userProfile);
            AddSubdirectoriesOf(appDataRoaming);
            AddSubdirectoriesOf(appDataLocal);
            var appDataLocalLow = Path.Combine(userProfile, "AppData", "LocalLow");
            AddSubdirectoriesOf(appDataLocalLow);
            AddSubdirectoriesOf(programData);
            AddPath(userProfile); AddPath(appDataRoaming); AddPath(appDataLocal); AddPath(appDataLocalLow); AddPath(programData);
            AddPath(Path.Combine(userProfile, "Downloads"));
            AddPath(Path.Combine(userProfile, "Documents"));
            AddPath(Path.Combine(userProfile, "Desktop"));
            try
            {
                var usersRoot = Path.GetDirectoryName(userProfile);
                if (Directory.Exists(usersRoot))
                {
                    foreach (var userDir in Directory.GetDirectories(usersRoot))
                    {
                        var name = Path.GetFileName(userDir);
                        if (new[] { "Public", "Default", "Default User", "All Users" }.Any(x => name.Equals(x, StringComparison.OrdinalIgnoreCase))) continue;
                        AddPath(userDir);
                        AddPath(Path.Combine(userDir, "Downloads"));
                        AddPath(Path.Combine(userDir, "Desktop"));
                        AddPath(Path.Combine(userDir, "Documents"));
                        AddSubdirectoriesOf(Path.Combine(userDir, "AppData", "Local"));
                        AddSubdirectoriesOf(Path.Combine(userDir, "AppData", "Roaming"));
                    }
                }
            }
            catch { }
            AddPath(Path.Combine(windowsDir, "Prefetch"));
            AddPath(Path.Combine(windowsDir, "Temp"));
            AddSubdirectoriesOf(Path.Combine(windowsDir, "System32\\tasks"));
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                string root = drive.RootDirectory.FullName;
                AddPath(root);
                foreach (var r in new[] { "Games", "Gaming", "Steam", "SteamLibrary", "Riot Games", "Epic Games" })
                    AddPath(Path.Combine(root, r));
            }
            return paths.ToList();
        }

        private async Task StartEmbedCheatScanAsync()
        {
            _scanCts = new CancellationTokenSource();
            var token = _scanCts.Token;
            var progress = new Progress<ScanProgress>(p => UpdateEmbedScanProgress(p));
            _pnlScanIdle.Visible = false;
            _pnlScanResults.Visible = false;
            _pnlScanRunning.Visible = true;
            foreach (Control c in _pnlScanRunning.Controls)
            {
                if (c is CyberButton btn && btn.Text.Contains("Прерывание"))
                {
                    btn.Enabled = true;
                    btn.Text = "Прервать сканирование";
                }
            }

            List<string> foundCheats;
            int pathsScanned = 0, filesScanned = 0;
            try
            {
                var result = await Task.Run(() => RunCheatScan(progress, token), token);
                foundCheats = result.Results;
                pathsScanned = result.PathsScanned;
                filesScanned = result.TotalFilesScanned;
            }
            catch (OperationCanceledException)
            {
                _pnlScanRunning.Visible = false;
                _pnlScanIdle.Visible = true;
                await ShowCustomMessageBoxAsync("Сканирование завершено по требованию пользователя.", "Отменено", "Warning");
                return;
            }
            catch (Exception ex)
            {
                Logger.Error("CheatScanEmbed", ex);
                _pnlScanRunning.Visible = false;
                _pnlScanIdle.Visible = true;
                await ShowCustomMessageBoxAsync($"Критический сбой процесса поиска:\n{ex.Message}", "Ошибка", "Error");
                return;
            }

            _pnlScanRunning.Visible = false;
            _pnlScanResults.Visible = true;
            _embedResultsListBox.Items.Clear();
            if (foundCheats.Count > 0)
            {
                var unique = foundCheats.Distinct().ToList();
                _embedResultsHeader.Text = $"Обнаружено угроз: {unique.Count} (Проверено: {filesScanned:N0})";
                _embedResultsHeader.ForeColor = CyberPalette.AccentNeon;
                foreach (var path in unique) _embedResultsListBox.Items.Add(path);
            }
            else
            {
                _embedResultsHeader.Text = $"Угроз не найдено (Проверено: {filesScanned:N0} файлов)";
                _embedResultsHeader.ForeColor = CyberPalette.AccentEmerald;
                _embedResultsListBox.Items.Add("Активных следов запрещенного ПО не обнаружено.");
            }
        }

        private void UpdateEmbedScanProgress(ScanProgress p)
        {
            if (this.IsDisposed || _embedProgressBar == null) return;
            try
            {
                _embedProgressBar.Value = Math.Max(0, Math.Min(100, p.Percent));
                _embedScanPercent.Text = $"{p.Percent}%";
                _embedScanStatus.Text = p.Status;
                _embedScanPath.Text = p.Path;
                _embedFilesCount.Text = $"ФАЙЛОВ: {p.FilesScanned:N0}";
                _embedCheatsCount.Text = $"УГРОЗЫ: {p.CheatsFound}";
                _embedPathsCount.Text = $"МАРШРУТЫ: {p.PathsCompleted}/{p.PathsTotal}";
            }
            catch { }
        }

        private (List<string> Results, int PathsScanned, int TotalFilesScanned) RunCheatScan(IProgress<ScanProgress> progress, CancellationToken token)
        {
            var scanPaths = BuildScanPaths();
            int pathsScanned = scanPaths.Count;
            int estimatedTotalFiles = 0;
            foreach (var path in scanPaths)
            {
                try { estimatedTotalFiles += Directory.GetFileSystemEntries(path).Length * 50; }
                catch { estimatedTotalFiles += 1000; }
            }
            estimatedTotalFiles = Math.Max(estimatedTotalFiles, 1);

            var results = new ConcurrentBag<string>();
            int completedPaths = 0, globalFilesScanned = 0, cheatsFound = 0, activePathCounter = 0;
            var options = new ParallelOptions
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
            };

            Parallel.ForEach(scanPaths, options, scanPath =>
            {
                int pathFilesProcessed = 0;
                string folderName = GetFolderDisplayName(scanPath);
                int myPathIndex = Interlocked.Increment(ref activePathCounter);
                try
                {
                    foreach (var item in SafeEnumerateFiles(scanPath))
                    {
                        token.ThrowIfCancellationRequested();
                        pathFilesProcessed++;
                        if (pathFilesProcessed % 1000 == 0)
                        {
                            int beforeAdd = globalFilesScanned;
                            Interlocked.Add(ref globalFilesScanned, 1000);
                            int afterAdd = beforeAdd + 1000;
                            long currentTicks = DateTime.UtcNow.Ticks;
                            long lastTicks = Interlocked.Read(ref _lastProgressReportTicks);
                            if (currentTicks - lastTicks >= 1000000)
                            {
                                if (Interlocked.CompareExchange(ref _lastProgressReportTicks, currentTicks, lastTicks) == lastTicks)
                                {
                                    int percent = estimatedTotalFiles > 0 ? Math.Min(99, (int)((double)afterAdd / estimatedTotalFiles * 100)) : 0;
                                    progress.Report(new ScanProgress
                                    {
                                        Percent = percent,
                                        Status = $"[{myPathIndex}/{pathsScanned}] {folderName}",
                                        Path = scanPath,
                                        FilesScanned = afterAdd,
                                        CheatsFound = cheatsFound,
                                        PathsCompleted = completedPaths,
                                        PathsTotal = pathsScanned
                                    });
                                }
                            }
                        }

                        string itemName;
                        try { itemName = Path.GetFileName(item); } catch { continue; }
                        if (string.IsNullOrEmpty(itemName)) continue;

                        bool isPathExcluded = false;
                        foreach (var exc in CheatSignatures.ExcludePaths)
                        {
                            if (item.IndexOf(exc, StringComparison.OrdinalIgnoreCase) >= 0) { isPathExcluded = true; break; }
                        }
                        if (isPathExcluded)
                        {
                            string ext = "";
                            try { ext = Path.GetExtension(item); } catch { }
                            if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                                ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                                ext.Equals(".sys", StringComparison.OrdinalIgnoreCase))
                                isPathExcluded = false;
                        }
                        if (isPathExcluded) continue;
                        if (CheatSignatures.ExactExcludes.Contains(itemName)) continue;

                        bool exactExcluded = false;
                        foreach (var exc in CheatSignatures.ExactExcludes)
                        {
                            if (itemName.StartsWith(exc + ".", StringComparison.OrdinalIgnoreCase) ||
                                itemName.EndsWith("." + exc, StringComparison.OrdinalIgnoreCase))
                            { exactExcluded = true; break; }
                        }
                        if (exactExcluded) continue;

                        foreach (var cheat in CheatSignatures.CheatNames)
                        {
                            bool match = itemName.Equals(cheat, StringComparison.OrdinalIgnoreCase) ||
                                         itemName.StartsWith(cheat + ".", StringComparison.OrdinalIgnoreCase) ||
                                         itemName.StartsWith(cheat + "-", StringComparison.OrdinalIgnoreCase) ||
                                         itemName.StartsWith(cheat + "_", StringComparison.OrdinalIgnoreCase) ||
                                         itemName.IndexOf("_" + cheat, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         itemName.IndexOf("-" + cheat, StringComparison.OrdinalIgnoreCase) >= 0;
                            if (match && itemName.Length < 40)
                            {
                                string ext;
                                try { ext = Path.GetExtension(item); } catch { ext = ""; }
                                if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                                    ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                                    ext.Equals(".sys", StringComparison.OrdinalIgnoreCase) ||
                                    ext.Equals(".ini", StringComparison.OrdinalIgnoreCase) ||
                                    ext.Equals(".cfg", StringComparison.OrdinalIgnoreCase) ||
                                    ext.Equals(".dat", StringComparison.OrdinalIgnoreCase) ||
                                    ext.Equals(".lua", StringComparison.OrdinalIgnoreCase) ||
                                    ext.Equals(".ahk", StringComparison.OrdinalIgnoreCase))
                                {
                                    string icon = ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ? "[EXE]" :
                                                  ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) ? "[DLL]" :
                                                  ext.Equals(".sys", StringComparison.OrdinalIgnoreCase) ? "[SYS]" :
                                                  ext.Equals(".lua", StringComparison.OrdinalIgnoreCase) ? "[LUA]" :
                                                  ext.Equals(".ahk", StringComparison.OrdinalIgnoreCase) ? "[AHK]" : "[FILE]";
                                    results.Add($"{icon} {ext.ToUpperInvariant()}: {item}");
                                    Interlocked.Increment(ref cheatsFound);
                                }
                                break;
                            }
                        }
                    }
                    int remainder = pathFilesProcessed % 1000;
                    if (remainder > 0) Interlocked.Add(ref globalFilesScanned, remainder);
                    Interlocked.Increment(ref completedPaths);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Logger.Error($"Scan path {scanPath}", ex); }
            });

            progress.Report(new ScanProgress
            {
                Percent = 100,
                Status = "Сканирование завершено",
                Path = "",
                FilesScanned = globalFilesScanned,
                CheatsFound = cheatsFound,
                PathsCompleted = pathsScanned,
                PathsTotal = pathsScanned
            });
            return (results.ToList(), pathsScanned, globalFilesScanned);
        }
        #endregion

        #region Поиск Steam-профилей (ЛОКАЛЬНО, без Steam API)
        // Единый сбор путей установки Steam (реестр + типовые папки на дисках).
        private HashSet<string> GetSteamInstallPaths()
        {
            var steamPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rPath in new[] { @"SOFTWARE\Valve\Steam", @"SOFTWARE\Wow6432Node\Valve\Steam" })
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(rPath);
                    var path = key?.GetValue("InstallPath") as string;
                    if (!string.IsNullOrEmpty(path)) steamPaths.Add(path.Replace('/', '\\'));
                }
                catch { }
            }
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                var path = key?.GetValue("SteamPath") as string;
                if (!string.IsNullOrEmpty(path)) steamPaths.Add(path.Replace('/', '\\'));
            }
            catch { }
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.Name))
            {
                foreach (var cand in new[] {
                    Path.Combine(drive, "Program Files (x86)", "Steam"),
                    Path.Combine(drive, "Program Files", "Steam"),
                    Path.Combine(drive, "Games", "Steam"),
                    Path.Combine(drive, "Steam") })
                {
                    if (Directory.Exists(cand)) steamPaths.Add(cand);
                }
            }
            return steamPaths;
        }

        // Имя профиля из loginusers.vdf (локально). В сеть НЕ ходит.
        private string TryGetPersonaNameFromVdf(string id64)
        {
            try
            {
                foreach (var sPath in GetSteamInstallPaths())
                {
                    var vdf = Path.Combine(sPath, "config", "loginusers.vdf");
                    if (!File.Exists(vdf)) continue;
                    string content = File.ReadAllText(vdf);
                    int idx = content.IndexOf(id64, StringComparison.Ordinal);
                    if (idx < 0) continue;
                    int blockEnd = content.IndexOf('}', idx);
                    if (blockEnd < 0) continue;
                    string block = content.Substring(idx, blockEnd - idx);
                    var m = Regex.Match(block, "\"PersonaName\"\\s*\"([^\"]*)\"");
                    if (m.Success && !string.IsNullOrWhiteSpace(m.Groups[1].Value))
                        return m.Groups[1].Value.Trim();
                }
            }
            catch { }
            return null;
        }

        private Control CreateSteamLinkButton(string text, Color color, int x, int y, object tag, Action<object> onClick)
        {
            var btn = new CyberButton
            {
                Text = text,
                Size = new Size(100, 30),
                Location = new Point(x, y),
                AccentColor = color,
                Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
                ForeColor = CyberPalette.TextPrimary,
                Tag = tag
            };
            btn.Click += (s, e) => onClick(((CyberButton)s).Tag);
            return btn;
        }

        private async Task FindSteamAccountsAsync()
        {
            if (_steamPanel == null) return;
            var loadingLabel = new Label
            {
                Text = "Сканирование локального реестра и конфигурационных файлов Steam...",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = CyberPalette.AccentCyan,
                AutoSize = true,
                Location = new Point(5, 130)
            };
            _steamPanel.Controls.Add(loadingLabel);
            _steamPanel.Refresh();
            await Task.Delay(50);

            var allSteamIds = await Task.Run(() => GetSteamAccounts());

            // Локальная предзагрузка имён и аватаров с диска (без сети), чтобы не тормозить UI в цикле.
            var localInfo = await Task.Run(() =>
            {
                var d = new Dictionary<string, (string? persona, string? avatar)>();
                foreach (var id in allSteamIds.Keys)
                    d[id] = (TryGetPersonaNameFromVdf(id), SteamAvatarLocator.TryFindAvatarFile(id));
                return d;
            });

            _steamPanel.Controls.Remove(loadingLabel);
            loadingLabel.Dispose();

            if (_steamFlow == null)
                _steamFlow = CreateScrollableFlow(_steamPanel, 5, 120, 790, 400, FlowDirection.TopDown, false);
            else
                _steamFlow.Controls.Clear();

            if (allSteamIds.Count > 0)
            {
                _steamFlow.Controls.Add(new Label
                {
                    Text = $"Авторизованных локальных профилей: {allSteamIds.Count}",
                    Font = new Font("Segoe UI Semibold", 11, FontStyle.Bold),
                    ForeColor = CyberPalette.AccentEmerald,
                    AutoSize = true
                });
                var sourceCount = new Dictionary<string, int>();
                foreach (var kvp in allSteamIds)
                {
                    if (!sourceCount.ContainsKey(kvp.Value)) sourceCount[kvp.Value] = 0;
                    sourceCount[kvp.Value]++;
                }
                _steamFlow.Controls.Add(new Label
                {
                    Text = $"Статистика источников: {string.Join(", ", sourceCount.Select(k => $"{k.Key}: {k.Value}"))}",
                    Font = new Font("Segoe UI Semibold", 8, FontStyle.Bold),
                    ForeColor = CyberPalette.TextSecondary,
                    AutoSize = true
                });
                _steamFlow.Controls.Add(new Panel { Size = new Size(760, 1), BackColor = CyberPalette.BorderColor, Margin = new Padding(0, 10, 0, 10) });

                foreach (var kvp in allSteamIds)
                {
                    var id64 = kvp.Key;
                    var source = kvp.Value;
                    if (id64 == "0" || id64.Length < 17) continue;

                    localInfo.TryGetValue(id64, out var info);
                    string persona = info.persona;
                    string avatarPath = info.avatar;

                    var profileUrl = $"https://steamcommunity.com/profiles/{id64}";
                    var siteUrl = $"https://cswat.ch/stats/{id64}";

                    var card = new CyberPanel
                    {
                        Size = new Size(760, 140),
                        Margin = new Padding(0, 0, 0, 10)
                    };

                    // Аватар: локальный файл ИЛИ векторная заглушка с инициалом (без текста про API).
                    var avatarBox = new PictureBox
                    {
                        Size = new Size(64, 64),
                        Location = new Point(20, 20),
                        SizeMode = PictureBoxSizeMode.Zoom,
                        BackColor = Color.Transparent
                    };
                    if (!string.IsNullOrEmpty(avatarPath) && File.Exists(avatarPath))
                    {
                        try
                        {
                            // Независимая копия изображения, чтобы поток файла можно было закрыть.
                            using var fs = new FileStream(avatarPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                            using var tmp = Image.FromStream(fs);
                            avatarBox.Image = new Bitmap(tmp);
                        }
                        catch { avatarBox.Image = null; }
                    }
                    if (avatarBox.Image == null)
                    {
                        string initial = string.IsNullOrWhiteSpace(persona) ? "?" : persona.Substring(0, 1).ToUpperInvariant();
                        avatarBox.Paint += (s2, pe) =>
                        {
                            var g = pe.Graphics;
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                            var r = new Rectangle(0, 0, avatarBox.Width - 1, avatarBox.Height - 1);
                            using (var br = new LinearGradientBrush(r, CyberPalette.CardBgTop, CyberPalette.CardBg, 90f))
                                g.FillRectangle(br, r);
                            using (var pen = new Pen(CyberPalette.BorderColor, 1f))
                                g.DrawRectangle(pen, r);
                            TextRenderer.DrawText(g, initial, new Font("Segoe UI Semibold", 22f, FontStyle.Bold), r,
                                CyberPalette.AccentCyan, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                        };
                    }
                    card.Controls.Add(avatarBox);

                    string nameLabel = !string.IsNullOrEmpty(persona) ? $" [{persona}]" : "";
                    card.Controls.Add(new Label { Text = $"ID64 профиля: {id64}{nameLabel}", Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold), ForeColor = CyberPalette.AccentCyan, Size = new Size(420, 22), Location = new Point(100, 20) });
                    card.Controls.Add(new Label { Text = $"ID3: {SteamIdHelper.ToId3(id64)}", Font = new Font("Segoe UI", 9), ForeColor = CyberPalette.TextSecondary, Size = new Size(200, 18), Location = new Point(100, 42) });
                    card.Controls.Add(new Label { Text = $"Метод обнаружения: {source}", Font = new Font("Segoe UI Semibold", 8, FontStyle.Bold), ForeColor = CyberPalette.AccentRedMuted, Size = new Size(420, 18), Location = new Point(100, 60) });
                    // Строки «Статус: API недоступно» больше НЕТ — данные берутся локально.

                    card.Controls.Add(CreateSteamLinkButton("Открыть", CyberPalette.AccentCyan, 640, 18, profileUrl, url => Process.Start(new ProcessStartInfo(url.ToString()) { UseShellExecute = true })));
                    card.Controls.Add(CreateSteamLinkButton("Копировать", CyberPalette.BorderHover, 640, 55, id64, id => { Clipboard.SetText(id.ToString()); _ = ShowCustomMessageBoxAsync("ID64 скопирован в буфер обмена.", "Успешно", "Success"); }));
                    card.Controls.Add(CreateSteamLinkButton("Статистика", CyberPalette.AccentEmerald, 640, 92, siteUrl, url => Process.Start(new ProcessStartInfo(url.ToString()) { UseShellExecute = true })));
                    _steamFlow.Controls.Add(card);
                }
            }
            else
            {
                _steamFlow.Controls.Add(new Label
                {
                    Text = "Конфигураций авторизованных аккаунтов в системе не обнаружено.",
                    ForeColor = CyberPalette.TextSecondary,
                    Font = new Font("Segoe UI", 10),
                    AutoSize = true
                });
            }
        }

        private Dictionary<string, string> GetSteamAccounts()
        {
            var steamIds = new Dictionary<string, string>();
            var steamPaths = GetSteamInstallPaths();

            void RegisterId(string rawId, string source)
            {
                if (SteamIdHelper.TryConvertToSteamID64(rawId, out string id64) && !steamIds.ContainsKey(id64))
                    steamIds[id64] = source;
            }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\Users");
                if (key != null) foreach (var subKey in key.GetSubKeyNames()) RegisterId(subKey, "Реестр Steam (Локальные пользователи)");
            }
            catch { }
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\RememberedAccounts");
                if (key != null)
                {
                    foreach (var accName in key.GetSubKeyNames())
                    {
                        using var accKey = key.OpenSubKey(accName);
                        var id3 = accKey?.GetValue("MostRecentMiniprofileAccountId")?.ToString();
                        if (!string.IsNullOrEmpty(id3)) RegisterId(id3, $"Реестр Steam (Сохраненный аккаунт: {accName})");
                    }
                }
            }
            catch { }

            foreach (var sPath in steamPaths)
            {
                var loginUsersFile = Path.Combine(sPath, "config", "loginusers.vdf");
                if (File.Exists(loginUsersFile))
                {
                    try
                    {
                        var content = File.ReadAllText(loginUsersFile);
                        var matches = Regex.Matches(content, @"""(7656119\d{10})""\s*\{");
                        foreach (Match m in matches)
                        {
                            string id64 = m.Groups[1].Value;
                            int index = content.IndexOf(id64);
                            if (index != -1)
                            {
                                int nextBlockEnd = content.IndexOf('}', index);
                                if (nextBlockEnd != -1)
                                {
                                    string block = content.Substring(index, nextBlockEnd - index);
                                    if (block.Contains("\"RememberPassword\"") && block.Contains("\"1\""))
                                    { RegisterId(id64, "Файл loginusers.vdf (Сохранен пароль)"); continue; }
                                }
                            }
                            RegisterId(id64, "Файл loginusers.vdf (Вход выполнен)");
                        }
                    }
                    catch { }
                }
                var userDir = Path.Combine(sPath, "userdata");
                if (Directory.Exists(userDir))
                {
                    try
                    {
                        foreach (var dir in Directory.GetDirectories(userDir))
                        {
                            var folderName = Path.GetFileName(dir);
                            if (long.TryParse(folderName, out var id3) && id3 > 0)
                            {
                                string configPath = Path.Combine(dir, "config");
                                if (Directory.Exists(configPath) || Directory.GetDirectories(dir).Length > 0)
                                    RegisterId(folderName, "Папка Userdata (Локальный профиль)");
                            }
                        }
                    }
                    catch { }
                }
            }
            return steamIds.Where(kvp => SteamIdHelper.IsValidSteamId(kvp.Key)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }
        #endregion

        #region Данные ПК
        private async Task<PCInfoData> RetrievePCInfoDataAsync()
        {
            return await Task.Run(() =>
            {
                var data = new PCInfoData();
                try
                {
                    using var osSearcher = new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem");
                    using var collection = osSearcher.Get();
                    var lastBoot = collection.OfType<ManagementObject>().FirstOrDefault()?["LastBootUpTime"];
                    if (lastBoot != null)
                    {
                        _pcBootTime = ManagementDateTimeConverter.ToDateTime(lastBoot.ToString());
                        var uptime = DateTime.Now - _pcBootTime.Value;
                        data.Uptime = $"{uptime.Days}д {uptime.Hours}ч {uptime.Minutes}м {uptime.Seconds}с";
                    }
                }
                catch { data.Uptime = "Ошибка чтения"; }
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                    using var collection = searcher.Get();
                    foreach (ManagementObject obj in collection) { data.Cpu = obj["Name"]?.ToString(); break; }
                }
                catch { data.Cpu = "Ошибка чтения"; }
                try
                {
                    using var ramSearcher = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory");
                    using var collection = ramSearcher.Get();
                    long totalRam = 0;
                    foreach (ManagementObject obj in collection) totalRam += Convert.ToInt64(obj["Capacity"]);
                    data.Ram = $"{Math.Round(totalRam / (1024.0 * 1024 * 1024))} ГБ";
                }
                catch { data.Ram = "Ошибка чтения"; }
                try
                {
                    using var gpuSearcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                    using var collection = gpuSearcher.Get();
                    var gpus = collection.OfType<ManagementObject>().Select(obj => obj["Name"]?.ToString()?.Trim()).Where(name => !string.IsNullOrEmpty(name)).Distinct().ToList();
                    data.Gpu = gpus.Count > 0 ? string.Join(" / ", gpus) : "Встроенная / Неизвестно";
                }
                catch { data.Gpu = "Ошибка чтения"; }
                try
                {
                    using var osSearcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem");
                    using var collection = osSearcher.Get();
                    foreach (ManagementObject obj in collection) { data.Os = obj["Caption"]?.ToString(); break; }
                }
                catch { data.Os = "Ошибка чтения"; }
                try
                {
                    if (IsRunningInVirtualMachine(out string vmName)) { data.VmStatus = $"Обнаружена виртуальная машина: {vmName}"; data.VmColor = CyberPalette.AccentNeon; }
                    else { data.VmStatus = "Физическое устройство (Не виртуалка)"; data.VmColor = CyberPalette.AccentEmerald; }
                }
                catch { data.VmStatus = "Ошибка детекции среды"; data.VmColor = CyberPalette.TextSecondary; }
                try
                {
                    using var moboSearcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
                    using var collection = moboSearcher.Get();
                    foreach (ManagementObject obj in collection) { data.Motherboard = $"{obj["Manufacturer"]?.ToString() ?? ""} {obj["Product"]?.ToString() ?? ""}".Trim(); break; }
                }
                catch { data.Motherboard = "Ошибка чтения"; }

                var suspiciousDevices = DetectSuspiciousPciDevices();
                if (suspiciousDevices.Count > 0) { data.DmaStatus = $"Внимание: обнаружено устройство DMA: {string.Join(", ", suspiciousDevices)}"; data.DmaColor = CyberPalette.AccentNeon; }
                else { data.DmaStatus = "Сигнатуры DMA плат сопряжения не обнаружены"; data.DmaColor = CyberPalette.AccentEmerald; }

                var recorderApps = DetectScreenRecorders();
                if (recorderApps.Count > 0) { data.RecordersStatus = $"Внимание: запущен софт видеозахвата: {string.Join(", ", recorderApps.Distinct())}"; data.RecordersColor = CyberPalette.AccentNeon; }
                else { data.RecordersStatus = "Активных процессов видеозахвата не обнаружено"; data.RecordersColor = CyberPalette.AccentEmerald; }

                return data;
            });
        }

        private static bool IsRunningInVirtualMachine(out string vmName)
        {
            vmName = "Физическое устройство";
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
                using (var collection = searcher.Get())
                {
                    foreach (ManagementObject obj in collection)
                    {
                        string manufacturer = obj["Manufacturer"]?.ToString()?.ToLowerInvariant() ?? "";
                        string model = obj["Model"]?.ToString()?.ToLowerInvariant() ?? "";
                        if (manufacturer.Contains("microsoft") && model.Contains("virtual")) { vmName = "Hyper-V / Microsoft VM"; return true; }
                        if (manufacturer.Contains("vmware") || model.Contains("vmware")) { vmName = "VMware"; return true; }
                        if (manufacturer.Contains("oracle") || model.Contains("virtualbox") || manufacturer.Contains("virtualbox")) { vmName = "VirtualBox"; return true; }
                        if (manufacturer.Contains("qemu") || model.Contains("qemu") || model.Contains("kvm") || manufacturer.Contains("red hat")) { vmName = "QEMU / KVM"; return true; }
                        if (manufacturer.Contains("xen") || model.Contains("xen")) { vmName = "Xen VM"; return true; }
                        if (manufacturer.Contains("parallels") || model.Contains("parallels")) { vmName = "Parallels VM"; return true; }
                    }
                }
                using (var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
                using (var collection = searcher.Get())
                {
                    foreach (ManagementObject obj in collection)
                    {
                        string manufacturer = obj["Manufacturer"]?.ToString()?.ToLowerInvariant() ?? "";
                        string product = obj["Product"]?.ToString()?.ToLowerInvariant() ?? "";
                        if (manufacturer.Contains("oracle") || product.Contains("virtualbox")) { vmName = "VirtualBox (BaseBoard)"; return true; }
                        if (manufacturer.Contains("vmware") || product.Contains("vmware")) { vmName = "VMware (BaseBoard)"; return true; }
                    }
                }
            }
            catch (Exception ex) { Logger.Error("IsRunningInVirtualMachine", ex); }
            return false;
        }

        private List<string> DetectSuspiciousPciDevices()
        {
            var suspiciousDevices = new List<string>();
            var officialVendors = new[] { "Intel", "AMD", "NVIDIA", "Microsoft", "Realtek", "ASUS", "Gigabyte", "MSI", "ASRock" };
            var suspiciousKeywords = new[] { "FPGA", "Xilinx", "Altera", "Cyclone", "Artix", "Kintex", "Zynq", "LeetDMA", "Specter", "PCILeech", "Inception" };
            var fpgaVenIds = new[] { "VEN_10EE", "VEN_1172", "VEN_113C", "VEN_12AB" };
            try
            {
                using var pciSearcher = new ManagementObjectSearcher("SELECT Name, Manufacturer, DeviceID FROM Win32_PnPEntity WHERE PNPClass='PCI' OR Name LIKE '%PCI%'");
                using var collection = pciSearcher.Get();
                foreach (ManagementObject device in collection)
                {
                    var deviceName = device["Name"]?.ToString() ?? "";
                    var manufacturer = device["Manufacturer"]?.ToString() ?? "";
                    var deviceId = device["DeviceID"]?.ToString() ?? "";
                    bool isLegitimate = officialVendors.Any(vendor => manufacturer.IndexOf(vendor, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (isLegitimate) continue;
                    foreach (var venId in fpgaVenIds)
                        if (deviceId.IndexOf(venId, StringComparison.OrdinalIgnoreCase) >= 0) { suspiciousDevices.Add($"{deviceName} (HW ID: {venId})"); break; }
                    foreach (var keyword in suspiciousKeywords)
                        if (deviceName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) { suspiciousDevices.Add($"{deviceName} (Ключевое слово: {keyword})"); break; }
                }
            }
            catch (Exception ex) { Logger.Error("DetectSuspiciousPciDevices", ex); }
            return suspiciousDevices.Distinct().ToList();
        }

        private List<string> DetectScreenRecorders()
        {
            var recorderApps = new List<string>();
            try
            {
                foreach (var proc in Process.GetProcesses())
                {
                    if (proc.ProcessName.IndexOf("obs", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        proc.ProcessName.IndexOf("sharex", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        proc.ProcessName.IndexOf("bandicam", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        proc.ProcessName.IndexOf("fraps", StringComparison.OrdinalIgnoreCase) >= 0)
                        recorderApps.Add($"{proc.ProcessName}.exe");
                }
            }
            catch { }
            return recorderApps;
        }
        #endregion

        #region Реестр и DNS-кэш
        private async Task UpdateRegistryStatusesAsync()
        {
            if (_additionalPanel == null || !_additionalPanel.Visible || _isCheckingRegistry) return;
            _isCheckingRegistry = true;
            try
            {
                foreach (var item in _monitoredRegistryPaths)
                {
                    string path = item.Path;
                    Label statusLabel = item.StatusLabel;
                    if (statusLabel.IsDisposed) continue;
                    string resultText = await Task.Run(() => GetRegistryStatusText(path));
                    if (!statusLabel.IsDisposed)
                    {
                        statusLabel.Text = resultText;
                        if (resultText.Contains("ОК") || resultText.Contains("Активен")) statusLabel.ForeColor = CyberPalette.AccentEmerald;
                        else if (resultText.Contains("Доступ") || resultText.Contains("ограничен")) statusLabel.ForeColor = CyberPalette.AccentNeon;
                        else statusLabel.ForeColor = CyberPalette.TextSecondary;
                    }
                }
            }
            catch (Exception ex) { Logger.Error("UpdateRegistryStatusesAsync", ex); }
            finally { _isCheckingRegistry = false; }
        }

        private string GetRegistryStatusText(string path)
        {
            try
            {
                RegistryKey baseKey = null;
                string subPath = "";
                if (path.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase)) { baseKey = Registry.LocalMachine; subPath = path.Substring("HKEY_LOCAL_MACHINE\\".Length); }
                else if (path.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase)) { baseKey = Registry.CurrentUser; subPath = path.Substring("HKEY_CURRENT_USER\\".Length); }
                if (baseKey == null) return "Некорректный путь";
                using var key = baseKey.OpenSubKey(subPath, false);
                if (key == null) return "Не найден";
                int subKeysCount = key.SubKeyCount, valuesCount = key.ValueCount;
                if (subKeysCount == 0 && valuesCount == 0) return "Активен (пусто)";
                return $"ОК ({subKeysCount} разд., {valuesCount} знач.)";
            }
            catch (System.Security.SecurityException) { return "Доступ ограничен (нужны права Администратора)"; }
            catch (Exception) { return "Ошибка чтения"; }
        }

        private void NavigateRegistryKey(string regPath)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit");
                if (key != null) key.SetValue("LastKey", "Computer\\" + regPath, RegistryValueKind.String);
                Clipboard.SetText(regPath);
                Process.Start(new ProcessStartInfo("regedit.exe") { UseShellExecute = true });
                _ = ShowCustomMessageBoxAsync(
                    $"Редактор реестра открыт по пути:\n{regPath}\n\nРасположение зафиксировано в буфере.\nЕсли автоматический переход не сработал:\n1. Кликните по адресной строке в Regedit\n2. Нажмите Ctrl+V и примените переход клавишей Enter",
                    "Навигация Реестра", "Info");
            }
            catch (Exception ex)
            {
                Logger.Error("NavigateRegistryKey", ex);
                try { Clipboard.SetText(regPath); } catch { }
                _ = ShowCustomMessageBoxAsync($"Не удалось выполнить перенаправление:\n{ex.Message}\n\nАдрес сохранен в буфер:\n{regPath}", "Ошибка", "Error");
            }
        }

        private async Task RunDnsCacheScanAsync(Label statusLabel, CyberButton scanBtn)
        {
            scanBtn.Enabled = false;
            scanBtn.Text = "Поиск...";
            statusLabel.Text = "Анализ системного DNS-кэша...";
            statusLabel.ForeColor = CyberPalette.TextSecondary;
            try
            {
                var foundTraces = await Task.Run(() =>
                {
                    var matches = new List<string>();
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "ipconfig",
                        Arguments = "/displaydns",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8
                    };
                    using var process = Process.Start(startInfo);
                    if (process != null)
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit();
                        var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            if (line.Contains(":"))
                            {
                                var parts = line.Split(new[] { ':' }, 2);
                                if (parts.Length > 1)
                                {
                                    string value = parts[1].Trim().ToLowerInvariant().TrimEnd('.');
                                    if (value.Contains(".") && Regex.IsMatch(value, @"^[a-z0-9\-_.]+$"))
                                    {
                                        foreach (var keyword in CheatSignatures.DnsCheatKeywords)
                                        {
                                            if (value.Contains(keyword)) { matches.Add(value); break; }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    return matches.Distinct().ToList();
                });

                if (foundTraces.Count > 0)
                {
                    statusLabel.Text = $"Обнаружено совпадений: {foundTraces.Count}";
                    statusLabel.ForeColor = CyberPalette.AccentNeon;
                    string report = string.Join("\n", foundTraces.Select(t => $"• {t}"));
                    await ShowCustomMessageBoxAsync(
                        $"В системном кэше сетевых запросов (DNS) обнаружены записи авторизации софта:\n\n{report}\n\nСледы подтверждают факт обращения к сетевым хостам.",
                        "Анализ DNS-кэша", "Warning");
                }
                else
                {
                    statusLabel.Text = "Угроз в DNS не обнаружено";
                    statusLabel.ForeColor = CyberPalette.AccentEmerald;
                    await ShowCustomMessageBoxAsync(
                        "Следов обращения к известным серверам авторизации и хостам разработчиков читов в DNS-кэше не обнаружено.",
                        "Анализ DNS завершен", "Success");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("DnsCacheScan", ex);
                statusLabel.Text = "Ошибка запуска";
                statusLabel.ForeColor = CyberPalette.AccentNeon;
                await ShowCustomMessageBoxAsync($"Не удалось выполнить опрос DNS-кэша:\n{ex.Message}", "Внутренняя ошибка", "Error");
            }
            finally
            {
                scanBtn.Enabled = true;
                scanBtn.Text = "Проверить DNS-кэш";
            }
        }
        #endregion
    }
}
#pragma warning restore CS8618, CS8625, CS8601, CS8602, CS8603, CS8604, CS8600, CS8629