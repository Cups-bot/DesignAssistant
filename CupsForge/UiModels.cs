using System.Windows;
using System.Windows.Media;

namespace CupsForge
{
    /// <summary>Как выглядит сообщение: определяет иконку и цвет.</summary>
    public enum NoticeKind
    {
        /// <summary>Что-то доступно: новая версия, шаблоны.</summary>
        Info,
        /// <summary>Работать можно, но не всё так, как ожидалось.</summary>
        Warning,
        /// <summary>Работать нельзя, пока не разберутся.</summary>
        Blocking
    }

    /// <summary>
    /// Строка результата: «Артикул — DW90-430».
    ///
    /// Состав строк — данные, а не разметка: он строится из загруженного заказа
    /// и каталога. Новый продукт со своим полем появится сам, дорисовывать
    /// строку в XAML не нужно.
    /// </summary>
    public sealed class ResultField
    {
        public string Label { get; init; } = "";
        public string Value { get; init; } = "";

        /// <summary>Значение не распознано и мешает создать проект.</summary>
        public bool Warn { get; init; }

        /// <summary>
        /// Какое поле правит лист точечной правки. Пусто — строка только для чтения.
        /// </summary>
        public string? FixKey { get; init; }

        public string? Hint { get; init; }

        public Brush ValueBrush => Warn ? Ui.Brush("Warn") : Ui.Brush("Text");

        public Visibility FixVisibility =>
            string.IsNullOrEmpty(FixKey) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Запись журнала. Время и значок вместо сплошной простыни текста.</summary>
    public sealed class LogEntry
    {
        public string Time { get; init; } = "";
        public string Text { get; init; } = "";
        public NoticeKind Kind { get; init; }

        public Geometry Icon => Kind switch
        {
            NoticeKind.Info => Ui.Icon("I.CheckCircle"),
            _ => Ui.Icon("I.Warn")
        };

        public Brush IconBrush => Kind switch
        {
            NoticeKind.Info => Ui.Brush("Ok"),
            NoticeKind.Warning => Ui.Brush("Warn"),
            _ => Ui.Brush("Danger")
        };
    }

    /// <summary>
    /// Сообщение в полоске под шапкой.
    ///
    /// Полоска одна, а поводов много: новая версия, шаблоны, каталог не оттуда,
    /// ненастроенные папки. Раньше на каждый повод была своя полоска, и все
    /// лежали в одной строке сетки друг поверх друга — два сообщения сразу,
    /// и второго не видно вовсе. Теперь это очередь: показывается одно,
    /// остальные ждут.
    /// </summary>
    public sealed class Notice
    {
        /// <summary>Чем вызвано. По нему сообщение заменяется и снимается.</summary>
        public string Id { get; init; } = "";

        public string Text { get; set; } = "";
        public NoticeKind Kind { get; init; }

        /// <summary>Надпись на кнопке. null — кнопки нет.</summary>
        public string? ActionTitle { get; set; }

        public Action? Action { get; set; }

        /// <summary>Можно ли закрыть крестиком. Блокирующие снимать нельзя.</summary>
        public bool Dismissable => Kind != NoticeKind.Blocking;

        public Geometry Icon => Kind switch
        {
            NoticeKind.Info => Ui.Icon("I.Download"),
            _ => Ui.Icon("I.Warn")
        };

        public Brush IconBrush => Kind switch
        {
            NoticeKind.Info => Ui.Brush("Accent"),
            NoticeKind.Warning => Ui.Brush("Warn"),
            _ => Ui.Brush("Danger")
        };
    }

    /// <summary>
    /// Доступ к токенам из кода.
    ///
    /// Цвета и иконки нужны и в разметке, и в коде (строка журнала выбирает
    /// значок по своему виду). Брать их отсюда, а не заводить второй набор
    /// констант в C#: иначе тема разъедется ровно пополам.
    /// </summary>
    public static class Ui
    {
        public static Brush Brush(string key) =>
            Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;

        public static Geometry Icon(string key) =>
            Application.Current?.TryFindResource(key) as Geometry ?? Geometry.Empty;
    }
}
