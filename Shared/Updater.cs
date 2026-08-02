using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CupsCore
{
    /// <summary>Что лежит на раздаче: версия и папка с ней.</summary>
    public sealed class ReleaseInfo
    {
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("folder")]  public string Folder { get; set; } = "";
        [JsonPropertyName("notes")]   public string Notes { get; set; } = "";
    }

    /// <summary>
    /// Обновление программы без прав администратора.
    ///
    /// Всё происходит в профиле пользователя: программа живёт в %LOCALAPPDATA%,
    /// оттуда же и обновляется. Права администратора не нужны, потому что
    /// в Program Files ничего не пишется.
    ///
    /// Запускать программу прямо с сетевого диска нельзя: пока кто-то работает,
    /// файл заблокирован и выложить новую версию не получится. Поэтому на диске
    /// лежит раздача, а работает каждый со своей локальной копии.
    ///
    /// На удалёнке сетевой раздачи нет — проверка молча ничего не находит,
    /// и новую версию дизайнер забирает вместе с шаблонами через RDP.
    /// </summary>
    public static class Updater
    {
        public const string ReleaseFileName = "latest.json";

        /// <summary>Версия этой сборки.</summary>
        public static string CurrentVersion =>
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

        /// <summary>
        /// Есть ли на раздаче версия новее текущей. null — нет, недоступна или
        /// настроена пустая раздача. Ошибки наружу не выпускаются: не смогли
        /// проверить — просто работаем дальше.
        /// </summary>
        public static ReleaseInfo? Check(out string? diagnosis)
        {
            diagnosis = null;

            string configured = MachineProfile.Current.UpdateSource;
            if (string.IsNullOrWhiteSpace(configured))
                return null;

            string folder;
            try
            {
                folder = PathResolver.Expand(configured);
            }
            catch (PathResolutionException)
            {
                return null; // корни ещё не настроены — не повод шуметь
            }

            string file = Path.Combine(folder, ReleaseFileName);
            if (!File.Exists(file))
                return null;

            ReleaseInfo? release;
            try
            {
                release = JsonSerializer.Deserialize<ReleaseInfo>(File.ReadAllText(file));
            }
            catch (Exception ex)
            {
                diagnosis = $"Не удалось прочитать {file}: {ex.Message}";
                return null;
            }

            if (release == null || string.IsNullOrWhiteSpace(release.Version))
                return null;

            if (!IsNewer(release.Version, CurrentVersion))
                return null;

            string exe = Path.Combine(folder, release.Folder, "CupsForge.exe");
            if (!File.Exists(exe))
            {
                diagnosis = $"Заявлена версия {release.Version}, но файла нет: {exe}";
                return null;
            }

            return release;
        }

        public static bool IsNewer(string candidate, string current)
        {
            return Version.TryParse(candidate, out var a)
                && Version.TryParse(current, out var b)
                && a > b;
        }

        /// <summary>Куда программа устанавливается локально.</summary>
        public static string InstallFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CupsForge");

        /// <summary>Рабочая папка обновления: туда кладётся новая версия и пишется лог.</summary>
        public static string StagingFolder => Path.Combine(InstallFolder, "update");

        private const string ApplyLogName = "apply.log";

        /// <summary>
        /// Чем закончилась прошлая попытка обновиться. null — жаловаться не на что.
        ///
        /// Подмену файла делает скрипт без окна: сообщить что-либо на экран ему
        /// некому, поэтому итог он пишет в apply.log. Раньше этот файл никто не
        /// читал — обновление молча не применялось, программа запускалась старой,
        /// и заметить это можно было, только заглянув в служебную папку.
        ///
        /// След убирается в любом случае: жаловаться повторно на разобранную
        /// проблему хуже, чем не жаловаться вовсе.
        /// </summary>
        public static string? TakeUpdateProblem(string? stagingFolder = null)
        {
            string log = Path.Combine(stagingFolder ?? StagingFolder, ApplyLogName);

            string text;
            try
            {
                if (!File.Exists(log))
                    return null;

                text = File.ReadAllText(log);
            }
            catch
            {
                return null; // не прочитали — не повод шуметь
            }

            try { File.Delete(log); } catch { /* удалить не вышло — не беда */ }

            if (!text.Contains("FAILED", StringComparison.Ordinal))
                return null;

            return "Прошлое обновление не применилось: программа была занята и файл " +
                   "заменить не удалось. Сейчас работает прежняя версия — закройте её " +
                   "полностью и попробуйте обновиться ещё раз.";
        }

        /// <summary>Лежит ли запущенный файл внутри папки установки.</summary>
        public static bool IsInsideInstallFolder(string exePath)
        {
            try
            {
                string install = Path.GetFullPath(InstallFolder)
                    .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return Path.GetFullPath(exePath)
                    .StartsWith(install, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Скачивает новую версию рядом и передаёт замену маленькому скрипту:
        /// сам себя работающий файл переписать не может. Скрипт ждёт выхода
        /// программы, подменяет файл и запускает её снова.
        ///
        /// Возвращает true, если обновление запущено — вызывающая сторона
        /// должна сразу завершить программу.
        /// </summary>
        public static bool Apply(ReleaseInfo release, out string error)
        {
            try
            {
                string source = Path.Combine(
                    PathResolver.Expand(MachineProfile.Current.UpdateSource),
                    release.Folder, "CupsForge.exe");

                return ApplyFile(File.ReadAllBytes(source), out error);
            }
            catch (Exception ex)
            {
                error = "Не удалось обновиться: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Ставит уже полученную новую версию. Байты, а не путь: из раздачи
        /// программа приезжает по сети, а не лежит в папке.
        /// </summary>
        public static bool ApplyFile(byte[] exe, out string error)
        {
            error = "";
            try
            {
                string? current = Environment.ProcessPath;
                if (string.IsNullOrEmpty(current))
                {
                    error = "Не удалось определить путь к запущенной программе.";
                    return false;
                }

                // Обновлять имеет смысл только свою локальную копию. Если программу
                // запустили прямо с сетевого диска, подмена файла испортила бы раздачу
                // для всех остальных — и, скорее всего, просто не удалась бы.
                if (!IsInsideInstallFolder(current))
                {
                    error = "Программа запущена не из своей папки, обновление отменено.\n" +
                            $"Запущено: {current}\n" +
                            $"Ожидалось внутри: {InstallFolder}\n" +
                            "Скопируйте программу в эту папку и запускайте оттуда.";
                    return false;
                }

                string staging = StagingFolder;
                Directory.CreateDirectory(staging);
                string staged = Path.Combine(staging, "CupsForge.exe");

                File.WriteAllBytes(staged, exe);

                // Скрипт: подождать выхода, подменить файл, запустить снова, убрать себя.
                //
                // Внутри — только латиница, и файл пишется в ASCII. cmd.exe читает
                // .cmd в кодировке консоли (обычно cp866), а не в UTF-8: кириллица
                // в теле скрипта превратилась бы в мусор, и при кириллическом имени
                // пользователя пути перестали бы находиться.
                //
                // Итог работы пишется в лог рядом: окна у скрипта нет, показать
                // сообщение на экране некому, а разбираться потом по чему-то надо.
                string script = Path.Combine(staging, "apply.cmd");
                string log = Path.Combine(staging, ApplyLogName);

                // Пути НЕ подставляются в текст скрипта, а передаются аргументами:
                // аргументы процесса уходят в Unicode, а тело .cmd читается в кодировке
                // консоли. Иначе кириллица в имени пользователя (C:\Users\Дмитрий\…)
                // превратилась бы в мусор, и файл перестал бы находиться.
                File.WriteAllText(script, """
                    @echo off
                    setlocal
                    set "target=%~1"
                    set "staged=%~2"
                    set "log=%~3"

                    rem Ждём, пока программа закроется и отпустит файл (до 40 секунд).
                    for /l %%i in (1,1,40) do (
                        move /y "%staged%" "%target%" >nul 2>&1 && goto started
                        ping -n 2 127.0.0.1 >nul
                    )

                    echo [%date% %time%] FAILED: target locked, update not applied>>"%log%"
                    exit /b 1

                    :started
                    echo [%date% %time%] OK: updated>>"%log%"
                    start "" "%target%"
                    del "%~f0" >nul 2>&1
                    """, System.Text.Encoding.ASCII);

                // Внешняя пара кавычек — соглашение cmd: /c "…" снимает её сам,
                // а внутренние кавычки достаются аргументам.
                Process.Start(new ProcessStartInfo("cmd.exe")
                {
                    Arguments = $"/c \"\"{script}\" \"{current}\" \"{staged}\" \"{log}\"\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                return true;
            }
            catch (Exception ex)
            {
                error = "Не удалось обновиться: " + ex.Message;
                return false;
            }
        }
    }
}
