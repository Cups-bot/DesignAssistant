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
                RawLang = d.Lang ?? ""
            };
            resolved.Warnings.AddRange(warnings);

            if (string.IsNullOrWhiteSpace(resolved.ProductArticul))
                resolved.Warnings.Add("Артикул (product) пустой — шаблон определить нельзя.");
            if (string.IsNullOrWhiteSpace(resolved.DesignCode))
                resolved.Warnings.Add("Код дизайна пустой — имя папки определить нельзя.");

            return resolved;
        }

        private static bool Has(string? s, string sub) =>
            !string.IsNullOrEmpty(s) && s.Contains(sub, StringComparison.OrdinalIgnoreCase);

        public static Brand MapBrand(string? project, List<string> warnings)
        {
            if (Has(project, "cupto") || Has(project, "cupsto")) return Brand.CuptoYou;
            if (Has(project, "formacia") || Has(project, "flexo") || Has(project, "флекс")) return Brand.Flexo;
            if (Has(project, "mycups") || Has(project, "my cups")) return Brand.MyCups;
            warnings.Add($"Направление \"{project}\" не распознано — принято как MyCups.");
            return Brand.MyCups;
        }

        public static Country MapCountry(string? lang)
        {
            string s = (lang ?? "").Trim().ToLowerInvariant();
            if (s.StartsWith("tr") || s.Contains("turk") || s.Contains("тур")) return Country.TR;
            if (s.StartsWith("de") || s.Contains("germ") || s.Contains("deutsch") || s.Contains("нем")) return Country.DE;
            if (s.StartsWith("it") || s.Contains("ital") || s.Contains("итал")) return Country.IT;
            if (s.StartsWith("en") || s.Contains("engl") || s.Contains("англ")) return Country.EN;
            return Country.TR;
        }

        public static PrintTech MapPrintTech(string? print, List<string> warnings)
        {
            if (Has(print, "офсет") || Has(print, "offset"))  return PrintTech.Offset;
            if (Has(print, "цифр") || Has(print, "digital"))  return PrintTech.Digital;
            if (Has(print, "pantone") || Has(print, "пантон") || Has(print, "флексо") || Has(print, "flexo"))
                return PrintTech.Pantone;
            warnings.Add($"Способ печати \"{print}\" не распознан — принят как «Офсет».");
            return PrintTech.Offset;
        }

        public static Material MapMaterial(string? side, List<string> warnings)
        {
            // Порядок важен: "uncoated" содержит подстроку "coated".
            if (Has(side, "немелован") || Has(side, "крафт") || Has(side, "uncoated")) return Material.Uncoated;
            if (Has(side, "мелован") || Has(side, "coated")) return Material.Coated;
            if (string.IsNullOrWhiteSpace(side)) return Material.Uncoated;
            warnings.Add($"Материал \"{side}\" не распознан — принят как «Uncoated».");
            return Material.Uncoated;
        }

        public static Coating MapCoating(string? coating)
        {
            if (Has(coating, "soft"))  return Coating.SoftTouch;
            if (Has(coating, "color")) return Coating.ColorTouch;
            return Coating.None;
        }

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
