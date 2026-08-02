using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CupsCore
{
    /// <summary>
    /// Журнал на диск — диагностика без прав администратора.
    ///
    /// Журнал в окне живёт только в памяти: закрыл программу — и разбираться
    /// не с чем. Дизайнер на удалёнке говорит «не работает», а подключиться
    /// к его машине нельзя: прав нет ни у него, ни у нас. Файл превращает
    /// разговор «расскажите, что было» в «пришлите файл».
    ///
    /// Лежит в профиле пользователя, рядом с настройками: писать туда можно
    /// всегда и без разрешений.
    /// </summary>
    public static class DiagnosticLog
    {
        /// <summary>Сколько дней хранить. Дальше файл всё равно никто не откроет.</summary>
        private const int KeepDays = 14;

        /// <summary>
        /// Потолок на файл. Обновление шаблонов пишет строку на каждый из
        /// четырёхсот файлов — без потолка день работы даёт десятки мегабайт.
        /// </summary>
        private const long MaxBytes = 2 * 1024 * 1024;

        private static readonly object Lock = new();
        private static bool _trimmed;

        public static string DirectoryPath => Path.Combine(MachineProfile.DirectoryPath, "logs");

        public static string TodayFile =>
            Path.Combine(DirectoryPath, $"cupsforge-{DateTime.Now:yyyy-MM-dd}.log");

        /// <summary>
        /// Пишет строку. Никогда не бросает: журнал — вспомогательная вещь,
        /// и падать из-за него посреди создания проекта недопустимо.
        /// </summary>
        public static void Write(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                lock (Lock)
                {
                    Directory.CreateDirectory(DirectoryPath);

                    if (!_trimmed)
                    {
                        _trimmed = true;
                        RemoveOld();
                    }

                    string file = TodayFile;
                    if (File.Exists(file) && new FileInfo(file).Length > MaxBytes)
                        return; // потолок достигнут — дальше молчим, но не падаем

                    File.AppendAllText(file,
                        $"{DateTime.Now:HH:mm:ss}  {Scrub(message)}{Environment.NewLine}",
                        Encoding.UTF8);
                }
            }
            catch
            {
                // Диск полон, файл занят, папка недоступна — не наша забота.
            }
        }

        /// <summary>
        /// Вычищает то, чего в файле, уходящем по почте, быть не должно.
        ///
        /// Ключ доступа к Bitrix — это доступ к API, открытому в интернет.
        /// Он попадает в сообщения об ошибках HTTP, а файл дизайнер отправит
        /// не задумываясь. Проще вычистить здесь, чем надеяться, что он нигде
        /// не всплывёт.
        /// </summary>
        public static string Scrub(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Заголовок авторизации в любом виде.
            text = Regex.Replace(text, @"(?i)\b(Basic|Bearer)\s+[A-Za-z0-9+/=._\-]{8,}",
                                 "$1 «ключ скрыт»");

            // Сам ключ из профиля, если он вдруг попал в строку целиком.
            try
            {
                string key = MachineProfile.Current.Bitrix.AuthorizationHeader;
                if (!string.IsNullOrWhiteSpace(key) && key.Length >= 8)
                    text = text.Replace(key, "«ключ скрыт»", StringComparison.Ordinal);
            }
            catch
            {
                // Профиль недоступен — заголовок мы уже вычистили.
            }

            return text;
        }

        /// <summary>Файлы старше срока хранения. Возвращает, сколько убрано.</summary>
        public static int RemoveOld()
        {
            int removed = 0;
            try
            {
                DateTime edge = DateTime.Now.Date.AddDays(-KeepDays);
                foreach (string file in Directory.GetFiles(DirectoryPath, "cupsforge-*.log"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < edge)
                        {
                            File.Delete(file);
                            removed++;
                        }
                    }
                    catch { /* занят — уберём в следующий раз */ }
                }
            }
            catch { /* папки нет — и убирать нечего */ }

            return removed;
        }

        /// <summary>Список файлов журнала, свежие первыми. Для окна настроек.</summary>
        public static IReadOnlyList<string> Files()
        {
            try
            {
                return Directory.GetFiles(DirectoryPath, "cupsforge-*.log")
                    .OrderByDescending(File.GetLastWriteTime)
                    .ToList();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }
}
