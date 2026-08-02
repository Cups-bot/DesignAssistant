using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using CupsCore;
using CupsForge.Models;
using CupsForge.Services;

namespace CupsForge
{
    public partial class AutoWindow : Window
    {
        private readonly AppConfig _config = AppConfig.Load();
        private ResolvedDesign? _resolved;

        /// <summary>
        /// Состояния окна. Ровно одно видно в каждый момент — это и есть
        /// «один следующий шаг»: пока идёт проверка, кнопки «Создать проект»
        /// не существует, а не «она есть, но погашена».
        ///
        /// empty → checking → result → running → done → empty
        /// </summary>
        private enum Stage { Empty, Checking, Result, Running, Done, Manual }

        private Stage _stage = Stage.Empty;

        public AutoWindow()
        {
            InitializeComponent();

            // Окно настроек живёт в общем слое и о Bitrix не знает — даём ему
            // проверку связи отсюда, где есть HTTP-клиент.
            SettingsPanel.ConnectionTest = BitrixProbe.TestAsync;

            ResultFields.ItemsSource = _fields;
            InitFixSheet();
            LogBox.ItemsSource = _log;
            FooterHint.Text = "v" + Updater.CurrentVersion;

            // Углы скругляет композитор Windows. Через прозрачность было бы
            // проще, но она отключает ClearType, и весь текст становится мягче.
            WindowRounding.Attach(this);

            Loaded += (_, _) =>
            {
                // Настроено ли рабочее место. Если нет — панель открывается сама
                // и не закрывается, пока папки не найдутся: за ней всё равно нет
                // ничего, чем можно пользоваться.
                bool needs = SettingsPanel.NeedsWizard(out string why);
                ApplySetupState(!needs);
                if (needs)
                    OpenSettings(why);
                ReportCatalog();
                CheckForUpdate();
                CheckDistribution();
                SetStage(Stage.Empty);
                OfferClipboard();
            };

            // Ссылку могли скопировать, пока окно было свёрнуто.
            Activated += (_, _) =>
            {
                if (_stage == Stage.Empty)
                    OfferClipboard();
            };
        }

        // ═══════════ окно ═══════════

        private void Close_Click(object sender, MouseButtonEventArgs e) => Close();
        private void Minimize_Click(object sender, MouseButtonEventArgs e) => WindowState = WindowState.Minimized;

        // ═══════════ состояния ═══════════

        private void SetStage(Stage stage)
        {
            _stage = stage;

            StateEmpty.Visibility    = Visible(stage == Stage.Empty);
            StateChecking.Visibility = Visible(stage == Stage.Checking);
            StateResult.Visibility   = Visible(stage == Stage.Result);
            StateRunning.Visibility  = Visible(stage == Stage.Running);
            StateDone.Visibility     = Visible(stage == Stage.Done);
            StateManual.Visibility   = Visible(stage == Stage.Manual);

            _manualMode = stage == Stage.Manual;

            // Крутилки живут только в своём состоянии: анимация под невидимым
            // слоем продолжала бы будить отрисовку без всякой пользы.
            Spin(SpinnerRotation, stage == Stage.Checking);
            Slide(RunningBarShift, stage == Stage.Running);

            RefreshBuildAvailability();

            if (stage == Stage.Empty)
                LinkBox.Focus();
        }

        private static Visibility Visible(bool show) => show ? Visibility.Visible : Visibility.Collapsed;

        private void Spin(System.Windows.Media.RotateTransform target, bool run)
        {
            if (!run)
            {
                target.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
                return;
            }

            target.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(0.85)))
                {
                    RepeatBehavior = RepeatBehavior.Forever
                });
        }

        private void Slide(System.Windows.Media.TranslateTransform target, bool run)
        {
            if (!run)
            {
                target.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
                return;
            }

            target.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
                new DoubleAnimation(-120, 360, new Duration(TimeSpan.FromSeconds(1.1)))
                {
                    RepeatBehavior = RepeatBehavior.Forever
                });
        }

        // ═══════════ уведомления ═══════════

        private readonly List<Notice> _notices = new();

        /// <summary>
        /// Ставит сообщение в очередь. Повторный вызов с тем же Id заменяет
        /// прежнее, а не добавляет второе: иначе за смену накапливается десяток
        /// одинаковых «Доступна версия».
        /// </summary>
        private void ShowNotice(string id, string text, NoticeKind kind,
                                string? actionTitle = null, Action? action = null)
        {
            _notices.RemoveAll(n => n.Id == id);
            _notices.Add(new Notice
            {
                Id = id, Text = text, Kind = kind,
                ActionTitle = actionTitle, Action = action
            });

            // Блокирующие — вперёд: без них работать всё равно нельзя.
            _notices.Sort((a, b) => b.Kind.CompareTo(a.Kind));
            RefreshNotice();
        }

        private void DropNotice(string id)
        {
            _notices.RemoveAll(n => n.Id == id);
            RefreshNotice();
        }

        private void RefreshNotice()
        {
            Notice? top = _notices.Count > 0 ? _notices[0] : null;
            if (top == null)
            {
                NoticeBar.Visibility = Visibility.Collapsed;
                return;
            }

            NoticeText.Text = top.Text;
            NoticeIcon.Data = top.Icon;
            NoticeIcon.Stroke = top.IconBrush;

            NoticeAction.Content = top.ActionTitle;
            NoticeAction.Visibility = Visible(top.ActionTitle != null);
            NoticeAction.IsEnabled = true;
            NoticeClose.Visibility = Visible(top.Dismissable);

            NoticeBar.Visibility = Visibility.Visible;
        }

        private void NoticeAction_Click(object sender, RoutedEventArgs e)
        {
            if (_notices.Count == 0)
                return;

            // Действие может занять время — гасим кнопку, чтобы не нажали дважды.
            NoticeAction.IsEnabled = false;
            _notices[0].Action?.Invoke();
        }

        private void NoticeClose_Click(object sender, RoutedEventArgs e)
        {
            if (_notices.Count > 0 && _notices[0].Dismissable)
                DropNotice(_notices[0].Id);
        }

        // ═══════════ настроенность рабочего места ═══════════

        private bool _configured = true;

        private void ApplySetupState(bool configured)
        {
            _configured = configured;

            if (configured)
            {
                DropNotice(NoticeIds.Setup);
            }
            else
            {
                SettingsPanel.NeedsWizard(out string why);
                ShowNotice(NoticeIds.Setup,
                    string.IsNullOrWhiteSpace(why) ? "Рабочее место не настроено" : why,
                    NoticeKind.Blocking, "Настроить", () => OpenSettings());
                Log(why, NoticeKind.Blocking);
            }

            RefreshBuildAvailability();
        }

        /// <summary>
        /// Единственное место, где решается доступность «Создать проект».
        /// Раньше её включали и гасили из четырёх мест, и они разошлись.
        /// </summary>
        private void RefreshBuildAvailability()
        {
            bool allowed = _configured && (_stage == Stage.Manual ? _manualNameFilled : _canBuildResolved);
            BuildButton.IsEnabled = allowed;
            ManualBuildButton.IsEnabled = allowed;
        }

        private bool _canBuildResolved;

        // ═══════════ панель настроек ═══════════

        private SettingsPanel? _settings;

        private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettings();

        private void SettingsScrim_Click(object sender, MouseButtonEventArgs e)
        {
            // Пока рабочее место не настроено, закрыть панель щелчком мимо нельзя:
            // за ней всё равно нет ничего, чем можно пользоваться.
            if (!_configured)
                return;

            CloseSettings();
        }

        /// <summary>
        /// Открывает панель настроек. <paramref name="announce"/> — почему она
        /// открылась сама (первый запуск, пропали папки).
        /// </summary>
        private void OpenSettings(string? announce = null)
        {
            if (_settings == null)
            {
                _settings = new SettingsPanel();
                _settings.Saved += (_, _) =>
                {
                    Log("Настройки сохранены.");
                    // Папки могли появиться прямо сейчас — снимаем запрет,
                    // не требуя перезапуска.
                    ApplySetupState(!SettingsPanel.NeedsWizard(out _));
                    ReportCatalog();

                    if (_configured)
                        CloseSettings();
                    else
                        _settings!.Announce("Часть папок всё ещё не найдена.");
                };
                _settings.Cancelled += (_, _) =>
                {
                    if (_configured)
                        CloseSettings();
                    else
                        _settings!.Announce("Без рабочих папок создать проект не получится.");
                };
                SettingsHost.Content = _settings;
            }

            _settings.Load();
            if (announce != null)
                _settings.Announce(announce);

            SettingsOverlay.Visibility = Visibility.Visible;

            // Выезд справа — из-за края окна, а не «возникает на месте».
            SettingsShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
                new DoubleAnimation((double)FindResource("Size.SettingsPanel"), 0,
                                    (Duration)FindResource("M.Base"))
                {
                    EasingFunction = (IEasingFunction)FindResource("Ease")
                });
        }

        private void CloseSettings() => SettingsOverlay.Visibility = Visibility.Collapsed;

        // ═══════════ буфер обмена ═══════════

        private string _clipboardOffered = "";

        /// <summary>
        /// Ссылку почти всегда копируют из браузера перед тем, как открыть
        /// программу. Предлагаем взять её сами — это снимает «щёлкнуть в поле,
        /// Ctrl+V» с каждого заказа. Молча подставлять не годится: в буфере
        /// может лежать что угодно, и подмена введённого была бы хамством.
        /// </summary>
        private void OfferClipboard()
        {
            string text;
            try
            {
                text = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
            }
            catch
            {
                return; // буфер занят другой программой — не повод шуметь
            }

            if (string.IsNullOrWhiteSpace(text) ||
                text == _clipboardOffered ||
                text == LinkBox.Text.Trim() ||
                !LinkParser.TryParseId(text, out long id, out _))
            {
                return;
            }

            _clipboardOffered = text;
            ClipboardText.Text = $"В буфере: заказ {id}";
            ClipboardHint.Visibility = Visibility.Visible;
        }

        private void ClipboardTake_Click(object sender, RoutedEventArgs e)
        {
            LinkBox.Text = _clipboardOffered;
            ClipboardHint.Visibility = Visibility.Collapsed;
            _ = FetchAsync();
        }

        // ═══════════ получение данных ═══════════

        private void LinkBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) _ = FetchAsync();
        }

        private void LinkBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            // Вставили сами — подсказка про буфер больше не нужна.
            if (!string.IsNullOrWhiteSpace(LinkBox.Text))
                ClipboardHint.Visibility = Visibility.Collapsed;
        }

        private void FetchButton_Click(object sender, RoutedEventArgs e) => _ = FetchAsync();

        private async Task FetchAsync()
        {
            _resolved = null;
            _canBuildResolved = false;

            if (!LinkParser.TryParseId(LinkBox.Text, out long id, out string err))
            {
                Log(err, NoticeKind.Warning);
                ShowNotice(NoticeIds.Link, err, NoticeKind.Warning);
                return;
            }

            DropNotice(NoticeIds.Link);

            string auth = _config.Bitrix.ResolveAuthHeader();
            if (string.IsNullOrEmpty(auth))
            {
                Log(BitrixAccess.NotConfiguredMessage, NoticeKind.Blocking);
                ShowNotice(NoticeIds.Bitrix, "Не задан доступ к Bitrix",
                           NoticeKind.Warning, "Настроить", () => OpenSettings());
                return;
            }

            CheckingLink.Text = $"заказ {id}";
            SetStage(Stage.Checking);
            Log($"Запрос данных заказа #{id}…");

            try
            {
                using var client = new BitrixClient(_config.Bitrix);
                DesignData data = await client.GetDataAsync(id);
                if (_closed) return;

                // Код дизайна берём из названия заказа (отдельный эндпоинт не нужен).
                var resolved = BitrixMapper.Map(data, "");
                ShowResult(resolved);
                Log($"Данные получены: {resolved.Brand} · {resolved.ProductArticul}.");
            }
            catch (BitrixException bex)
            {
                if (_closed) return;
                Log("Ошибка: " + bex.Message, NoticeKind.Blocking);
                SetStage(Stage.Empty);
            }
            catch (Exception ex)
            {
                if (_closed) return;
                Log("Сбой запроса: " + ex.Message, NoticeKind.Blocking);
                SetStage(Stage.Empty);
            }
        }

        // ═══════════ результат ═══════════

        private readonly ObservableCollection<ResultField> _fields = new();

        /// <summary>
        /// Показывает разобранный заказ. Строки собираются из данных: пустые
        /// не показываются вовсе. Раньше панель всегда рисовала все десять
        /// строк, половина из которых была «—», и глаз тонул в прочерках.
        /// </summary>
        internal void ShowResult(ResolvedDesign r)
        {
            // Показанный заказ И ЕСТЬ текущий. Раньше присваивание жило отдельно,
            // у вызывающей стороны, и метод оказывался согласован только по
            // договорённости: показать один заказ, а работать с другим было
            // технически возможно. Лист правки на это и наткнулся.
            _resolved = r;
            _fields.Clear();

            Add("Заказ", r.Id.ToString());
            Add("Название", r.OrderName);
            Add("Направление", Mapped(r.RawProject, r.Brand.ToString()));
            Add("Тип", Mapped(r.RawType, r.ProductType));
            Add("Печать", Mapped(r.RawPrint, r.PrintTech.ToString()));
            Add("Материал", Mapped(r.RawSide, r.Material.ToString()));

            if (!string.IsNullOrWhiteSpace(r.RawCoating))
                Add("Покрытие", Mapped(r.RawCoating, r.Coating.ToString()));

            if (!string.IsNullOrWhiteSpace(r.Variant))
                Add("Вкус / вид", Mapped(r.RawFlavor, r.Variant));

            if (r.Brand == Brand.CuptoYou)
                Add("Страна", Mapped(r.RawLang, r.Country.ToString()));

            // Артикул обязателен не всегда: у шоколада шаблон выбирается по вкусу,
            // и пустой артикул — норма. Спрашиваем у каталога, а не гадаем.
            bool needsArticle = CatalogService.Current.Match(r.ToBuildRequest().Spec)
                                    is not { Variants.Count: > 0 };
            bool articleMissing = needsArticle && string.IsNullOrWhiteSpace(r.ProductArticul);

            _fields.Add(new ResultField
            {
                Label = "Артикул",
                Value = articleMissing ? "не распознан" : r.ProductArticul,
                Warn = articleMissing,
                FixKey = FixKeys.Article,
                Hint = "Щёлкните, чтобы указать артикул"
            });

            bool nameMissing = string.IsNullOrWhiteSpace(r.DesignCode);
            _canBuildResolved = !nameMissing && !articleMissing;

            if (_canBuildResolved)
            {
                ResultHeadline.Text = "Заказ распознан";
                ResultSubline.Text = r.DesignCode;
                ResultBadgeIcon.Data = Ui.Icon("I.Check");
                ResultBadgeIcon.Stroke = Ui.Brush("Ok");
                ResultBadge.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(0x1A, 0x34, 0xD3, 0x99));
                BuildButtonText.Text = "Создать проект";
            }
            else
            {
                ResultHeadline.Text = nameMissing ? "Не разобрано имя дизайна" : "Артикул не распознан";
                ResultSubline.Text = nameMissing ? r.OrderName : r.DesignCode;
                ResultBadgeIcon.Data = Ui.Icon("I.Warn");
                ResultBadgeIcon.Stroke = Ui.Brush("Warn");
                ResultBadge.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(0x17, 0xFB, 0xBF, 0x24));
                BuildButtonText.Text = "Указать артикул";
            }

            foreach (string warning in r.Warnings)
                Log(warning, NoticeKind.Warning);

            SetStage(Stage.Result);

            void Add(string label, string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;

                string? fix = FixKeyFor(label, r);
                _fields.Add(new ResultField
                {
                    Label = label, Value = value, FixKey = fix,
                    Hint = fix == null ? null : "Щёлкните, чтобы поправить"
                });
            }

            static string Mapped(string raw, string result) =>
                string.IsNullOrWhiteSpace(raw) ? result : $"{raw}  →  {result}";
        }

        /// <summary>
        /// Клик по строке результата открывает лист правки ЭТОГО поля.
        /// Раньше вёл в ручной ввод целиком — дизайнер перезаполнял шесть
        /// верных полей ради одного неверного.
        /// </summary>
        private void ResultField_Click(object sender, RoutedEventArgs e)
        {
            if (_resolved == null || sender is not System.Windows.Controls.Button { Tag: ResultField field })
                return;

            if (!string.IsNullOrEmpty(field.FixKey))
                OpenFix(field.FixKey);
        }

        // ═══════════ создание проекта ═══════════

        private BuildResult? _lastBuild;

        private void BuildButton_Click(object sender, RoutedEventArgs e)
        {
            // Результат с неразобранным артикулом ведёт не в сборку, а в правку —
            // ровно того поля, которого не хватает.
            if (_stage == Stage.Result && !_canBuildResolved)
            {
                if (_resolved != null)
                    OpenFix(FixKeys.Article);
                return;
            }

            BuildRequest request;
            if (_stage == Stage.Manual)
            {
                request = BuildManualRequest();
            }
            else if (_resolved != null)
            {
                request = _resolved.ToBuildRequest();
            }
            else
            {
                Log("Сначала загрузите данные заказа.", NoticeKind.Warning);
                return;
            }

            RunningPath.Text = request.DesignCode;
            SetStage(Stage.Running);

            // Источник один и тот же ProjectBuilder — меняется только то,
            // откуда взялись параметры: из Bitrix или из полей ручного ввода.
            BuildResult result = ProjectBuilder.Build(request);
            _lastBuild = result;

            foreach (var line in result.Log)
                Log(line, result.Success ? NoticeKind.Info : NoticeKind.Blocking);

            if (!result.Success)
            {
                ShowNotice(NoticeIds.Build, "Проект не создан — подробности в журнале",
                           NoticeKind.Warning);
                SetStage(_stage == Stage.Running && _resolved != null ? Stage.Result : Stage.Manual);
                return;
            }

            DropNotice(NoticeIds.Build);

            DoneHeadline.Text = result.IllustratorLaunched
                ? "Illustrator открывает макет"
                : result.AlreadyExisted ? "Папка уже существовала" : "Проект создан";
            DonePath.Text = result.ProjectPath;
            SetStage(Stage.Done);
        }

        /// <summary>
        /// Отказ от разобранного заказа. Ссылка стирается вся: оставлять её
        /// в поле значит предлагать «проверить то же самое ещё раз», а человек
        /// нажал «Отменить» именно потому, что заказ не тот.
        /// </summary>
        private void CancelResult_Click(object sender, RoutedEventArgs e) => ResetToEmpty();

        private void NextOrder_Click(object sender, RoutedEventArgs e) => ResetToEmpty();

        private void ResetToEmpty()
        {
            _resolved = null;
            _canBuildResolved = false;
            _fields.Clear();
            LinkBox.Clear();
            SetStage(Stage.Empty);
            OfferClipboard();
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            string? path = _lastBuild?.ProjectPath;
            if (string.IsNullOrWhiteSpace(path))
                return;

            try { System.Diagnostics.Process.Start("explorer.exe", path); }
            catch (Exception ex) { Log("Не удалось открыть папку: " + ex.Message, NoticeKind.Warning); }
        }

        // ═══════════ ручной ввод ═══════════

        private void PencilButton_Click(object sender, RoutedEventArgs e)
        {
            if (BrandCombo.ItemsSource == null)
                InitManual();

            // Если заказ загружен — переносим его в поля, чтобы поправить и создать.
            if (_resolved != null)
            {
                FillManualFrom(_resolved);
                Log("Данные заказа перенесены в поля ручного ввода.");
            }

            SetStage(Stage.Manual);
        }

        private void ManualClose_Click(object sender, RoutedEventArgs e) =>
            SetStage(_resolved != null ? Stage.Result : Stage.Empty);

        private void ManualName_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _manualNameFilled = !string.IsNullOrWhiteSpace(ManualNameBox.Text);
            RefreshBuildAvailability();
        }

        private bool _manualNameFilled;

        // ═══════════ каталог ═══════════

        private void ReportCatalog()
        {
            try
            {
                var catalog = CatalogService.Current;
                CatalogStamp.Text = $"каталог v{catalog.Version} · {catalog.Updated}";
                Log($"Каталог: версия {catalog.Version} от {catalog.Updated} — {catalog.SourceName}");

                if (!CatalogService.IsUsingEmbedded)
                {
                    DropNotice(NoticeIds.Catalog);
                    return;
                }

                string expected = CatalogService.ExpectedPath;
                if (string.IsNullOrWhiteSpace(expected))
                    return;

                ShowNotice(NoticeIds.Catalog, "Каталог взят из копии внутри программы", NoticeKind.Warning);
                Log($"Правки каталога не действуют. Ожидался здесь: {expected}", NoticeKind.Warning);

                foreach (string line in CatalogService.LoadLog)
                    Log("  " + line, NoticeKind.Warning);
            }
            catch (CatalogException ex)
            {
                CatalogStamp.Text = "каталог не загружен";
                Log(ex.Message, NoticeKind.Blocking);
            }
        }

        private void ReloadCatalog_Click(object sender, RoutedEventArgs e)
        {
            CatalogService.Reload();
            ReportCatalog();
        }

        // ═══════════ обновление ═══════════

        private ReleaseInfo? _update;

        private void CheckForUpdate()
        {
            // Прошлая попытка могла провалиться молча: подменяет файл скрипт без
            // окна, и сказать об этом на экране он не может.
            string? previous = Updater.TakeUpdateProblem();
            if (previous != null)
            {
                Log(previous, NoticeKind.Warning);
                ShowNotice(NoticeIds.UpdateFailed, "Прошлое обновление не применилось", NoticeKind.Warning);
            }

            try
            {
                _update = Updater.Check(out string? diagnosis);
                if (diagnosis != null)
                    Log(diagnosis, NoticeKind.Warning);
            }
            catch (Exception ex)
            {
                Log("Проверка обновлений не удалась: " + ex.Message, NoticeKind.Warning);
                return;
            }

            if (_update == null)
                return;

            OfferUpdate($"Доступна версия {_update.Version}" +
                        (string.IsNullOrWhiteSpace(_update.Notes) ? "" : $" · {_update.Notes}"));
        }

        /// <summary>
        /// Предлагает обновиться — но только если обновиться действительно можно.
        /// Раньше кнопка была всегда, и при запуске не из своей папки человек
        /// получал отказ уже по нажатию: со стороны это выглядит как поломка.
        /// </summary>
        private void OfferUpdate(string headline)
        {
            if (Updater.CanApplyHere(out string? cannot))
            {
                ShowNotice(NoticeIds.Update, headline, NoticeKind.Info, "Обновить", ApplyUpdate);
                return;
            }

            ShowNotice(NoticeIds.Update, headline + " — обновиться отсюда нельзя", NoticeKind.Warning);
            Log(cannot!, NoticeKind.Warning);
        }

        private async void ApplyUpdate()
        {
            // Из раздачи: качаем по сети и подменяем.
            if (_update == null && _distApp != null)
            {
                var (data, downloadError) = await DistributionClient.DownloadAppAsync(_distApp);
                if (_closed) return;

                if (data == null)
                {
                    Log(downloadError ?? "Не удалось скачать обновление.", NoticeKind.Warning);
                    RefreshNotice();
                    return;
                }

                if (Updater.ApplyFile(data, out string applyError))
                {
                    Application.Current.Shutdown();
                    return;
                }

                Log(applyError, NoticeKind.Warning);
                RefreshNotice();
                return;
            }

            if (_update == null)
            {
                DropNotice(NoticeIds.Update);
                return;
            }

            // Заменить работающий файл нельзя, поэтому подмену делает скрипт:
            // он ждёт закрытия программы и запускает её заново.
            if (Updater.Apply(_update, out string error))
            {
                Application.Current.Shutdown();
                return;
            }

            Log(error, NoticeKind.Warning);
            RefreshNotice();
        }

        // ═══════════ журнал ═══════════

        private readonly ObservableCollection<LogEntry> _log = new();

        private void JournalToggle_Click(object sender, RoutedEventArgs e) => ToggleJournal();
        private void JournalScrim_Click(object sender, MouseButtonEventArgs e) => ToggleJournal();

        private void ToggleJournal()
        {
            bool show = JournalOverlay.Visibility != Visibility.Visible;
            JournalOverlay.Visibility = Visible(show);

            if (!show)
                return;

            // Выезд снизу: шторка, а не внезапно возникшая панель.
            JournalShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new DoubleAnimation(40, 0, (Duration)FindResource("M.Base"))
                {
                    EasingFunction = (IEasingFunction)FindResource("Ease")
                });

            JournalScroll.ScrollToEnd();
        }

        private void Log(string message, NoticeKind kind = NoticeKind.Info)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            _log.Add(new LogEntry
            {
                Time = DateTime.Now.ToString("HH:mm"),
                Text = message,
                Kind = kind
            });

            // Журнал за смену вырастает в тысячи строк, а нужны последние.
            while (_log.Count > 200)
                _log.RemoveAt(0);
        }
    }

    /// <summary>
    /// Поводы для уведомлений. Строками, а не enum: список открытый, и каждый
    /// повод должен уметь снять сам себя, когда причина исчезла.
    /// </summary>
    internal static class NoticeIds
    {
        public const string Setup = "setup";
        public const string Update = "update";
        public const string UpdateFailed = "update-failed";
        public const string Sync = "sync";
        public const string Catalog = "catalog";
        public const string Bitrix = "bitrix";
        public const string Link = "link";
        public const string Build = "build";
    }
}
