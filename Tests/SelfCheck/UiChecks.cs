using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using CupsCore;
using CupsForge;

namespace SelfCheck;

/// <summary>
/// Открывает настоящее окно программы и нажимает кнопки так же, как пользователь.
/// Ошибки привязок в WPF молчаливые — компилятор их не видит, поэтому ловим отдельно.
/// </summary>
public static class UiChecks
{
    public static void Run(Checker check)
    {
        check.Section("Интерфейс");

        PresentationTraceSources.Refresh();
        var listener = new BindingErrorListener(check);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

        // Профиль удалённой машины во временной папке — как у дизайнера дома.
        string sandbox = Path.Combine(Path.GetTempPath(), "cupsforge_selfcheck_ui", "STAKANY");
        var profile = MachineProfile.FromStakanyRoot(sandbox);
        profile.IllustratorExe = Path.Combine(sandbox, "no-illustrator.exe");

        // Создаём ВСЕ рабочие папки: иначе окно при запуске покажет мастер настройки
        // модально, и прогон повиснет — закрыть его в тесте некому.
        // Это же и проверка того, что при полном комплекте папок мастер не лезет.
        foreach (string key in MachineProfile.Root.AllIncludingBase)
            Directory.CreateDirectory(profile.Roots[key]);

        MachineProfile.Set(profile);
        Paths.ResetIllustratorCache();
        CatalogService.Reload();

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        // Прогон создаёт СВОЁ приложение, а токены подключает App.xaml.cs рабочего.
        // Без этого окно открывалось бы здесь без ресурсов — то есть проверка
        // разошлась бы с тем, что видит дизайнер.
        Theme.Apply(app);
        CheckTokens(check, app);
        app.DispatcherUnhandledException += (_, e) =>
        {
            check.Fail("Необработанное исключение: " + e.Exception.Message);
            e.Handled = true;
            app.Shutdown();
        };

        AutoWindow? window = null;
        try
        {
            window = new AutoWindow();
            window.Show();
            check.True("окно открывается", true);
        }
        catch (Exception ex)
        {
            check.Fail("окно не открылось: " + ex.Message);
        }

        if (window != null)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try { Inspect(check, window); }
                catch (Exception ex) { check.Fail("сбой проверки интерфейса: " + ex.Message); }
                window.Close();
                app.Shutdown();
            };
            timer.Start();
            app.Run();
        }

        PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
        try { Directory.Delete(Path.Combine(Path.GetTempPath(), "cupsforge_selfcheck_ui"), true); } catch { }
    }

    private static void Inspect(Checker check, AutoWindow w)
    {
        var manual = w.FindName("ManualPanel") as Border;
        check.True("ручная панель по умолчанию скрыта", manual?.Visibility != Visibility.Visible);

        if (w.FindName("PencilButton") is not Button pencil)
        {
            check.Fail("кнопка карандаша не найдена");
            return;
        }

        pencil.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        check.True("карандаш раскрывает ручную панель", manual?.Visibility == Visibility.Visible);

        // Встречная проверка к запрету ниже: на настроенном рабочем месте ручной
        // ввод обязан работать. Иначе «починка» кнопки выключила бы её всем.
        check.True("на настроенном месте ручной ввод разрешает «Создать проект»",
                   (w.FindName("BuildButton") as Button)?.IsEnabled == true);

        // Списки строятся из каталога, поэтому проверяем именно их содержимое.
        check.Equal("направления из каталога", Items(w, "BrandCombo"),
                    "MyCups, CupToYou, Formacia (флексо)");
        check.Equal("направление по умолчанию", Selected(w, "BrandCombo"), "MyCups");
        check.Equal("типы продуктов из каталога", Items(w, "TypeCombo"),
                    "Стаканы, Пластик, Сахар, Шоколад, Конфеты");
        check.True("строка «Вариант» у стаканов скрыта", !Visible(w, "VariantCombo"));
        check.True("строка «Страна» у MyCups скрыта", !Visible(w, "CountryCombo"));
        check.True("строка «Материал» у стаканов показана", Visible(w, "MaterialCombo"));

        // Шоколад: печать выбирать не из чего, зато появляются вкусы.
        if (w.FindName("TypeCombo") is ComboBox types)
        {
            for (int i = 0; i < types.Items.Count; i++)
            {
                if (types.Items[i]?.ToString() == "Шоколад")
                {
                    types.SelectedIndex = i;
                    break;
                }
            }

            check.Equal("шоколад → подобран продукт",
                        (w.FindName("ManualProductInfo") as TextBlock)?.Text, "Продукт: MyCups — шоколад");
            check.True("шоколад → строка «Печать» скрыта (выбирать не из чего)", !Visible(w, "TechCombo"));
            check.True("шоколад → строка «Вариант» показана", Visible(w, "VariantCombo"));
            check.Equal("шоколад → вкусы из каталога", Items(w, "VariantCombo"),
                        "Молочный, Тёмный, Апельсин, Клубника, Белый");
            check.True("шоколад → строка «Материал» скрыта", !Visible(w, "MaterialCombo"));
        }

        WizardCancelled(check);

        // Панель проверки данных должна сворачиваться.
        if (w.FindName("SpecBody") is Grid body && w.FindName("SpecToggle") is Button toggle)
        {
            var before = body.Visibility;
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            check.True("«Проверка данных» сворачивается", before != body.Visibility);
            check.Equal("шеврон меняется",
                        (w.FindName("SpecChevron") as TextBlock)?.Text,
                        body.Visibility == Visibility.Visible ? "⌃" : "⌄");
        }
    }

    /// <summary>
    /// Все токены на месте и разбираются.
    ///
    /// В WPF опечатка в имени ресурса и кривая геометрия иконки молчат до
    /// открытия окна — а окно у дизайнера открывается позже, чем прогон у нас.
    /// Список ключей в Theme.RequiredKeys и есть договор между токенами и
    /// разметкой: убрали токен, не поправив окна, — краснеет здесь, а не там.
    /// </summary>
    private static void CheckTokens(Checker check, Application app)
    {
        var missing = new List<string>();
        foreach (string key in Theme.RequiredKeys)
        {
            if (!app.Resources.Contains(key))
                missing.Add(key);
        }

        check.True(missing.Count == 0
                ? "дизайн-токены на месте"
                : "дизайн-токены на месте — не найдены: " + string.Join(", ", missing),
            missing.Count == 0);

        // Геометрия иконок разбирается только при обращении: битая строка пути
        // до этого момента выглядит как обычный текст.
        int icons = 0;
        var broken = new List<string>();
        foreach (string key in Theme.RequiredKeys)
        {
            if (!key.StartsWith("I.", StringComparison.Ordinal))
                continue;

            try
            {
                if (app.Resources[key] is Geometry g && !g.IsEmpty())
                    icons++;
                else
                    broken.Add(key);
            }
            catch (Exception ex)
            {
                broken.Add($"{key} ({ex.Message})");
            }
        }

        check.True(broken.Count == 0
                ? $"иконки разбираются в геометрию ({icons} шт.)"
                : "иконки разбираются в геометрию — сломаны: " + string.Join(", ", broken),
            broken.Count == 0);
    }

    /// <summary>
    /// Отмена мастера настройки должна что-то менять. Раньше EnsureConfigured
    /// возвращал признак, который никто не проверял: человек жал «Отмена»,
    /// окно открывалось как ни в чём не бывало, «Создать проект» звало сборщик
    /// с несуществующими папками — и вместо «настройте рабочее место» дизайнер
    /// получал ошибку про ненайденный файл шаблона.
    /// </summary>
    private static void WizardCancelled(Checker check)
    {
        MachineProfile saved = MachineProfile.Current;
        try
        {
            // Профиль с заведомо отсутствующими папками — мастер обязан понадобиться.
            MachineProfile.Set(MachineProfile.FromStakanyRoot(
                Path.Combine(Path.GetTempPath(), "cupsforge_selfcheck_ui", "нет-такой-папки")));

            check.True("без рабочих папок мастер нужен", SettingsWindow.NeedsWizard(out string why));
            check.True("причина названа словами", !string.IsNullOrWhiteSpace(why));

            // Имитируем «Отмена» — показать настоящий диалог прогон не может.
            SettingsWindow.WizardOverride = _ => false;
            var window = new AutoWindow();
            try
            {
                window.Show();
                check.True("отмена мастера гасит «Создать проект»",
                    (window.FindName("BuildButton") as Button)?.IsEnabled == false);
                check.True("отмена мастера объясняется на экране",
                    (window.FindName("SetupWarningBar") as UIElement)?.Visibility == Visibility.Visible);

                // Настоящая ловушка: кнопка выключена и так, по разметке. Но карандаш
                // включал её безусловно — и «Создать проект» звал сборщик с папками,
                // которых нет. Ручной ввод не обходит ненастроенное рабочее место.
                (window.FindName("PencilButton") as Button)?
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                check.True("ручной ввод не обходит ненастроенное рабочее место",
                    (window.FindName("BuildButton") as Button)?.IsEnabled == false);
            }
            finally
            {
                window.Close();
            }
        }
        catch (Exception ex)
        {
            check.Fail("проверка отмены мастера сорвалась: " + ex.Message);
        }
        finally
        {
            SettingsWindow.WizardOverride = null;
            MachineProfile.Set(saved);
        }
    }

    private static bool Visible(Window w, string name) =>
        (w.FindName(name) as UIElement)?.Visibility == Visibility.Visible;

    private static string Items(Window w, string name) =>
        w.FindName(name) is ComboBox c
            ? string.Join(", ", c.Items.Cast<object?>().Select(i => i?.ToString() ?? "?"))
            : "(списка нет)";

    private static string Selected(Window w, string name) =>
        (w.FindName(name) as ComboBox)?.SelectedItem?.ToString() ?? "(ничего)";

    private sealed class BindingErrorListener : TraceListener
    {
        private readonly Checker _check;
        private readonly System.Text.StringBuilder _buffer = new();

        public BindingErrorListener(Checker check) => _check = check;

        public override void Write(string? message) => _buffer.Append(message);

        public override void WriteLine(string? message)
        {
            _buffer.Append(message);
            string line = _buffer.ToString().Trim();
            _buffer.Clear();
            if (line.Length > 0)
                _check.Fail("ошибка привязки WPF: " + line);
        }
    }
}
