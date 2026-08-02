using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CupsCore
{
    /// <summary>
    /// Настройки конкретной машины: где лежат папки, где Illustrator, доступ к Bitrix.
    ///
    /// Хранится в %APPDATA%\CupsForge\profile.json и НИКОГДА не попадает ни в сборку,
    /// ни в git — у каждого дизайнера он свой (офис / удалёнка / другая версия
    /// Illustrator). Программа при этом одна на всех.
    ///
    /// Код и каталог ссылаются только на ЛОГИЧЕСКИЕ корни (<see cref="Root"/>),
    /// профиль превращает их в физические пути.
    /// </summary>
    public sealed class MachineProfile
    {
        public const int CurrentVersion = 1;

        /// <summary>Значение <see cref="IllustratorExe"/>, означающее «найти самому».</summary>
        public const string AutoDetect = "auto";

        /// <summary>Структура папок в офисе — от неё же строится и локальная копия.</summary>
        public const string OfficeStakanyRoot = @"Y:\STAKANY";

        // ---------- логические корни ----------

        /// <summary>
        /// Ключи логических корней. Всё остальное в программе адресуется через них,
        /// физических путей в коде больше нет.
        /// </summary>
        public static class Root
        {
            /// <summary>
            /// Сама папка STAKANY. Нужна каталогу: новый продукт может складываться
            /// в свою папку рядом с остальными — "output": "{base}/Lids".
            /// В мастере настройки отдельной строкой не показывается: она задаётся
            /// полем «папка STAKANY» наверху.
            /// </summary>
            public const string Base = "base";

            public const string Templates = "templates";
            public const string MyCups    = "out.mycups";
            public const string CuptoYou  = "out.cuptoyou";
            public const string Flexo     = "out.flexo";
            public const string Offset    = "out.offset";
            public const string PadPrint  = "out.padprint";

            /// <summary>Корни, показываемые в мастере настройки.</summary>
            public static readonly string[] All =
            {
                Templates, MyCups, CuptoYou, Flexo, Offset, PadPrint
            };

            /// <summary>Все корни, включая базовую папку — на них может ссылаться каталог.</summary>
            public static readonly string[] AllIncludingBase =
            {
                Base, Templates, MyCups, CuptoYou, Flexo, Offset, PadPrint
            };

            /// <summary>Человеческое название для мастера настройки.</summary>
            public static string Title(string key) => key switch
            {
                Base      => "Папка STAKANY",
                Templates => "Шаблоны (.ai)",
                MyCups    => "Готовые проекты — MyCups",
                CuptoYou  => "Готовые проекты — CupToYou",
                Flexo     => "Готовые проекты — Флексо/Pantone",
                Offset    => "Готовые проекты — Офсет (Formacia)",
                PadPrint  => "Готовые проекты — Пластик (PadPrint)",
                _         => key
            };

            /// <summary>Путь корня относительно папки STAKANY — офисный скелет.</summary>
            public static string RelativeToStakany(string key) => key switch
            {
                Base      => "",
                Templates => "_Templates",
                MyCups    => "MyCups",
                CuptoYou  => "CuptoYou",
                Flexo     => "Flexo",
                Offset    => "Offset",
                PadPrint  => "PadPrint",
                _         => key
            };
        }

        // ---------- данные профиля ----------

        [JsonPropertyName("version")]
        public int Version { get; set; } = CurrentVersion;

        /// <summary>"office" — работа с сетевого диска, "remote" — локальная копия структуры.</summary>
        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "office";

        /// <summary>Логический корень → физический путь на этой машине.</summary>
        [JsonPropertyName("roots")]
        public Dictionary<string, string> Roots { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Путь к Illustrator.exe либо <see cref="AutoDetect"/>.</summary>
        [JsonPropertyName("illustratorExe")]
        public string IllustratorExe { get; set; } = AutoDetect;

        /// <summary>JSX-скрипт запуска. Поддерживает подстановку {templates}.</summary>
        [JsonPropertyName("jsxScript")]
        public string JsxScript { get; set; } = @"{templates}\Scripts\0_DW_Start.jsx";

        /// <summary>
        /// Каталог продуктов. По умолчанию лежит в папке шаблонов: каталог без
        /// шаблонов бесполезен, поэтому они и путешествуют вместе.
        /// </summary>
        [JsonPropertyName("catalogPath")]
        public string CatalogPath { get; set; } = @"{templates}\catalog.json";

        /// <summary>
        /// Папка раздачи новых версий. По умолчанию — соседняя с рабочей папкой
        /// (Y:\STAKANY\..\Soft\CupsForge\release). На домашней машине такой папки
        /// нет, и проверка просто ничего не находит.
        /// Пустая строка — не проверять обновления вовсе.
        /// </summary>
        [JsonPropertyName("updateSource")]
        public string UpdateSource { get; set; } = @"{base}\..\Soft\CupsForge\release";

        /// <summary>
        /// Опись раздачи — единственный адрес, по которому программа ходит в сеть
        /// за обновлениями. Раздача открыта на чтение, ключей не требует.
        /// Пустая строка — не проверять обновления из интернета.
        /// </summary>
        [JsonPropertyName("distributionUrl")]
        public string DistributionUrl { get; set; } =
            "https://raw.githubusercontent.com/Cups-bot/CupsForge-public/main/manifest.json";

        /// <summary>Где лежат сами файлы раздачи (вложения выпуска).</summary>
        [JsonPropertyName("distributionAssets")]
        public string DistributionAssets { get; set; } =
            "https://github.com/Cups-bot/CupsForge-public/releases/download/dist";

        /// <summary>
        /// Обновлять шаблоны молча. Выключено до первого согласия: первая загрузка
        /// весит сотни мегабайт, и тянуть их без спроса на домашнем интернете нельзя.
        /// </summary>
        [JsonPropertyName("autoSyncTemplates")]
        public bool AutoSyncTemplates { get; set; }

        /// <summary>
        /// Доступ к Bitrix. Живёт здесь, а не в appsettings.json рядом с exe:
        /// appsettings лежит в git-репозитории, а это учётные данные к API,
        /// доступному из интернета.
        /// </summary>
        [JsonPropertyName("bitrix")]
        public BitrixAccess Bitrix { get; set; } = new();

        /// <summary>Развёрнута ли панель «Проверка данных» (запоминается между запусками).</summary>
        [JsonPropertyName("specPanelExpanded")]
        public bool SpecPanelExpanded { get; set; } = true;

        /// <summary>
        /// Разрешено ли сворачивать программу к правому краю экрана.
        ///
        /// Окно уезжает за край и прячется целиком, а у края остаётся узкий
        /// язычок ПОВЕРХ ВСЕХ ОКОН. Дизайнер работает в Illustrator весь день,
        /// а CupsForge нужен на минуту в начале каждого заказа: в панели задач
        /// его надо искать среди двадцати значков, у края экрана он всегда
        /// на одном месте и в одном щелчке.
        ///
        /// Включено по умолчанию. Кому мешает полоска поверх всех окон —
        /// выключает и пользуется обычным сворачиванием.
        /// </summary>
        [JsonPropertyName("sideDrawer")]
        public bool SideDrawer { get; set; } = true;

        /// <summary>
        /// Где по вертикали стоит язычок, если его перетаскивали. null — не
        /// трогали, и он встаёт по высоте окна.
        ///
        /// Хранится в профиле, а не в памяти: человек двигает язычок один раз,
        /// подальше от того, что у него на экране, и ожидает найти его там же
        /// завтра. Панелью настроек не правится — пишется прямо при перетаскивании.
        /// </summary>
        [JsonPropertyName("sideDrawerTop")]
        public double? SideDrawerTop { get; set; }

        // ---------- расположение файла ----------

        private static string? _storageOverride;

        public static string DirectoryPath => _storageOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CupsForge");

        public static string FilePath => Path.Combine(DirectoryPath, "profile.json");

        /// <summary>
        /// Увести хранение профиля в другую папку. Нужно ровно одному месту —
        /// самопроверке: она открывает настоящее окно, а окно при нажатии на
        /// панель сохраняет профиль на диск. Без подмены папки прогон затирал
        /// профиль рабочего места путями во временную папку, которую сам же
        /// потом и удалял. null — вернуть на место.
        /// </summary>
        public static void RedirectStorage(string? directory) => _storageOverride = directory;

        // ---------- загрузка / сохранение ----------

        private static MachineProfile? _current;

        /// <summary>
        /// Профиль этой машины. При первом обращении читается с диска; если файла нет —
        /// подставляются офисные пути, чтобы поведение на рабочих местах не изменилось.
        /// </summary>
        public static MachineProfile Current => _current ??= Load();

        /// <summary>Был ли профиль прочитан с диска (false — значит первый запуск).</summary>
        public static bool ExistsOnDisk => File.Exists(FilePath);

        public static void Reload() => _current = null;

        /// <summary>Подменить профиль в памяти (мастер настройки, тесты).</summary>
        public static void Set(MachineProfile profile) => _current = profile;

        public static MachineProfile Load()
        {
            if (!File.Exists(FilePath))
                return CreateOfficeDefault();

            try
            {
                var opts = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };
                var loaded = JsonSerializer.Deserialize<MachineProfile>(File.ReadAllText(FilePath), opts);
                if (loaded == null)
                    return CreateOfficeDefault();

                loaded.FillMissingRoots();
                return loaded;
            }
            catch (Exception ex)
            {
                // Битый профиль не должен мешать работать. Но и молча подменять его
                // офисными путями нельзя: человек увидит чужие Y:\ и не поймёт, откуда.
                // Сохраняем копию и оставляем след.
                LoadProblem = $"Профиль повреждён ({ex.Message}). Подставлены стандартные пути.";
                try
                {
                    string backup = FilePath + ".broken";
                    File.Copy(FilePath, backup, overwrite: true);
                    LoadProblem += $" Копия прежнего файла: {backup}";
                }
                catch { /* не смогли сохранить копию — не повод падать */ }

                return CreateOfficeDefault();
            }
        }

        /// <summary>
        /// Чем закончилась последняя загрузка, если что-то пошло не так.
        /// null — профиль прочитан нормально.
        /// </summary>
        public static string? LoadProblem { get; private set; }

        /// <summary>
        /// Независимая копия — окно настроек правит её, и «Отмена» ничего не ломает.
        /// </summary>
        public MachineProfile Clone()
        {
            var copy = new MachineProfile
            {
                Version = Version,
                Mode = Mode,
                IllustratorExe = IllustratorExe,
                JsxScript = JsxScript,
                CatalogPath = CatalogPath,
                UpdateSource = UpdateSource,
                DistributionUrl = DistributionUrl,
                DistributionAssets = DistributionAssets,
                AutoSyncTemplates = AutoSyncTemplates,
                SpecPanelExpanded = SpecPanelExpanded,
                SideDrawer = SideDrawer,
                SideDrawerTop = SideDrawerTop,
                Bitrix = new BitrixAccess
                {
                    AuthorizationHeader = Bitrix.AuthorizationHeader,
                    Login = Bitrix.Login,
                    Password = Bitrix.Password
                }
            };
            foreach (var pair in Roots)
                copy.Roots[pair.Key] = pair.Value;
            return copy;
        }

        /// <summary>
        /// Переносит в <paramref name="target"/> только то, чем распоряжается окно
        /// настроек. Остальное остаётся как есть.
        ///
        /// Окно правит копию, снятую при открытии, — так «Отмена» действительно
        /// ничего не меняет. Но сохранять эту копию целиком нельзя: пока окно
        /// открыто, программа могла записать в профиль что-то своё, и сохранение
        /// откатывало это назад. Пример из жизни: загрузка шаблонов ставит
        /// autoSyncTemplates, дизайнер в это время жмёт «Сохранить» — и на
        /// следующем запуске его снова спрашивают, качать ли сотни мегабайт.
        /// </summary>
        public void ApplyEditableTo(MachineProfile target)
        {
            target.Roots = new Dictionary<string, string>(Roots, StringComparer.OrdinalIgnoreCase);
            target.Mode = Mode;
            target.IllustratorExe = IllustratorExe;
            target.SideDrawer = SideDrawer;
            target.Bitrix = new BitrixAccess
            {
                AuthorizationHeader = Bitrix.AuthorizationHeader,
                Login = Bitrix.Login,
                Password = Bitrix.Password
            };
        }

        /// <summary>
        /// Сохраняет профиль. Пишем во временный файл и подменяем готовый:
        /// оборванная посреди записи запись оставила бы обрезанный JSON,
        /// а это потеря всех настроек рабочего места.
        /// </summary>
        public void Save()
        {
            Directory.CreateDirectory(DirectoryPath);
            var opts = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, opts));

            if (File.Exists(FilePath))
                File.Replace(temp, FilePath, destinationBackupFileName: null);
            else
                File.Move(temp, FilePath);
        }

        // ---------- конструирование ----------

        /// <summary>Офисный профиль — ровно те пути, что были зашиты в коде до рефакторинга.</summary>
        public static MachineProfile CreateOfficeDefault() => FromStakanyRoot(OfficeStakanyRoot, "office");

        /// <summary>
        /// Строит все корни от одной папки STAKANY по офисному скелету.
        /// Это кнопка «структура как в офисе» в мастере настройки.
        /// </summary>
        public static MachineProfile FromStakanyRoot(string stakanyRoot, string mode = "remote")
        {
            var profile = new MachineProfile { Mode = mode };
            profile.ApplyStakanyRoot(stakanyRoot);
            return profile;
        }

        /// <summary>Переписывает все корни от указанной папки STAKANY.</summary>
        public void ApplyStakanyRoot(string stakanyRoot)
        {
            stakanyRoot = (stakanyRoot ?? "").TrimEnd('\\', '/');
            foreach (string key in Root.AllIncludingBase)
                Roots[key] = Path.Combine(stakanyRoot, Root.RelativeToStakany(key));
        }

        /// <summary>
        /// Достраивает недостающие корни от базовой папки ЭТОГО профиля.
        /// Раньше они подставлялись офисными — и на домашней машине в профиле
        /// внезапно появлялись пути к сетевому диску, которого там нет.
        /// </summary>
        public void FillMissingRoots()
        {
            Roots ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string baseFolder = Roots.TryGetValue(Root.Base, out string? b) && !string.IsNullOrWhiteSpace(b)
                ? b
                : GuessBaseFromRoots() ?? OfficeStakanyRoot;

            foreach (string key in Root.AllIncludingBase)
            {
                if (!Roots.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value))
                    Roots[key] = Path.Combine(baseFolder, Root.RelativeToStakany(key));
            }
        }

        /// <summary>Базовая папка по любому заполненному корню, если сам base потерялся.</summary>
        private string? GuessBaseFromRoots()
        {
            foreach (string key in Root.All)
            {
                if (!Roots.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value))
                    continue;

                string relative = Root.RelativeToStakany(key);
                if (string.IsNullOrEmpty(relative))
                    continue;

                // Корень заканчивается своим относительным путём — отрезаем его.
                string trimmed = value.TrimEnd('\\', '/');
                if (trimmed.EndsWith(relative, StringComparison.OrdinalIgnoreCase))
                    return trimmed[..^relative.Length].TrimEnd('\\', '/');
            }
            return null;
        }

        // ---------- проверка ----------

        /// <summary>
        /// Корни, которых физически нет на этой машине. Пустой список — всё на месте.
        /// Используется мастером настройки и кнопкой «Проверить настройки».
        /// </summary>
        public List<string> FindMissingRoots()
        {
            var missing = new List<string>();
            // Базовая папка тоже проверяется: на неё ссылается каталог ("{base}/Lids"),
            // и её отсутствие ломает продукты со своей папкой вывода.
            foreach (string key in Root.AllIncludingBase)
            {
                string path = Roots.TryGetValue(key, out string? v) ? v : "";
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                    missing.Add(key);
            }
            return missing;
        }

        /// <summary>Создаёт недостающие папки по офисному скелету (кнопка «Создать структуру»).</summary>
        public void CreateMissingRoots()
        {
            foreach (string key in Root.All)
            {
                if (Roots.TryGetValue(key, out string? path) && !string.IsNullOrWhiteSpace(path))
                    Directory.CreateDirectory(path);
            }
        }
    }

    /// <summary>Учётные данные Bitrix, хранящиеся в профиле пользователя, а не в репозитории.</summary>
    public sealed class BitrixAccess
    {
        /// <summary>
        /// Что сказать, когда доступа нет.
        ///
        /// Живёт рядом с самим доступом, а не в окне: раньше текст ссылался на
        /// appsettings.json, которого на новой машине не существует, потом —
        /// на «логин и пароль», которых в настройках тоже нет (окно принимает
        /// готовый ключ и логин с паролем намеренно затирает). Человека дважды
        /// посылали искать поле, которого нет.
        /// </summary>
        public const string NotConfiguredMessage =
            "Не задан доступ к Bitrix. Откройте настройки (шестерёнка) и вставьте " +
            "ключ доступа в разделе «ДОСТУП К BITRIX». Ручной ввод работает и без него.";

        [JsonPropertyName("authorizationHeader")]
        public string AuthorizationHeader { get; set; } = "";

        [JsonPropertyName("login")]
        public string Login { get; set; } = "";

        [JsonPropertyName("password")]
        public string Password { get; set; } = "";

        [JsonIgnore]
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(AuthorizationHeader) || !string.IsNullOrWhiteSpace(Login);
    }
}
