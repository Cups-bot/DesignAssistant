namespace SelfCheck;

/// <summary>
/// Прогон самопроверки: dotnet run --project Tests/SelfCheck
///
/// Не заменяет ручное тестирование, но ловит главное — что поведение
/// не разъехалось после правок каталога или кода.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var check = new Checker();
        LogicChecks.Run(check);
        UiChecks.Run(check);
        return check.Report();
    }
}
