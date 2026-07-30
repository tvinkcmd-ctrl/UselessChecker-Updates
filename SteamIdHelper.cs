namespace UselessChecker
{
    // Чистые функции конвертации SteamID. Вынесено из Form1 как переиспользуемый хелпер.
    internal static class SteamIdHelper
    {
        private const long STEAMID64_BASE = 76561197960265728L;

        public static bool IsValidSteamId(string? steamId)
            => !string.IsNullOrEmpty(steamId)
               && steamId.Length == 17
               && long.TryParse(steamId, out long id)
               && id > STEAMID64_BASE
               && id < 76561200000000000L;

        public static bool TryConvertToSteamID64(string? input, out string steamId64)
        {
            steamId64 = string.Empty;
            if (string.IsNullOrWhiteSpace(input)) return false;

            if (input.Length == 17 && input.StartsWith("7656119"))
            {
                if (IsValidSteamId(input)) { steamId64 = input; return true; }
            }

            if (long.TryParse(input, out long id3) && id3 > 0 && id3 < 10000000000L)
            {
                string converted = (STEAMID64_BASE + id3).ToString();
                if (IsValidSteamId(converted)) { steamId64 = converted; return true; }
            }
            return false;
        }

        public static long ToId3(string steamId64)
            => long.TryParse(steamId64, out long v) ? v - STEAMID64_BASE : 0;
    }
}