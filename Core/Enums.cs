namespace CupsCore
{
    // Единый источник доменных enum'ов для обеих программ
    // (ручной редактор DesignAssistant и авто-программа CupsForge).
    public enum Brand { MyCups, CuptoYou, Flexo }
    public enum PrintTech { Offset, Digital, Pantone }
    public enum Material { Uncoated, Coated }
    public enum Country { TR, DE, EN, IT }

    public enum ProductType
    {
        Cups,
        Plastic,
        Sugar,
        Choko,
        Candy
    }

    public enum ChokoType
    {
        Milk,
        Dark,
        Orange,
        Strawberry,
        White
    }

    public enum CandyType
    {
        Assorted,
        Dubai
    }

    public enum Coating
    {
        None,
        SoftTouch,
        ColorTouch
    }
}
