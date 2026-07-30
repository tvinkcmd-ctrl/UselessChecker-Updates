using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace UselessChecker
{
    // Локальный поиск кэшированного аватара Steam НА ДИСКЕ пользователя.
    // В сеть НЕ ходит, API НЕ использует. Если ничего не найдено — возвращает null,
    // и UI рисует стильную заглушку с инициалами (без текста об ошибке).
    internal static class SteamAvatarLocator
    {
        private static readonly string[] ImageExt = { ".jpg", ".jpeg", ".png", ".bmp" };

        // Собираем кандидатные корневые папки Steam (реестр + типовые пути на дисках).
        private static IEnumerable<string> GetSteamRoots()
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string? p) { if (!string.IsNullOrWhiteSpace(p)) roots.Add(p.Replace('/', '\\').TrimEnd('\\')); }

            try { using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam"); Add(k?.GetValue("InstallPath") as string); } catch { }
            try { using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Wow6432Node\Valve\Steam"); Add(k?.GetValue("InstallPath") as string); } catch { }
            try { using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"); Add(k?.GetValue("SteamPath") as string); } catch { }

            foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady))
            {
                Add(Path.Combine(d.Name, "Program Files (x86)", "Steam"));
                Add(Path.Combine(d.Name, "Program Files", "Steam"));
                Add(Path.Combine(d.Name, "Steam"));
            }
            return roots.Where(Directory.Exists);
        }

        private static bool IsImage(string file)
            => ImageExt.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);

        // Ищет файл аватара по SteamID64. Перебирает разумный набор локальных путей без дикой рекурсии.
        public static string? TryFindAvatarFile(string steamId64)
        {
            if (!SteamIdHelper.IsValidSteamId(steamId64)) return null;
            string id3 = SteamIdHelper.ToId3(steamId64).ToString();

            var probes = new List<string>();
            foreach (var root in GetSteamRoots())
            {
                probes.Add(Path.Combine(root, "config", "avatarcache"));
                probes.Add(Path.Combine(root, "config"));
                probes.Add(Path.Combine(root, "friends"));
                probes.Add(Path.Combine(root, "userdata", id3));
            }
            probes.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steam", "appcache"));
            probes.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Steam"));

            // 1) Точные совпадения имени файла с id64/id3 (самый надёжный случай).
            foreach (var dir in probes.Where(Directory.Exists))
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir))
                    {
                        var name = Path.GetFileNameWithoutExtension(f);
                        if (IsImage(f) && (name == steamId64 || name == id3
                            || name.StartsWith(steamId64, StringComparison.OrdinalIgnoreCase)
                            || name.StartsWith(id3 + "_", StringComparison.OrdinalIgnoreCase)))
                            return f;
                    }
                }
                catch { }
            }

            // 2) Мягкий fallback: любой файл-аватар в avatarcache (один уровень вглубь), если вдруг имя совпало частично.
            foreach (var root in GetSteamRoots())
            {
                var cache = Path.Combine(root, "config", "avatarcache");
                if (!Directory.Exists(cache)) continue;
                try
                {
                    foreach (var f in Directory.EnumerateFiles(cache, "*.*", SearchOption.TopDirectoryOnly))
                        if (IsImage(f) && Path.GetFileName(f).IndexOf(steamId64, StringComparison.OrdinalIgnoreCase) >= 0)
                            return f;
                }
                catch { }
            }

            return null; // Ничего локально нет — UI покажет заглушку, сеть не трогаем.
        }
    }
}