using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CupsCore
{
    /// <summary>
    /// Превращает логические пути в физические по профилю машины.
    ///
    /// Логический путь — это строка с подстановками вида
    /// <c>{templates}\2_Offset</c> или <c>{out.flexo}</c>. Такие строки лежат
    /// в коде и (со второго этапа) в каталоге продуктов, а конкретные диски
    /// и папки — только в профиле пользователя.
    /// </summary>
    public static class PathResolver
    {
        /// <summary>Физический путь логического корня. Неизвестный ключ — исключение (ошибка в каталоге).</summary>
        public static string Root(string key)
        {
            var roots = MachineProfile.Current.Roots;
            if (roots.TryGetValue(key, out string? path) && !string.IsNullOrWhiteSpace(path))
                return path;

            throw new PathResolutionException(
                $"В профиле не задан корень \"{key}\" ({MachineProfile.Root.Title(key)}). " +
                $"Откройте настройки и укажите папку.");
        }

        /// <summary>Физический путь корня либо null, если он не задан (без исключения).</summary>
        public static string? RootOrNull(string key)
        {
            var roots = MachineProfile.Current.Roots;
            return roots.TryGetValue(key, out string? path) && !string.IsNullOrWhiteSpace(path)
                ? path
                : null;
        }

        /// <summary>
        /// Разворачивает все подстановки <c>{ключ}</c> в строке.
        /// Строка без подстановок возвращается как есть — обычный путь тоже допустим.
        /// </summary>
        public static string Expand(string template)
        {
            if (string.IsNullOrEmpty(template))
                return "";

            // В каталоге разделители удобнее писать через «/» — приводим к виду Windows.
            template = template.Replace('/', Path.DirectorySeparatorChar);

            if (template.IndexOf('{') < 0)
                return template;

            var sb = new StringBuilder(template.Length + 64);
            int i = 0;
            while (i < template.Length)
            {
                char c = template[i];
                if (c != '{')
                {
                    sb.Append(c);
                    i++;
                    continue;
                }

                int close = template.IndexOf('}', i + 1);
                if (close < 0)
                {
                    // Незакрытая скобка — оставляем как есть, пусть будет видно в пути.
                    sb.Append(template, i, template.Length - i);
                    break;
                }

                string key = template.Substring(i + 1, close - i - 1);
                sb.Append(Root(key));
                i = close + 1;
            }

            return Normalize(sb.ToString());
        }

        /// <summary>
        /// Схлопывает «..» и лишние разделители: каталог может адресоваться
        /// относительно корня — например «{base}\..\Soft\CupsForge».
        /// Путь с недопустимыми символами возвращается как есть, чтобы человек
        /// увидел в сообщении именно то, что написал.
        /// </summary>
        private static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.Contains(".."))
                return path;

            try { return Path.GetFullPath(path); }
            catch { return path; }
        }

        /// <summary>Путь внутри логического корня: <c>Combine(Root.Templates, "2_Offset")</c>.</summary>
        public static string Combine(string rootKey, params string[] parts)
        {
            string basePath = Root(rootKey);
            if (parts == null || parts.Length == 0)
                return basePath;

            var all = new List<string>(parts.Length + 1) { basePath };
            all.AddRange(parts);
            return Path.Combine(all.ToArray());
        }
    }

    /// <summary>
    /// Программа настроена неправильно: не задан корень, каталог неполон и т.п.
    /// Такие ошибки показываются пользователю как есть — они написаны для него.
    /// </summary>
    public abstract class ConfigurationException : Exception
    {
        protected ConfigurationException(string message) : base(message) { }
    }

    /// <summary>Логический путь не удалось развернуть — корень не настроен в профиле.</summary>
    public sealed class PathResolutionException : ConfigurationException
    {
        public PathResolutionException(string message) : base(message) { }
    }
}
