namespace CupsCore
{
    /// <summary>
    /// Параметры заказа, которых достаточно, чтобы выбрать шаблон и папку вывода.
    ///
    /// Тип продукта и вариант — строки, а не enum'ы, и это принципиально: их набор
    /// задаёт каталог. Новый продукт (крышки, пакеты) появляется правкой catalog.json,
    /// без правки кода и пересборки.
    /// </summary>
    public sealed class DesignSpec
    {
        public Brand Brand { get; init; } = Brand.MyCups;

        /// <summary>Идентификатор типа продукта из каталога: Cups, Plastic, Lids…</summary>
        public string ProductType { get; init; } = ProductTypes.Cups;

        public PrintTech PrintTech { get; init; } = PrintTech.Offset;

        public Country Country { get; init; } = Country.TR;

        /// <summary>Вариант продукта: вкус шоколада, вид конфет. Пусто — вариантов нет.</summary>
        public string Variant { get; init; } = "";
    }

    /// <summary>
    /// Идентификаторы типов продуктов, встречающиеся в коде.
    /// Каталог может содержать любые другие — код о них знать не обязан.
    /// </summary>
    public static class ProductTypes
    {
        public const string Cups = "Cups";

        /// <summary>Имя enum'а интерфейса как идентификатор каталога.</summary>
        public static string From(ProductType type) => type.ToString();
    }
}
