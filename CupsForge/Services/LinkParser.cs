using System.Text.RegularExpressions;

namespace CupsForge.Services
{
    /// <summary>
    /// Извлекает ID заказа из ссылки Bitrix, вставленной пользователем.
    /// Поддерживает:
    ///   https://bitrix.formacia.ru/mycups/edit.php?ID=1872914
    ///   .../edit.php?id=1872914&amp;foo=bar
    ///   голый id: 1872914
    /// </summary>
    public static class LinkParser
    {
        public static bool TryParseId(string? input, out long id, out string error)
        {
            id = 0;
            error = "";
            if (string.IsNullOrWhiteSpace(input))
            {
                error = "Пустая ссылка.";
                return false;
            }

            input = input.Trim();

            // 1) голый числовой id
            if (long.TryParse(input, out id) && id > 0)
                return true;

            // 2) параметр запроса ID/id/element_id
            if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
            {
                foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = pair.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = pair[..eq];
                    string val = Uri.UnescapeDataString(pair[(eq + 1)..]);
                    if (key.Equals("ID", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("ELEMENT_ID", StringComparison.OrdinalIgnoreCase))
                    {
                        if (long.TryParse(val, out id) && id > 0)
                            return true;
                    }
                }
            }

            // 3) fallback: ищем ID=NNN где угодно в строке
            var m = Regex.Match(input, @"(?:^|[?&/=_])(?:id|element_id)=?(\d{3,})", RegexOptions.IgnoreCase);
            if (m.Success && long.TryParse(m.Groups[1].Value, out id) && id > 0)
                return true;

            // 4) последнее длинное число в строке
            var nums = Regex.Matches(input, @"\d{4,}");
            if (nums.Count > 0 && long.TryParse(nums[^1].Value, out id) && id > 0)
                return true;

            error = "Не удалось извлечь ID заказа из ссылки.";
            return false;
        }
    }
}
