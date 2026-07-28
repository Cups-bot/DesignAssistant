using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace CupsCore
{
    /// <summary>
    /// Достаёт каталог продуктов и держит его в памяти.
    ///
    /// Источники по порядку — побеждает первый, который удалось прочитать:
    ///   1. catalog.json рядом с программой — ручная подмена, удобно при отладке;
    ///   2. каталог в папке шаблонов (путь из профиля) — обычный рабочий источник:
    ///      в офисе это сетевой диск, на удалёнке — локальная копия, которая
    ///      приезжает вместе с шаблонами;
    ///   3. копия, вшитая в программу — чтобы она работала всегда.
    ///
    /// Каталог без шаблонов бесполезен, поэтому он и лежит рядом с ними, а не
    /// доставляется отдельным каналом.
    /// </summary>
    public static class CatalogService
    {
        public const string FileName = "catalog.json";

        private static Catalog? _current;

        public static Catalog Current => _current ??= Load();

        /// <summary>Перечитать каталог (после правки файла или смены профиля).</summary>
        public static void Reload() => _current = null;

        /// <summary>Подменить каталог в памяти (тесты).</summary>
        public static void Set(Catalog catalog) => _current = catalog;

        /// <summary>
        /// Что произошло при последней загрузке: какой источник сработал и почему
        /// не подошли предыдущие. Показывается в настройках.
        /// </summary>
        public static IReadOnlyList<string> LoadLog => _loadLog;
        private static List<string> _loadLog = new();

        public static Catalog Load()
        {
            var log = new List<string>();

            foreach (var (name, reader) in Sources())
            {
                string? json;
                try
                {
                    json = reader();
                }
                catch (Exception ex)
                {
                    log.Add($"{name}: не прочитан ({ex.Message})");
                    continue;
                }

                if (json == null)
                {
                    log.Add($"{name}: нет");
                    continue;
                }

                Catalog? catalog;
                try
                {
                    catalog = Parse(json);
                }
                catch (Exception ex)
                {
                    // Битый файл не должен ронять программу — идём к следующему источнику.
                    log.Add($"{name}: разобрать не удалось ({ex.Message})");
                    continue;
                }

                if (catalog == null)
                {
                    log.Add($"{name}: пустой файл");
                    continue;
                }

                catalog.SourceName = name;
                log.Add($"{name}: загружен, версия {catalog.Version} от {catalog.Updated}");
                _loadLog = log;
                return catalog;
            }

            _loadLog = log;
            throw new CatalogException(
                "Не удалось загрузить каталог продуктов ни из одного источника:\n" +
                string.Join("\n", log));
        }

        public static Catalog? Parse(string json)
        {
            var opts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            return JsonSerializer.Deserialize<Catalog>(json, opts);
        }

        // ---------- источники ----------

        private static IEnumerable<(string name, Func<string?> read)> Sources()
        {
            // 1. Рядом с программой.
            string local = Path.Combine(AppContext.BaseDirectory, FileName);
            yield return ($"файл рядом с программой ({local})",
                          () => File.Exists(local) ? File.ReadAllText(local) : null);

            // 2. Путь из профиля (по умолчанию — папка шаблонов).
            yield return ("каталог в папке шаблонов", () =>
            {
                string configured = MachineProfile.Current.CatalogPath;
                if (string.IsNullOrWhiteSpace(configured))
                    return null;

                string path;
                try
                {
                    path = PathResolver.Expand(configured);
                }
                catch (PathResolutionException)
                {
                    return null; // корень ещё не настроен — не ошибка
                }
                return File.Exists(path) ? File.ReadAllText(path) : null;
            });

            // 3. Копия внутри программы.
            yield return ("копия внутри программы", ReadEmbedded);
        }

        private static string? ReadEmbedded()
        {
            var assembly = Assembly.GetExecutingAssembly();

            // Имя ресурса зависит от сборки (DesignAssistant.* или CupsForge.*),
            // поэтому ищем по окончанию, а не по точному имени.
            string? resource = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("." + FileName, StringComparison.OrdinalIgnoreCase));

            if (resource == null)
                return null;

            using Stream? stream = assembly.GetManifestResourceStream(resource);
            if (stream == null)
                return null;

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
