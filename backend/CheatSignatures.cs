using System;
using System.Collections.Generic;

namespace UselessChecker
{
    // Все сигнатурные списки детекта читов/исключений/DNS, вынесенные из Form1.
    // Значения перенесены ДОСЛОВНО из оригинала, чтобы поведение сканера не изменилось.
    // Обращение из кода: CheatSignatures.CheatNames / .ExactExcludes / .ExcludePaths / .DnsCheatKeywords.
    internal static class CheatSignatures
    {
        public static readonly HashSet<string> CheatNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "xone", "midnight", "naim", "shark", "nixware", "fatality", "osiris", "neverlose", "gamesense", "skeet",
            "aimware", "primordial", "ev0lve", "eternity", "legendware", "aurora", "reborn", "keter", "interium",
            "exloader", "extrimhack", "blasthack", "ragehack", "moonware", "leetdma", "specter", "inception",
            "enigma", "vredux", "fecurity", "aimmy", "ekknod", "tkazer", "aimstar", "softhub", "plaguecheat",
            "omniam", "repa", "memesence", "aspyxia", "furios", "haunted", "sakara", "mixsoft", "w1nner", "wurst",
            "skillclient", "flux", "huzuni", "akrien", "aristois", "kamiblue", "pyro", "summit", "xulu",
            "phoenixhack", "z0rhack", "rusherclient", "akrienb3", "onix_client", "onix client", "novoline",
            "liquidbounce", "meteor", "impact", "bleachhack", "jigsaw", "harambe", "salhack", "nucleus",
            "phobos", "konas", "gamespeed", "aimlabs", "recoilscript", "no-recoil", "obsidian", "novo", "harpoon",
            "grapedma", "duckdma", "screamer", "doubletap", "desync", "triggerbot", "spinbot", "ragebot", "legitbot",
            "backtrack", "skush", "iniuria", "perfecthook", "mutiny", "rektware", "spiral", "interwebz", "unityhacks",
            "supremacy", "getware", "rifk7", "d3m", "simplecheats", "ezfrags", "r8cheats", "force-project", "freeqc",
            "ezinjector", "injector", "cheat-engine", "weave", "pandora", "nemesis", "vortek", "acidtrip", "overlight",
            "freemium", "monolith", "polyloader", "ratpoison", "sensum", "hexui", "sensory", "interstellar", "jupiter",
            "rawetrip", "titanium", "victoria", "desolver", "doras", "ezglobal", "zapped", "ninjaware", "bettergo",
            "ryzextr", "menthol", "sector", "dork", "maze", "singlelady", "terrority", "mirium", "hydrawebz", "oxide",
            "azrah", "alphen", "euclid", "wareware", "invertable", "smokeycheats", "smufrwrecker", "sunsum", "skeethook",
            "pussycat", "iccluded", "instinst", "samoware", "espdx", "boberhook", "eghack", "slowlyb1", "luno",
            "underical", "enternal", "onetap", "getspace", "baimless", "spirthack", "fanta", "plague", "fanta-cheat",
            "memesence.pub", "iniuria.us", "aimware.net", "neverlose.cc", "midnight.im", "xone.fun", "nixware.cc",
            "ev0lve.xyz", "gamesense.pub", "skeet.cc", "primordial.dev", "millionware.to", "harpoon-project",
            "sensum.pw", "hexui.ru", "chams", "wallhack", "esp", "aimbot", "wh", "silentaim", "bhop", "bunnyhop",
            "macro", "script", "norecoil", "no-recoil", "injector.exe", "dll-injector", "gh-injector",
            "hyper dma", "razer dma", "lightning dma", "clutch dma", "silver dma", "lynx dma", "atomic dma",
            "cyber dma", "kap dma", "custom dma", "screamer dma", "pcileech", "dma injector", "squirrel dma",
            "shrk dma", "immortal dma", "phantom dma", "vanguard bypass", "faceit bypass", "eac bypass",
            "be bypass", "battleye bypass", "ricochet bypass", "rust dma", "pubg dma", "apex dma",
            "tarkov dma", "eft dma", "arena dma", "arena breakout dma", "valorant dma", "dayz dma",
            "cod dma", "warzone dma", "fortnite dma", "rust external", "rust internal", "eft external",
            "eft internal", "apex external", "apex internal", "superpeople dma", "the finals dma",
            "overwatch dma", "cs2 dma", "cs2 external", "cs2 internal", "skeet dma", "neon dma",
            "oxide dma", "velocity dma", "nexus dma", "divine dma", "pro dma", "titan dma",
            "apex legends hack", "escape from tarkov hack", "rust hack", "dayz hack", "counter-strike 2 hack",
            "cs2 hack", "cs2 wh", "cs2 aim", "aimbot cs2", "wallhack cs2", "esp cs2", "triggerbot cs2",
            "bhop cs2", "recoil cs2", "rcs cs2", "vredux cs2", "midnight cs2", "xone cs2", "nixware cs2",
            "neverlose cs2", "iniuria cs2", "aimware cs2", "fecurity cs2", "aimstar cs2", "aimmy cs2",
            "ekknod cs2", "tkazer cs2", "softhub cs2", "plague cs2", "memesence cs2", "aspyxia cs2",
            "sakara cs2", "w1nner cs2", "exloader cs2", "extrimhack cs2", "ezfrags cs2", "r8 cs2"
        };

        public static readonly HashSet<string> ExactExcludes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "NexusIntegration", "NexusMods", "NexusClient", "MxDownloadManager", "MAGIX", "MagixSoftware",
            "OneDrive", "OneNote", "OneAuth", "WindowsUpdate", "WindowsDefender", "WindowsApps",
            "SteamApps", "SteamWorks", "SteamService", "DiscordApp", "DiscordUpdate", "DiscordCanary",
            "EpicGames", "EpicOnlineServices", "UnityHub", "UnityEditor", "UnityCache",
            "VisualStudio", "VSCode", "VSTO", "NVIDIA", "NVIDIAGeForce", "NVIDIAContainer",
            "AMDDriver", "AMDSoftware", "AMDRyzen", "IntelDriver", "IntelOptane", "IntelGraphics",
            "AdobeCreative", "AdobeUpdate", "AdobeCC", "GoogleChrome", "GoogleUpdate", "GoogleDrive",
            "MozillaFirefox", "MozillaMaintenance", "YandexBrowser", "YandexDisk",
            "OperaBrowser", "OperaStable", "MicrosoftEdge", "MicrosoftOffice", "MicrosoftTeams",
            "NodeJS", "NodeModules", "NpmCache", "Python3", "PythonSoftware", "JavaSoft", "JavaUpdate",
            "GitForWindows", "GitHubDesktop", "DockerDesktop", "DockerEngine",
            "TelegramDesktop", "TelegramUpdate", "SpotifyApp", "SpotifyWeb",
            "ZoomUs", "ZoomPlugin", "SkypeApp", "SkypeBridge", "SlackApp", "SlackUpdate",
            "NotionApp", "NotionWeb", "FigmaApp", "FigmaAgent", "PostmanApp", "PostmanRuntime",
            "VirtualBox", "VirtualBoxVM", "VMwareWorkstation", "VMwarePlayer",
            "XboxApp", "XboxGameBar", "XboxServices", "MinecraftLauncher", "MinecraftRuntime",
            "RobloxPlayer", "RobloxStudio", "GenshinImpact", "GenshinCloud",
            "ValorantClient", "ValorantAntiCheat", "LeagueClient", "LeagueUpdate",
            "RiotClient", "RiotGames", "BlueStacks", "BlueStacksEngine",
            "LDPlayer", "LDPlayer9", "NoxPlayer", "NoxVM", "WSL", "WSL2", "WindowsSubsystem"
        };

        public static readonly string[] ExcludePaths = {
            @"\Steam\", @"\SteamApps\", @"\steamapps\", @"\Program Files\WindowsApps\",
            @"\Windows\WinSxS\", @"\Windows\Installer\", @"\Windows\assembly",
            @"\node_modules\", @"\npm-cache\", @"\npm\", @"\yarn\", @"\pnpm",
            @"\.git\", @"\.svn\", @"\.vs", @"\obj\", @"\bin",
            @"\AppData\Roaming\Discord\", @"\AppData\Local\Discord",
            @"\AppData\Roaming\Spotify\", @"\AppData\Local\Spotify",
            @"\AppData\Roaming\Telegram Desktop",
            @"\AppData\Roaming\Google\Chrome\User Data\Default\Cache",
            @"\AppData\Local\Google\Chrome\User Data\Default\Cache",
            @"\cache\", @"\temp\", @"\Temp\", @"\Cache",
            @"\logs\", @"\Logs\", @"\log\", @"\Log",
            @"\backup\", @"\Backup\", @"\backups\", @"\Backups",
            @"\Packages\", @"\Windows.old", @"\$Recycle.Bin\"
        };

        public static readonly HashSet<string> DnsCheatKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "midnight.im", "xone.fun", "neverlose.cc", "iniuria.us", "aimware.net",
            "nixware.cc", "gamesense.pub", "skeet.cc", "ev0lve.xyz", "primordial.dev",
            "fecurity", "exloader", "extrimhack", "memesence", "vredux", "rawetrip",
            "spirthack", "fanta-cheat", "interium", "ezglobal", "r8cheats", "esper.net",
            "fatality.win", "legendware", "aurora.one", "millionware", "pellix"
        };
    }
}