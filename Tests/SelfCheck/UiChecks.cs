using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CupsCore;
using CupsForge;
using CupsForge.Models;

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

        // Создаём ВСЕ рабочие папки: при полном комплекте панель настроек
        // не открывается сама, и окно начинается с обычного пустого состояния.
        // Случай «папок нет» проверяется отдельно, в WizardCancelled.
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

        // Скругление углов снимком не проверить: его режет композитор Windows
        // уже поверх окна, а RenderTargetBitmap снимает только содержимое.
        // Зато можно спросить саму Windows, приняла ли она запрос.
        // На Windows 10 атрибута нет — там ответ «нет», и это не провал.
        bool rounded = WindowRounding.Apply(w);
        check.Info(rounded
            ? "скругление углов: Windows запрос приняла"
            : "скругление углов: Windows запрос отклонила (ожидаемо до Windows 11)");
        check.True("запрос на скругление не падает", true);

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

        FixOneField(check, w, width, height);

        // Настройки — панель справа ПОВЕРХ сцены, не второе окно.
        if (w.FindName("SettingsButton") is Button gear)
        {
            gear.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            check.True("шестерёнка открывает панель настроек", Visible(w, "SettingsOverlay"));
            Snapshot(w, "4-настройки");
            SameSize(check, w, "настройки", width, height);

            if (w.FindName("SettingsHost") is ContentControl host &&
                host.Content is SettingsPanel panel &&
                panel.FindName("CancelButton") is Button cancel)
            {
                cancel.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                check.True("на настроенном месте панель закрывается отменой",
                           !Visible(w, "SettingsOverlay"));
            }
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

        CheckIconsMatchSvg(check, app);
    }

    /// <summary>
    /// Значки в программе совпадают с исходными SVG.
    ///
    /// Дизайнер правит Icons\*.svg в редакторе, sync-icons.cmd переносит их в
    /// Icons.xaml. Забыть второй шаг легко, и тогда получается худший вид
    /// расхождения: в репозитории значок новый, в программе старый, и никто
    /// об этом не узнает. Сверяем разобранную геометрию, а не текст: одна и та
    /// же фигура записывается по-разному («M5,12» и «M 5 12»), и сравнение
    /// строк ругалось бы на пустом месте.
    /// </summary>
    private static void CheckIconsMatchSvg(Checker check, Application app)
    {
        var assembly = typeof(CupsForge.AutoWindow).Assembly;
        var svgNames = assembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (svgNames.Count == 0)
        {
            check.Fail("исходные SVG значков не вложены в сборку");
            return;
        }

        var stale = new List<string>();
        int compared = 0;

        foreach (string resource in svgNames)
        {
            string file = resource[(resource.LastIndexOf('.', resource.Length - 5) + 1)..];
            string key = "I." + string.Concat(
                Path.GetFileNameWithoutExtension(file).Split('-')
                    .Where(p => p.Length > 0)
                    .Select(p => char.ToUpperInvariant(p[0]) + p[1..]));

            string svg;
            using (var stream = assembly.GetManifestResourceStream(resource))
            {
                if (stream == null) continue;
                using var reader = new StreamReader(stream);
                svg = reader.ReadToEnd();
            }

            var paths = System.Text.RegularExpressions.Regex.Matches(svg, "<path[^>]*\\sd=\"([^\"]+)\"");
            if (paths.Count == 0)
            {
                stale.Add($"{file} (нет <path d=…>)");
                continue;
            }

            string data = string.Join(" ", paths.Select(m => m.Groups[1].Value.Trim()));

            if (app.Resources[key] is not Geometry inProgram)
            {
                stale.Add($"{file} (в программе нет {key})");
                continue;
            }

            try
            {
                if (Geometry.Parse(data).ToString() != inProgram.ToString())
                    stale.Add(file);
                else
                    compared++;
            }
            catch (Exception ex)
            {
                stale.Add($"{file} ({ex.Message})");
            }
        }

        check.True(stale.Count == 0
                ? $"значки совпадают с исходными SVG ({compared} шт.)"
                : "значки разошлись с SVG — запустите sync-icons.cmd: " + string.Join(", ", stale),
            stale.Count == 0);
    }

    /// <summary>
    /// Ошибка чинится на месте, одним полем.
    ///
    /// Разбор Bitrix ошибается обычно в одном поле из семи. Раньше клик по строке
    /// открывал ручной ввод целиком, и дизайнер перезаполнял шесть верных полей
    /// ради одного неверного. Проверяем весь путь: результат → клик по строке →
    /// лист → выбор → значение изменилось, окно не выросло.
    /// </summary>
    private static void FixOneField(Checker check, AutoWindow w, double width, double height)
    {
        // Заказ как из Bitrix, но без похода в сеть.
        w.ShowResult(new ResolvedDesign
        {
            Id = 132583,
            OrderName = "CarBar (132583 CarBar ST DW90-430)",
            DesignCode = "132583 CarBar ST DW90-430",
            Brand = Brand.MyCups,
            ProductType = ProductTypes.Cups,
            ProductArticul = "DW90-430",
            PrintTech = PrintTech.Offset,
            Material = Material.Uncoated,
            RawSide = "Белый немелованный"
        });

        check.True("разобранный заказ показывается результатом", Visible(w, "StateResult"));
        check.True("«Создать проект» доступно на разобранном заказе",
                   (w.FindName("BuildButton") as Button)?.IsEnabled == true);
        Snapshot(w, "5-результат");

        if (w.FindName("ResultFields") is not ItemsControl fields)
        {
            check.Fail("список полей результата не найден");
            return;
        }

        // Строки — данные: состав собран из заказа, пустые не показываются.
        check.True($"поля результата собраны из данных ({fields.Items.Count} строк)",
                   fields.Items.Count >= 5);

        var material = fields.Items.Cast<ResultField>()
            .FirstOrDefault(f => f.Label == "Материал");

        if (material == null)
        {
            check.Fail("строки «Материал» нет в результате");
            return;
        }

        check.True("у поправимой строки есть ключ правки", !string.IsNullOrEmpty(material.FixKey));

        // Открываем лист так же, как пользователь: через кнопку строки.
        var row = FindRowButton(fields, material);
        if (row == null)
        {
            check.Fail("строка «Материал» не нажимается");
            return;
        }

        row.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        check.True("клик по строке открывает лист правки", Visible(w, "FixOverlay"));
        SameSize(check, w, "лист правки", width, height);
        Snapshot(w, "6-правка-поля");

        check.Equal("лист назван по правимому полю",
                    (w.FindName("FixTitle") as TextBlock)?.Text, "Выберите материал");

        if (w.FindName("FixOptions") is not ItemsControl options || options.Items.Count == 0)
        {
            check.Fail("в листе правки нет вариантов");
            return;
        }

        check.Equal("варианты материала",
                    string.Join(", ", options.Items.Cast<FixOption>().Select(o => o.Label)),
                    "Немелованный, Мелованный");

        // Выбираем «Мелованный» — значение в результате обязано смениться.
        var pick = options.ItemContainerGenerator.Items.Cast<FixOption>()
            .Select((o, i) => (o, i)).First(p => p.o.Label == "Мелованный");

        if (FindOptionButton(options, pick.o) is not Button optionButton)
        {
            check.Fail("вариант «Мелованный» не нажимается");
            return;
        }

        optionButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        check.True("лист закрывается после выбора", !Visible(w, "FixOverlay"));

        var after = (w.FindName("ResultFields") as ItemsControl)?.Items.Cast<ResultField>()
            .FirstOrDefault(f => f.Label == "Материал");
        check.Equal("правка применилась к результату", after?.Value, "Coated");

        // Правка одного поля не должна ронять остальные.
        check.Equal("остальные поля не потерялись",
                    (w.FindName("ResultFields") as ItemsControl)?.Items.Cast<ResultField>()
                        .FirstOrDefault(f => f.Label == "Артикул")?.Value,
                    "DW90-430");
    }

    private static Button? FindRowButton(ItemsControl list, ResultField field) =>
        FindButton(list, field);

    private static Button? FindOptionButton(ItemsControl list, FixOption option) =>
        FindButton(list, option);

    /// <summary>Кнопка строки по её данным: нажимаем так же, как пользователь.</summary>
    private static Button? FindButton(ItemsControl list, object item)
    {
        list.UpdateLayout();
        var container = list.ItemContainerGenerator.ContainerFromItem(item) as DependencyObject;
        return container == null ? null : Descendant<Button>(container);
    }

    private static T? Descendant<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T hit)
            return hit;

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var found = Descendant<T>(VisualTreeHelper.GetChild(root, i));
            if (found != null)
                return found;
        }
        return null;
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

            check.True("без рабочих папок мастер нужен", SettingsPanel.NeedsWizard(out string why));
            check.True("причина названа словами", !string.IsNullOrWhiteSpace(why));

            // Раньше здесь стояла подмена показа диалога: мастер был модальным
            // окном, и прогон на нём вис — закрыть его некому. Панель внутри
            // окна проверяется как обычная разметка, шов больше не нужен.
            var window = new AutoWindow();
            try
            {
                window.Show();
                window.UpdateLayout();

                check.True("панель настроек открывается сама",
                    (window.FindName("SettingsOverlay") as UIElement)?.Visibility == Visibility.Visible);
                check.True("ненастроенное место гасит «Создать проект»",
                    (window.FindName("BuildButton") as Button)?.IsEnabled == false);
                check.True("ненастроенное место объясняется на экране",
                    (window.FindName("NoticeBar") as UIElement)?.Visibility == Visibility.Visible);
                check.True("причина видна в самом уведомлении",
                    !string.IsNullOrWhiteSpace((window.FindName("NoticeText") as TextBlock)?.Text));

                // Закрыть панель, ничего не настроив, нельзя: за ней всё равно
                // нет ничего, чем можно пользоваться.
                if (window.FindName("SettingsHost") is ContentControl host &&
                    host.Content is SettingsPanel panel &&
                    panel.FindName("CancelButton") is Button cancel)
                {
                    cancel.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    check.True("отмена не закрывает панель, пока папок нет",
                        (window.FindName("SettingsOverlay") as UIElement)?.Visibility == Visibility.Visible);
                }
                else
                {
                    check.Fail("панель настроек не найдена внутри окна");
                }

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
