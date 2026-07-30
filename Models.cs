using System;
using System.Drawing;

namespace UselessChecker
{
    // Модель диагностической утилиты (раздел «Программы»).
    internal class DiagnosticToolInfo
    {
        public string Number { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string LocalPath { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string FallbackUrl { get; set; } = "";
        public bool IsZip { get; set; }
    }

    // Прогресс фонового сканирования системы.
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

    // Модель профиля Steam. БЕЗ полей Steam-API: аватар берётся с диска, имя — из loginusers.vdf.
    public class SteamAccountInfo
    {
        public string? LocalAvatarPath { get; set; } // путь к локальному файлу аватара или null
        public string? PersonaName { get; set; }     // имя из loginusers.vdf
    }

    // Кэш характеристик ПК (раздел «Данные ПК»).
    public class PCInfoData
    {
        public string Uptime { get; set; } = "Загрузка...";
        public string Cpu { get; set; } = "Загрузка...";
        public string Ram { get; set; } = "Загрузка...";
        public string Gpu { get; set; } = "Загрузка...";
        public string Os { get; set; } = "Загрузка...";
        public string VmStatus { get; set; } = "Загрузка...";
        public Color VmColor { get; set; } = CyberPalette.TextSecondary;
        public string Motherboard { get; set; } = "Загрузка...";
        public string DmaStatus { get; set; } = "Загрузка...";
        public Color DmaColor { get; set; } = CyberPalette.TextSecondary;
        public string RecordersStatus { get; set; } = "Загрузка...";
        public Color RecordersColor { get; set; } = CyberPalette.TextSecondary;
    }
}