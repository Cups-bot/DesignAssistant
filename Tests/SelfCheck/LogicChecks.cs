using System.IO;
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

        Catalog(check);
        OutputRoots(check);
        Templates(check);
        Articles(check);
        BitrixText(check);
        NewProductWithoutCode(check);
        RemoteProfile(check);
        Regressions(check);
        EndToEnd(check);
        Updates(check);

        MachineProfile.Set(MachineProfile.CreateOfficeDefault());
        Paths.ResetIllustratorCache();
        CatalogService.Reload();
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

        CreateFakeTemplates();

        void Build(string what, BuildRequest request, string expectedFolder, params string[] expectedArgs)
        {
            BuildResult r = ProjectBuilder.Build(request);
            if (!r.Success)
            {
                check.Fail($"{what}: проект не создан — {string.Join("; ", r.Log)}");
                return;
            }

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
    /// Обновления. Саму подмену файла не гоняем — она перезапускает программу,
    /// поэтому проверяем всё, что до неё: сравнение версий, раскрытие пути
    /// раздачи и поведение, когда раздачи нет.
    /// </summary>
    private static void Updates(Checker check)
    {
        check.Section("Обновления");

        check.True("2.0.0 новее 1.9.9", Updater.IsNewer("2.0.0", "1.9.9"));
        check.True("3.0.10 новее 3.0.9", Updater.IsNewer("3.0.10", "3.0.9"));
        check.True("та же версия не новее", !Updater.IsNewer("3.0.0", "3.0.0"));
        check.True("старая версия не новее", !Updater.IsNewer("2.9.9", "3.0.0"));
        check.True("мусор не считается новее", !Updater.IsNewer("абра-кадабра", "1.0.0"));

        // Раздача лежит рядом с рабочей папкой: Y:\STAKANY\..\Soft → Y:\Soft.
        MachineProfile.Set(MachineProfile.CreateOfficeDefault());
        check.Equal("путь раздачи схлопывает «..»",
            PathResolver.Expand(MachineProfile.Current.UpdateSource),
            @"Y:\Soft\CupsForge\release");

        // Раздачи нет (домашняя машина) — проверка молчит, а не падает.
        string sandbox = Path.Combine(Path.GetTempPath(), "cupsforge_selfcheck_upd");
        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true);

        var profile = MachineProfile.FromStakanyRoot(Path.Combine(sandbox, "STAKANY"));
        MachineProfile.Set(profile);
        check.True("нет раздачи — обновление не найдено и ошибки нет",
            Updater.Check(out string? d1) == null && d1 == null);

        // Раздача есть и версия новее — должна найтись.
        string release = Path.Combine(sandbox, "release");
        Directory.CreateDirectory(Path.Combine(release, "99.0.0"));
        File.WriteAllText(Path.Combine(release, "99.0.0", "CupsForge.exe"), "");
        File.WriteAllText(Path.Combine(release, Updater.ReleaseFileName),
            """{"version":"99.0.0","folder":"99.0.0","notes":"тест"}""");

        profile.UpdateSource = release;
        MachineProfile.Set(profile);
        ReleaseInfo? found = Updater.Check(out _);
        check.Equal("новая версия на раздаче найдена", found?.Version, "99.0.0");
        check.Equal("примечание к версии прочитано", found?.Notes, "тест");

        // Версия не новее текущей — предлагать нечего.
        File.WriteAllText(Path.Combine(release, Updater.ReleaseFileName),
            """{"version":"0.0.1","folder":"99.0.0"}""");
        check.True("старая версия на раздаче игнорируется", Updater.Check(out _) == null);

        // Заявлена версия, но файла нет — честно сообщаем, а не молчим.
        File.WriteAllText(Path.Combine(release, Updater.ReleaseFileName),
            """{"version":"98.0.0","folder":"нет-такой-папки"}""");
        Updater.Check(out string? d2);
        check.True("обещанная версия без файла даёт понятную жалобу",
            d2 != null && d2.Contains("98.0.0"));

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
        var saved = MachineProfile.FromStakanyRoot(@"D:\Work\STAKANY");
        saved.Bitrix.Login = "designer";
        saved.Bitrix.Password = "секрет";
        var back = System.Text.Json.JsonSerializer.Deserialize<MachineProfile>(
            System.Text.Json.JsonSerializer.Serialize(saved))!;
        check.Equal("логин Bitrix переживает сохранение", back.Bitrix.Login, "designer");
        check.Equal("пароль Bitrix переживает сохранение", back.Bitrix.Password, "секрет");
        check.True("заполненный доступ считается настроенным", back.Bitrix.IsConfigured);

        // Устаревший путь к Illustrator не выдаётся за рабочий.
        string ghost = Path.Combine(sandbox, "нет", "Illustrator.exe");
        string? resolved = IllustratorLocator.Resolve(ghost);
        check.True("несуществующий путь к Illustrator отбрасывается", resolved != ghost);

        // Обновление отказывается трогать копию вне папки установки.
        check.True("запуск из папки установки распознаётся",
            Updater.IsInsideInstallFolder(Path.Combine(Updater.InstallFolder, "CupsForge.exe")));
        check.True("запуск с сетевого диска не считается установкой",
            !Updater.IsInsideInstallFolder(@"Y:\Soft\CupsForge\release\3.0.0\CupsForge.exe"));

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
