using System.Text.Json.Serialization;
using CupsCore;

namespace CupsForge.Models
{
    /// <summary>
    /// Блок "result" ответа Bitrix (POST /…/getData { id }).
    /// Поля приходят строками (русскими для MyCups/Флексо, английскими для CupToYou).
    /// </summary>
    public sealed class DesignData
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        /// <summary>Направление: "mycups" | "cuptoyou" | "formacia" (Флексо).</summary>
        [JsonPropertyName("project")]
        public string? Project { get; set; }

        /// <summary>Название заказа, напр. "CarBar (132583 CarBar ST DW90-430)".</summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>Тип продукта, напр. "Бумажный стакан" (может отсутствовать).</summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        /// <summary>Язык/страна для CupToYou, напр. "Turkish" (может отсутствовать).</summary>
        [JsonPropertyName("lang")]
        public string? Lang { get; set; }

        /// <summary>Вкус для шоколада/конфет, напр. "Dark" (может отсутствовать/быть пустым).</summary>
        [JsonPropertyName("flavor")]
        public string? Flavor { get; set; }

        /// <summary>Тип продукта (артикул), напр. "DW90-430" — ключ шаблона.</summary>
        [JsonPropertyName("product")]
        public string? Product { get; set; }

        /// <summary>Способ печати, напр. "Офсет".</summary>
        [JsonPropertyName("print")]
        public string? Print { get; set; }

        /// <summary>Материал, напр. "Белый мелованный".</summary>
        [JsonPropertyName("side")]
        public string? Side { get; set; }

        /// <summary>Покрытие, напр. "Soft Touch" (может отсутствовать).</summary>
        [JsonPropertyName("coating")]
        public string? Coating { get; set; }
    }

    /// <summary>
    /// Данные заказа, приведённые к enum'ам приложения (результат маппинга).
    /// </summary>
    public sealed class ResolvedDesign
    {
        public long Id { get; init; }
        public string OrderName { get; init; } = "";
        public string DesignCode { get; init; } = "";   // "132583 CarBar ST DW90-430" — имя папки/файла

        public Brand Brand { get; init; }                 // направление (project)
        public Country Country { get; init; }             // страна для CupToYou (lang)

        /// <summary>Тип продукта — идентификатор из каталога, а не enum: набор задаёт каталог.</summary>
        public string ProductType { get; init; } = ProductTypes.Cups;

        public string ProductArticul { get; init; } = ""; // "DW90-430" — ключ шаблона
        public PrintTech PrintTech { get; init; }
        public Material Material { get; init; }
        public Coating Coating { get; init; }

        /// <summary>Вариант продукта из каталога: вкус шоколада, вид конфет. Пусто — вариантов нет.</summary>
        public string Variant { get; init; } = "";

        // Исходные строки Bitrix (для отображения "как есть").
        public string RawProject { get; init; } = "";
        public string RawType { get; init; } = "";
        public string RawPrint { get; init; } = "";
        public string RawSide { get; init; } = "";
        public string RawCoating { get; init; } = "";
        public string RawLang { get; init; } = "";
        public string RawFlavor { get; init; } = "";

        // Предупреждения маппинга (нераспознанные значения и т.п.).
        public List<string> Warnings { get; } = new();

        /// <summary>
        /// Копия с поправленным полем — для листа точечной правки.
        ///
        /// Возвращает НОВЫЙ объект, а не меняет этот: разобранный заказ должен
        /// оставаться тем, что ответил Bitrix. Иначе «что пришло» и «что поправил
        /// человек» перемешиваются, и в журнале потом не разобрать, откуда
        /// взялось значение.
        ///
        /// Исходные строки Bitrix (Raw*) НЕ переносятся для поправленных полей:
        /// показывать «Офсет → Цифра» после ручной правки значит утверждать, что
        /// так распознал разбор, а он распознал ровно наоборот.
        /// </summary>
        public ResolvedDesign With(Brand? brand = null, string? productType = null,
                                   PrintTech? printTech = null, Material? material = null,
                                   Coating? coating = null, Country? country = null,
                                   string? variant = null, string? article = null)
        {
            var copy = new ResolvedDesign
            {
                Id = Id,
                OrderName = OrderName,
                DesignCode = DesignCode,
                Brand = brand ?? Brand,
                Country = country ?? Country,
                ProductType = productType ?? ProductType,
                ProductArticul = article ?? ProductArticul,
                PrintTech = printTech ?? PrintTech,
                Material = material ?? Material,
                Coating = coating ?? Coating,
                Variant = variant ?? Variant,
                RawProject = brand == null ? RawProject : "",
                RawType = productType == null ? RawType : "",
                RawPrint = printTech == null ? RawPrint : "",
                RawSide = material == null ? RawSide : "",
                RawCoating = coating == null ? RawCoating : "",
                RawLang = country == null ? RawLang : "",
                RawFlavor = variant == null ? RawFlavor : ""
            };
            copy.Warnings.AddRange(Warnings);
            return copy;
        }

        /// <summary>
        /// Заявка на создание проекта. Артикул пришёл из Bitrix, поэтому передаётся
        /// как есть — из имени дизайна его доставать не нужно.
        /// </summary>
        public BuildRequest ToBuildRequest() => new()
        {
            DesignCode = DesignCode,
            Article = ProductArticul.Trim(),
            Material = Material,
            Coating = Coating,
            Spec = new DesignSpec
            {
                Brand = Brand,
                ProductType = ProductType,
                PrintTech = PrintTech,
                Country = Country,
                Variant = Variant
            }
        };
    }
}
