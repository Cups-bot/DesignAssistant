using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using CupsCore;
using CupsForge.Models;

namespace CupsForge
{
    /// <summary>
    /// Лист правки одного поля.
    ///
    /// Разбор Bitrix ошибается редко и обычно в одном поле из семи: не узнал
    /// материал, не разобрал вкус. Открывать ради этого ручной ввод целиком —
    /// значит заставить дизайнера перезаполнить шесть верных полей ради одного
    /// неверного. Лист правит ровно то поле, по которому щёлкнули, и возвращает
    /// в тот же результат.
    ///
    /// Варианты берутся из каталога. Новый материал или вкус появляется здесь
    /// сам — дорисовывать кнопку не нужно.
    /// </summary>
    public partial class AutoWindow
    {
        private readonly ObservableCollection<FixOption> _fixOptions = new();
        private string _fixKey = "";

        private void InitFixSheet() => FixOptions.ItemsSource = _fixOptions;

        /// <summary>
        /// Какие поля вообще можно поправить при текущем заказе. Спрашивается
        /// у каталога: у шоколада нет материала, у MyCups нет страны, и рисовать
        /// для них карандаш значит обещать несуществующее действие.
        /// </summary>
        private string? FixKeyFor(string label, ResolvedDesign r)
        {
            var product = CatalogService.Current.Match(r.ToBuildRequest().Spec);

            return label switch
            {
                "Направление" => FixKeys.Brand,
                "Тип"         => FixKeys.Type,
                "Печать"      => CatalogService.Current.PrintTechsOf(r.Brand, r.ProductType).Count > 1
                                 ? FixKeys.Tech : null,
                "Материал"    => product?.Coating == true ? FixKeys.Material : null,
                "Покрытие"    => product?.Coating == true && r.Material == Material.Coated
                                 ? FixKeys.Coating : null,
                "Вкус / вид"  => Catalog.VariantsOf(product).Count > 0 ? FixKeys.Variant : null,
                "Страна"      => r.Brand == Brand.CuptoYou ? FixKeys.Country : null,
                "Артикул"     => FixKeys.Article,
                _             => null
            };
        }

        private void OpenFix(string key)
        {
            if (_resolved == null)
                return;

            _fixKey = key;
            _fixOptions.Clear();

            var product = CatalogService.Current.Match(_resolved.ToBuildRequest().Spec);
            bool asText = key == FixKeys.Article;

            FixTitle.Text = key switch
            {
                FixKeys.Brand    => "Выберите направление",
                FixKeys.Type     => "Выберите тип продукта",
                FixKeys.Tech     => "Выберите способ печати",
                FixKeys.Material => "Выберите материал",
                FixKeys.Coating  => "Выберите покрытие",
                FixKeys.Variant  => "Выберите вкус или вид",
                FixKeys.Country  => "Выберите страну",
                _                => "Укажите артикул"
            };

            if (asText)
            {
                FixTextBox.Text = _resolved.ProductArticul;
                FixList.Visibility = Visibility.Collapsed;
                FixText.Visibility = Visibility.Visible;
            }
            else
            {
                foreach (var option in OptionsFor(key, _resolved, product))
                    _fixOptions.Add(option);

                FixList.Visibility = Visibility.Visible;
                FixText.Visibility = Visibility.Collapsed;
            }

            FixOverlay.Visibility = Visibility.Visible;

            FixShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new DoubleAnimation(40, 0, (Duration)FindResource("M.Base"))
                {
                    EasingFunction = (IEasingFunction)FindResource("Ease")
                });

            if (asText)
            {
                FixTextBox.Focus();
                FixTextBox.SelectAll();
            }
        }

        private static IEnumerable<FixOption> OptionsFor(string key, ResolvedDesign r, CatalogProduct? product)
        {
            switch (key)
            {
                case FixKeys.Brand:
                    foreach (var (id, title) in CatalogService.Current.BrandsForUi())
                        yield return new FixOption { Id = id.ToString(), Label = title };
                    break;

                case FixKeys.Type:
                    foreach (var (id, title) in CatalogService.Current.ProductTypesOf(r.Brand))
                        yield return new FixOption { Id = id, Label = title, Code = id };
                    break;

                case FixKeys.Tech:
                    foreach (var tech in CatalogService.Current.PrintTechsOf(r.Brand, r.ProductType))
                        yield return new FixOption { Id = tech.ToString(), Label = TechTitle(tech),
                                                     Code = tech.ToString().ToLowerInvariant() };
                    break;

                case FixKeys.Material:
                    yield return new FixOption { Id = nameof(Material.Uncoated), Label = "Немелованный", Code = "uncoated" };
                    yield return new FixOption { Id = nameof(Material.Coated), Label = "Мелованный", Code = "coated" };
                    break;

                case FixKeys.Coating:
                    yield return new FixOption { Id = nameof(Coating.None), Label = "Без покрытия", Code = "none" };
                    yield return new FixOption { Id = nameof(Coating.SoftTouch), Label = "Soft Touch", Code = "soft_touch" };
                    yield return new FixOption { Id = nameof(Coating.ColorTouch), Label = "Color Touch", Code = "color_touch" };
                    break;

                case FixKeys.Variant:
                    foreach (var (id, title) in Catalog.VariantsOf(product))
                        yield return new FixOption { Id = id, Label = title, Code = id };
                    break;

                case FixKeys.Country:
                    foreach (Country country in Enum.GetValues<Country>())
                        yield return new FixOption { Id = country.ToString(), Label = country.ToString() };
                    break;
            }
        }

        private void FixOption_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: FixOption option } || _resolved == null)
                return;

            ResolvedDesign fixedUp = _fixKey switch
            {
                FixKeys.Brand when Enum.TryParse<Brand>(option.Id, out var b)
                    => _resolved.With(brand: b),
                FixKeys.Type
                    => _resolved.With(productType: option.Id),
                FixKeys.Tech when Enum.TryParse<PrintTech>(option.Id, out var t)
                    => _resolved.With(printTech: t),
                FixKeys.Material when Enum.TryParse<Material>(option.Id, out var m)
                    => _resolved.With(material: m),
                FixKeys.Coating when Enum.TryParse<Coating>(option.Id, out var c)
                    => _resolved.With(coating: c),
                FixKeys.Variant
                    => _resolved.With(variant: option.Id),
                FixKeys.Country when Enum.TryParse<Country>(option.Id, out var c)
                    => _resolved.With(country: c),
                _ => _resolved
            };

            ApplyFix(fixedUp, $"{FixTitle.Text.Replace("Выберите ", "")}: {option.Label}");
        }

        private void FixTextApply_Click(object sender, RoutedEventArgs e) => ApplyArticle();

        private void FixTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ApplyArticle();
        }

        private void ApplyArticle()
        {
            if (_resolved == null)
                return;

            string article = FixTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(article))
            {
                Log("Артикул не может быть пустым.", NoticeKind.Warning);
                return;
            }

            ApplyFix(_resolved.With(article: article), "Артикул: " + article);
        }

        private void ApplyFix(ResolvedDesign updated, string what)
        {
            CloseFix();

            // Правка одного поля меняет подобранный продукт, а с ним — состав
            // остальных строк: у шоколада исчезает материал, у CupToYou
            // появляется страна. Поэтому результат пересобирается целиком.
            ShowResult(updated);
            Log("Поправлено вручную — " + what);
        }

        private void FixClose_Click(object sender, RoutedEventArgs e) => CloseFix();
        private void FixScrim_Click(object sender, MouseButtonEventArgs e) => CloseFix();

        private void CloseFix() => FixOverlay.Visibility = Visibility.Collapsed;
    }
}
