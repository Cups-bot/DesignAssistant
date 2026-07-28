namespace CupsCore
{
    /// <summary>
    /// Заявка на создание проекта. Её собирают все входы одинаково: и загрузка
    /// заказа из Bitrix, и ручной ввод. Дальше работает один <see cref="ProjectBuilder"/>.
    /// </summary>
    public sealed class BuildRequest
    {
        /// <summary>Имя дизайна: «132583 CarBar ST DW90-430». Из него получается имя папки.</summary>
        public string DesignCode { get; init; } = "";

        /// <summary>Предметные параметры — по ним каталог выбирает продукт.</summary>
        public DesignSpec Spec { get; init; } = new();

        /// <summary>
        /// Артикул. null — взять из имени дизайна по правилу продукта (ручной ввод);
        /// строка — использовать как есть (пришёл из Bitrix).
        /// </summary>
        public string? Article { get; init; }

        public Material Material { get; init; } = Material.Uncoated;

        public Coating Coating { get; init; } = Coating.None;
    }
}
