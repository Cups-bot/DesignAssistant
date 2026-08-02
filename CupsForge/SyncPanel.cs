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
        private SyncPlan? _plan;
        private SyncState _syncState = SyncState.Load();
        private bool _syncing;

        /// <summary>
        /// Окно закрыли. Дальше трогать его элементы нельзя: раздача отвечает
        /// по сети, ответ может прийти через несколько секунд после закрытия,
        /// и запись в UpdateText упала бы необработанным исключением — уже
        /// без окна, в котором её можно было бы показать.
        /// </summary>
        private bool _closed;

        protected override void OnClosed(EventArgs e)
        {
            _closed = true;
            CloseDock();
            base.OnClosed(e);
        }

        private async void CheckDistribution()
        {
            _manifest = await DistributionClient.FetchAsync();
            if (_closed)
                return;

            if (_manifest == null)
                return; // нет сети или раздачи — молча работаем на своём

            // Опись пришла — в ней есть версия каталога, и теперь видно,
            // отстал ли наш. Перерисовываем штамп в подвале журнала.
            ReportCatalog();

            // Программа обновляется отдельно, через Velopack (см. AppUpdates).
            // Здесь — только шаблоны и каталог: они меняются часто, весят сотни
            // мегабайт и живут своим сроком жизни.

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
                await RunSync();
                return;
            }

            ShowNotice(NoticeIds.Sync,
                _plan.IsFirstSync
                    ? $"Шаблоны ещё не скачаны: {_plan.Describe()}"
                    : $"Обновление шаблонов: {_plan.Describe()}",
                NoticeKind.Info, "Скачать", () => _ = RunSync());
        }

        private async Task RunSync()
        {
            if (_plan == null || _plan.IsEmpty || _syncing)
                return;

            _syncing = true;

            // Progress отдаёт в поток окна, но приходит и после закрытия — сотни
            // файлов качаются долго, и последние доклады запросто переживут окно.
            var progress = new Progress<string>(name =>
            {
                if (!_closed)
                    ShowNotice(NoticeIds.Sync, "Скачиваю " + name, NoticeKind.Info);
            });

            try
            {
                var (done, error) = await DistributionClient.DownloadAsync(_plan, _syncState, progress);

                _syncState.FirstSyncDone = true;
                _syncState.Save();

                // Скачивание идёт минутами: окно вполне могли закрыть. Состояние
                // на диск записали — оно не пропадёт, а трогать элементы уже нельзя.
                if (_closed)
                    return;

                if (error != null)
                {
                    ShowNotice(NoticeIds.Sync,
                        $"Скачано {done} из {_plan.Files.Count}, дальше не вышло",
                        NoticeKind.Warning, "Повторить", () => _ = RunSync());
                    Log("Обновление шаблонов: " + error, NoticeKind.Warning);
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

                DropNotice(NoticeIds.Sync);
                _plan = null;
            }
            catch (Exception ex)
            {
                if (_closed)
                    return;

                ShowNotice(NoticeIds.Sync, "Обновление не удалось",
                           NoticeKind.Warning, "Повторить", () => _ = RunSync());
                Log("Обновление шаблонов: " + ex.Message, NoticeKind.Warning);
            }
            finally
            {
                _syncing = false;
            }
        }
    }
}
