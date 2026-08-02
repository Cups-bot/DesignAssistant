using System;
using System.IO;

namespace CupsCore
{
    /// <summary>
    /// Пути к Illustrator и корням вывода.
    ///
    /// Физических путей здесь нет: корни задаются в профиле машины
    /// (<see cref="MachineProfile"/>), а какой корень какому продукту соответствует —
    /// в каталоге (<see cref="Catalog"/>). Благодаря этому одна и та же сборка
    /// работает и на рабочем месте в офисе, и на домашней машине, а новый продукт
    /// не требует правки кода.
    /// </summary>
    public static class Paths
    {
        // ---------- Illustrator ----------

        /// <summary>Текст для журнала, когда причина неизвестна (запасной вариант).</summary>
        public const string IllustratorNotFoundMessage =
            "Illustrator не найден на этой машине. Укажите путь к Illustrator.exe в настройках.";

        private static string? _illustratorCache;
        private static string? _illustratorProblem;
        private static bool _illustratorResolved;

        private static void ResolveIllustrator()
        {
            if (_illustratorResolved)
                return;

            IllustratorLocator.TryResolve(MachineProfile.Current.IllustratorExe,
                                          out _illustratorCache, out _illustratorProblem);
            _illustratorResolved = true;
        }

        /// <summary>
        /// Путь к Illustrator.exe: из профиля либо найденный автоматически.
        /// Пустая строка — запускать нечего (проверяйте <see cref="IllustratorFound"/>,
        /// исключение здесь не бросается, чтобы не ронять создание проекта).
        /// </summary>
        public static string IllustratorExe
        {
            get
            {
                ResolveIllustrator();
                return _illustratorCache ?? "";
            }
        }

        public static bool IllustratorFound => !string.IsNullOrEmpty(IllustratorExe);

        /// <summary>
        /// Почему Illustrator недоступен — готовая фраза для журнала. Общий текст
        /// «не найден» не годится: причины разные (не нашли вовсе / пропал именно
        /// выбранный в настройках), и чинятся они по-разному.
        /// null — всё в порядке.
        /// </summary>
        public static string? IllustratorProblem
        {
            get
            {
                ResolveIllustrator();
                return _illustratorProblem;
            }
        }

        /// <summary>Сбросить кэш поиска (после смены пути в настройках).</summary>
        public static void ResetIllustratorCache()
        {
            _illustratorResolved = false;
            _illustratorCache = null;
            _illustratorProblem = null;
        }

        /// <summary>JSX-скрипт запуска; путь берётся из профиля и разворачивается.</summary>
        public static string JsxScriptPath => PathResolver.Expand(MachineProfile.Current.JsxScript);

        // ---------- Корни ----------

        public static string TemplatesRoot   => PathResolver.Root(MachineProfile.Root.Templates);
        public static string MyCupsRoot      => PathResolver.Root(MachineProfile.Root.MyCups);
        public static string CuptoYouRoot    => PathResolver.Root(MachineProfile.Root.CuptoYou);
        public static string FlexoOutputRoot => PathResolver.Root(MachineProfile.Root.Flexo);
        public static string OffsetRoot      => PathResolver.Root(MachineProfile.Root.Offset);
        public static string PadPrintRoot    => PathResolver.Root(MachineProfile.Root.PadPrint);

        /// <summary>
        /// Куда складывать готовый проект. Правило берётся из каталога (поле "output"),
        /// а не из цепочки if-else, которая раньше была продублирована в двух программах.
        /// </summary>
        public static string GetOutputRoot(DesignSpec spec) =>
            CatalogService.Current.Require(spec).ResolveOutput();

        /// <inheritdoc cref="GetOutputRoot(DesignSpec)"/>
        public static string GetOutputRoot(Brand brand, string productType, PrintTech printTech) =>
            GetOutputRoot(new DesignSpec
            {
                Brand = brand,
                ProductType = productType,
                PrintTech = printTech
            });
    }
}
