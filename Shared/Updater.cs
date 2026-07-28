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
            error = "";
            try
            {
                string source = Path.Combine(
                    PathResolver.Expand(MachineProfile.Current.UpdateSource),
                    release.Folder, "CupsForge.exe");

                string? current = Environment.ProcessPath;
                if (string.IsNullOrEmpty(current))
                {
                    error = "Не удалось определить путь к запущенной программе.";
                    return false;
                }

                string staging = Path.Combine(InstallFolder, "update");
                Directory.CreateDirectory(staging);
                string staged = Path.Combine(staging, "CupsForge.exe");

                File.Copy(source, staged, overwrite: true);

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
                string log = Path.Combine(staging, "apply.log");

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
