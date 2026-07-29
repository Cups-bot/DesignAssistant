using System.Windows;
using CupsCore;

namespace CupsForge
{
    /// <summary>
    /// Обновление шаблонов из публичной раздачи.
    ///
    /// Первая загрузка спрашивает разрешения — там сотни мегабайт, и тянуть их
    /// без спроса на домашнем интернете нельзя. После согласия программа
    /// обновляется молча: дизайнеру не нужно следить за этим, а вам — никого
    /// предупреждать о правках.
    /// </summary>
    public partial class AutoWindow
    {
        private DistManifest? _manifest;
        private DistApp? _distApp;
        private SyncPlan? _plan;
        private SyncState _syncState = SyncState.Load();
        private bool _syncing;

        private async void CheckDistribution()
        {
            _manifest = await DistributionClient.FetchAsync();
            if (_manifest == null)
                return; // нет сети или раздачи — молча работаем на своём

            // Новая версия программы из раздачи — тот же канал, что и шаблоны.
            var newApp = DistributionClient.NewerApp(_manifest);
            if (newApp != null && _update == null)
            {
                _distApp = newApp;
                UpdateText.Text = $"Доступна версия {newApp.Version}" +
                                  (string.IsNullOrWhiteSpace(newApp.Notes) ? "" : $" · {newApp.Notes}");
                UpdateBar.Visibility = Visibility.Visible;
            }

            _plan = DistributionClient.Compare(_manifest, _syncState);
            if (_plan.IsEmpty)
            {
                // Отпечатки могли посчитаться заново — сохраним, чтобы не считать снова.
                _syncState.Save();
                return;
            }

            // Согласие уже давали — обновляемся молча.
            if (MachineProfile.Current.AutoSyncTemplates)
            {
                await RunSync(silent: true);
                return;
            }

            SyncText.Text = _plan.IsFirstSync
                ? $"Шаблоны ещё не скачаны: {_plan.Describe()}"
                : $"Обновление шаблонов: {_plan.Describe()}";
            SyncButton.Content = "Скачать";
            SyncButton.IsEnabled = true;
            SyncBar.Visibility = Visibility.Visible;
        }

        private async void SyncButton_Click(object sender, RoutedEventArgs e) =>
            await RunSync(silent: false);

        private async Task RunSync(bool silent)
        {
            if (_plan == null || _plan.IsEmpty || _syncing)
                return;

            _syncing = true;
            SyncButton.IsEnabled = false;
            SyncBar.Visibility = Visibility.Visible;

            var progress = new Progress<string>(name => SyncText.Text = "Скачиваю " + name);

            try
            {
                var (done, error) = await DistributionClient.DownloadAsync(_plan, _syncState, progress);

                _syncState.FirstSyncDone = true;
                _syncState.Save();

                if (error != null)
                {
                    SyncText.Text = $"Скачано {done} из {_plan.Files.Count}, дальше не вышло";
                    SyncButton.Content = "Повторить";
                    SyncButton.IsEnabled = true;
                    Log("Обновление шаблонов: " + error);
                    return;
                }

                // Согласие спрашиваем один раз. Дальше молча.
                if (!MachineProfile.Current.AutoSyncTemplates)
                {
                    MachineProfile.Current.AutoSyncTemplates = true;
                    try { MachineProfile.Current.Save(); }
                    catch { /* не сохранился — спросим ещё раз, не беда */ }
                }

                Log($"Шаблоны обновлены: {done} файл(ов).");

                // Каталог мог приехать вместе с ними — перечитываем, иначе новые
                // продукты появятся только после перезапуска.
                CatalogService.Reload();
                ReportCatalog();

                SyncBar.Visibility = Visibility.Collapsed;
                _plan = null;
            }
            catch (Exception ex)
            {
                SyncText.Text = "Обновление не удалось";
                SyncButton.Content = "Повторить";
                SyncButton.IsEnabled = true;
                Log("Обновление шаблонов: " + ex.Message);
            }
            finally
            {
                _syncing = false;
            }
        }
    }
}
