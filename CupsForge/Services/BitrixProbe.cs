using System.Net.Http;
using CupsForge.Models;

namespace CupsForge.Services
{
    /// <summary>
    /// Проверка ключа доступа к Bitrix из окна настроек.
    ///
    /// Спрашивает заведомо несуществующий заказ: важен не ответ, а то, пропустил ли
    /// нас сервер. 401 — ключ не принят, всё остальное значит, что связь есть
    /// и ключ рабочий.
    /// </summary>
    public static class BitrixProbe
    {
        /// <summary>Идентификатор, которого заведомо нет — данные нас не интересуют.</summary>
        private const long ProbeOrderId = 1;

        public static async Task<(bool ok, string message)> TestAsync(string key)
        {
            // Берём адрес и таймаут из профиля, а ключ — тот, что сейчас введён
            // в поле: проверять надо именно его, а не сохранённый.
            var live = CupsCore.MachineProfile.Current.Bitrix;
            var config = new CupsCore.BitrixAccess
            {
                AuthorizationHeader = key,
                BaseUrl = live.BaseUrl,
                DataPath = live.DataPath,
                TimeoutSeconds = live.TimeoutSeconds
            };

            try
            {
                using var client = new BitrixClient(config);
                await client.GetDataAsync(ProbeOrderId);

                // Заказ неожиданно нашёлся — связь и ключ точно в порядке.
                return (true, $"Связь есть, ключ принят ({config.BaseUrl}).");
            }
            catch (BitrixException ex) when (ex.Message.Contains("401"))
            {
                return (false, "Сервер не принял ключ (401). Проверьте, что вставлен он целиком.");
            }
            catch (BitrixException ex)
            {
                // «Заказ не найден», «Дизайн не найден» и подобное означают,
                // что авторизация прошла — а это всё, что мы проверяем.
                return (true, $"Связь есть, ключ принят. Ответ сервера: {ex.Message}");
            }
            catch (HttpRequestException ex)
            {
                return (false, $"Сервер недоступен: {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                return (false, $"Сервер не ответил за {config.TimeoutSeconds} с.");
            }
            catch (Exception ex)
            {
                return (false, "Не удалось проверить: " + ex.Message);
            }
        }
    }
}
