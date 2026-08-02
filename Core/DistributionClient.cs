using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CupsCore
{
    /// <summary>
    /// Забирает обновления из публичной раздачи на GitHub.
    ///
    /// Опись лежит в git и читается напрямую, файлы — вложениями к выпуску.
    /// Ключей не нужно: раздача открыта на чтение. Токен есть только у того,
    /// кто публикует, и живёт на его машине.
    ///
    /// Скачивается ТОЛЬКО отличающееся: сравниваются отпечатки. Правка одного
    /// шаблона стоит дизайнеру одного файла, а не всей папки.
    /// </summary>
    public static class DistributionClient
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            // GitHub отвечает 403 без User-Agent.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CupsForge");
            return client;
        }

        /// <summary>Опись раздачи. null — не достучались или её там нет.</summary>
        public static async Task<DistManifest?> FetchAsync(CancellationToken ct = default)
        {
            string url = MachineProfile.Current.DistributionUrl;
            if (string.IsNullOrWhiteSpace(url))
                return null;

            try
            {
                string json = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
                return JsonSerializer.Deserialize<DistManifest>(json);
            }
            catch
            {
                // Нет сети, нет файла, мусор в ответе — работаем на том, что есть.
                return null;
            }
        }

        /// <summary>
        /// Что нужно скачать. Отпечатки своих файлов берём из состояния, а не
        /// пересчитываем: четыреста мегабайт на каждом запуске — это секунды
        /// ожидания на пустом месте.
        /// </summary>
        public static SyncPlan Compare(DistManifest manifest, SyncState state)
        {
            string templates;
            try
            {
                templates = PathResolver.Root(MachineProfile.Root.Templates);
            }
            catch (PathResolutionException)
            {
                return new SyncPlan();
            }

            var plan = new SyncPlan { IsFirstSync = !state.FirstSyncDone };

            foreach (var file in All(manifest))
            {
                string local = Path.Combine(templates, file.Path.Replace('/', Path.DirectorySeparatorChar));

                // Файл на месте и отпечаток тот же — пропускаем.
                if (File.Exists(local) &&
                    state.Files.TryGetValue(file.Path, out string? known) &&
                    string.Equals(known, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Состояния нет (первый запуск после установки), но файл лежит —
                // проверяем по-настоящему, чтобы не качать уже имеющееся.
                if (File.Exists(local) && !state.Files.ContainsKey(file.Path))
                {
                    try
                    {
                        if (string.Equals(FileHash.Of(local), file.Sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            state.Files[file.Path] = file.Sha256;
                            continue;
                        }
                    }
                    catch { /* не прочитали — просто скачаем заново */ }
                }

                plan.Files.Add(file);
            }

            return plan;
        }

        private static IEnumerable<DistFile> All(DistManifest manifest)
        {
            if (manifest.Catalog != null)
                yield return manifest.Catalog;

            foreach (var file in manifest.Templates)
                yield return file;
        }

        /// <summary>
        /// Скачивает запланированное. Каждый файл проверяется по отпечатку и
        /// кладётся на место только целиком: оборванная закачка не должна
        /// оставить наполовину записанный шаблон.
        /// </summary>
        public static async Task<(int done, string? error)> DownloadAsync(
            SyncPlan plan, SyncState state, IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            string templates;
            try
            {
                templates = PathResolver.Root(MachineProfile.Root.Templates);
            }
            catch (PathResolutionException ex)
            {
                return (0, ex.Message);
            }

            string baseUrl = MachineProfile.Current.DistributionAssets.TrimEnd('/');
            int done = 0;

            foreach (var file in plan.Files)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"{file.Path} ({done + 1} из {plan.Files.Count})");

                string local = Path.Combine(templates, file.Path.Replace('/', Path.DirectorySeparatorChar));
                string temp = local + ".part";

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(local)!);

                    byte[] data = await Http.GetByteArrayAsync($"{baseUrl}/{file.Asset}", ct)
                                            .ConfigureAwait(false);

                    string actual = FileHash.OfBytes(data);
                    if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
                        return (done, $"{file.Path}: файл скачался повреждённым, отпечаток не совпал.");

                    await File.WriteAllBytesAsync(temp, data, ct).ConfigureAwait(false);

                    if (File.Exists(local)) File.Replace(temp, local, null);
                    else File.Move(temp, local);

                    state.Files[file.Path] = file.Sha256;
                    done++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    TryDelete(temp);
                    return (done, $"{file.Path}: {ex.Message}");
                }
            }

            return (done, null);
        }

        // Раздача программы отсюда УБРАНА: этим занимается Velopack (см. AppUpdates).
        // Он качает только изменившееся — 294 КБ вместо 76 МБ, — сам подменяет
        // файлы и перезапускается. Держать рядом второй способ обновлять то же
        // самое значило бы завести два места, где живёт одна логика.
        //
        // Здесь остаются шаблоны и каталог: у них свой срок жизни, свой объём
        // и своё правило «качаем только то, чего нет».

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
