using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CupsCore
{
    /// <summary>
    /// Настройки рабочего места: папки, режим (офис/своя машина), Illustrator,
    /// доступ к Bitrix, каталог.
    ///
    /// Панель внутри главного окна, а не отдельное окно. Правки идут в КОПИЮ
    /// профиля, поэтому «Отмена» действительно ничего не меняет; при сохранении
    /// в живой профиль переносятся только поля этой панели (см. ApplyEditableTo).
    /// </summary>
    public partial class SettingsPanel : UserControl
    {
        private MachineProfile _draft = MachineProfile.Current.Clone();
        private readonly ObservableCollection<RootRow> _rows = new();
        private List<IllustratorChoice> _illustratorChoices = new();
        private bool _loading = true;

        /// <summary>Настройки сохранены. Окно должно перечитать состояние.</summary>
        public event EventHandler? Saved;

        /// <summary>Панель закрыта без сохранения.</summary>
        public event EventHandler? Cancelled;

        public SettingsPanel()
        {
            InitializeComponent();
            RootsList.ItemsSource = _rows;
            Load();
        }

        /// <summary>
        /// Перечитывает всё из профиля. Зовётся при каждом открытии панели:
        /// она живёт вместе с окном, а профиль за это время мог измениться —
        /// например, мастер настройки создал папки.
        /// </summary>
        public void Load()
        {
            _loading = true;

            _draft = MachineProfile.Current.Clone();

            LoadRoots();
            LoadIllustrator();
            LoadCatalogInfo();
            LoadBitrix();

            DrawerSwitch.IsChecked = _draft.SideDrawer;
            OfficeRadio.IsChecked = !IsRemote(_draft.Mode);
            RemoteRadio.IsChecked = IsRemote(_draft.Mode);
            BaseBox.Text = GuessStakanyRoot();

            _loading = false;
            RefreshStatuses();
            HideMessage();
        }

        /// <summary>
        /// Нужен ли мастер настройки и что сказать человеку.
        ///
        /// Решает наличие рабочих папок, а не наличие файла профиля. Раньше
        /// сохранённый профиль отключал мастер навсегда: сменилась буква диска —
        /// и вернуться к настройке было уже неоткуда.
        /// </summary>
        public static bool NeedsWizard(out string message)
        {
            var missing = MachineProfile.Current.FindMissingRoots();
            if (missing.Count == 0 && MachineProfile.LoadProblem == null)
            {
                message = "";
                return false;
            }

            if (MachineProfile.LoadProblem != null)
                message = MachineProfile.LoadProblem + " Проверьте папки.";
            else if (MachineProfile.ExistsOnDisk)
                message = "Не найдены рабочие папки: " +
                          string.Join(", ", missing.Select(MachineProfile.Root.Title)) +
                          ". Возможно, изменилась буква диска или папку перенесли.";
            else
                message = "Похоже, это первый запуск на новой машине: рабочих папок " +
                          "по стандартным путям нет. Укажите, где они лежат здесь.";

            return true;
        }

        /// <summary>Показать подсказку сверху — например, зачем панель открылась сама.</summary>
        public void Announce(string message) => ShowMessage(message);

        private static bool IsRemote(string mode) =>
            string.Equals(mode, "remote", StringComparison.OrdinalIgnoreCase);

        // ---------- загрузка ----------

        private void LoadRoots()
        {
            _rows.Clear();
            foreach (string key in MachineProfile.Root.All)
            {
                var row = new RootRow
                {
                    Key = key,
                    Title = MachineProfile.Root.Title(key),
                    Path = _draft.Roots.TryGetValue(key, out string? p) ? p : ""
                };
                row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(RootRow.Path))
                        row.RefreshStatus();
                };
                _rows.Add(row);
            }
        }

        /// <summary>
        /// Общая папка STAKANY, если корни лежат по офисному скелету.
        /// Нужна, чтобы кнопка «разложить по офисной структуре» открывалась не пустой.
        /// </summary>
        private string GuessStakanyRoot()
        {
            if (_draft.Roots.TryGetValue(MachineProfile.Root.Base, out string? b) &&
                !string.IsNullOrWhiteSpace(b))
            {
                return b;
            }

            if (!_draft.Roots.TryGetValue(MachineProfile.Root.Templates, out string? templates) ||
                string.IsNullOrWhiteSpace(templates))
            {
                return "";
            }

            return System.IO.Path.GetDirectoryName(templates.TrimEnd('\\', '/')) ?? "";
        }

        private void LoadIllustrator()
        {
            var installs = IllustratorLocator.FindAll();
            _illustratorChoices = new List<IllustratorChoice>
            {
                new(MachineProfile.AutoDetect, installs.Count > 0
                    ? $"Автоматически — {installs[0].Title}"
                    : "Автоматически — пока не найден")
            };

            foreach (var install in installs)
                _illustratorChoices.Add(new IllustratorChoice(install.ExePath, install.Title));

            // Путь, прописанный руками и не совпавший ни с одной находкой.
            string configured = _draft.IllustratorExe;
            if (!string.IsNullOrWhiteSpace(configured) &&
                !string.Equals(configured, MachineProfile.AutoDetect, StringComparison.OrdinalIgnoreCase) &&
                !_illustratorChoices.Any(c => string.Equals(c.Value, configured, StringComparison.OrdinalIgnoreCase)))
            {
                _illustratorChoices.Add(new IllustratorChoice(configured, configured));
            }

            IllustratorCombo.ItemsSource = _illustratorChoices;
            IllustratorCombo.DisplayMemberPath = nameof(IllustratorChoice.Display);
            IllustratorCombo.SelectedItem =
                _illustratorChoices.FirstOrDefault(c =>
                    string.Equals(c.Value, configured, StringComparison.OrdinalIgnoreCase))
                ?? _illustratorChoices[0];

            IllustratorHint.Text = installs.Count switch
            {
                0 => "Не найден автоматически. Укажите Illustrator.exe вручную — " +
                     "программа запомнит его только на этой машине.",
                1 => $"Найдена одна установка: {installs[0].Title}.",
                _ => $"Найдено установок: {installs.Count}. По умолчанию берётся самая свежая."
            };
        }

        private void LoadCatalogInfo()
        {
            try
            {
                var catalog = CatalogService.Current;
                CatalogInfo.Text = $"Версия {catalog.Version} от {catalog.Updated} · продуктов: {catalog.Products.Count}";
                CatalogSource.Text = catalog.SourceName;
            }
            catch (CatalogException ex)
            {
                CatalogInfo.Text = "Каталог не загружен.";
                CatalogSource.Text = ex.Message;
            }
        }

        /// <summary>
        /// Доступ к Bitrix. Хранится в профиле рабочего места, а не рядом с программой:
        /// раньше он лежал в appsettings.json, который попадал в репозиторий, и до новой
        /// машины не доезжал вовсе — вводить его было негде.
        /// </summary>
        private void LoadBitrix()
        {
            BitrixKeyBox.Text = _draft.Bitrix.AuthorizationHeader;
            BitrixHint.Text =
                "Ключ выдаёт тот, кто ведёт Bitrix. Можно как есть, можно с приставкой " +
                "«Basic». Нужен только для загрузки заказа по ссылке. Хранится на этой машине.";
        }

        /// <summary>
        /// Проверка связи с Bitrix. Ставится приложением: HTTP-клиент живёт в его слое,
        /// а эта панель общая и о Bitrix знать не обязана.
        /// </summary>
        public static Func<string, Task<(bool ok, string message)>>? ConnectionTest { get; set; }

        private async void BitrixTest_Click(object sender, RoutedEventArgs e)
        {
            string key = BitrixKeyBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                ShowMessage("Сначала вставьте ключ доступа.");
                return;
            }

            if (ConnectionTest == null)
            {
                ShowMessage("Проверка связи в этой сборке недоступна.");
                return;
            }

            BitrixTestButton.IsEnabled = false;
            ShowMessage("Проверяю связь…", ok: true);
            try
            {
                var (ok, message) = await ConnectionTest(key);
                ShowMessage(message, ok);
            }
            catch (Exception ex)
            {
                ShowMessage("Проверка не удалась: " + ex.Message);
            }
            finally
            {
                BitrixTestButton.IsEnabled = true;
            }
        }

        // ---------- действия ----------

        private void ReloadCatalog_Click(object sender, RoutedEventArgs e)
        {
            CatalogService.Reload();
            LoadCatalogInfo();
            ShowMessage("Каталог перечитан.", ok: true);
        }

        /// <summary>
        /// Проверяет и само описание продуктов, и наличие файлов шаблонов.
        /// Второе особенно нужно на удалёнке: сразу видно, что не всё докопировано.
        /// </summary>
        private void CheckCatalog_Click(object sender, RoutedEventArgs e)
        {
            // Проверяем по путям, введённым в панели, а не по сохранённому профилю —
            // иначе кнопка врала бы до нажатия «Сохранить».
            MachineProfile saved = MachineProfile.Current;
            var probe = _draft.Clone();
            foreach (var row in _rows)
            {
                if (!string.IsNullOrWhiteSpace(row.Path))
                    probe.Roots[row.Key] = row.Path.Trim();
            }
            if (!string.IsNullOrWhiteSpace(BaseBox.Text))
                probe.Roots[MachineProfile.Root.Base] = BaseBox.Text.Trim();

            MachineProfile.Set(probe);
            try
            {
                Catalog catalog = CatalogService.Current;

                var errors = catalog.Validate();
                if (errors.Count > 0)
                {
                    ShowMessage($"Ошибки в описании каталога ({errors.Count}):\n• " +
                                string.Join("\n• ", errors.Take(4)));
                    return;
                }

                var missing = catalog.CheckTemplateFiles();
                if (missing.Count == 0)
                {
                    ShowMessage($"Каталог в порядке: {catalog.Products.Count} продуктов, все шаблоны на месте.", ok: true);
                    return;
                }

                string list = string.Join("\n• ", missing.Take(5));
                string tail = missing.Count > 5 ? $"\n…и ещё {missing.Count - 5}" : "";
                ShowMessage($"Не хватает файлов шаблонов ({missing.Count}):\n• {list}{tail}");
            }
            catch (CatalogException ex)
            {
                ShowMessage(ex.Message);
            }
            finally
            {
                MachineProfile.Set(saved);
                CatalogService.Reload();
            }
        }

        private void ModeChanged(object sender, RoutedEventArgs e)
        {
            if (_loading)
                return;

            _draft.Mode = RemoteRadio.IsChecked == true ? "remote" : "office";

            // Переключение в офис — вернуть сетевые пути, если поля ещё не трогали.
            if (OfficeRadio.IsChecked == true && string.IsNullOrWhiteSpace(BaseBox.Text))
                BaseBox.Text = MachineProfile.OfficeStakanyRoot;
        }

        private void BrowseBase_Click(object sender, RoutedEventArgs e)
        {
            string? picked = PickFolder("Выберите папку STAKANY", BaseBox.Text);
            if (picked != null)
                BaseBox.Text = picked;
        }

        private void ApplyBase_Click(object sender, RoutedEventArgs e)
        {
            string root = BaseBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(root))
            {
                ShowMessage("Сначала укажите папку STAKANY.");
                return;
            }

            var sample = MachineProfile.FromStakanyRoot(root);
            foreach (var row in _rows)
                row.Path = sample.Roots[row.Key];

            HideMessage();
            RefreshStatuses();
        }

        private void BrowseRoot_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: RootRow row })
                return;

            string? picked = PickFolder(row.Title, row.Path);
            if (picked != null)
                row.Path = picked;

            RefreshStatuses();
        }

        private void BrowseIllustrator_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Укажите Illustrator.exe",
                Filter = "Illustrator|Illustrator.exe|Программы (*.exe)|*.exe",
                CheckFileExists = true
            };

            if (dialog.ShowDialog(Window.GetWindow(this)) != true)
                return;

            var choice = new IllustratorChoice(dialog.FileName, dialog.FileName);
            _illustratorChoices.Add(choice);
            IllustratorCombo.ItemsSource = null;
            IllustratorCombo.ItemsSource = _illustratorChoices;
            IllustratorCombo.SelectedItem = choice;
        }

        private void Check_Click(object sender, RoutedEventArgs e)
        {
            RefreshStatuses();

            var missing = _rows.Where(r => !r.Exists).Select(r => r.Title).ToList();
            if (missing.Count == 0)
                ShowMessage("Все папки на месте.", ok: true);
            else
                ShowMessage("Не найдено: " + string.Join(", ", missing) +
                            ". Проверьте пути или нажмите «Создать папки».");
        }

        private void CreateFolders_Click(object sender, RoutedEventArgs e)
        {
            var created = new List<string>();
            var failed = new List<string>();

            foreach (var row in _rows)
            {
                if (string.IsNullOrWhiteSpace(row.Path) || Directory.Exists(row.Path))
                    continue;

                try
                {
                    Directory.CreateDirectory(row.Path);
                    created.Add(row.Title);
                }
                catch (Exception ex)
                {
                    failed.Add($"{row.Title} ({ex.Message})");
                }
            }

            // Базовая папка тоже нужна: на неё ссылается каталог ("{base}/Lids").
            string baseFolder = BaseBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(baseFolder) && !Directory.Exists(baseFolder))
            {
                try { Directory.CreateDirectory(baseFolder); created.Add("Папка STAKANY"); }
                catch (Exception ex) { failed.Add($"Папка STAKANY ({ex.Message})"); }
            }

            RefreshStatuses();

            if (failed.Count > 0)
                ShowMessage("Не удалось создать: " + string.Join("; ", failed));
            else if (created.Count > 0)
                ShowMessage($"Создано папок: {created.Count}.", ok: true);
            else
                ShowMessage("Все папки уже существуют.", ok: true);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            foreach (var row in _rows)
            {
                if (string.IsNullOrWhiteSpace(row.Path))
                {
                    ShowMessage($"Не заполнено: {row.Title}.");
                    return;
                }
                _draft.Roots[row.Key] = row.Path.Trim();
            }

            // Базовая папка — тоже корень: каталог может складывать новые продукты
            // в свои папки рядом с остальными ("{base}/Lids").
            string baseFolder = BaseBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(baseFolder))
                _draft.Roots[MachineProfile.Root.Base] = baseFolder;

            // Работа идёт по готовому ключу, поэтому логин с паролем очищаем:
            // иначе они соберут свой заголовок и перебьют введённый.
            _draft.Bitrix.AuthorizationHeader = BitrixKeyBox.Text.Trim();
            _draft.Bitrix.Login = "";
            _draft.Bitrix.Password = "";

            _draft.Mode = RemoteRadio.IsChecked == true ? "remote" : "office";
            _draft.SideDrawer = DrawerSwitch.IsChecked == true;
            _draft.IllustratorExe = (IllustratorCombo.SelectedItem as IllustratorChoice)?.Value
                                    ?? MachineProfile.AutoDetect;

            // Сохраняем не копию целиком, а свои поля поверх живого профиля: пока
            // панель была открыта, программа могла записать туда что-то своё
            // (например, согласие на автозагрузку шаблонов).
            MachineProfile live = MachineProfile.Current;
            _draft.ApplyEditableTo(live);

            try
            {
                live.Save();
            }
            catch (Exception ex)
            {
                ShowMessage("Не удалось сохранить профиль: " + ex.Message);
                return;
            }

            Paths.ResetIllustratorCache();
            CatalogService.Reload();

            Saved?.Invoke(this, EventArgs.Empty);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) =>
            Cancelled?.Invoke(this, EventArgs.Empty);

        // ---------- служебное ----------

        private void RefreshStatuses()
        {
            foreach (var row in _rows)
                row.RefreshStatus();
        }

        private string? PickFolder(string title, string initial)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title };
            if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial))
                dialog.InitialDirectory = initial;

            return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FolderName : null;
        }

        private void ShowMessage(string text, bool ok = false)
        {
            MessageText.Text = text;
            MessageText.Foreground = Ui.Brush(ok ? "Ok" : "Warn");
            MessagePanel.Background = new SolidColorBrush(ok
                ? Color.FromRgb(0x0C, 0x1F, 0x1A)
                : Color.FromRgb(0x2A, 0x1B, 0x10));
            MessagePanel.BorderBrush = new SolidColorBrush(ok
                ? Color.FromRgb(0x14, 0x5A, 0x45)
                : Color.FromRgb(0x7C, 0x4A, 0x12));
            MessagePanel.Visibility = Visibility.Visible;
        }

        private void HideMessage() => MessagePanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>Строка списка папок: путь плюс отметка «есть / не найдена».</summary>
    public sealed class RootRow : INotifyPropertyChanged
    {
        private string _path = "";
        private Brush _statusBrush = Brushes.Gray;
        private Geometry _statusIcon = Geometry.Empty;

        public string Key { get; init; } = "";
        public string Title { get; init; } = "";

        public string Path
        {
            get => _path;
            set { _path = value ?? ""; Raise(nameof(Path)); }
        }

        public Brush StatusBrush
        {
            get => _statusBrush;
            private set { _statusBrush = value; Raise(nameof(StatusBrush)); }
        }

        /// <summary>
        /// Значок вместо надписи «✓ есть / не найдена»: в панели 262 пикселя
        /// текстовая отметка съедала половину строки, а понятна ровно так же.
        /// </summary>
        public Geometry StatusIcon
        {
            get => _statusIcon;
            private set { _statusIcon = value; Raise(nameof(StatusIcon)); }
        }

        public bool Exists => !string.IsNullOrWhiteSpace(Path) && Directory.Exists(Path);

        public void RefreshStatus()
        {
            if (Exists)
            {
                StatusIcon = Ui.Icon("I.CheckCircle");
                StatusBrush = Ui.Brush("Ok");
            }
            else
            {
                StatusIcon = Ui.Icon("I.Warn");
                StatusBrush = Ui.Brush(string.IsNullOrWhiteSpace(Path) ? "Dim" : "Warn");
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Пункт списка Illustrator: «авто» либо конкретный exe.</summary>
    public sealed class IllustratorChoice
    {
        public IllustratorChoice(string value, string display)
        {
            Value = value;
            Display = display;
        }

        public string Value { get; }
        public string Display { get; }
    }

    /// <summary>
    /// Доступ к токенам из кода. Цвета и значки нужны и в разметке, и здесь
    /// (строка папки выбирает значок по состоянию). Брать их отсюда, а не
    /// заводить второй набор констант: иначе тема разъедется пополам.
    /// </summary>
    public static class Ui
    {
        public static Brush Brush(string key) =>
            Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;

        public static Geometry Icon(string key) =>
            Application.Current?.TryFindResource(key) as Geometry ?? Geometry.Empty;
    }
}
