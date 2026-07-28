using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace CupsCore
{
    /// <summary>Найденная на машине установка Illustrator.</summary>
    public sealed class IllustratorInstall
    {
        public string ExePath { get; init; } = "";

        /// <summary>Как показать в списке: «Adobe Illustrator 2025».</summary>
        public string Title { get; init; } = "";

        /// <summary>Мажорная версия для сортировки (2025 → 29). 0 — определить не удалось.</summary>
        public int Version { get; init; }

        public override string ToString() => Title;
    }

    /// <summary>
    /// Ищет Illustrator на машине сам, чтобы путь к нему не приходилось зашивать в код.
    /// Именно это делало сборку персональной: у одного 2025, у другого 2022.
    ///
    /// Порядок поиска: реестр App Paths → ветка Adobe → перебор Program Files.
    /// Любой источник может отсутствовать — это не ошибка.
    /// </summary>
    public static class IllustratorLocator
    {
        private const string ExeName = "Illustrator.exe";

        /// <summary>Все найденные установки, самая свежая первой. Дубликаты отброшены.</summary>
        public static IReadOnlyList<IllustratorInstall> FindAll()
        {
            var found = new Dictionary<string, IllustratorInstall>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in CollectCandidatePaths())
            {
                if (string.IsNullOrWhiteSpace(path) || found.ContainsKey(path) || !File.Exists(path))
                    continue;

                found[path] = Describe(path);
            }

            var list = new List<IllustratorInstall>(found.Values);
            list.Sort((a, b) => b.Version.CompareTo(a.Version));
            return list;
        }

        /// <summary>Самая свежая установка либо null, если Illustrator не найден.</summary>
        public static string? FindBest()
        {
            var all = FindAll();
            return all.Count > 0 ? all[0].ExePath : null;
        }

        /// <summary>
        /// Значение из профиля → реальный путь. <c>"auto"</c> (или пусто) запускает поиск,
        /// явный путь возвращается как есть. null — Illustrator не найден.
        /// </summary>
        public static string? Resolve(string? configured)
        {
            if (string.IsNullOrWhiteSpace(configured) ||
                string.Equals(configured, MachineProfile.AutoDetect, StringComparison.OrdinalIgnoreCase))
            {
                return FindBest();
            }
            return configured;
        }

        // ---------- источники ----------

        private static IEnumerable<string> CollectCandidatePaths()
        {
            foreach (string p in FromAppPaths()) yield return p;
            foreach (string p in FromAdobeKey()) yield return p;
            foreach (string p in FromProgramFiles()) yield return p;
        }

        /// <summary>Реестр App Paths — работает и при установке на нестандартный диск.</summary>
        private static List<string> FromAppPaths()
        {
            var result = new List<string>();
            string subKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + ExeName;

            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                try
                {
                    using RegistryKey? key = hive.OpenSubKey(subKey);
                    if (key?.GetValue(null) is string path && !string.IsNullOrWhiteSpace(path))
                        result.Add(path.Trim('"'));
                }
                catch
                {
                    // Ветки может не быть или не хватать прав — просто пропускаем источник.
                }
            }
            return result;
        }

        /// <summary>Ветка Adobe: по одной подпапке на версию, внутри путь установки.</summary>
        private static List<string> FromAdobeKey()
        {
            var result = new List<string>();

            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                try
                {
                    using RegistryKey? root = hive.OpenSubKey(@"SOFTWARE\Adobe\Adobe Illustrator");
                    if (root == null)
                        continue;

                    foreach (string versionName in root.GetSubKeyNames())
                    {
                        using RegistryKey? versionKey = root.OpenSubKey(versionName);
                        if (versionKey == null)
                            continue;

                        foreach (string valueName in new[] { "InstallDir", "Install Dir", "ApplicationPath" })
                        {
                            if (versionKey.GetValue(valueName) is not string dir || string.IsNullOrWhiteSpace(dir))
                                continue;

                            result.Add(Path.Combine(dir.Trim('"'), "Support Files", "Contents", "Windows", ExeName));
                            result.Add(Path.Combine(dir.Trim('"'), ExeName));
                        }
                    }
                }
                catch
                {
                    // Источник необязательный.
                }
            }
            return result;
        }

        /// <summary>Перебор стандартных папок установки — покрывает подавляющее большинство машин.</summary>
        private static List<string> FromProgramFiles()
        {
            var result = new List<string>();

            var bases = new List<string?>
            {
                Environment.GetEnvironmentVariable("ProgramW6432"),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };

            foreach (string? baseDir in bases)
            {
                if (string.IsNullOrWhiteSpace(baseDir))
                    continue;

                string adobeDir = Path.Combine(baseDir, "Adobe");
                if (!Directory.Exists(adobeDir))
                    continue;

                string[] versionDirs;
                try
                {
                    versionDirs = Directory.GetDirectories(adobeDir, "Adobe Illustrator*");
                }
                catch
                {
                    continue;
                }

                foreach (string dir in versionDirs)
                {
                    result.Add(Path.Combine(dir, "Support Files", "Contents", "Windows", ExeName));
                    result.Add(Path.Combine(dir, ExeName));
                }
            }
            return result;
        }

        // ---------- описание ----------

        private static IllustratorInstall Describe(string exePath)
        {
            int version = 0;
            string title = "";

            try
            {
                var info = FileVersionInfo.GetVersionInfo(exePath);
                version = info.FileMajorPart;
                if (!string.IsNullOrWhiteSpace(info.ProductName))
                    title = info.ProductName.Trim();
            }
            catch
            {
                // Версию не прочитали — обойдёмся именем папки.
            }

            // Имя папки надёжнее для человека: «Adobe Illustrator 2025».
            string folderTitle = FolderTitle(exePath);
            if (!string.IsNullOrWhiteSpace(folderTitle))
                title = folderTitle;

            if (string.IsNullOrWhiteSpace(title))
                title = Path.GetFileName(exePath);

            return new IllustratorInstall { ExePath = exePath, Title = title, Version = version };
        }

        /// <summary>Поднимается от exe вверх до папки вида «Adobe Illustrator ...».</summary>
        private static string FolderTitle(string exePath)
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(exePath) ?? "");
            for (int depth = 0; dir != null && depth < 5; depth++, dir = dir.Parent)
            {
                if (dir.Name.StartsWith("Adobe Illustrator", StringComparison.OrdinalIgnoreCase))
                    return dir.Name;
            }
            return "";
        }
    }
}
