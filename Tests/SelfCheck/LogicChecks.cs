using System.IO;
using System.Linq;
using CupsCore;

namespace SelfCheck;

/// <summary>
/// Сверка поведения с тем, что программа делала до рефакторинга: пути шаблонов,
/// корни вывода, выбор шаблона, разбор текста из Bitrix и создание проекта целиком.
///
/// Ожидаемые значения здесь — не «как получилось», а как было до переноса правил
/// в каталог. Если что-то из этого покраснеет, значит поведение поехало.
/// </summary>
public static class LogicChecks
{
    public static void Run(Checker check)
    {
        MachineProfile.Set(MachineProfile.CreateOfficeDefault());
        CatalogService.Reload();

        Layers(check);
        Catalog(check);
        OutputRoots(check);
        Templates(check);
        Articles(check);
        BitrixText(check);
        NewProductWithoutCode(check);
        RemoteProfile(check);
        Regressions(check);
        CatalogSources(check);
        ScriptEncodings(check);
        Distribution(check);
        EndToEnd(check);
        Updates(check);

        MachineProfile.Set(MachineProfile.CreateOfficeDefault());
        Paths.ResetIllustratorCache();
        CatalogService.Reload();
    }

    /// <summary>
    /// Границы слоёв. Ядро не должно знать про окна.
    ///
    /// Это единственный инвариант, который раньше держался только на
    /// договорённости: код лежал в папке, которую приложение втягивало в свою
    /// сборку, и ничто не мешало написать в нём using System.Windows. К моменту
    /// проверки договорённость была нарушена в четырёх файлах, и никто этого
    /// не замечал.
    ///
    /// Теперь ядро — отдельная сборка без WPF, и нарушение не соберётся.
    /// Проверка сторожит от возврата: кто-нибудь включит UseWPF «на минутку»,
    /// сборка пройдёт, а граница исчезнет молча.
    /// </summary>
    private static void Layers(Checker check)
    {
        check.Section("Границы слоёв");

        var core = typeof(MachineProfile).Assembly;
        check.Equal("ядро — отдельная сборка", core.GetName().Name, "CupsForge.Core");

        var wpf = core.GetReferencedAssemblies()
            .Where(a => a.Name is "PresentationFramework" or "PresentationCore" or "WindowsBase")
            .Select(a => a.Name!)
            .ToList();

        check.True(wpf.Count == 0
                ? "ядро не ссылается на WPF"
                : "ядро не ссылается на WPF — а ссылается на: " + string.Join(", ", wpf),
            wpf.Count == 0);

        // Обратное направление обязано работать: приложение ядро видит.
        check.True("приложение ссылается на ядро",
            typeof(CupsForge.AutoWindow).Assembly.GetReferencedAssemblies()
                .Any(a => a.Name == "CupsForge.Core"));
    }

    private static void Catalog(Checker check)
    {
        check.Section("Каталог");
        var catalog = CatalogService.Current;
        check.Info($"источник: {catalog.SourceName}");
        check.Info($"версия {catalog.Version} от {catalog.Updated}, продуктов: {catalog.Products.Count}");

        var errors = catalog.Validate();
        check.True("каталог проходит проверку", errors.Count == 0);
        foreach (string e in errors)
            check.Info(e);
    }

    private static void OutputRoots(Checker check)
    {
        check.Section("Корни вывода (как до переноса правил в каталог)");
        check.Equal("MyCups / Стаканы / Офсет",
            Paths.GetOutputRoot(Brand.MyCups, "Cups", PrintTech.Offset), @"Y:\STAKANY\MyCups");
        check.Equal("MyCups / Стаканы / Цифра",
            Paths.GetOutputRoot(Brand.MyCups, "Cups", PrintTech.Digital), @"Y:\STAKANY\MyCups");
        check.Equal("MyCups / Стаканы / Pantone",
            Paths.GetOutputRoot(Brand.MyCups, "Cups", PrintTech.Pantone), @"Y:\STAKANY\Flexo");
        check.Equal("MyCups / Пластик",
            Paths.GetOutputRoot(Brand.MyCups, "Plastic", PrintTech.Pantone), @"Y:\STAKANY\PadPrint");
        check.Equal("MyCups / Сахар",
            Paths.GetOutputRoot(Brand.MyCups, "Sugar", PrintTech.Pantone), @"Y:\STAKANY\MyCups");
        check.Equal("MyCups / Шоколад",
            Paths.GetOutputRoot(Brand.MyCups, "Choko", PrintTech.Offset), @"Y:\STAKANY\MyCups");
        check.Equal("MyCups / Конфеты",
            Paths.GetOutputRoot(Brand.MyCups, "Candy", PrintTech.Offset), @"Y:\STAKANY\MyCups");
        check.Equal("CupToYou",
            Paths.GetOutputRoot(Brand.CuptoYou, "Cups", PrintTech.Offset), @"Y:\STAKANY\CuptoYou");
        check.Equal("Flexo / Офсет",
            Paths.GetOutputRoot(Brand.Flexo, "Cups", PrintTech.Offset), @"Y:\STAKANY\Offset");
        check.Equal("Flexo / Pantone",
            Paths.GetOutputRoot(Brand.Flexo, "Cups", PrintTech.Pantone), @"Y:\STAKANY\Flexo");
    }

    private static void Template(Checker check, string what, DesignSpec spec, string inArticle,
                                 string expectedPath, string expectedFile, string? expectedArticle = null)
    {
        string article = inArticle;
        var (path, file) = TemplateResolver.ResolveTemplate(spec, ref article);
        check.Equal(what + " → папка", path, expectedPath);
        check.Equal(what + " → файл", file, expectedFile);
        if (expectedArticle != null)
            check.Equal(what + " → артикул", article, expectedArticle);
    }

    private static void Templates(Checker check)
    {
        check.Section("Выбор шаблона");

        Template(check, "MyCups Офсет DW90-430",
            new DesignSpec { Brand = Brand.MyCups, ProductType = "Cups", PrintTech = PrintTech.Offset },
            "DW90-430", @"Y:\STAKANY\_Templates\2_Offset", "_DW90-430-0000 MC_work.ai");

        Template(check, "MyCups Pantone HB80-280",
            new DesignSpec { Brand = Brand.MyCups, ProductType = "Cups", PrintTech = PrintTech.Pantone },
            "HB80-280", @"Y:\STAKANY\_Templates\1_Flexo\MyCups", "_HB80-280-0000.ai");

        Template(check, "CupToYou TR DW90-430",
            new DesignSpec { Brand = Brand.CuptoYou, Country = Country.TR },
            "DW90-430", @"Y:\STAKANY\_Templates\3_CupsToYou_Tr", "_DW90-430-0000 MC_tr.ai");

        Template(check, "CupToYou IT EM90-530",
            new DesignSpec { Brand = Brand.CuptoYou, Country = Country.IT },
            "EM90-530", @"Y:\STAKANY\_Templates\6_CupsToYou_It", "_EM90-530-0000 MC_it.ai");

        Template(check, "Flexo Офсет DW90-430",
            new DesignSpec { Brand = Brand.Flexo, ProductType = "Cups", PrintTech = PrintTech.Offset },
            "DW90-430", @"Y:\STAKANY\_Templates\2_Offset", "_DW90-430-0000 MC_work.ai");

        Template(check, "Flexo Pantone HB90-530",
            new DesignSpec { Brand = Brand.Flexo, ProductType = "Cups", PrintTech = PrintTech.Pantone },
            "HB90-530", @"Y:\STAKANY\_Templates\1_Flexo", "_HB90-530-0000.ai");

        Template(check, "Шоколад Dark",
            new DesignSpec { Brand = Brand.MyCups, ProductType = "Choko", Variant = "Dark" },
            "CHOKO", @"Y:\STAKANY\_Templates\8_Choko", "CHOKO_Dark.ai");

        Template(check, "Шоколад без вкуса → Milk",
            new DesignSpec { Brand = Brand.MyCups, ProductType = "Choko" },
            "CHOKO", @"Y:\STAKANY\_Templates\8_Choko", "CHOKO_Milk.ai");

        Template(check, "Пластик BM90-500",
            new DesignSpec { Brand = Brand.MyCups, ProductType = "Plastic" },
            "BM90-500", @"Y:\STAKANY\_Templates\7_Plastic", "BM90-500_work.ai");

        Template(check, "Сахар SUGAR-ST",
            new DesignSpec { Brand = Brand.MyCups, ProductType = "Sugar" },
            "SUGAR-ST", @"Y:\STAKANY\_Templates\9_Sugar", "SUGAR-ST.ai");

        // Ручной ввод даёт артикулом последнее слово («D») — совпадения нет,
        // подставляется вид конфет вместе с правильным артикулом.
        Template(check, "Конфеты Dubai (артикул из имени)",
            new DesignSpec { Brand = Brand.MyCups, ProductType = "Candy", Variant = "Dubai" },
            "D", @"Y:\STAKANY\_Templates\10_Candy", "SWEET_D.ai", "SWEET D");

        Template(check, "Конфеты по артикулу из Bitrix",
            new DesignSpec { Brand = Brand.MyCups, ProductType = "Candy", Variant = "Assorted" },
            "SWEET AS", @"Y:\STAKANY\_Templates\10_Candy", "SWEET_AS.ai", "SWEET AS");
    }

    private static void Articles(Checker check)
    {
        check.Section("Артикул из имени дизайна");

        string Article(Brand brand, string type, PrintTech tech, string name) =>
            CatalogService.Current
                .Require(new DesignSpec { Brand = brand, ProductType = type, PrintTech = tech })
                .ExtractArticle(name);

        check.Equal("стаканы: последние 8 символов",
            Article(Brand.MyCups, "Cups", PrintTech.Offset, "132583 CarBar ST DW90-430"), "DW90-430");
        check.Equal("пластик: последнее слово",
            Article(Brand.MyCups, "Plastic", PrintTech.Pantone, "12345 Some Name BM90-410-V"), "BM90-410-V");
        check.Equal("сахар: последнее слово",
            Article(Brand.MyCups, "Sugar", PrintTech.Pantone, "777 Cafe SUGAR-ST-P"), "SUGAR-ST-P");
        check.Equal("CupToYou: последние 8 символов",
            Article(Brand.CuptoYou, "Cups", PrintTech.Offset, "900 Kahve EM90-430"), "EM90-430");
    }

    private static void BitrixText(Checker check)
    {
        check.Section("Разбор текста из Bitrix");
        var catalog = CatalogService.Current;

        check.Equal("«Бумажный стакан» → Cups",
            catalog.ProductTypeFromText(Brand.MyCups, "Бумажный стакан"), "Cups");
        check.Equal("«Пластик» → Plastic",
            catalog.ProductTypeFromText(Brand.MyCups, "Пластик"), "Plastic");
        check.Equal("«Шоколад молочный» → Choko",
            catalog.ProductTypeFromText(Brand.MyCups, "Шоколад молочный"), "Choko");
        check.Equal("«Конфеты» → Candy",
            catalog.ProductTypeFromText(Brand.MyCups, "Конфеты"), "Candy");
        check.True("незнакомый тип не распознаётся",
            catalog.ProductTypeFromText(Brand.MyCups, "Нечто неизвестное") == null);

        // Заказ 1879395 «132736 Арчи PG95-400»: тип уходил в стаканы, потому что
        // «Пластиковый стакан» содержит и «пластик», и «стакан», а побеждало первое
        // по порядку. Теперь побеждает самое длинное совпадение.
        check.Equal("«Пластиковый стакан» → Plastic, а не Cups",
            catalog.ProductTypeFromText(Brand.MyCups, "Пластиковый стакан"), "Plastic");
        check.Equal("«Бумажный стакан» по-прежнему Cups",
            catalog.ProductTypeFromText(Brand.MyCups, "Бумажный стакан"), "Cups");

        // Способ печати переехал в каталог: «Тампопечать» раньше не распознавалась.
        var warnings = new List<string>();
        check.Equal("«Тампопечать» распознаётся",
            CupsForge.Services.BitrixMapper.MapPrintTech("Тампопечать", warnings).ToString(), "Pantone");
        check.True("и не выдаёт предупреждения", warnings.Count == 0);
        check.Equal("«Офсет» по-прежнему Offset",
            CupsForge.Services.BitrixMapper.MapPrintTech("Офсет", warnings).ToString(), "Offset");
        check.Equal("«Цифровая печать» по-прежнему Digital",
            CupsForge.Services.BitrixMapper.MapPrintTech("Цифровая печать", warnings).ToString(), "Digital");

        // «немелованный» содержит «мелован» — без правила самого длинного слова
        // материал определялся бы наоборот.
        check.Equal("«Белый мелованный» → Coated",
            CupsForge.Services.BitrixMapper.MapMaterial("Белый мелованный", warnings).ToString(), "Coated");
        check.Equal("«Немелованный» → Uncoated",
            CupsForge.Services.BitrixMapper.MapMaterial("Немелованный", warnings).ToString(), "Uncoated");

        check.Equal("направление «mycups» → MyCups",
            CupsForge.Services.BitrixMapper.MapBrand("mycups", warnings).ToString(), "MyCups");
        check.Equal("страна «Turkish» → TR",
            CupsForge.Services.BitrixMapper.MapCountry("Turkish").ToString(), "TR");
        check.Equal("страна «Italian» → IT",
            CupsForge.Services.BitrixMapper.MapCountry("Italian").ToString(), "IT");

        var choko = catalog.Require(new DesignSpec { Brand = Brand.MyCups, ProductType = "Choko" });
        check.Equal("вкус «Dark» → Dark", CupsCore.Catalog.VariantFromText(choko, "Dark"), "Dark");
        check.Equal("вкус «тёмный» → Dark", CupsCore.Catalog.VariantFromText(choko, "тёмный шоколад"), "Dark");
        check.Equal("вкус «клубника» → Strawberry", CupsCore.Catalog.VariantFromText(choko, "клубника"), "Strawberry");
        check.Equal("вкус пустой → Milk", CupsCore.Catalog.VariantFromText(choko, ""), "Milk");

        var candy = catalog.Require(new DesignSpec { Brand = Brand.MyCups, ProductType = "Candy" });
        check.Equal("конфеты «Dubai» → Dubai", CupsCore.Catalog.VariantFromText(candy, "Dubai style"), "Dubai");
        check.Equal("конфеты «SWEET D» → Dubai", CupsCore.Catalog.VariantFromText(candy, " SWEET D"), "Dubai");
        check.Equal("конфеты пустые → Assorted", CupsCore.Catalog.VariantFromText(candy, ""), "Assorted");
    }

    /// <summary>
    /// Главная проверка перехода на каталог: продукт, которого нет в коде,
    /// должен полностью заработать после правки одного JSON.
    /// </summary>
    private static void NewProductWithoutCode(Checker check)
    {
        check.Section("Новый продукт только правкой каталога");

        const string extended = """
        {
          "version": 99,
          "updated": "тестовый",
          "articleTables": {
            "lids": { "LID-80": "LID-80_work.ai", "LID-90": "LID-90_work.ai" }
          },
          "products": [
            {
              "id": "mycups-lids",
              "title": "MyCups — крышки картонно-алюминиевые",
              "typeTitle": "Крышки",
              "brand": "MyCups",
              "productType": "Lids",
              "typeKeywords": ["крышк"],
              "output": "{base}/Lids",
              "naming": "plain",
              "argsTech": "offset",
              "articleFrom": "lastWord",
              "templateFolder": "{templates}/11_Lids",
              "articles": "lids"
            }
          ]
        }
        """;

        var catalog = CatalogService.Parse(extended)!;
        catalog.SourceName = "тестовый каталог";
        CatalogService.Set(catalog);

        check.True("расширенный каталог проходит проверку", catalog.Validate().Count == 0);

        var spec = new DesignSpec { Brand = Brand.MyCups, ProductType = "Lids", PrintTech = PrintTech.Offset };
        check.Equal("крышки → папка вывода", Paths.GetOutputRoot(spec), @"Y:\STAKANY\Lids");

        string article = "LID-90";
        var (path, file) = TemplateResolver.ResolveTemplate(spec, ref article);
        check.Equal("крышки → папка шаблонов", path, @"Y:\STAKANY\_Templates\11_Lids");
        check.Equal("крышки → файл шаблона", file, "LID-90_work.ai");
        check.Equal("крышки → тип из текста Bitrix",
            catalog.ProductTypeFromText(Brand.MyCups, "Крышка картонно-алюминиевая"), "Lids");
        check.Equal("крышки → артикул из имени",
            catalog.Require(spec).ExtractArticle("55123 Coffee House LID-90"), "LID-90");
        check.Equal("крышки → название типа в списке ручного ввода",
            catalog.ProductTypesOf(Brand.MyCups).FirstOrDefault().Title, "Крышки");

        CatalogService.Reload();
    }

    private static void RemoteProfile(Checker check)
    {
        check.Section("Профиль удалённой машины");
        MachineProfile.Set(MachineProfile.FromStakanyRoot(@"D:\Work\STAKANY"));

        check.Equal("вывод MyCups",
            Paths.GetOutputRoot(Brand.MyCups, "Cups", PrintTech.Offset), @"D:\Work\STAKANY\MyCups");
        check.Equal("вывод Pantone",
            Paths.GetOutputRoot(Brand.Flexo, "Cups", PrintTech.Pantone), @"D:\Work\STAKANY\Flexo");
        check.Equal("JSX-скрипт", Paths.JsxScriptPath,
            @"D:\Work\STAKANY\_Templates\Scripts\0_DW_Start.jsx");

        string article = "DW90-430";
        var (path, _) = TemplateResolver.ResolveTemplate(
            new DesignSpec { Brand = Brand.MyCups, ProductType = "Cups", PrintTech = PrintTech.Offset },
            ref article);
        check.Equal("папка шаблонов", path, @"D:\Work\STAKANY\_Templates\2_Offset");
    }

    /// <summary>
    /// Создаём проекты по-настоящему, в песочнице, и сверяем результат посимвольно:
    /// имя папки, наличие .ai и содержимое args.txt.
    /// </summary>
    private static void EndToEnd(Checker check)
    {
        check.Section("Создание проекта целиком (песочница)");

        string sandbox = Path.Combine(Path.GetTempPath(), "cupsforge_selfcheck");
        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true);
        string stakany = Path.Combine(sandbox, "STAKANY");

        var profile = MachineProfile.FromStakanyRoot(stakany);
        // Заведомо несуществующий путь: настоящий Illustrator запускаться не должен.
        profile.IllustratorExe = Path.Combine(sandbox, "no-illustrator.exe");
        MachineProfile.Set(profile);
        Paths.ResetIllustratorCache();
        CatalogService.Reload();

        // Прогон не имеет права трогать Illustrator на машине разработчика.
        // Раньше указанный выше несуществующий путь молча заменялся найденным
        // автопоиском, и самопроверка открывала настоящий Illustrator с файлом-
        // заглушкой из песочницы. Он показывал «ID: -54» и ждал нажатия «ОК» —
        // прогон стоял, пока человек не подойдёт к чужому окну.
        check.True("настоящий Illustrator в песочнице не подбирается", !Paths.IllustratorFound);

        CreateFakeTemplates();

        void Build(string what, BuildRequest request, string expectedFolder, params string[] expectedArgs)
        {
            BuildResult r = ProjectBuilder.Build(request);
            if (!r.Success)
            {
                check.Fail($"{what}: проект не создан — {string.Join("; ", r.Log)}");
                return;
            }

            // Прямое доказательство, что чужой Illustrator не трогали: косвенной
            // проверки «путь не подобрался» мало — запуск идёт другой веткой.
            check.True(what + " → Illustrator не запускался", !r.IllustratorLaunched);

            check.Equal(what + " → имя папки", Path.GetFileName(r.ProjectPath), expectedFolder);
            check.True(what + " → .ai на месте",
                File.Exists(Path.Combine(r.ProjectPath, expectedFolder + ".ai")));

            string argsPath = Path.Combine(r.ProjectPath, "In", "args.txt");
            if (!File.Exists(argsPath))
            {
                check.Fail(what + ": нет args.txt");
                return;
            }
            check.Equal(what + " → args.txt",
                string.Join(" | ", File.ReadAllLines(argsPath)),
                string.Join(" | ", expectedArgs));
        }

        Build("MyCups офсет, немелованный",
            new BuildRequest
            {
                DesignCode = "132583 CarBar ST DW90-430",
                Spec = new DesignSpec { Brand = Brand.MyCups, ProductType = "Cups", PrintTech = PrintTech.Offset },
                Material = Material.Uncoated
            },
            "132583 CarBar ST DW90-430",
            "132583 CarBar ST DW90-430", "DW90-430", "offset", "uncoated");

        Build("MyCups цифра, мелованный + Soft Touch",
            new BuildRequest
            {
                DesignCode = "999001 Kofeinya XX EM90-530",
                Spec = new DesignSpec { Brand = Brand.MyCups, ProductType = "Cups", PrintTech = PrintTech.Digital },
                Material = Material.Coated,
                Coating = Coating.SoftTouch
            },
            "999001 Kofeinya XX EM90-530",
            "999001 Kofeinya XX EM90-530", "EM90-530", "digital", "coated", "coating:soft_touch");

        Build("MyCups Pantone — имя папки по правилу Pantone",
            new BuildRequest
            {
                DesignCode = "440022 Some Coffee HB80-280",
                Spec = new DesignSpec { Brand = Brand.MyCups, ProductType = "Cups", PrintTech = PrintTech.Pantone },
                Material = Material.Uncoated
            },
            "HB80-280-440022_Some Coffee",
            "440022 Some Coffee HB80-280", "HB80-280", "pantone", "uncoated");

        Build("Пластик — способ печати подменяется каталогом",
            new BuildRequest
            {
                DesignCode = "5511 Bar Nasosnaya BM90-500",
                Spec = new DesignSpec { Brand = Brand.MyCups, ProductType = "Plastic", PrintTech = PrintTech.Offset },
                Material = Material.Uncoated
            },
            "5511 Bar Nasosnaya BM90-500",
            "5511 Bar Nasosnaya BM90-500", "BM90-500", "pantone", "uncoated");

        Build("Шоколад Dark — покрытие не пишется",
            new BuildRequest
            {
                DesignCode = "7788 Shoko Bar CHOKO",
                Spec = new DesignSpec { Brand = Brand.MyCups, ProductType = "Choko", Variant = "Dark" },
                Material = Material.Coated,
                Coating = Coating.SoftTouch
            },
            "7788 Shoko Bar CHOKO",
            "7788 Shoko Bar CHOKO", "CHOKO", "offset", "coated");

        Build("Конфеты Dubai — артикул подставляется по виду",
            new BuildRequest
            {
                DesignCode = "3344 Sweet Shop D",
                Spec = new DesignSpec { Brand = Brand.MyCups, ProductType = "Candy", Variant = "Dubai" },
                Material = Material.Uncoated
            },
            "3344 Sweet Shop D",
            "3344 Sweet Shop D", "SWEET D", "offset", "uncoated");

        Build("CupToYou TR — страна последней строкой",
            new BuildRequest
            {
                DesignCode = "8080 Kahve Duragi DW90-430",
                Spec = new DesignSpec { Brand = Brand.CuptoYou, Country = Country.TR },
                Material = Material.Uncoated
            },
            "8080 Kahve Duragi DW90-430",
            "8080 Kahve Duragi DW90-430", "DW90-430", "offset", "uncoated", "TR");

        check.True("пластик ушёл в PadPrint",
            Directory.Exists(Path.Combine(stakany, "PadPrint", "5511 Bar Nasosnaya BM90-500")));
        check.True("Pantone ушёл во Flexo",
            Directory.Exists(Path.Combine(stakany, "Flexo", "HB80-280-440022_Some Coffee")));
        check.True("CupToYou ушёл в свой корень",
            Directory.Exists(Path.Combine(stakany, "CuptoYou", "8080 Kahve Duragi DW90-430")));

        Directory.Delete(sandbox, true);
    }

    /// <summary>
    /// Обновления.
    ///
    /// Скачивание и подмену файлов делает Velopack — их не гоняем: для этого
    /// нужна настоящая раздача, а применение перезапускает программу. Проверяем
    /// всё, что решаем МЫ: какие каналы, в каком порядке, и что программа
    /// говорит, когда обновляться нельзя.
    ///
    /// Часть прежних проверок отсюда ушла вместе с механизмом, который они
    /// стерегли: сравнение версий, чтение latest.json, разбор следа
    /// .cmd-скрипта и запрет писать на раздачу. Всё это теперь внутри Velopack.
    /// </summary>
    private static void Updates(Checker check)
    {
        check.Section("Обновления");

        // Раздача лежит рядом с рабочей папкой: Y:\STAKANY\..\Soft → Y:\Soft.
        MachineProfile.Set(MachineProfile.CreateOfficeDefault());
        check.Equal("путь раздачи схлопывает «..»",
            PathResolver.Expand(MachineProfile.Current.UpdateSource),
            System.IO.Path.Combine(@"Y:\Soft\CupsForge", "release"));

        // Каналов два, и порядок не случаен: сетевой диск быстр и работает
        // без интернета, раздача — единственное, что достаёт до дома.
        string sandbox = Path.Combine(Path.GetTempPath(), "cupsforge_selfcheck_upd");
        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true);

        var office = MachineProfile.FromStakanyRoot(Path.Combine(sandbox, "STAKANY"));
        office.UpdateSource = Path.Combine(sandbox, "release");
        Directory.CreateDirectory(office.UpdateSource);

        var both = AppUpdates.DescribeSources(office);
        check.True($"каналов обновления два ({both.Count})", both.Count == 2);
        check.True("сетевой диск проверяется первым",
            both.Count > 0 && both[0].StartsWith("сетевой диск", StringComparison.Ordinal));
        check.True("раздача проверяется второй",
            both.Count > 1 && both[1].StartsWith("раздача", StringComparison.Ordinal));

        // Дома сетевого диска нет — остаётся один канал, и это НЕ ошибка.
        var home = MachineProfile.FromStakanyRoot(Path.Combine(sandbox, "нет-такой"));
        home.UpdateSource = Path.Combine(sandbox, "тоже-нет");
        var homeOnly = AppUpdates.DescribeSources(home);
        check.True($"без сетевого диска остаётся раздача ({homeOnly.Count})", homeOnly.Count == 1);
        check.True("и это именно раздача",
            homeOnly.Count > 0 && homeOnly[0].StartsWith("раздача", StringComparison.Ordinal));

        // Отключённая раздача убирает и второй канал: пустая строка означает
        // «не ходить в интернет вовсе».
        var offline = home.Clone();
        offline.UpdateRepo = "";
        check.True("пустой адрес раздачи выключает канал",
            AppUpdates.DescribeSources(offline).Count == 0);

        // Портативный запуск обновлять нельзя, и программа обязана сказать
        // почему. Прогон идёт именно так — из папки сборки, не из установки.
        check.True("портативный запуск не считается установкой", !AppUpdates.IsInstalled);
        string? cannot = AppUpdates.Unavailable();
        check.True("отказ обновления объяснён", !string.IsNullOrWhiteSpace(cannot));
        check.True("отказ ведёт к установщику",
            (cannot ?? "").Contains("Setup.exe", StringComparison.OrdinalIgnoreCase));

        check.True($"своя версия читается ({AppUpdates.CurrentVersion})",
            System.Version.TryParse(AppUpdates.CurrentVersion, out _));

        Directory.Delete(sandbox, true);
        MachineProfile.Set(MachineProfile.CreateOfficeDefault());
    }

    /// <summary>
    /// Поведение, которое чинили после разбора. Каждая проверка соответствует
    /// найденному дефекту — чтобы он не вернулся незамеченным.
    /// </summary>
    private static void Regressions(Checker check)
    {
        check.Section("Починенные дефекты");

        string sandbox = Path.Combine(Path.GetTempPath(), "cupsforge_selfcheck_reg");
        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true);
        Directory.CreateDirectory(sandbox);

        // Потерянный корень достраивается от СВОЕЙ базовой папки, а не от офисной:
        // раньше на домашней машине в профиле появлялись пути к сетевому диску.
        var profile = MachineProfile.FromStakanyRoot(@"D:\Work\STAKANY");
        profile.Roots.Remove(MachineProfile.Root.Flexo);
        profile.FillMissingRoots();
        check.Equal("потерянный корень достраивается от своей папки, не от офисной",
            profile.Roots[MachineProfile.Root.Flexo], @"D:\Work\STAKANY\Flexo");

        // То же, когда потерялась и сама базовая папка — достраиваем по любому уцелевшему корню.
        var noBaseProfile = MachineProfile.FromStakanyRoot(@"E:\Design\STAKANY");
        noBaseProfile.Roots.Remove(MachineProfile.Root.Base);
        noBaseProfile.Roots.Remove(MachineProfile.Root.PadPrint);
        noBaseProfile.FillMissingRoots();
        check.Equal("базовая папка восстанавливается по уцелевшему корню",
            noBaseProfile.Roots[MachineProfile.Root.PadPrint], @"E:\Design\STAKANY\PadPrint");

        // Базовая папка участвует в проверке: на неё ссылается каталог ("{base}/Lids").
        var noBase = MachineProfile.FromStakanyRoot(Path.Combine(sandbox, "нет-такой"));
        check.True("отсутствие базовой папки замечается",
            noBase.FindMissingRoots().Contains(MachineProfile.Root.Base));

        // Профиль сохраняется атомарно и переживает перезапись.
        // Доступ к Bitrix — готовый ключ, а не логин с паролем.
        var saved = MachineProfile.FromStakanyRoot(@"D:\Work\STAKANY");
        saved.Bitrix.AuthorizationHeader = "dGVzdC1rZXk=";
        var back = System.Text.Json.JsonSerializer.Deserialize<MachineProfile>(
            System.Text.Json.JsonSerializer.Serialize(saved))!;
        check.Equal("ключ доступа переживает сохранение",
            back.Bitrix.AuthorizationHeader, "dGVzdC1rZXk=");
        check.True("заполненный ключ считается настроенным доступом", back.Bitrix.IsConfigured);
        check.True("пустой доступ настроенным не считается",
            !new MachineProfile().Bitrix.IsConfigured);

        // Сообщение об отсутствии доступа обязано вести туда, где поле есть.
        // Оно уже дважды вело не туда: сперва в appsettings.json, которого на
        // новой машине нет, потом в «логин и пароль», которых нет в настройках.
        string noAccess = BitrixAccess.NotConfiguredMessage;
        check.True("отказ по Bitrix не зовёт в appsettings.json",
            !noAccess.Contains("appsettings", StringComparison.OrdinalIgnoreCase));
        check.True("отказ по Bitrix не зовёт вводить пароль",
            !noAccess.Contains("парол", StringComparison.OrdinalIgnoreCase));
        check.True("отказ по Bitrix называет поле, которое есть в настройках",
            noAccess.Contains("ключ", StringComparison.OrdinalIgnoreCase));

        // Окно настроек правит копию, снятую при открытии, — иначе «Отмена» не
        // работала бы. Но сохранять копию ЦЕЛИКОМ нельзя: пока окно открыто,
        // программа могла записать в профиль своё, и запись клона откатывала это.
        var live = MachineProfile.FromStakanyRoot(@"D:\Work\STAKANY");
        var draft = live.Clone();
        draft.Roots[MachineProfile.Root.Templates] = @"E:\Новое\_Templates";
        draft.IllustratorExe = @"C:\AI\Illustrator.exe";

        // Пока окно было открыто, программа согласилась качать шаблоны сама
        // и запомнила состояние панели.
        live.AutoSyncTemplates = true;
        live.SpecPanelExpanded = false;

        draft.ApplyEditableTo(live);
        check.Equal("сохранение настроек переносит правки",
            live.Roots[MachineProfile.Root.Templates], @"E:\Новое\_Templates");
        check.Equal("сохранение настроек переносит Illustrator",
            live.IllustratorExe, @"C:\AI\Illustrator.exe");
        check.True("сохранение настроек не откатывает согласие на шаблоны",
            live.AutoSyncTemplates);
        check.True("сохранение настроек не откатывает состояние панели",
            !live.SpecPanelExpanded);

        // Устаревший путь к Illustrator не выдаётся за рабочий.
        string ghost = Path.Combine(sandbox, "нет", "Illustrator.exe");
        string? resolved = IllustratorLocator.Resolve(ghost);
        check.True("несуществующий путь к Illustrator отбрасывается", resolved != ghost);

        // ...и не подменяется другой установкой молча. Дизайнер выбирает версию
        // не от скуки: под неё написан JSX-скрипт. Запустить вместо 2022 найденный
        // рядом 2025 — это испортить макет и не сказать об этом ни слова.
        // Прежняя проверка выше этого не ловила: она сравнивала с самим путём,
        // а подмена как раз даёт «что-то другое» и выглядела успехом.
        check.True("исчезнувший Illustrator не подменяется другим молча", resolved == null);

        // Отказ обязан объяснять себя и вести в настройки: «не удалось запустить»
        // без адреса — это ровно та невнятность, из-за которой баг завели.
        bool ok = IllustratorLocator.TryResolve(ghost, out _, out string? why);
        check.True("отказ назван словами", !ok && !string.IsNullOrWhiteSpace(why));
        check.True("в отказе есть пропавший путь", (why ?? "").Contains(ghost, StringComparison.Ordinal));
        check.True("отказ ведёт в настройки", (why ?? "").Contains("настройк", StringComparison.OrdinalIgnoreCase));

        // Защёлка: песочница не должна попадать в настоящий профиль.
        // Сторож в конце прогона ловит это ПОСЛЕ факта — здесь запись просто
        // не происходит. Именно так рабочие настройки однажды и подменились
        // путями во временную папку, а заметили это спустя недели.
        var real = MachineProfile.CreateOfficeDefault();
        MachineProfile.RedirectStorage(null);
        try
        {
            real.Roots[MachineProfile.Root.Templates] =
                Path.Combine(Path.GetTempPath(), "cupsforge_selfcheck_ui", "STAKANY", "_Templates");

            bool refused = false;
            try { real.Save(); }
            catch (InvalidOperationException) { refused = true; }

            check.True("профиль с путём во временной папке сохранить нельзя", refused);
        }
        finally
        {
            // Возвращаем увод: остальной прогон обязан писать в песочницу.
            MachineProfile.RedirectStorage(
                Path.Combine(Path.GetTempPath(), "cupsforge_selfcheck_profile"));
        }

        // Запрет обновлять копию вне папки установки переехал в AppUpdates
        // (проверяется в разделе «Обновления»): его держит Velopack, который
        // сам знает, установлен он или запущен портативно.

        Directory.Delete(sandbox, true);
        MachineProfile.Set(MachineProfile.CreateOfficeDefault());
    }

    /// <summary>
    /// Откуда берётся каталог. Дизайнеры на удалёнке кладут его в папку шаблонов
    /// и ждут, что он подменит вшитую в программу копию. Проверяем, что так и есть.
    /// </summary>
    private static void CatalogSources(Checker check)
    {
        check.Section("Источник каталога");

        string sandbox = Path.Combine(Path.GetTempPath(), "cupsforge_selfcheck_src");
        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true);

        var profile = MachineProfile.FromStakanyRoot(Path.Combine(sandbox, "STAKANY"));
        MachineProfile.Set(profile);

        string templates = PathResolver.Root(MachineProfile.Root.Templates);
        Directory.CreateDirectory(templates);

        // Каталог с заведомо отличимой версией — чтобы понять, какой именно взяли.
        const string marked = """
        {
          "version": 99,
          "updated": "из папки шаблонов",
          "products": [
            {
              "id": "probe", "title": "Проба", "brand": "MyCups",
              "productType": "Probe", "output": "out.mycups",
              "templateFolder": "{templates}/probe",
              "byArticle": { "X": "x.ai" }
            }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(templates, CatalogService.FileName), marked);

        CatalogService.Reload();
        check.Equal("каталог из папки шаблонов побеждает вшитый",
            CatalogService.Current.Version.ToString(), "99");
        check.True("и это видно в источнике",
            CatalogService.Current.SourceName.Contains("шаблон"));

        // Файла нет — работаем на вшитой копии, а не падаем.
        File.Delete(Path.Combine(templates, CatalogService.FileName));
        CatalogService.Reload();
        check.True("без файла берётся копия внутри программы",
            CatalogService.Current.SourceName.Contains("внутри программы"));

        // Битый файл не должен молча подменяться вшитым без следа в журнале.
        File.WriteAllText(Path.Combine(templates, CatalogService.FileName), "{ это не json ");
        CatalogService.Reload();
        check.True("нечитаемый каталог не роняет программу",
            CatalogService.Current.Version > 0);
        check.True("про нечитаемый каталог остаётся запись в журнале",
            CatalogService.LoadLog.Any(l => l.Contains("разобрать не удалось")));
        check.True("и видно, что работаем на вшитой копии", CatalogService.IsUsingEmbedded);
        check.True("путь, где каталог ожидался, известен",
            CatalogService.ExpectedPath.EndsWith(CatalogService.FileName));

        Directory.Delete(sandbox, true);
        MachineProfile.Set(MachineProfile.CreateOfficeDefault());
        CatalogService.Reload();
    }

    /// <summary>
    /// Кодировки скриптов. Windows PowerShell 5.1 читает .ps1 без BOM как ANSI —
    /// кириллица разваливается, и сценарий не запускается вовсе. У .cmd наоборот:
    /// BOM ломает первую строку. Наступали на это дважды, поэтому проверяем.
    /// </summary>
    private static void ScriptEncodings(Checker check)
    {
        check.Section("Кодировки скриптов");

        // Ищем корень репозитория: прогон запускается из своей подпапки.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CupsForge.sln")))
            dir = dir.Parent;

        if (dir == null)
        {
            check.Info("корень репозитория не найден — проверка пропущена");
            return;
        }

        byte[] bom = { 0xEF, 0xBB, 0xBF };

        bool StartsWithBom(string path)
        {
            using var stream = File.OpenRead(path);
            var head = new byte[3];
            return stream.Read(head, 0, 3) == 3 && head.SequenceEqual(bom);
        }

        foreach (string path in Directory.GetFiles(dir.FullName, "*.ps1"))
            check.True($"{Path.GetFileName(path)} — с BOM", StartsWithBom(path));

        foreach (string path in Directory.GetFiles(dir.FullName, "*.cmd"))
        {
            check.True($"{Path.GetFileName(path)} — без BOM", !StartsWithBom(path));

            // cmd.exe читает .cmd в кодировке консоли, а не в UTF-8. Кириллица
            // в комментариях разваливается, и обломки иногда выглядят для cmd
            // как разделитель команд — строка распадается, сыплются «не является
            // внутренней или внешней командой». Поэтому в .cmd только латиница,
            // а всё, что читает человек, печатает .ps1.
            byte[] bytes = File.ReadAllBytes(path);
            int offender = Array.FindIndex(bytes, b => b > 127);
            check.True($"{Path.GetFileName(path)} — только латиница", offender < 0);
            if (offender >= 0)
                check.Info($"первый не-ASCII байт в позиции {offender}");
        }

        // BOM на месте — ещё не значит, что скрипт запустится. Кириллица, прочитанная
        // как ANSI, разваливает разбор, а узнаётся это обычно в самый неподходящий
        // момент. Поэтому спрашиваем сам PowerShell, читается ли файл целиком.
        // Именно тот PowerShell, что стоит в системе, — 5.1 придирчивее нового.
        ParsesInWindowsPowerShell(check, dir.FullName);
    }

    private static void ParsesInWindowsPowerShell(Checker check, string root)
    {
        const string script =
            "$bad = 0; " +
            "foreach ($f in Get-ChildItem -LiteralPath $args[0] -Filter *.ps1) { " +
            "  $e = $null; " +
            "  [void][System.Management.Automation.Language.Parser]::ParseFile($f.FullName, [ref]$null, [ref]$e); " +
            "  if ($e) { Write-Output ($f.Name + ': ' + $e[0].Message); $bad++ } " +
            "} " +
            "if ($bad -eq 0) { Write-Output 'ok' }";

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(script);
            psi.ArgumentList.Add(root);

            using var proc = System.Diagnostics.Process.Start(psi)!;
            string output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(30_000);

            check.True("все .ps1 читаются PowerShell без ошибок", output == "ok");
            if (output != "ok")
                foreach (string line in output.Split('\n'))
                    check.Info(line.Trim());
        }
        catch (Exception ex)
        {
            // Нет powershell.exe — не повод краснеть, но и молчать нельзя.
            check.Info("разбор .ps1 пропущен: " + ex.Message);
        }
    }

    /// <summary>
    /// Докачка из раздачи. Главное свойство: качается только отличающееся.
    /// Если сломается — дизайнер будет тянуть 400 МБ на каждую правку.
    /// </summary>
    private static void Distribution(Checker check)
    {
        check.Section("Обновление из раздачи");

        string sandbox = Path.Combine(Path.GetTempPath(), "cupsforge_selfcheck_dist");
        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true);

        var profile = MachineProfile.FromStakanyRoot(Path.Combine(sandbox, "STAKANY"));
        MachineProfile.Set(profile);
        string templates = PathResolver.Root(MachineProfile.Root.Templates);
        Directory.CreateDirectory(Path.Combine(templates, "2_Offset"));

        // Два файла у себя: один совпадает с раздачей, второй устарел.
        string same = Path.Combine(templates, "2_Offset", "same.ai");
        string old = Path.Combine(templates, "2_Offset", "old.ai");
        File.WriteAllText(same, "одинаковый");
        File.WriteAllText(old, "старая версия");

        var manifest = new DistManifest
        {
            Templates =
            {
                new DistFile { Path = "2_Offset/same.ai", Sha256 = FileHash.Of(same), Size = 10, Asset = "f-1.ai" },
                new DistFile { Path = "2_Offset/old.ai",  Sha256 = FileHash.OfBytes(System.Text.Encoding.UTF8.GetBytes("новая версия")), Size = 12, Asset = "f-2.ai" },
                new DistFile { Path = "7_Plastic/new.ai", Sha256 = "abc", Size = 20, Asset = "f-3.ai" }
            }
        };

        var state = new SyncState();
        var plan = DistributionClient.Compare(manifest, state);

        check.Equal("к загрузке отобраны только изменившиеся",
            plan.Files.Count.ToString(), "2");
        check.True("совпадающий файл не качается",
            !plan.Files.Any(f => f.Path.EndsWith("same.ai")));
        check.True("устаревший качается",
            plan.Files.Any(f => f.Path.EndsWith("old.ai")));
        check.True("отсутствующий качается",
            plan.Files.Any(f => f.Path.EndsWith("new.ai")));
        check.True("первая загрузка распознана", plan.IsFirstSync);

        // Совпавший файл занесён в состояние — второй раз его не хешируем.
        check.True("совпадение запомнено в состоянии",
            state.Files.ContainsKey("2_Offset/same.ai"));

        // Повторное сравнение с заполненным состоянием ничего не меняет.
        var again = DistributionClient.Compare(manifest, state);
        check.Equal("повторная проверка даёт тот же список",
            again.Files.Count.ToString(), "2");

        // Всё скачано — плана нет.
        foreach (var f in manifest.Templates)
            state.Files[f.Path] = f.Sha256;
        File.WriteAllText(old, "новая версия");
        Directory.CreateDirectory(Path.Combine(templates, "7_Plastic"));
        File.WriteAllText(Path.Combine(templates, "7_Plastic", "new.ai"), "x");
        check.True("когда всё на месте — качать нечего",
            DistributionClient.Compare(manifest, state).IsEmpty);

        check.Equal("объём считается для вопроса пользователю",
            new SyncPlan { Files = { new DistFile { Size = 3 * 1024 * 1024 } } }.Describe(),
            "1 файл(ов), 3 МБ");

        Directory.Delete(sandbox, true);
        MachineProfile.Set(MachineProfile.CreateOfficeDefault());
    }

    /// <summary>Пустые файлы шаблонов там, где их ждёт каталог.</summary>
    private static void CreateFakeTemplates()
    {
        foreach (var p in CatalogService.Current.Products)
        {
            var folders = new List<string>();
            if (p.TemplateFolderByCountry is { Count: > 0 })
                folders.AddRange(p.TemplateFolderByCountry.Values.Select(PathResolver.Expand));
            else if (p.TemplateFolder != null)
                folders.Add(PathResolver.Expand(p.TemplateFolder));

            var files = new List<string>();
            var table = CatalogService.Current.ResolveArticleTable(p);
            if (table != null) files.AddRange(table.Values);
            if (p.Variants != null) files.AddRange(p.Variants.Values.Select(v => v.File));

            // Шаблоны по образцу имени — кладём те, что нужны проверкам.
            if (p.FilePattern != null)
                foreach (string cc in new[] { "tr", "de", "en", "it" })
                    files.Add($"_DW90-430-0000 MC_{cc}.ai");

            foreach (string folder in folders)
            {
                Directory.CreateDirectory(folder);
                foreach (string f in files)
                    File.WriteAllText(Path.Combine(folder, f), "");
            }
        }
    }
}
