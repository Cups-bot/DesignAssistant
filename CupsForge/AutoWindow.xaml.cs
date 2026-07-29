using System.Windows;
using System.Windows.Input;
using CupsCore;
using CupsForge.Models;
using CupsForge.Services;

namespace CupsForge
{
    public partial class AutoWindow : Window
    {
        private readonly AppConfig _config = AppConfig.Load();
        private ResolvedDesign? _resolved;

        public AutoWindow()
        {
            InitializeComponent();

            // Окно настроек живёт в общем слое и о Bitrix не знает — даём ему
            // проверку связи отсюда, где есть HTTP-клиент.
            SettingsWindow.ConnectionTest = BitrixProbe.TestAsync;

            // Мастер настройки покажется только если рабочих папок на этой машине нет
            // (первый запуск на домашнем компьютере). В офисе окно не появляется.
            Loaded += (_, _) =>
            {
                SettingsWindow.EnsureConfigured(this);
                SetSpecExpanded(MachineProfile.Current.SpecPanelExpanded, remember: false);
                ReportCatalog();
                CheckForUpdate();
                CheckDistribution();
            };
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new SettingsWindow { Owner = this };
            if (window.ShowDialog() == true)
                Log("Настройки сохранены.");
        }

        // ---------- окно ----------
        private void Close_Click(object sender, MouseButtonEventArgs e) => Close();
        private void Minimize_Click(object sender, MouseButtonEventArgs e) => WindowState = WindowState.Minimized;

        // Высота окна меняется автоматически (SizeToContent) при появлении/скрытии
        // панели проверки. Держим нижний край на месте — окно растёт вверх.
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!IsLoaded || !e.HeightChanged)
                return;

            double delta = e.NewSize.Height - e.PreviousSize.Height;
            Top -= delta;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        // ---------- получение данных ----------
        private void LinkBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) _ = FetchAsync();
        }

        private void FetchButton_Click(object sender, RoutedEventArgs e) => _ = FetchAsync();

        private async Task FetchAsync()
        {
            _resolved = null;
            // В ручном режиме кнопка живёт своей жизнью: гасить её из-за загрузки
            // заказа нельзя, иначе случайный Enter в поле ссылки блокирует ручной ввод.
            if (!_manualMode)
                BuildButton.IsEnabled = false;
            SpecPanel.Visibility = Visibility.Collapsed;
            WarnPanel.Visibility = Visibility.Collapsed;

            if (!LinkParser.TryParseId(LinkBox.Text, out long id, out string err))
            {
                Log(err);
                return;
            }

            string auth = _config.Bitrix.ResolveAuthHeader();
            if (string.IsNullOrEmpty(auth))
            {
                Log("Не задан доступ к Bitrix. Откройте настройки и укажите логин и пароль.");
                return;
            }

            SetBusy(true);
            Log($"Запрос данных заказа #{id}…");

            try
            {
                using var client = new BitrixClient(_config.Bitrix);
                DesignData data = await client.GetDataAsync(id);

                // Код дизайна берём из названия заказа (отдельный эндпоинт не нужен).
                var resolved = BitrixMapper.Map(data, "");
                _resolved = resolved;

                ShowSpec(resolved);

                // Артикул обязателен не всегда: у шоколада шаблон выбирается по вкусу,
                // и пустой артикул — норма. Спрашиваем у каталога, а не гадаем.
                bool needsArticle = CatalogService.Current.Match(resolved.ToBuildRequest().Spec)
                                        is not { Variants.Count: > 0 };
                BuildButton.IsEnabled = !string.IsNullOrWhiteSpace(resolved.DesignCode)
                                        && (!needsArticle || !string.IsNullOrWhiteSpace(resolved.ProductArticul));
                Log($"Данные получены: {resolved.Brand} · {resolved.ProductArticul}. Проверьте и нажмите «Создать проект».");
            }
            catch (BitrixException bex)
            {
                Log("Ошибка: " + bex.Message);
            }
            catch (Exception ex)
            {
                Log("Сбой запроса: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void ShowSpec(ResolvedDesign r)
        {
            ValBrand.Text    = string.IsNullOrWhiteSpace(r.RawProject) ? r.Brand.ToString() : $"{r.RawProject}  →  {r.Brand}";
            ValOrder.Text    = r.Id.ToString();
            ValName.Text     = r.OrderName;
            ValType.Text     = string.IsNullOrWhiteSpace(r.RawType) ? r.ProductType : $"{r.RawType}  →  {r.ProductType}";
            ValArticul.Text  = string.IsNullOrWhiteSpace(r.ProductArticul) ? "—" : r.ProductArticul;
            ValPrint.Text    = $"{r.RawPrint}  →  {r.PrintTech}";
            ValMaterial.Text = $"{r.RawSide}  →  {r.Material}";
            ValCoating.Text  = string.IsNullOrWhiteSpace(r.RawCoating) ? "—" : $"{r.RawCoating}  →  {r.Coating}";
            // Вкус показывается там, где он есть: сверять его глазами тоже нужно.
            ValVariant.Text  = string.IsNullOrWhiteSpace(r.Variant)
                ? "—"
                : (string.IsNullOrWhiteSpace(r.RawFlavor) ? r.Variant : $"{r.RawFlavor}  →  {r.Variant}");

            ValCountry.Text  = r.Brand == Brand.CuptoYou
                ? (string.IsNullOrWhiteSpace(r.RawLang) ? r.Country.ToString() : $"{r.RawLang}  →  {r.Country}")
                : "—";

            if (r.Warnings.Count > 0)
            {
                WarnText.Text = "⚠ " + string.Join("\n⚠ ", r.Warnings);
                WarnPanel.Visibility = Visibility.Visible;
            }
            else
            {
                WarnPanel.Visibility = Visibility.Collapsed;
            }

            SpecPanel.Visibility = Visibility.Visible;
        }

        // ---------- создание проекта ----------
        private void BuildButton_Click(object sender, RoutedEventArgs e)
        {
            // Источник один и тот же ProjectBuilder — меняется только то,
            // откуда взялись параметры: из Bitrix или из полей ручного ввода.
            if (_manualMode)
            {
                ReportBuild(ProjectBuilder.Build(BuildManualRequest()));
                return;
            }

            if (_resolved == null)
            {
                Log("Сначала загрузите данные заказа.");
                return;
            }

            BuildResult result = ProjectBuilder.Build(_resolved.ToBuildRequest());
            ReportBuild(result);
        }

        private void ReportBuild(BuildResult result)
        {
            foreach (var line in result.Log)
                Log(line);

            if (result.Success && !result.AlreadyExisted)
                Log("Готово.");
        }

        // ---------- ручной ввод ----------

        /// <summary>
        /// Карандаш раскрывает панель ручного ввода прямо здесь. Раньше он запускал
        /// вторую программу по жёстко прописанному пути и передавал данные через
        /// временный файл — на чужой машине этот путь всегда был неверным.
        /// </summary>
        private void PencilButton_Click(object sender, RoutedEventArgs e)
        {
            bool show = ManualPanel.Visibility != Visibility.Visible;
            ToggleManual(show);

            if (!show)
                return;

            // Если заказ загружен — переносим его в поля, чтобы поправить и создать.
            if (_resolved != null)
            {
                FillManualFrom(_resolved);
                Log("Данные заказа перенесены в поля ручного ввода.");
            }
        }

        /// <summary>
        /// Говорит, откуда взят каталог. Если работаем на копии внутри программы,
        /// а файл на машине настроен — правки дизайнера не действуют, и молчать
        /// об этом нельзя: со стороны выглядит как «замена каталога не помогает».
        /// </summary>
        private void ReportCatalog()
        {
            try
            {
                var catalog = CatalogService.Current;
                Log($"Каталог: версия {catalog.Version} от {catalog.Updated} — {catalog.SourceName}");

                if (!CatalogService.IsUsingEmbedded)
                    return;

                string expected = CatalogService.ExpectedPath;
                if (string.IsNullOrWhiteSpace(expected))
                    return;

                CatalogWarning.Text =
                    "Каталог взят из копии внутри программы — правки файла не действуют. " +
                    $"Ожидался здесь: {expected}";
                CatalogWarningBar.Visibility = Visibility.Visible;

                foreach (string line in CatalogService.LoadLog)
                    Log("  " + line);
            }
            catch (CatalogException ex)
            {
                Log(ex.Message);
            }
        }

        // ---------- обновление ----------

        private ReleaseInfo? _update;

        /// <summary>
        /// Тихо смотрит, нет ли на раздаче версии новее. Ничего не нашли или
        /// раздача недоступна (удалёнка) — пользователь об этом не узнаёт.
        /// </summary>
        private void CheckForUpdate()
        {
            try
            {
                _update = Updater.Check(out string? diagnosis);
                if (diagnosis != null)
                    Log(diagnosis);
            }
            catch (Exception ex)
            {
                Log("Проверка обновлений не удалась: " + ex.Message);
                return;
            }

            if (_update == null)
                return;

            UpdateText.Text = $"Доступна версия {_update.Version}" +
                              (string.IsNullOrWhiteSpace(_update.Notes) ? "" : $" · {_update.Notes}");
            UpdateBar.Visibility = Visibility.Visible;
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateButton.IsEnabled = false;
            UpdateText.Text = "Обновление…";

            // Из раздачи: качаем по сети и подменяем.
            if (_update == null && _distApp != null)
            {
                var (data, downloadError) = await DistributionClient.DownloadAppAsync(_distApp);
                if (data == null)
                {
                    UpdateButton.IsEnabled = true;
                    UpdateText.Text = $"Доступна версия {_distApp.Version}";
                    Log(downloadError ?? "Не удалось скачать обновление.");
                    return;
                }

                if (Updater.ApplyFile(data, out string applyError))
                {
                    Application.Current.Shutdown();
                    return;
                }

                UpdateButton.IsEnabled = true;
                UpdateText.Text = $"Доступна версия {_distApp.Version}";
                Log(applyError);
                return;
            }

            if (_update == null)
            {
                UpdateBar.Visibility = Visibility.Collapsed;
                return;
            }

            // Заменить работающий файл нельзя, поэтому подмену делает скрипт:
            // он ждёт закрытия программы и запускает её заново.
            if (Updater.Apply(_update, out string error))
            {
                Application.Current.Shutdown();
                return;
            }

            UpdateButton.IsEnabled = true;
            UpdateText.Text = $"Доступна версия {_update.Version}";
            Log(error);
        }

        // ---------- сворачивание панели проверки ----------

        private void SpecToggle_Click(object sender, RoutedEventArgs e) =>
            SetSpecExpanded(SpecBody.Visibility != Visibility.Visible, remember: true);

        private void SetSpecExpanded(bool expanded, bool remember)
        {
            SpecBody.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            SpecChevron.Text = expanded ? "⌃" : "⌄";

            if (!remember)
                return;

            // Состояние переживает перезапуск — оно в профиле рабочего места.
            var profile = MachineProfile.Current;
            if (profile.SpecPanelExpanded == expanded)
                return;

            profile.SpecPanelExpanded = expanded;
            try { profile.Save(); }
            catch (Exception ex) { Log("Не удалось сохранить настройку панели: " + ex.Message); }
        }

        private void ToggleLogButton_Click(object sender, RoutedEventArgs e)
        {
            bool show = LogPanel.Visibility != Visibility.Visible;
            LogPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            ToggleLogButton.Content = show ? "📄 Скрыть журнал" : "📄 Показать журнал";
        }

        // ---------- служебное ----------
        private void SetBusy(bool busy)
        {
            FetchButton.IsEnabled = !busy;
            LinkBox.IsEnabled = !busy;
            Cursor = busy ? Cursors.Wait : Cursors.Arrow;
        }

        private void Log(string message)
        {
            LogBox.AppendText($"{message}{Environment.NewLine}");
            LogBox.ScrollToEnd();
        }
    }
}
