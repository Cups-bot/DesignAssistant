using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        // Габарит окна — константа во всех состояниях. Это главное обещание
        // редизайна: раньше стояло SizeToContent, окно росло от каждой панели,
        // и кнопка «Создать проект» ездила на десятки пикселей между заказами.
        double width = w.ActualWidth;
        double height = w.ActualHeight;
        Snapshot(w, "1-пусто");

        check.True("окно начинается пустым состоянием",
                   Visible(w, "StateEmpty") && !Visible(w, "StateManual"));

        if (w.FindName("PencilButton") is not Button pencil)
        {
            check.Fail("кнопка карандаша не найдена");
            return;
        }

        pencil.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        check.True("карандаш переводит окно в ручной ввод",
                   Visible(w, "StateManual") && !Visible(w, "StateEmpty"));
        SameSize(check, w, "ручной ввод", width, height);
        Snapshot(w, "2-ручной-ввод");

        // «Создать проект» существует только когда есть что создавать: пустое
        // имя дизайна — не повод звать сборщик, чтобы тот пожаловался в журнал.
        check.True("без имени дизайна создавать нечего",
                   (w.FindName("ManualBuildButton") as Button)?.IsEnabled == false);

        if (w.FindName("ManualNameBox") is TextBox nameBox)
        {
            nameBox.Text = "132583 CarBar ST DW90-430";
            check.True("на настроенном месте ручной ввод разрешает «Создать проект»",
                       (w.FindName("ManualBuildButton") as Button)?.IsEnabled == true);
        }

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

        // Журнал — шторка ПОВЕРХ сцены. Раньше он разворачивал окно вниз,
        // и всё, что ниже, уезжало. Проверяем и то, что открылся, и то,
        // что окно от этого не выросло.
        if (w.FindName("JournalToggle") is Button journal)
        {
            journal.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            check.True("журнал открывается шторкой", Visible(w, "JournalOverlay"));
            Snapshot(w, "3-журнал");
            SameSize(check, w, "журнал", width, height);

            journal.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            check.True("журнал закрывается", !Visible(w, "JournalOverlay"));
        }

        // Возврат из ручного ввода — окно по-прежнему того же размера.
        if (w.FindName("ManualCloseButton") is Button back)
        {
            back.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            check.True("закрытие ручного ввода возвращает к ссылке", Visible(w, "StateEmpty"));
            SameSize(check, w, "возврат к ссылке", width, height);
        }

        WizardCancelled(check);
    }

    /// <summary>
    /// Даёт окну пожить указанное время, не блокируя его поток: обычный Sleep
    /// на потоке интерфейса остановил бы и анимации, которых мы ждём.
    /// </summary>
    private static void Pump(Window w, TimeSpan time)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(time, DispatcherPriority.Background,
            (_, _) => frame.Continue = false, w.Dispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    /// <summary>
    /// Снимок окна в файл. Зелёная проверка говорит, что элементы на месте,
    /// но не что окно выглядит правильно: съехавший отступ, невидимый на тёмном
    /// текст и не прокрасившийся системный контрол проверками не ловятся.
    ///
    /// Включается переменной среды CUPSFORGE_SNAPSHOT=папка — по умолчанию
    /// прогон файлов не пишет.
    /// </summary>
    private static void Snapshot(Window w, string name)
    {
        string? folder = Environment.GetEnvironmentVariable("CUPSFORGE_SNAPSHOT");
        if (string.IsNullOrWhiteSpace(folder))
            return;

        try
        {
            Directory.CreateDirectory(folder);

            // Без принудительного пересчёта снимок берёт дерево ДО перестройки:
            // состояние уже переключено, а на картинке предыдущее (и чаще пустое).
            // Снимок, который врёт, хуже отсутствующего.
            //
            // Пересчёта мало: шторка выезжает анимацией, и снимок ловил её на
            // полпути — низ оказывался срезан, и это выглядело как ошибка вёрстки.
            // Крутим очередь сообщений, пока анимации не встанут.
            w.UpdateLayout();
            Pump(w, TimeSpan.FromMilliseconds(450));
            w.UpdateLayout();

            // Масштаб 2x: на снимке видно сглаживание и полупиксельные отступы.
            var bitmap = new RenderTargetBitmap(
                (int)(w.ActualWidth * 2), (int)(w.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
            bitmap.Render(w);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var stream = File.Create(Path.Combine(folder, name + ".png"));
            encoder.Save(stream);
        }
        catch
        {
            // Снимок — удобство, а не проверка: не вышло, и ладно.
        }
    }

    /// <summary>
    /// Габарит окна не изменился. Вынесено отдельно: это обещание нарушается
    /// незаметно — достаточно одного SizeToContent или панели, вставленной
    /// в строку сетки вместо слоя поверх.
    /// </summary>
    private static void SameSize(Checker check, Window w, string what, double width, double height)
    {
        bool same = Math.Abs(w.ActualWidth - width) < 0.5 &&
                    Math.Abs(w.ActualHeight - height) < 0.5;

        check.True(same
                ? $"габарит окна не изменился ({what})"
                : $"габарит окна не изменился ({what}) — было {width}x{height}, стало {w.ActualWidth}x{w.ActualHeight}",
            same);
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
                    (window.FindName("NoticeBar") as UIElement)?.Visibility == Visibility.Visible);
                check.True("причина видна в самом уведомлении",
                    !string.IsNullOrWhiteSpace((window.FindName("NoticeText") as TextBlock)?.Text));

                // Настоящая ловушка: кнопка выключена и так, по разметке. Но карандаш
                // включал её безусловно — и «Создать проект» звал сборщик с папками,
                // которых нет. Ручной ввод не обходит ненастроенное рабочее место.
                (window.FindName("PencilButton") as Button)?
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                if (window.FindName("ManualNameBox") is TextBox box)
                    box.Text = "132583 CarBar ST DW90-430";

                check.True("ручной ввод не обходит ненастроенное рабочее место",
                    (window.FindName("ManualBuildButton") as Button)?.IsEnabled == false);
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
