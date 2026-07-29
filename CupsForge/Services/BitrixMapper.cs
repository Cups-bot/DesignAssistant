using CupsCore;
using CupsForge.Models;

namespace CupsForge.Services
{
    /// <summary>
    /// Приводит строки Bitrix (русские и английские) к enum'ам приложения.
    /// Направление определяется полем project. Сопоставления вынесены сюда —
    /// при появлении новых значений в Bitrix правьте только этот файл.
    /// </summary>
    public static class BitrixMapper
    {
        public static ResolvedDesign Map(DesignData d, string designCode)
        {
            var warnings = new List<string>();

            Brand brand = MapBrand(d.Project, warnings);
            Country country = MapCountry(d.Lang);
            PrintTech printTech = MapPrintTech(d.Print, warnings);
            Material material = MapMaterial(d.Side, warnings);
            Coating coating = MapCoating(d.Coating);

            // Тип продукта имеет смысл только для MyCups; у CupToYou/Флексо — всегда стаканы.
            // Слова для распознавания лежат в каталоге, поэтому новый тип продукта
            // начинает распознаваться сразу после правки catalog.json.
            string productType = ProductTypes.Cups;
            if (brand == Brand.MyCups)
            {
                string? matched = CatalogService.Current.ProductTypeFromText(brand, d.Type);
                if (matched != null)
                {
                    productType = matched;
                }
                else if (!string.IsNullOrWhiteSpace(d.Type))
                {
                    warnings.Add($"Тип продукта \"{d.Type}\" не распознан — принят как «Стаканы».");
                }
            }

            // Вариант (вкус шоколада, вид конфет) определяется по тому же каталогу.
            var probe = new DesignSpec
            {
                Brand = brand,
                ProductType = productType,
                PrintTech = printTech,
                Country = country
            };
            CatalogProduct? product = CatalogService.Current.Match(probe);
            string variant = product == null
                ? ""
                : Catalog.VariantFromText(product, $"{d.Flavor} {d.Product}");

            var resolved = new ResolvedDesign
            {
                Id = d.Id,
                OrderName = d.Name ?? "",
                DesignCode = string.IsNullOrWhiteSpace(designCode) ? ExtractCodeFromName(d.Name) : designCode.Trim(),
                Brand = brand,
                Country = country,
                ProductType = productType,
                ProductArticul = (d.Product ?? "").Trim(),
                PrintTech = printTech,
                Material = material,
                Coating = coating,
                Variant = variant,
                RawProject = d.Project ?? "",
                RawType = d.Type ?? "",
                RawPrint = d.Print ?? "",
                RawSide = d.Side ?? "",
                RawCoating = d.Coating ?? "",
                RawLang = d.Lang ?? "",
                RawFlavor = d.Flavor ?? ""
            };
            resolved.Warnings.AddRange(warnings);

            if (string.IsNullOrWhiteSpace(resolved.ProductArticul))
                resolved.Warnings.Add("Артикул (product) пустой — шаблон определить нельзя.");
            if (string.IsNullOrWhiteSpace(resolved.DesignCode))
                resolved.Warnings.Add("Код дизайна пустой — имя папки определить нельзя.");

            return resolved;
        }

        // Слов, по которым распознаются значения, здесь больше нет: они лежат
        // в каталоге, рядом с шаблонами. Новое написание из Bitrix («Тампопечать»)
        // добавляется правкой catalog.json, без пересборки программы.

        public static Brand MapBrand(string? project, List<string> warnings)
        {
            var found = Catalog.EnumFromWords<Brand>(CatalogService.Current.Words.Brand, project);
            if (found.HasValue)
                return found.Value;

            warnings.Add($"Направление \"{project}\" не распознано — принято как MyCups.");
            return Brand.MyCups;
        }

        public static Country MapCountry(string? lang) =>
            Catalog.EnumFromWords<Country>(CatalogService.Current.Words.Country, lang) ?? Country.TR;

        public static PrintTech MapPrintTech(string? print, List<string> warnings)
        {
            var found = Catalog.EnumFromWords<PrintTech>(CatalogService.Current.Words.PrintTech, print);
            if (found.HasValue)
                return found.Value;

            warnings.Add($"Способ печати \"{print}\" не распознан — принят как «Офсет». " +
                         "Добавьте это название в catalog.json, раздел bitrixWords.");
            return PrintTech.Offset;
        }

        public static Material MapMaterial(string? side, List<string> warnings)
        {
            var found = Catalog.EnumFromWords<Material>(CatalogService.Current.Words.Material, side);
            if (found.HasValue)
                return found.Value;

            if (!string.IsNullOrWhiteSpace(side))
                warnings.Add($"Материал \"{side}\" не распознан — принят как «Uncoated».");
            return Material.Uncoated;
        }

        public static Coating MapCoating(string? coating) =>
            Catalog.EnumFromWords<Coating>(CatalogService.Current.Words.Coating, coating) ?? Coating.None;

        // Вкусы шоколада и виды конфет раньше распознавались здесь двумя switch'ами.
        // Теперь слова лежат в каталоге рядом с файлами шаблонов — см. Catalog.VariantFromText.

        /// <summary>
        /// Извлекает код дизайна из скобок названия, напр.
        /// "CarBar (132583 CarBar ST DW90-430)" → "132583 CarBar ST DW90-430".
        /// </summary>
        public static string ExtractCodeFromName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            int open = name.LastIndexOf('(');
            int close = name.LastIndexOf(')');
            if (open >= 0 && close > open)
                return name.Substring(open + 1, close - open - 1).Trim();
            return name.Trim();
        }
    }
}
