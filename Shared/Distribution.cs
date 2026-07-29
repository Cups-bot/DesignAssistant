using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CupsCore
{
    /// <summary>
    /// Опись раздачи: что сейчас актуально и где это лежит.
    ///
    /// Сам файл крошечный и хранится в git публичного репозитория — история его
    /// правок полезна, видно что и когда менялось. Настоящие файлы лежат
    /// вложениями к выпуску: они в историю НЕ попадают, их можно удалять,
    /// и место при этом действительно освобождается.
    /// </summary>
    public sealed class DistManifest
    {
        [JsonPropertyName("generated")] public string Generated { get; set; } = "";

        /// <summary>Свежая версия программы. null — в раздаче её пока нет.</summary>
        [JsonPropertyName("app")] public DistApp? App { get; set; }

        /// <summary>Каталог продуктов — приезжает вместе с шаблонами.</summary>
        [JsonPropertyName("catalog")] public DistFile? Catalog { get; set; }

        [JsonPropertyName("templates")] public List<DistFile> Templates { get; set; } = new();

        /// <summary>Общий объём шаблонов — чтобы спросить «скачать 400 МБ?» осмысленно.</summary>
        [JsonIgnore]
        public long TemplatesSize => Templates.Sum(t => t.Size);
    }

    public sealed class DistApp
    {
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("notes")]   public string Notes { get; set; } = "";
        [JsonPropertyName("asset")]   public string Asset { get; set; } = "";
        [JsonPropertyName("size")]    public long Size { get; set; }
        [JsonPropertyName("sha256")]  public string Sha256 { get; set; } = "";
    }

    /// <summary>Один файл раздачи: где он должен лежать и каким вложением приехать.</summary>
    public sealed class DistFile
    {
        /// <summary>Путь относительно папки шаблонов: «2_Offset/_DW90-430-0000 MC_work.ai».</summary>
        [JsonPropertyName("path")]   public string Path { get; set; } = "";

        /// <summary>Имя вложения в выпуске. Оно из отпечатка, а не из имени файла:
        /// GitHub переименовывает вложения с пробелами и кириллицей.</summary>
        [JsonPropertyName("asset")]  public string Asset { get; set; } = "";

        [JsonPropertyName("size")]   public long Size { get; set; }
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    }

    /// <summary>
    /// Что уже скачано на этой машине. Нужно, чтобы не пересчитывать отпечатки
    /// четырёхсот мегабайт при каждом запуске.
    /// </summary>
    public sealed class SyncState
    {
        [JsonPropertyName("files")]
        public Dictionary<string, string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Спрашивали ли уже разрешение на первую загрузку.</summary>
        [JsonPropertyName("firstSyncDone")]
        public bool FirstSyncDone { get; set; }

        public static string FilePath => System.IO.Path.Combine(MachineProfile.DirectoryPath, "sync-state.json");

        public static SyncState Load()
        {
            try
            {
                return File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<SyncState>(File.ReadAllText(FilePath)) ?? new SyncState()
                    : new SyncState();
            }
            catch
            {
                return new SyncState();
            }
        }

        public void Save()
        {
            Directory.CreateDirectory(MachineProfile.DirectoryPath);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));

            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
            else File.Move(temp, FilePath);
        }
    }

    /// <summary>Что предстоит скачать.</summary>
    public sealed class SyncPlan
    {
        public List<DistFile> Files { get; } = new();
        public long Size => Files.Sum(f => f.Size);
        public bool IsEmpty => Files.Count == 0;

        /// <summary>Первая загрузка: своих шаблонов на машине ещё нет.</summary>
        public bool IsFirstSync { get; init; }

        public string Describe()
        {
            if (IsEmpty) return "всё свежее";
            string size = Size >= 1024 * 1024
                ? $"{Size / 1024.0 / 1024.0:0.#} МБ"
                : $"{Size / 1024.0:0} КБ";
            return $"{Files.Count} файл(ов), {size}";
        }
    }

    /// <summary>Отпечаток файла — по нему решается, устарел он или нет.</summary>
    public static class FileHash
    {
        public static string Of(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }

        public static string OfBytes(byte[] data)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();
        }
    }
}
