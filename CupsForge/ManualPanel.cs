using System.Windows;
using System.Windows.Controls;
using CupsCore;
using CupsForge.Models;

namespace CupsForge
{
    /// <summary>
    /// Ручной ввод: та же программа, просто раскрывается панель.
    ///
    /// Раньше это было отдельное приложение, которое запускалось по жёстко
    /// прописанному пути и получало данные через временный файл. Теперь ничего
    /// запускать не нужно, и путь настраивать тоже.
    ///
    /// Состав полей берётся из каталога: направления, типы продуктов, способы печати
    /// и варианты — всё запрашивается у него. Новый продукт появляется в списках сам,
    /// рисовать для него кнопку не требуется.
    /// </summary>
    public partial class AutoWindow
    {
        /// <summary>Пункт выпадающего списка: что показать и что это значит.</summary>
        private sealed record ComboItem(string Id, string Title)
        {
            public override string ToString() => Title;
        }

        private bool _manualMode;
        private bool _manualLoading;

        // ---------- построение ----------

        private void InitManual()
        {
            _manualLoading = true;

            MaterialCombo.ItemsSource = new[]
            {
                new ComboItem(nameof(Material.Uncoated), "Немелованный"),
                new ComboItem(nameof(Material.Coated), "Мелованный")
            };
            MaterialCombo.SelectedIndex = 0;

            CoatingCombo.ItemsSource = new[]
            {
                new ComboItem(nameof(Coating.None), "Без покрытия"),
                new ComboItem(nameof(Coating.SoftTouch), "Soft Touch"),
                new ComboItem(nameof(Coating.ColorTouch), "Color Touch")
            };
            CoatingCombo.SelectedIndex = 0;

            var countries = new List<ComboItem>();
            foreach (Country c in Enum.GetValues<Country>())
                countries.Add(new ComboItem(c.ToString(), c.ToString()));
            CountryCombo.ItemsSource = countries;
            CountryCombo.SelectedIndex = 0;

            try
            {
                var brands = new List<ComboItem>();
                foreach (var (id, title) in CatalogService.Current.BrandsForUi())
                    brands.Add(new ComboItem(id.ToString(), title));
                BrandCombo.ItemsSource = brands;
                BrandCombo.SelectedIndex = 0;
            }
            catch (CatalogException ex)
            {
                Log(ex.Message);
            }

            _manualLoading = false;
            FillTypes();
        }

        private void ManualBrand_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_manualLoading) return;
            FillTypes();
        }

        private void ManualType_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_manualLoading) return;
            FillTechs();
        }

        private void ManualDetail_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_manualLoading) return;
            RefreshManualProduct();
        }

        /// <summary>Типы продуктов выбранного направления. Нет типов — строка прячется.</summary>
        private void FillTypes()
        {
            _manualLoading = true;

            Brand brand = SelectedBrand();
            var types = CatalogService.Current.ProductTypesOf(brand);

            var items = types.Select(t => new ComboItem(t.Id, t.Title)).ToList();
            TypeCombo.ItemsSource = items;
            if (items.Count > 0) TypeCombo.SelectedIndex = 0;

            ShowRow(LblType, TypeCombo, items.Count > 0);

            _manualLoading = false;
            FillTechs();
        }

        /// <summary>
        /// Способы печати показываем, только если между ними есть реальный выбор,
        /// то есть разные значения дают разные продукты. Иначе строка бессмысленна.
        /// </summary>
        private void FillTechs()
        {
            _manualLoading = true;

            var techs = CatalogService.Current.PrintTechsOf(SelectedBrand(), SelectedProductType());

            var items = techs.Select(t => new ComboItem(t.ToString(), TechTitle(t))).ToList();
            TechCombo.ItemsSource = items;
            if (items.Count > 0) TechCombo.SelectedIndex = 0;

            ShowRow(LblTech, TechCombo, items.Count > 0);

            _manualLoading = false;
            RefreshManualProduct();
        }

        private static string TechTitle(PrintTech tech) => tech switch
        {
            PrintTech.Digital => "Цифра",
            PrintTech.Pantone => "Pantone / флексо",
            _ => "Офсет"
        };

        /// <summary>
        /// Подбирает продукт под текущий выбор и показывает ровно те строки,
        /// которые для него имеют смысл.
        /// </summary>
        private void RefreshManualProduct()
        {
            CatalogProduct? product = CatalogService.Current.Match(CurrentSpec());

            if (product == null)
            {
                ManualProductInfo.Text = "Для такого сочетания в каталоге нет продукта.";
                ShowRow(LblVariant, VariantCombo, false);
                ShowRow(LblCountry, CountryCombo, false);
                ShowRow(LblMaterial, MaterialCombo, false);
                ShowRow(LblCoating, CoatingCombo, false);
                return;
            }

            _manualLoading = true;

            // Варианты (вкус шоколада, вид конфет).
            var variants = Catalog.VariantsOf(product);
            var variantItems = variants.Select(v => new ComboItem(v.Id, v.Title)).ToList();
            string? keep = (VariantCombo.SelectedItem as ComboItem)?.Id;
            VariantCombo.ItemsSource = variantItems;
            if (variantItems.Count > 0)
            {
                int index = variantItems.FindIndex(v => v.Id == keep);
                VariantCombo.SelectedIndex = index >= 0 ? index : 0;
            }
            ShowRow(LblVariant, VariantCombo, variantItems.Count > 0);

            // Страна — там, где она влияет на шаблон или уходит в args.txt.
            bool needCountry = product.TemplateFolderByCountry is { Count: > 0 } || product.ArgsCountry;
            ShowRow(LblCountry, CountryCombo, needCountry);

            // Материал и покрытие имеют смысл только там, где покрытие вообще бывает.
            ShowRow(LblMaterial, MaterialCombo, product.Coating);
            bool coated = product.Coating && SelectedMaterial() == Material.Coated;
            ShowRow(LblCoating, CoatingCombo, coated);
            if (!coated) CoatingCombo.SelectedIndex = 0;

            _manualLoading = false;

            ManualProductInfo.Text = $"Продукт: {product.Title}";
        }

        private static void ShowRow(UIElement label, UIElement field, bool show)
        {
            var visibility = show ? Visibility.Visible : Visibility.Collapsed;
            label.Visibility = visibility;
            field.Visibility = visibility;
        }

        // ---------- текущее состояние ----------

        private Brand SelectedBrand() =>
            Enum.TryParse<Brand>((BrandCombo.SelectedItem as ComboItem)?.Id, out var b) ? b : Brand.MyCups;

        private string SelectedProductType() =>
            (TypeCombo.SelectedItem as ComboItem)?.Id ?? ProductTypes.Cups;

        private PrintTech SelectedPrintTech() =>
            Enum.TryParse<PrintTech>((TechCombo.SelectedItem as ComboItem)?.Id, out var t) ? t : PrintTech.Offset;

        private Country SelectedCountry() =>
            Enum.TryParse<Country>((CountryCombo.SelectedItem as ComboItem)?.Id, out var c) ? c : Country.TR;

        private Material SelectedMaterial() =>
            Enum.TryParse<Material>((MaterialCombo.SelectedItem as ComboItem)?.Id, out var m) ? m : Material.Uncoated;

        private Coating SelectedCoating() =>
            Enum.TryParse<Coating>((CoatingCombo.SelectedItem as ComboItem)?.Id, out var c) ? c : Coating.None;

        private DesignSpec CurrentSpec() => new()
        {
            Brand = SelectedBrand(),
            ProductType = SelectedProductType(),
            PrintTech = SelectedPrintTech(),
            Country = SelectedCountry(),
            Variant = (VariantCombo.SelectedItem as ComboItem)?.Id ?? ""
        };

        /// <summary>Заявка на создание проекта по тому, что введено руками.</summary>
        private BuildRequest BuildManualRequest() => new()
        {
            DesignCode = ManualNameBox.Text.Trim(),
            Article = null, // достанется из имени по правилу продукта
            Material = SelectedMaterial(),
            Coating = SelectedCoating(),
            Spec = CurrentSpec()
        };

        // ---------- переключение режима ----------

        private void ToggleManual(bool show)
        {
            if (show && BrandCombo.ItemsSource == null)
                InitManual();

            _manualMode = show;
            ManualPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

            // В ручном режиме создавать можно всегда — проверку имени сделает сборщик.
            if (show)
                BuildButton.IsEnabled = true;
            else
                BuildButton.IsEnabled = _resolved != null;
        }

        /// <summary>Переносит загруженный заказ в поля ручного ввода — чтобы поправить и создать.</summary>
        private void FillManualFrom(ResolvedDesign r)
        {
            if (BrandCombo.ItemsSource == null)
                InitManual();

            _manualLoading = true;
            SelectCombo(BrandCombo, r.Brand.ToString());
            _manualLoading = false;
            FillTypes();

            _manualLoading = true;
            SelectCombo(TypeCombo, r.ProductType);
            _manualLoading = false;
            FillTechs();

            _manualLoading = true;
            SelectCombo(TechCombo, r.PrintTech.ToString());
            SelectCombo(CountryCombo, r.Country.ToString());
            SelectCombo(MaterialCombo, r.Material.ToString());
            SelectCombo(CoatingCombo, r.Coating.ToString());
            _manualLoading = false;

            RefreshManualProduct();

            _manualLoading = true;
            SelectCombo(VariantCombo, r.Variant);
            _manualLoading = false;

            ManualNameBox.Text = r.DesignCode;
        }

        private static void SelectCombo(ComboBox combo, string? id)
        {
            if (string.IsNullOrWhiteSpace(id) || combo.ItemsSource is not IEnumerable<ComboItem> items)
                return;

            foreach (var item in items)
            {
                if (string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = item;
                    return;
                }
            }
        }
    }
}
