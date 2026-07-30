using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace UselessChecker.Backend
{
    /// <summary>
    /// Основная бизнес-логика приложения - сканирование, поиск Steam, информация о ПК
    /// </summary>
    public static class CheckerLogic
    {
        private static readonly string ToolsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UselessChecker", "Tools");

        #region Сканер угроз

        public class ScanResult
        {
            public List<string> FoundCheats { get; set; } = new();
            public int FilesScanned { get; set; }
            public int PathsScanned { get; set; }
        }

        public class ScanProgress
        {
            public int Percent { get; set; }
            public string Status { get; set; } = "";
            public string Path { get; set; } = "";
            public long FilesScanned { get; set; }
            public int CheatsFound { get; set; }
            public int PathsCompleted { get; set; }
            public int PathsTotal { get; set; }
        }

        public static async Task<ScanResult> RunCheatScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var scanPaths = BuildScanPaths();
                int pathsScanned = scanPaths.Count;
                int estimatedTotalFiles = 0;

                foreach (var path in scanPaths)
                {
                    try 
                    { 
                        estimatedTotalFiles += Directory.GetFileSystemEntries(path).Length * 50; 
                    }
                    catch 
                    { 
                        estimatedTotalFiles += 1000; 
                    }
                }
                estimatedTotalFiles = Math.Max(estimatedTotalFiles, 1);

                var results = new List<string>();
                int globalFilesScanned = 0;
                int completedPaths = 0;

                foreach (var scanPath in scanPaths)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    try
                    {
                        foreach (var item in SafeEnumerateFiles(scanPath))
                        {
                            if (cancellationToken.IsCancellationRequested)
                                break;

                            globalFilesScanned++;

                            if (globalFilesScanned % 1000 == 0 && progress != null)
                            {
                                int percent = Math.Min(99, (int)((double)globalFilesScanned / estimatedTotalFiles * 100));
                                progress.Report(new ScanProgress
                                {
                                    Percent = percent,
                                    Status = $"[{completedPaths + 1}/{pathsScanned}] {GetFolderDisplayName(scanPath)}",
                                    Path = scanPath,
                                    FilesScanned = globalFilesScanned,
                                    CheatsFound = results.Count,
                                    PathsCompleted = completedPaths,
                                    PathsTotal = pathsScanned
                                });
                            }

                            string itemName;
                            try 
                            { 
                                itemName = Path.GetFileName(item); 
                            } 
                            catch 
                            { 
                                continue; 
                            }

                            if (string.IsNullOrEmpty(itemName))
                                continue;

                            // Исключения путей
                            bool isPathExcluded = false;
                            foreach (var exc in CheatSignatures.ExcludePaths)
                            {
                                if (item.IndexOf(exc, StringComparison.OrdinalIgnoreCase) >= 0) 
                                { 
                                    isPathExcluded = true; 
                                    break; 
                                }
                            }

                            if (isPathExcluded)
                            {
                                string ext = "";
                                try 
                                { 
                                    ext = Path.GetExtension(item); 
                                } 
                                catch { }

                                if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                                    ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                                    ext.Equals(".sys", StringComparison.OrdinalIgnoreCase))
                                    isPathExcluded = false;
                            }

                            if (isPathExcluded)
                                continue;

                            // Точные исключения
                            if (CheatSignatures.ExactExcludes.Contains(itemName))
                                continue;

                            bool exactExcluded = false;
                            foreach (var exc in CheatSignatures.ExactExcludes)
                            {
                                if (itemName.StartsWith(exc + ".", StringComparison.OrdinalIgnoreCase) ||
                                    itemName.EndsWith("." + exc, StringComparison.OrdinalIgnoreCase))
                                { 
                                    exactExcluded = true; 
                                    break; 
                                }
                            }

                            if (exactExcluded)
                                continue;

                            // Проверка на сигнатуры читов
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
                                    string ext = "";
                                    try 
                                    { 
                                        ext = Path.GetExtension(item); 
                                    } 
                                    catch 
                                    { 
                                        ext = ""; 
                                    }

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
                                    }
                                    break;
                                }
                            }
                        }

                        completedPaths++;
                    }
                    catch (Exception ex) 
                    { 
                        Logger.Error($"Scan path {scanPath}", ex); 
                    }
                }

                progress?.Report(new ScanProgress
                {
                    Percent = 100,
                    Status = "Сканирование завершено",
                    Path = "",
                    FilesScanned = globalFilesScanned,
                    CheatsFound = results.Count,
                    PathsCompleted = pathsScanned,
                    PathsTotal = pathsScanned
                });

                return new ScanResult
                {
                    FoundCheats = results.Distinct().ToList(),
                    FilesScanned = globalFilesScanned,
                    PathsScanned = pathsScanned
                };
            }, cancellationToken);
        }

        private static List<string> BuildScanPaths()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var appDataRoaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appDataLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            void AddPath(string p) 
            { 
                if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p)) 
                    paths.Add(p); 
            }

            void AddSubdirectoriesOf(string root)
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) 
                    return;
                
                try 
                { 
                    foreach (var dir in Directory.GetDirectories(root)) 
                        AddPath(dir); 
                } 
                catch { }
            }

            AddSubdirectoriesOf(userProfile);
            AddSubdirectoriesOf(appDataRoaming);
            AddSubdirectoriesOf(appDataLocal);
            
            var appDataLocalLow = Path.Combine(userProfile, "AppData", "LocalLow");
            AddSubdirectoriesOf(appDataLocalLow);
            AddSubdirectoriesOf(programData);
            
            AddPath(userProfile); 
            AddPath(appDataRoaming); 
            AddPath(appDataLocal); 
            AddPath(appDataLocalLow); 
            AddPath(programData);
            
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
                        if (new[] { "Public", "Default", "Default User", "All Users" }.Any(x => name.Equals(x, StringComparison.OrdinalIgnoreCase))) 
                            continue;
                        
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

        private static IEnumerable<string> SafeEnumerateFiles(string rootPath)
        {
            var dirs = new Queue<string>();
            dirs.Enqueue(rootPath);
            
            while (dirs.Count > 0)
            {
                string currentDir = dirs.Dequeue();
                IEnumerable<string> files = null;
                
                try 
                { 
                    files = Directory.EnumerateFiles(currentDir); 
                } 
                catch { }

                if (files != null)
                    foreach (var file in files) 
                        yield return file;

                IEnumerable<string> subDirs = null;
                try 
                { 
                    subDirs = Directory.EnumerateDirectories(currentDir); 
                } 
                catch { }

                if (subDirs != null)
                    foreach (var dir in subDirs) 
                        dirs.Enqueue(dir);
            }
        }

        private static string GetFolderDisplayName(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) 
                return path;
            
            string normalized = path.TrimEnd('\\', '/');
            
            if (normalized.Length == 2 && normalized[1] == ':') 
                return normalized + "\\";
            
            if (normalized.StartsWith(@"\\"))
            {
                string fileName = Path.GetFileName(normalized);
                return string.IsNullOrEmpty(fileName) ? normalized : fileName;
            }
            
            string result = Path.GetFileName(normalized);
            return string.IsNullOrEmpty(result) ? normalized : result;
        }

        #endregion

        #region Информация о ПК

        public class PCInfo
        {
            public string Uptime { get; set; } = "";
            public string Cpu { get; set; } = "";
            public string Ram { get; set; } = "";
            public string Gpu { get; set; } = "";
            public string Os { get; set; } = "";
            public string VmStatus { get; set; } = "";
            public bool IsVm { get; set; }
            public string Motherboard { get; set; } = "";
            public string DmaStatus { get; set; } = "";
            public bool HasDma { get; set; }
            public string RecordersStatus { get; set; } = "";
            public bool HasRecorders { get; set; }
        }

        public static async Task<PCInfo> GetPCInfoAsync()
        {
            return await Task.Run(() =>
            {
                var info = new PCInfo();

                // Uptime
                try
                {
                    using var osSearcher = new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem");
                    using var collection = osSearcher.Get();
                    var lastBoot = collection.OfType<ManagementObject>().FirstOrDefault()?["LastBootUpTime"];
                    
                    if (lastBoot != null)
                    {
                        var bootTime = ManagementDateTimeConverter.ToDateTime(lastBoot.ToString());
                        var uptime = DateTime.Now - bootTime;
                        info.Uptime = $"{uptime.Days}д {uptime.Hours}ч {uptime.Minutes}м {uptime.Seconds}с";
                    }
                }
                catch 
                { 
                    info.Uptime = "Ошибка чтения"; 
                }

                // CPU
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                    using var collection = searcher.Get();
                    foreach (ManagementObject obj in collection) 
                    { 
                        info.Cpu = obj["Name"]?.ToString() ?? "Неизвестно"; 
                        break; 
                    }
                }
                catch 
                { 
                    info.Cpu = "Ошибка чтения"; 
                }

                // RAM
                try
                {
                    using var ramSearcher = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory");
                    using var collection = ramSearcher.Get();
                    long totalRam = 0;
                    
                    foreach (ManagementObject obj in collection) 
                        totalRam += Convert.ToInt64(obj["Capacity"]);
                    
                    info.Ram = $"{Math.Round(totalRam / (1024.0 * 1024 * 1024))} ГБ";
                }
                catch 
                { 
                    info.Ram = "Ошибка чтения"; 
                }

                // GPU
                try
                {
                    using var gpuSearcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                    using var collection = gpuSearcher.Get();
                    var gpus = collection.OfType<ManagementObject>()
                        .Select(obj => obj["Name"]?.ToString()?.Trim())
                        .Where(name => !string.IsNullOrEmpty(name))
                        .Distinct()
                        .ToList();
                    
                    info.Gpu = gpus.Count > 0 ? string.Join(" / ", gpus) : "Встроенная / Неизвестно";
                }
                catch 
                { 
                    info.Gpu = "Ошибка чтения"; 
                }

                // OS
                try
                {
                    using var osSearcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem");
                    using var collection = osSearcher.Get();
                    foreach (ManagementObject obj in collection) 
                    { 
                        info.Os = obj["Caption"]?.ToString() ?? "Неизвестно"; 
                        break; 
                    }
                }
                catch 
                { 
                    info.Os = "Ошибка чтения"; 
                }

                // VM Detection
                try
                {
                    info.IsVm = IsRunningInVirtualMachine(out string vmName);
                    info.VmStatus = info.IsVm ? $"Обнаружена виртуальная машина: {vmName}" : "Физическое устройство (Не виртуалка)";
                }
                catch 
                { 
                    info.VmStatus = "Ошибка детекции среды"; 
                }

                // Motherboard
                try
                {
                    using var moboSearcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
                    using var collection = moboSearcher.Get();
                    foreach (ManagementObject obj in collection) 
                    { 
                        info.Motherboard = $"{obj["Manufacturer"]?.ToString() ?? ""} {obj["Product"]?.ToString() ?? ""}".Trim(); 
                        break; 
                    }
                }
                catch 
                { 
                    info.Motherboard = "Ошибка чтения"; 
                }

                // DMA Detection
                var suspiciousDevices = DetectSuspiciousPciDevices();
                info.HasDma = suspiciousDevices.Count > 0;
                info.DmaStatus = info.HasDma 
                    ? $"Внимание: обнаружено устройство DMA: {string.Join(", ", suspiciousDevices)}" 
                    : "Сигнатуры DMA плат сопряжения не обнаружены";

                // Screen Recorders Detection
                var recorderApps = DetectScreenRecorders();
                info.HasRecorders = recorderApps.Count > 0;
                info.RecordersStatus = info.HasRecorders 
                    ? $"Внимание: запущен софт видеозахвата: {string.Join(", ", recorderApps.Distinct())}" 
                    : "Активных процессов видеозахвата не обнаружено";

                return info;
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

                        if (manufacturer.Contains("microsoft") && model.Contains("virtual")) 
                        { 
                            vmName = "Hyper-V / Microsoft VM"; 
                            return true; 
                        }
                        if (manufacturer.Contains("vmware") || model.Contains("vmware")) 
                        { 
                            vmName = "VMware"; 
                            return true; 
                        }
                        if (manufacturer.Contains("oracle") || model.Contains("virtualbox") || manufacturer.Contains("virtualbox")) 
                        { 
                            vmName = "VirtualBox"; 
                            return true; 
                        }
                        if (manufacturer.Contains("qemu") || model.Contains("qemu") || model.Contains("kvm") || manufacturer.Contains("red hat")) 
                        { 
                            vmName = "QEMU / KVM"; 
                            return true; 
                        }
                        if (manufacturer.Contains("xen") || model.Contains("xen")) 
                        { 
                            vmName = "Xen VM"; 
                            return true; 
                        }
                        if (manufacturer.Contains("parallels") || model.Contains("parallels")) 
                        { 
                            vmName = "Parallels VM"; 
                            return true; 
                        }
                    }
                }

                using (var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
                using (var collection = searcher.Get())
                {
                    foreach (ManagementObject obj in collection)
                    {
                        string manufacturer = obj["Manufacturer"]?.ToString()?.ToLowerInvariant() ?? "";
                        string product = obj["Product"]?.ToString()?.ToLowerInvariant() ?? "";

                        if (manufacturer.Contains("oracle") || product.Contains("virtualbox")) 
                        { 
                            vmName = "VirtualBox (BaseBoard)"; 
                            return true; 
                        }
                        if (manufacturer.Contains("vmware") || product.Contains("vmware")) 
                        { 
                            vmName = "VMware (BaseBoard)"; 
                            return true; 
                        }
                    }
                }
            }
            catch (Exception ex) 
            { 
                Logger.Error("IsRunningInVirtualMachine", ex); 
            }

            return false;
        }

        private static List<string> DetectSuspiciousPciDevices()
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

                    bool isLegitimate = officialVendors.Any(vendor => 
                        manufacturer.IndexOf(vendor, StringComparison.OrdinalIgnoreCase) >= 0);

                    if (isLegitimate) 
                        continue;

                    foreach (var venId in fpgaVenIds)
                    {
                        if (deviceId.IndexOf(venId, StringComparison.OrdinalIgnoreCase) >= 0) 
                        { 
                            suspiciousDevices.Add($"{deviceName} (HW ID: {venId})"); 
                            break; 
                        }
                    }

                    foreach (var keyword in suspiciousKeywords)
                    {
                        if (deviceName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) 
                        { 
                            suspiciousDevices.Add($"{deviceName} (Ключевое слово: {keyword})"); 
                            break; 
                        }
                    }
                }
            }
            catch (Exception ex) 
            { 
                Logger.Error("DetectSuspiciousPciDevices", ex); 
            }

            return suspiciousDevices.Distinct().ToList();
        }

        private static List<string> DetectScreenRecorders()
        {
            var recorderApps = new List<string>();
            
            try
            {
                foreach (var proc in Process.GetProcesses())
                {
                    try
                    {
                        var name = proc.ProcessName.ToLowerInvariant();
                        if (name.Contains("obs") || 
                            name.Contains("sharex") || 
                            name.Contains("bandicam") || 
                            name.Contains("fraps"))
                        {
                            recorderApps.Add($"{proc.ProcessName}.exe");
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return recorderApps;
        }

        #endregion

        #region Steam Accounts

        public class SteamAccount
        {
            public string Id64 { get; set; } = "";
            public string? PersonaName { get; set; }
            public string? AvatarPath { get; set; }
            public string Source { get; set; } = "";
            public string Id3 => SteamIdHelper.ToId3(Id64).ToString();
        }

        public static async Task<List<SteamAccount>> FindSteamAccountsAsync()
        {
            return await Task.Run(() =>
            {
                var accounts = new List<SteamAccount>();
                var steamIds = GetSteamAccounts();

                foreach (var kvp in steamIds)
                {
                    var id64 = kvp.Key;
                    var source = kvp.Value;

                    if (id64 == "0" || id64.Length < 17)
                        continue;

                    var personaName = TryGetPersonaNameFromVdf(id64);
                    var avatarPath = SteamAvatarLocator.TryFindAvatarFile(id64);

                    accounts.Add(new SteamAccount
                    {
                        Id64 = id64,
                        PersonaName = personaName,
                        AvatarPath = avatarPath,
                        Source = source
                    });
                }

                return accounts;
            });
        }

        private static Dictionary<string, string> GetSteamAccounts()
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
                if (key != null)
                    foreach (var subKey in key.GetSubKeyNames())
                        RegisterId(subKey, "Реестр Steam (Локальные пользователи)");
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
                        if (!string.IsNullOrEmpty(id3))
                            RegisterId(id3, $"Реестр Steam (Сохраненный аккаунт: {accName})");
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
                                    { 
                                        RegisterId(id64, "Файл loginusers.vdf (Сохранен пароль)"); 
                                        continue; 
                                    }
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

            return steamIds.Where(kvp => SteamIdHelper.IsValidSteamId(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        private static HashSet<string> GetSteamInstallPaths()
        {
            var steamPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rPath in new[] { @"SOFTWARE\Valve\Steam", @"SOFTWARE\Wow6432Node\Valve\Steam" })
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(rPath);
                    var path = key?.GetValue("InstallPath") as string;
                    if (!string.IsNullOrEmpty(path))
                        steamPaths.Add(path.Replace('/', '\\'));
                }
                catch { }
            }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                var path = key?.GetValue("SteamPath") as string;
                if (!string.IsNullOrEmpty(path))
                    steamPaths.Add(path.Replace('/', '\\'));
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
                    if (Directory.Exists(cand))
                        steamPaths.Add(cand);
                }
            }

            return steamPaths;
        }

        private static string? TryGetPersonaNameFromVdf(string id64)
        {
            try
            {
                foreach (var sPath in GetSteamInstallPaths())
                {
                    var vdf = Path.Combine(sPath, "config", "loginusers.vdf");
                    if (!File.Exists(vdf))
                        continue;

                    string content = File.ReadAllText(vdf);
                    int idx = content.IndexOf(id64, StringComparison.Ordinal);
                    
                    if (idx < 0)
                        continue;

                    int blockEnd = content.IndexOf('}', idx);
                    if (blockEnd < 0)
                        continue;

                    string block = content.Substring(idx, blockEnd - idx);
                    var m = System.Text.RegularExpressions.Regex.Match(block, "\"PersonaName\"\\s*\"([^\"]*)\"");
                    
                    if (m.Success && !string.IsNullOrWhiteSpace(m.Groups[1].Value))
                        return m.Groups[1].Value.Trim();
                }
            }
            catch { }

            return null;
        }

        #endregion

        #region DNS Cache Scan

        public class DnsScanResult
        {
            public List<string> FoundTraces { get; set; } = new();
            public bool HasThreats => FoundTraces.Count > 0;
        }

        public static async Task<DnsScanResult> ScanDnsCacheAsync()
        {
            return await Task.Run(() =>
            {
                var result = new DnsScanResult();

                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "ipconfig",
                        Arguments = "/displaydns",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = System.Text.Encoding.UTF8
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
                                    
                                    if (value.Contains(".") && System.Text.RegularExpressions.Regex.IsMatch(value, @"^[a-z0-9\-_.]+$"))
                                    {
                                        foreach (var keyword in CheatSignatures.DnsCheatKeywords)
                                        {
                                            if (value.Contains(keyword))
                                            {
                                                result.FoundTraces.Add(value);
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("DnsCacheScan", ex);
                }

                result.FoundTraces = result.FoundTraces.Distinct().ToList();
                return result;
            });
        }

        #endregion
    }
}
