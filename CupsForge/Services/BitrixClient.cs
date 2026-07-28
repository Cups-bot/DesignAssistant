using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CupsForge.Models;

namespace CupsForge.Services
{
    public sealed class BitrixException : Exception
    {
        public BitrixException(string message) : base(message) { }
    }

    /// <summary>
    /// HTTP-клиент к API Bitrix MyCups. Basic-авторизация из конфига.
    /// </summary>
    public sealed class BitrixClient : IDisposable
    {
        private readonly BitrixConfig _cfg;
        private readonly HttpClient _http;

        public BitrixClient(BitrixConfig cfg)
        {
            _cfg = cfg;
            _http = new HttpClient
            {
                BaseAddress = new Uri(cfg.BaseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(cfg.TimeoutSeconds > 0 ? cfg.TimeoutSeconds : 30)
            };

            string auth = cfg.ResolveAuthHeader();
            if (!string.IsNullOrEmpty(auth))
            {
                var parts = auth.Split(' ', 2);
                _http.DefaultRequestHeaders.Authorization = parts.Length == 2
                    ? new AuthenticationHeaderValue(parts[0], parts[1])
                    : new AuthenticationHeaderValue("Basic", auth);
            }
        }

        /// <summary>
        /// POST getData { id }. Возвращает данные заказа или бросает BitrixException.
        /// </summary>
        public async Task<DesignData> GetDataAsync(long id, CancellationToken ct = default)
        {
            using var resp = await PostJsonAsync(_cfg.GetDataPath, new { id }, ct).ConfigureAwait(false);

            if (resp.StatusCode == HttpStatusCode.NotFound)
                throw new BitrixException($"Заказ {id} не найден (404).");
            if (resp.StatusCode == HttpStatusCode.Unauthorized)
                throw new BitrixException("Ошибка авторизации (401). Проверьте логин/пароль в appsettings.json.");
            if (!resp.IsSuccessStatusCode)
                throw new BitrixException($"Сервер вернул {(int)resp.StatusCode} {resp.ReasonPhrase}.");

            string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            // result может быть объектом (успех) либо строкой с текстом ошибки
            // (напр. {"result":"Дизайн не найден!","status":"error"}), поэтому
            // разбираем через JsonDocument и проверяем status.
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(body);
            }
            catch (Exception ex)
            {
                throw new BitrixException($"Не удалось разобрать ответ сервера: {ex.Message}");
            }

            using (doc)
            {
                var root = doc.RootElement;

                string? status = root.TryGetProperty("status", out var st) ? st.GetString() : null;
                bool hasResult = root.TryGetProperty("result", out var resultEl);

                if (string.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
                {
                    string msg = hasResult && resultEl.ValueKind == JsonValueKind.String
                        ? (resultEl.GetString() ?? "Дизайн не найден.")
                        : "Дизайн не найден.";
                    throw new BitrixException(msg);
                }

                if (!hasResult || resultEl.ValueKind != JsonValueKind.Object)
                    throw new BitrixException("Ответ без блока result.");

                var data = resultEl.Deserialize<DesignData>(
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (data == null)
                    throw new BitrixException("Пустой блок result.");

                if (data.Id == 0) data.Id = id;
                return data;
            }
        }

        private Task<HttpResponseMessage> PostJsonAsync(string path, object payload, CancellationToken ct)
        {
            string json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            return _http.PostAsync(path.TrimStart('/'), content, ct);
        }

        public void Dispose() => _http.Dispose();
    }
}
