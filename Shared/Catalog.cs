using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace CupsCore
{
    /// <summary>
    /// Каталог продуктов — данные, а не код.
    ///
    /// Описывает, какие бывают продукты, где лежат их шаблоны, как называется папка
    /// проекта и куда она складывается. Добавление нового типа продукта — правка
    /// catalog.json, без пересборки программы.
    /// </summary>
    public sealed class Catalog
    {
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("updated")]
        public string Updated { get; set; } = "";

        /// <summary>Таблицы «артикул → файл шаблона», общие для нескольких продуктов.</summary>
        [JsonPropertyName("articleTables")]
        public Dictionary<string, Dictionary<string, string>> ArticleTables { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Направления в порядке показа. Порядок продуктов для этого не годится:
        /// он задаёт приоритет подбора (частное выше общего), а не удобство выбора.
        /// Если раздел не задан, направления берутся из продуктов.
        /// </summary>
        [JsonPropertyName("brands")]
        public List<CatalogBrand> BrandList { get; set; } = new();

        [JsonPropertyName("products")]
        public List<CatalogProduct> Products { get; set; } = new();

        /// <summary>
        /// Слова, которыми Bitrix называет направления, способы печати и прочее.
        /// Раньше они были зашиты в коде, и каждое новое значение («Тампопечать»)
        /// означало пересборку. Теперь достаточно дописать строчку сюда.
        /// </summary>
        [JsonPropertyName("bitrixWords")]
        public BitrixWords Words { get; set; } = new();

        /// <summary>Откуда каталог загружен — показывается пользователю.</summary>
        [JsonIgnore]
        public string SourceName { get; set; } = "";

        // ---------- подбор продукта ----------

        /// <summary>
        /// Первый продукт, подходящий под параметры заказа. Порядок в файле — порядок
        /// проверки, поэтому частные случаи стоят выше общих.
        /// </summary>
        public CatalogProduct? Match(DesignSpec spec) =>
            Products.FirstOrDefault(p => p.Matches(spec));

        /// <summary>То же, но с понятной ошибкой вместо null.</summary>
        public CatalogProduct Require(DesignSpec spec)
        {
            return Match(spec) ?? throw new CatalogException(
                $"В каталоге нет продукта для сочетания: направление {spec.Brand}, " +
                $"тип {spec.ProductType}, печать {spec.PrintTech}. " +
                $"Добавьте его в catalog.json (источник: {SourceName}).");
        }

        /// <summary>
        /// Тип продукта по тексту из Bitrix («Бумажный стакан» → Cups).
        /// Ключевые слова живут в каталоге, поэтому новый тип продукта начинает
        /// распознаваться сразу после правки catalog.json.
        /// Возвращает null, если ничего не подошло.
        /// </summary>
        public string? ProductTypeFromText(Brand brand, string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            string brandName = brand.ToString();

            // Побеждает САМОЕ ТОЧНОЕ слово, а не первое по порядку.
            // «Пластиковый стакан» содержит и «пластик», и «стакан»; при выборе
            // по порядку выигрывали стаканы, и заказ уходил не в ту папку.
            // Более длинное совпадение — более конкретное, поэтому берём его.
            foreach (bool sameBrandOnly in new[] { true, false })
            {
                string? best = null;
                int bestLength = 0;

                foreach (var p in Products)
                {
                    if (string.IsNullOrWhiteSpace(p.ProductType))
                        continue;
                    if (sameBrandOnly && !string.Equals(p.Brand, brandName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    int length = p.LongestTypeKeywordIn(text);
                    if (length > bestLength)
                    {
                        bestLength = length;
                        best = p.ProductType;
                    }
                }

                if (best != null)
                    return best;
            }
            return null;
        }

        /// <summary>
        /// Значение перечисления по тексту из Bitrix и словарю слов из каталога.
        /// Побеждает самое длинное совпадение: «немелованный» содержит «мелован»,
        /// и без этого правила материал определялся бы наоборот.
        /// null — ничего не подошло.
        /// </summary>
        public static T? EnumFromWords<T>(Dictionary<string, List<string>> words, string? text)
            where T : struct, Enum
        {
            if (string.IsNullOrWhiteSpace(text) || words.Count == 0)
                return null;

            T? best = null;
            int bestLength = 0;

            foreach (var (name, keywords) in words)
            {
                if (!Enum.TryParse<T>(name, true, out var value) || keywords == null)
                    continue;

                foreach (string keyword in keywords)
                {
                    if (string.IsNullOrWhiteSpace(keyword) || keyword.Length <= bestLength)
                        continue;
                    if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        bestLength = keyword.Length;
                        best = value;
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// Вариант продукта по тексту вкуса/вида из Bitrix («Dark» → Dark).
        /// Возвращает defaultVariant продукта, если ничего не подошло.
        /// </summary>
        public static string VariantFromText(CatalogProduct product, string? text)
        {
            if (product.Variants is not { Count: > 0 })
                return "";

            if (!string.IsNullOrWhiteSpace(text))
            {
                foreach (var (name, variant) in product.Variants)
                {
                    // Имя варианта тоже считается ключевым словом.
                    if (text.Contains(name, StringComparison.OrdinalIgnoreCase))
                        return name;

                    if (variant.Keywords?.Any(k =>
                            !string.IsNullOrWhiteSpace(k) &&
                            text.Contains(k, StringComparison.OrdinalIgnoreCase)) == true)
                    {
                        return name;
                    }
                }
            }

            return product.DefaultVariant ?? "";
        }

        // ---------- запросы для интерфейса ----------
        //
        // Ручная панель строится этими методами, а не разметкой. Поэтому новый
        // продукт из каталога сразу появляется в списках — рисовать кнопку не нужно.

        /// <summary>Направления для списка: идентификатор и название, в порядке показа.</summary>
        public List<(Brand Id, string Title)> BrandsForUi()
        {
            var result = new List<(Brand, string)>();

            foreach (var b in BrandList)
            {
                if (Enum.TryParse<Brand>(b.Id, true, out var brand) && !result.Any(x => x.Item1 == brand))
                    result.Add((brand, string.IsNullOrWhiteSpace(b.Title) ? brand.ToString() : b.Title));
            }

            // Раздел "brands" не обязателен — добираем всё, что встретилось в продуктах.
            foreach (var p in Products)
            {
                if (Enum.TryParse<Brand>(p.Brand, true, out var brand) && !result.Any(x => x.Item1 == brand))
                    result.Add((brand, brand.ToString()));
            }

            return result;
        }

        /// <summary>
        /// Типы продуктов направления: идентификатор и название для списка.
        /// Пустой список — у направления типов нет (выбирать нечего, строку прячем).
        /// </summary>
        public List<(string Id, string Title)> ProductTypesOf(Brand brand)
        {
            var result = new List<(string, string)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in Products)
            {
                if (!Enum.TryParse<Brand>(p.Brand, true, out var b) || b != brand)
                    continue;
                if (string.IsNullOrWhiteSpace(p.ProductType) || !seen.Add(p.ProductType))
                    continue;

                result.Add((p.ProductType, string.IsNullOrWhiteSpace(p.TypeTitle) ? p.ProductType : p.TypeTitle));
            }
            return result;
        }

        /// <summary>
        /// Способы печати, между которыми есть реальный выбор: то есть такие,
        /// что разные значения приводят к разным продуктам. Если продукт один и тот же,
        /// выбор бессмысленный — возвращается пустой список, и строку можно спрятать.
        /// </summary>
        public List<PrintTech> PrintTechsOf(Brand brand, string productType)
        {
            var available = new List<PrintTech>();
            var products = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (PrintTech tech in Enum.GetValues<PrintTech>())
            {
                var found = Match(new DesignSpec { Brand = brand, ProductType = productType, PrintTech = tech });
                if (found == null)
                    continue;

                available.Add(tech);
                products.Add(found.Id);
            }

            return products.Count > 1 ? available : new List<PrintTech>();
        }

        /// <summary>Варианты продукта: идентификатор и название для списка.</summary>
        public static List<(string Id, string Title)> VariantsOf(CatalogProduct? product)
        {
            var result = new List<(string, string)>();
            if (product?.Variants == null)
                return result;

            foreach (var (key, variant) in product.Variants)
                result.Add((key, string.IsNullOrWhiteSpace(variant.Title) ? key : variant.Title));

            return result;
        }

        // ---------- проверка ----------

        /// <summary>
        /// Ошибки в каталоге человеческим языком. Пустой список — каталог корректен.
        /// Файлы шаблонов здесь не проверяются, для этого есть <see cref="CheckTemplateFiles"/>.
        /// </summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (Version <= 0)
                errors.Add("Не указана версия каталога (\"version\").");

            if (Products.Count == 0)
                errors.Add("В каталоге нет ни одного продукта.");

            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var knownRoots = new HashSet<string>(MachineProfile.Root.AllIncludingBase, StringComparer.OrdinalIgnoreCase);

            foreach (var p in Products)
            {
                string where = string.IsNullOrWhiteSpace(p.Id) ? "(продукт без id)" : $"продукт \"{p.Id}\"";

                if (string.IsNullOrWhiteSpace(p.Id))
                    errors.Add("У продукта не задан \"id\".");
                else if (!seenIds.Add(p.Id))
                    errors.Add($"{where}: такой id уже есть, id должен быть уникальным.");

                if (!Enum.TryParse<Brand>(p.Brand, true, out _))
                    errors.Add($"{where}: неизвестное направление \"{p.Brand}\". Допустимо: {Names<Brand>()}.");

                // Тип продукта намеренно не проверяется по списку: его набор задаёт
                // сам каталог, иначе новый продукт снова требовал бы правки кода.

                foreach (string tech in p.PrintTech ?? new List<string>())
                {
                    if (!Enum.TryParse<PrintTech>(tech, true, out _))
                        errors.Add($"{where}: неизвестный способ печати \"{tech}\". Допустимо: {Names<PrintTech>()}.");
                }

                if (string.IsNullOrWhiteSpace(p.Output))
                    errors.Add($"{where}: не указано \"output\" — куда складывать готовый проект.");
                else if (!p.Output.Contains('{') && !knownRoots.Contains(p.Output))
                    errors.Add($"{where}: неизвестный корень \"{p.Output}\". Допустимо: " +
                               $"{string.Join(", ", MachineProfile.Root.AllIncludingBase)} " +
                               "либо путь с подстановкой, например \"{base}/Lids\".");

                if (p.Naming is not ("plain" or "pantone"))
                    errors.Add($"{where}: \"naming\" должно быть plain или pantone, а не \"{p.Naming}\".");

                if (!string.Equals(p.ArticleFrom, "lastWord", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(p.ArticleFrom, "last8", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"{where}: \"articleFrom\" должно быть lastWord или last8, а не \"{p.ArticleFrom}\".");
                }

                bool hasFolder = !string.IsNullOrWhiteSpace(p.TemplateFolder);
                bool hasCountryFolders = p.TemplateFolderByCountry is { Count: > 0 };
                if (!hasFolder && !hasCountryFolders)
                    errors.Add($"{where}: не указана папка шаблонов (\"templateFolder\" или \"templateFolderByCountry\").");

                if (!string.IsNullOrWhiteSpace(p.Articles) && !ArticleTables.ContainsKey(p.Articles))
                    errors.Add($"{where}: таблица артикулов \"{p.Articles}\" не найдена в \"articleTables\".");

                bool canFindFile = !string.IsNullOrWhiteSpace(p.Articles)
                                   || p.ByArticle is { Count: > 0 }
                                   || p.Variants is { Count: > 0 }
                                   || !string.IsNullOrWhiteSpace(p.FilePattern);
                if (!canFindFile)
                    errors.Add($"{where}: не задано, как искать файл шаблона " +
                               "(\"articles\", \"byArticle\", \"variants\" или \"filePattern\").");

                if (!string.IsNullOrWhiteSpace(p.DefaultVariant) &&
                    (p.Variants == null || !p.Variants.ContainsKey(p.DefaultVariant)))
                {
                    errors.Add($"{where}: \"defaultVariant\" = \"{p.DefaultVariant}\", но такого варианта нет.");
                }
            }

            return errors;
        }

        /// <summary>
        /// Проверяет, что все файлы шаблонов из каталога физически на месте.
        /// Именно этим удалённый дизайнер ловит «не докопировал шаблоны».
        /// </summary>
        public List<string> CheckTemplateFiles()
        {
            var problems = new List<string>();

            foreach (var p in Products)
            {
                var folders = new List<string>();
                try
                {
                    if (p.TemplateFolderByCountry is { Count: > 0 })
                        folders.AddRange(p.TemplateFolderByCountry.Values.Select(PathResolver.Expand));
                    else if (!string.IsNullOrWhiteSpace(p.TemplateFolder))
                        folders.Add(PathResolver.Expand(p.TemplateFolder));
                }
                catch (PathResolutionException ex)
                {
                    problems.Add($"{p.Id}: {ex.Message}");
                    continue;
                }

                foreach (string folder in folders)
                {
                    if (!Directory.Exists(folder))
                    {
                        problems.Add($"{p.Title}: нет папки шаблонов {folder}");
                        continue;
                    }

                    foreach (string file in EnumerateKnownFiles(p))
                    {
                        if (!File.Exists(Path.Combine(folder, file)))
                            problems.Add($"{p.Title}: нет файла {file} в {folder}");
                    }
                }
            }

            return problems;
        }

        /// <summary>Имена файлов, которые продукт может запросить (шаблоны по образцу не считаются).</summary>
        private IEnumerable<string> EnumerateKnownFiles(CatalogProduct p)
        {
            var table = ResolveArticleTable(p);
            if (table != null)
            {
                foreach (string file in table.Values)
                    yield return file;
            }

            if (p.Variants != null)
            {
                foreach (var v in p.Variants.Values)
                    yield return v.File;
            }
        }

        /// <summary>Таблица «артикул → файл» продукта: своя либо общая из articleTables.</summary>
        public Dictionary<string, string>? ResolveArticleTable(CatalogProduct p)
        {
            if (p.ByArticle is { Count: > 0 })
                return p.ByArticle;

            if (!string.IsNullOrWhiteSpace(p.Articles) &&
                ArticleTables.TryGetValue(p.Articles, out var table))
            {
                return table;
            }
            return null;
        }

        private static string Names<T>() where T : struct, Enum =>
            string.Join(", ", Enum.GetNames<T>());
    }

    /// <summary>
    /// Словари слов, которыми Bitrix называет значения. Ключ — имя значения
    /// перечисления, список — слова, по которым его узнавать (сравнение по вхождению,
    /// без учёта регистра).
    /// </summary>
    public sealed class BitrixWords
    {
        [JsonPropertyName("brand")]
        public Dictionary<string, List<string>> Brand { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("printTech")]
        public Dictionary<string, List<string>> PrintTech { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("material")]
        public Dictionary<string, List<string>> Material { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("coating")]
        public Dictionary<string, List<string>> Coating { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("country")]
        public Dictionary<string, List<string>> Country { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Направление в списке ручного ввода.</summary>
    public sealed class CatalogBrand
    {
        [JsonPropertyName("id")]    public string Id { get; set; } = "";
        [JsonPropertyName("title")] public string Title { get; set; } = "";
    }

    /// <summary>Один продукт каталога.</summary>
    public sealed class CatalogProduct
    {
        [JsonPropertyName("id")]          public string Id { get; set; } = "";
        [JsonPropertyName("title")]       public string Title { get; set; } = "";

        // --- отбор ---
        [JsonPropertyName("brand")]       public string Brand { get; set; } = "";
        [JsonPropertyName("productType")] public string? ProductType { get; set; }

        /// <summary>Название типа продукта для списка в ручном режиме («Стаканы»).</summary>
        [JsonPropertyName("typeTitle")]   public string? TypeTitle { get; set; }
        [JsonPropertyName("printTech")]   public List<string>? PrintTech { get; set; }

        /// <summary>
        /// Слова, по которым тип продукта из Bitrix относится к этому продукту
        /// («стакан», «пластик», «крышк»). Сравнение — вхождение без учёта регистра.
        /// </summary>
        [JsonPropertyName("typeKeywords")]
        public List<string>? TypeKeywords { get; set; }

        /// <summary>Участвует ли покрытие (soft touch и т.п.) в args.txt.</summary>
        [JsonPropertyName("coating")]
        public bool Coating { get; set; }

        /// <summary>Дописывать ли страну последней строкой args.txt (CupToYou).</summary>
        [JsonPropertyName("argsCountry")]
        public bool ArgsCountry { get; set; }

        // --- результат ---
        [JsonPropertyName("output")]      public string Output { get; set; } = "";
        [JsonPropertyName("naming")]      public string Naming { get; set; } = "plain";
        [JsonPropertyName("argsTech")]    public string? ArgsTech { get; set; }

        /// <summary>
        /// Как ручной редактор достаёт артикул из имени дизайна:
        /// lastWord — последнее слово; last8 — последние 8 символов.
        /// В автоматическом режиме не используется: там артикул приходит из Bitrix.
        /// </summary>
        [JsonPropertyName("articleFrom")]
        public string ArticleFrom { get; set; } = "lastWord";

        /// <summary>Артикул из введённого имени дизайна по правилу продукта.</summary>
        public string ExtractArticle(string designName)
        {
            string trimmed = (designName ?? "").Trim();

            if (string.Equals(ArticleFrom, "last8", StringComparison.OrdinalIgnoreCase))
                return trimmed.Length >= 8 ? trimmed[^8..] : "";

            string[] words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length > 0 ? words[^1] : "";
        }

        // --- шаблоны ---
        [JsonPropertyName("templateFolder")]
        public string? TemplateFolder { get; set; }

        [JsonPropertyName("templateFolderByCountry")]
        public Dictionary<string, string>? TemplateFolderByCountry { get; set; }

        /// <summary>Имя таблицы из articleTables.</summary>
        [JsonPropertyName("articles")]
        public string? Articles { get; set; }

        /// <summary>Таблица «артикул → файл» прямо здесь (альтернатива articles).</summary>
        [JsonPropertyName("byArticle")]
        public Dictionary<string, string>? ByArticle { get; set; }

        [JsonPropertyName("variants")]
        public Dictionary<string, CatalogVariant>? Variants { get; set; }

        [JsonPropertyName("defaultVariant")]
        public string? DefaultVariant { get; set; }

        /// <summary>Образец имени файла: {article}, {country} (строчными), {COUNTRY} (прописными).</summary>
        [JsonPropertyName("filePattern")]
        public string? FilePattern { get; set; }

        [JsonIgnore]
        public bool IsPantoneNaming => string.Equals(Naming, "pantone", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Папка, куда складывать готовые проекты этого продукта.
        /// "output" — либо имя логического корня ("out.mycups"), либо путь
        /// с подстановками ("{base}/Lids") для продукта со своей папкой.
        /// </summary>
        public string ResolveOutput() =>
            Output.Contains('{') ? PathResolver.Expand(Output) : PathResolver.Root(Output);

        public bool Matches(DesignSpec spec)
        {
            if (!Enum.TryParse<Brand>(Brand, true, out var brand) || brand != spec.Brand)
                return false;

            if (!string.IsNullOrWhiteSpace(ProductType) &&
                !string.Equals(ProductType, spec.ProductType, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (PrintTech is { Count: > 0 })
            {
                bool any = PrintTech.Any(t =>
                    Enum.TryParse<PrintTech>(t, true, out var tech) && tech == spec.PrintTech);
                if (!any)
                    return false;
            }

            return true;
        }

        /// <summary>Упоминается ли в тексте одно из ключевых слов типа продукта.</summary>
        public bool MatchesTypeText(string? text) => LongestTypeKeywordIn(text) > 0;

        /// <summary>
        /// Длина самого длинного ключевого слова этого продукта, найденного в тексте.
        /// 0 — не совпало. По этой длине выбирается самый точный продукт из нескольких.
        /// </summary>
        public int LongestTypeKeywordIn(string? text)
        {
            if (string.IsNullOrWhiteSpace(text) || TypeKeywords == null)
                return 0;

            int best = 0;
            foreach (string keyword in TypeKeywords)
            {
                if (!string.IsNullOrWhiteSpace(keyword) &&
                    keyword.Length > best &&
                    text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    best = keyword.Length;
                }
            }
            return best;
        }
    }

    /// <summary>Вариант продукта: вкус шоколада, вид конфет.</summary>
    public sealed class CatalogVariant
    {
        [JsonPropertyName("file")]
        public string File { get; set; } = "";

        /// <summary>Артикул, который надо подставить, если продукт выбран по варианту.</summary>
        [JsonPropertyName("article")]
        public string? Article { get; set; }

        /// <summary>Название варианта для списка в ручном режиме («Тёмный»).</summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>
        /// Слова, по которым вкус/вид из Bitrix относится к этому варианту
        /// («dark», «тёмн», «горьк»). Раньше это были switch'и в коде.
        /// </summary>
        [JsonPropertyName("keywords")]
        public List<string>? Keywords { get; set; }
    }

    /// <summary>Проблема в каталоге продуктов.</summary>
    public sealed class CatalogException : ConfigurationException
    {
        public CatalogException(string message) : base(message) { }
    }
}
