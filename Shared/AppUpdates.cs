using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace CupsCore
{
    /// <summary>Что нашлось на раздаче.</summary>
    public sealed class AppUpdateInfo
    {
        public string Version { get; init; } = "";

        /// <summary>«Что изменилось» — показывается человеку в полоске.</summary>
        public string Notes { get; init; } = "";

        /// <summary>Откуда приехало: сетевой диск или интернет.</summary>
        public string SourceName { get; init; } = "";

        internal UpdateManager? Manager { get; init; }
        internal UpdateInfo? Raw { get; init; }
    }

    /// <summary>
    /// Обновление программы.
    ///
    /// Внутри Velopack: он скачивает только изменившееся (на нашей программе
    /// это 294 КБ вместо 76 МБ — среда .NET между выпусками не меняется и
    /// не качается повторно), сам подменяет файлы и перезапускает программу.
    ///
    /// Прежний самопис делал это .cmd-скриптом: тот ждал выхода программы,
    /// переписывал exe и запускался снова. Скрипт дал два бага — кодировку
    /// пути с кириллицей и невидимое зависание на «pause» в окне, которого
    /// нет. Теперь этим занимается отдельный Update.exe, проверенный не только
    /// у нас.
    ///
    /// КАНАЛА ДВА, и порядок не случаен:
    ///   1. сетевой диск — в офисе он быстрый и работает без интернета;
    ///   2. публичная раздача — единственное, что достаёт до домашних машин.
    /// Сначала пробуем первый: дома его просто нет, и проверка идёт дальше.
    /// </summary>
    public static class AppUpdates
    {
        /// <summary>Версия этой сборки.</summary>
        public static string CurrentVersion =>
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

        /// <summary>
        /// Установлена ли программа как положено. Портативный запуск (из папки
        /// сборки, с флешки, прямо с раздачи) обновлять нельзя: подменять там
        /// нечего, а при запуске с раздачи подмена испортила бы её всем.
        /// </summary>
        public static bool IsInstalled
        {
            get
            {
                try
                {
                    // Источник тут ни при чём — спрашиваем только про установку.
                    // Но пустой источник Velopack не принимает: раньше здесь
                    // стоял new UpdateManager(""), он падал, признак всегда
                    // выходил «не установлена», и программа НЕ ПРЕДЛАГАЛА
                    // ОБНОВЛЕНИЯ НИКОГДА. Прогон это пропускал: он и правда
                    // портативный, и верный ответ получался по неверной причине.
                    // Поймано живым запуском установленной копии.
                    var any = new SimpleFileSource(new DirectoryInfo(AppContext.BaseDirectory));
                    return new UpdateManager(any).IsInstalled;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Почему обновляться нельзя. null — можно.
        /// Спрашивается ДО того, как предложить кнопку: обещание, которое
        /// заведомо не выполнится, читается как поломка программы.
        /// </summary>
        public static string? Unavailable()
        {
            if (IsInstalled)
                return null;

            return "Обновляться отсюда нельзя: программа запущена не из установленной копии. " +
                   "Установите её через Setup.exe и запускайте ярлыком с рабочего стола.";
        }

        /// <summary>
        /// Каналы обновления в порядке проверки — как их назвать человеку.
        /// Вынесено отдельно, чтобы порядок и состав можно было проверить
        /// прогоном: сама проверка обновлений требует настоящей раздачи.
        /// </summary>
        public static IReadOnlyList<string> DescribeSources(MachineProfile profile)
        {
            var names = new List<string>();

            string? folder = ExpandFolder(profile);
            if (folder != null)
                names.Add("сетевой диск: " + folder);

            if (!string.IsNullOrWhiteSpace(profile.UpdateRepo))
                names.Add("раздача: " + profile.UpdateRepo);

            return names;
        }

        /// <summary>
        /// Папка раздачи на диске, если она настроена и существует.
        /// null — канала нет: дома сетевого диска не видно, и это норма,
        /// а не повод шуметь.
        /// </summary>
        private static string? ExpandFolder(MachineProfile profile)
        {
            if (string.IsNullOrWhiteSpace(profile.UpdateSource))
                return null;

            try
            {
                string folder = PathResolver.Expand(profile.UpdateSource);
                return Directory.Exists(folder) ? folder : null;
            }
            catch (PathResolutionException)
            {
                return null; // корни ещё не настроены
            }
        }

        /// <summary>
        /// Ищет версию новее текущей. null — нечего ставить либо каналы
        /// недоступны. Ошибки наружу не выпускаются: не смогли проверить —
        /// работаем дальше, это не повод мешать человеку.
        /// </summary>
        public static async Task<AppUpdateInfo?> CheckAsync(MachineProfile profile, Action<string> log)
        {
            if (!IsInstalled)
                return null;

            foreach (var (name, source) in Sources(profile))
            {
                try
                {
                    var manager = new UpdateManager(source);
                    UpdateInfo? update = await manager.CheckForUpdatesAsync();
                    if (update == null)
                        continue;

                    return new AppUpdateInfo
                    {
                        Version = update.TargetFullRelease.Version.ToString(),
                        Notes = (update.TargetFullRelease.NotesMarkdown ?? "").Trim(),
                        SourceName = name,
                        Manager = manager,
                        Raw = update
                    };
                }
                catch (Exception ex)
                {
                    // Один канал недоступен — пробуем следующий. Молчать нельзя:
                    // «обновления не приходят» без объяснения не разобрать.
                    log($"Канал обновления «{name}» недоступен: {ex.Message}");
                }
            }

            return null;
        }

        private static IEnumerable<(string name, IUpdateSource source)> Sources(MachineProfile profile)
        {
            string? folder = ExpandFolder(profile);
            if (folder != null)
                yield return ("сетевой диск", new SimpleFileSource(new DirectoryInfo(folder)));

            if (!string.IsNullOrWhiteSpace(profile.UpdateRepo))
                yield return ("раздача", new GithubSource(profile.UpdateRepo, null, prerelease: false));
        }

        /// <summary>
        /// Скачивает и применяет обновление. При успехе программа
        /// ПЕРЕЗАПУСКАЕТСЯ и управление сюда не возвращается.
        /// Возвращает текст ошибки, если не вышло.
        /// </summary>
        public static async Task<string?> ApplyAsync(AppUpdateInfo update, Action<string> progress)
        {
            if (update.Manager == null || update.Raw == null)
                return "Обновление уже недействительно — проверьте ещё раз.";

            try
            {
                await update.Manager.DownloadUpdatesAsync(
                    update.Raw,
                    percent => progress($"Скачано {percent}%"));

                update.Manager.ApplyUpdatesAndRestart(update.Raw);
                return null;
            }
            catch (Exception ex)
            {
                return "Не удалось обновиться: " + ex.Message;
            }
        }
    }
}
