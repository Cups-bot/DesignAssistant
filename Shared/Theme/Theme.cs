using System;
using System.Collections.Generic;
using System.Windows;

namespace CupsCore
{
    /// <summary>
    /// Подключение дизайн-токенов и иконок к приложению.
    ///
    /// Живёт кодом, а не строчками в App.xaml, по одной причине: приложений два.
    /// Рабочее (CupsForge) и то, которое создаёт самопроверка, чтобы открыть
    /// настоящее окно. Если словари подключены только в App.xaml, самопроверка
    /// открывает окно без токенов и падает на первом же StaticResource —
    /// то есть ломается ровно там, где должна была бы стеречь.
    ///
    /// Одно место — один список.
    /// </summary>
    public static class Theme
    {
        /// <summary>
        /// Адреса словарей. Порядок важен: Icons ссылается на размеры из Tokens.
        ///
        /// Имя сборки в адресе указано НАМЕРЕННО. Короткая форма
        /// (pack://application:,,,/Shared/…) ищет ресурс в запускающей сборке,
        /// а запускающих две: рабочая CupsForge и selfcheck. В рабочей короткая
        /// форма сработала бы, в прогоне — «не удается найти ресурс». Ровно тот
        /// сорт различий, из-за которого «у меня работает».
        /// </summary>
        private const string Component = "pack://application:,,,/CupsForge;component/Shared/Theme/";

        public static readonly string[] Dictionaries =
        {
            Component + "Tokens.xaml",
            Component + "Icons.xaml",
            Component + "Controls.xaml"
        };

        /// <summary>
        /// Домешивает словари в ресурсы приложения. Повторный вызов безвреден:
        /// уже подключённые не дублируются — вторая копия словаря завела бы
        /// вторые экземпляры кистей, и «поменяли цвет, а половина осталась
        /// прежней» стало бы нормой.
        /// </summary>
        public static void Apply(Application app)
        {
            foreach (string source in Dictionaries)
            {
                var uri = new Uri(source, UriKind.Absolute);
                if (AlreadyMerged(app.Resources, uri))
                    continue;

                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
            }
        }

        private static bool AlreadyMerged(ResourceDictionary target, Uri uri)
        {
            foreach (ResourceDictionary merged in target.MergedDictionaries)
            {
                if (merged.Source == uri)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Ключи, без которых разметка не соберётся. Список ведётся руками
        /// намеренно: он и есть договор между токенами и окнами. Опечатка в
        /// имени ресурса в WPF молчит до открытия окна — а окно у дизайнера
        /// открывается позже, чем прогон у нас.
        /// </summary>
        public static IReadOnlyList<string> RequiredKeys { get; } = new[]
        {
            // цвет
            "Bg", "Surface", "Panel", "Input", "Line", "LineSoft",
            "Text", "Muted", "Dim", "Accent", "AccentDeep", "Ok", "Warn", "Danger",
            "Scrim", "AccentGradient", "WindowBg",
            // форма
            "R.Win", "R.Md", "R.Sm", "R.Pill",
            "S.1", "S.2", "S.3", "S.4", "S.5", "StagePadding",
            // размеры
            "Size.Window", "Size.WindowHeight", "Size.Titlebar", "Size.Footer",
            "Size.Field", "Size.Action", "Size.SettingsPanel", "Size.Icon", "Size.Stroke",
            // типографика
            "F.Regular", "F.Medium", "F.Semibold", "F.Bold", "F.Mono",
            "T.Title", "T.Value", "T.Body", "T.Label", "T.Mono",
            "Title", "Value", "Body", "Label", "Mono",
            // движение
            "M.Fast", "M.Base", "M.Slow", "Ease",
            // контролы
            "Field", "PrimaryAction", "SubmitButton", "IconButton", "Ghost", "LinkAction",
            "Combo", "ComboItem", "ScrollThumb", "FadeBottom",
            // иконки
            "Icon", "IconPlain",
            "I.Link", "I.Arrow", "I.Pencil", "I.Sliders", "I.Check", "I.CheckCircle",
            "I.Warn", "I.ChevronDown", "I.ChevronUp", "I.ChevronRight",
            "I.Folder", "I.FolderPlus", "I.Refresh", "I.Close", "I.Doc",
            "I.Download", "I.Brush", "I.Clipboard", "I.Disk"
        };
    }
}
