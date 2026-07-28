using System;
using System.Collections.Generic;

namespace CupsCore
{
    /// <summary>
    /// Находит шаблон для заказа. Сами данные (какие бывают продукты, какие у них
    /// артикулы и файлы) живут в каталоге — здесь только правило поиска.
    ///
    /// До перехода на каталог эти таблицы были зашиты в код в двух экземплярах,
    /// и добавление продукта требовало пересборки и раскладки программы всем.
    /// </summary>
    public static class TemplateResolver
    {
        /// <summary>
        /// Папка шаблонов и имя .ai-файла для заказа — единственная копия этого
        /// правила, её зовут и ручной редактор, и автоматическая программа.
        ///
        /// <paramref name="article"/> может уточниться: если продукт выбран по варианту
        /// (вид конфет), в args.txt должен попасть артикул этого варианта.
        /// </summary>
        public static (string path, string? file) ResolveTemplate(DesignSpec spec, ref string article)
        {
            CatalogProduct product = CatalogService.Current.Require(spec);
            return ResolveTemplate(product, spec, ref article);
        }

        public static (string path, string? file) ResolveTemplate(
            CatalogProduct product, DesignSpec spec, ref string article)
        {
            string folder = ResolveFolder(product, spec);
            string? file = ResolveFile(product, spec, ref article);
            return (folder, file);
        }

        /// <summary>Папка шаблонов продукта — общая либо своя для каждой страны.</summary>
        public static string ResolveFolder(CatalogProduct product, DesignSpec spec)
        {
            if (product.TemplateFolderByCountry is { Count: > 0 })
            {
                string country = spec.Country.ToString();
                if (!product.TemplateFolderByCountry.TryGetValue(country, out string? byCountry))
                {
                    throw new CatalogException(
                        $"Продукт \"{product.Title}\": не задана папка шаблонов для страны {country}.");
                }
                return PathResolver.Expand(byCountry);
            }

            if (string.IsNullOrWhiteSpace(product.TemplateFolder))
                throw new CatalogException($"Продукт \"{product.Title}\": не задана папка шаблонов.");

            return PathResolver.Expand(product.TemplateFolder);
        }

        /// <summary>
        /// Имя файла шаблона. Порядок поиска: таблица артикулов → вариант → образец имени.
        /// </summary>
        private static string? ResolveFile(CatalogProduct product, DesignSpec spec, ref string article)
        {
            // 1. По артикулу.
            Dictionary<string, string>? table = CatalogService.Current.ResolveArticleTable(product);
            if (table != null && !string.IsNullOrWhiteSpace(article) &&
                table.TryGetValue(article, out string? byArticle))
            {
                return byArticle;
            }

            // 2. По варианту (вкус шоколада, вид конфет).
            if (product.Variants is { Count: > 0 })
            {
                string variant = spec.Variant;
                if (string.IsNullOrWhiteSpace(variant) || !product.Variants.ContainsKey(variant))
                    variant = product.DefaultVariant ?? "";

                if (!string.IsNullOrWhiteSpace(variant) &&
                    product.Variants.TryGetValue(variant, out CatalogVariant? chosen))
                {
                    // Артикул варианта важен: он уходит в args.txt и в журнал.
                    if (!string.IsNullOrWhiteSpace(chosen.Article))
                        article = chosen.Article;
                    return chosen.File;
                }
            }

            // 3. По образцу имени.
            if (!string.IsNullOrWhiteSpace(product.FilePattern))
            {
                return product.FilePattern
                    .Replace("{article}", article)
                    .Replace("{country}", spec.Country.ToString().ToLowerInvariant())
                    .Replace("{COUNTRY}", spec.Country.ToString().ToUpperInvariant());
            }

            return null;
        }
    }
}
