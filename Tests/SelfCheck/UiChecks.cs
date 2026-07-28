using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        Directory.CreateDirectory(Path.Combine(sandbox, "_Templates"));
        MachineProfile.Set(profile);
        Paths.ResetIllustratorCache();
        CatalogService.Reload();

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
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
