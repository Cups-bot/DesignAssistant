using System.Windows;
using System.Windows.Media;
using CupsCore;

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

    /// <summary>
    /// Вариант в листе точечной правки: «Мелованный · coated».
    ///
    /// Список вариантов — данные из каталога, а не разметка. Новый материал или
    /// вкус появляется здесь сам, дорисовывать кнопку не нужно.
    /// </summary>
    public sealed class FixOption
    {
        /// <summary>Что подставить: идентификатор направления, типа, вкуса.</summary>
        public string Id { get; init; } = "";

        public string Label { get; init; } = "";

        /// <summary>Техническая подпись справа — то, что уедет в args.txt.</summary>
        public string Code { get; init; } = "";
    }

    /// <summary>Что именно правит лист. Строкой: набор задаёт каталог, а не enum.</summary>
    public static class FixKeys
    {
        public const string Brand = "brand";
        public const string Type = "type";
        public const string Tech = "tech";
        public const string Material = "material";
        public const string Coating = "coating";
        public const string Variant = "variant";
        public const string Country = "country";
        public const string Article = "article";
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
}
