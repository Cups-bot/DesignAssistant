using System.IO;
using System.Text;
using System.Text.Json;

namespace CupsForge.Models
{
    public sealed class AppConfig
    {
        public BitrixConfig Bitrix { get; set; } = new();

        public static AppConfig Load()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(path))
                return new AppConfig();

            var opts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppConfig>(json, opts) ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }
    }

    public sealed class BitrixConfig
    {
        public string BaseUrl { get; set; } = "https://bitrix.formacia.ru";
        public string GetDataPath { get; set; } = "/mycups/api/design/getData";
        public string AuthorizationHeader { get; set; } = "";
        public string Login { get; set; } = "";
        public string Password { get; set; } = "";
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Готовое значение заголовка Authorization ("Basic ...").
        ///
        /// Приоритет у профиля пользователя (%APPDATA%\CupsForge\profile.json):
        /// appsettings.json лежит в git-репозитории, а это доступ к API, открытому
        /// в интернет. Значения из appsettings остаются как запасной вариант,
        /// чтобы уже настроенные машины продолжили работать без перенастройки.
        /// </summary>
        public string ResolveAuthHeader()
        {
            var fromProfile = CupsCore.MachineProfile.Current.Bitrix;
            if (fromProfile.IsConfigured)
                return Build(fromProfile.AuthorizationHeader, fromProfile.Login, fromProfile.Password);

            return Build(AuthorizationHeader, Login, Password);
        }

        private static string Build(string header, string login, string password)
        {
            if (!string.IsNullOrWhiteSpace(header))
                return header.Trim();

            if (!string.IsNullOrEmpty(login))
            {
                string token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{login}:{password}"));
                return $"Basic {token}";
            }
            return "";
        }
    }
}
