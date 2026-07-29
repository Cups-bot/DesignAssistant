using System.IO;
using CupsCore;

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

        // Профиль рабочего места на время прогона уводим в песочницу.
        // Проверки открывают настоящее окно и нажимают в нём кнопки, а окно
        // сохраняет профиль на диск — без подмены папки прогон затирал
        // настройки машины путями во временную папку, которую сам же и удалял.
        string realProfile = MachineProfile.FilePath;
        DateTime? realStamp = File.Exists(realProfile) ? File.GetLastWriteTimeUtc(realProfile) : null;

        string sandbox = Path.Combine(Path.GetTempPath(), "cupsforge_selfcheck_profile");
        MachineProfile.RedirectStorage(sandbox);

        var check = new Checker();
        try
        {
            LogicChecks.Run(check);
            UiChecks.Run(check);
        }
        finally
        {
            MachineProfile.RedirectStorage(null);
            try { Directory.Delete(sandbox, true); } catch { /* уже нет — и ладно */ }
        }

        // Сторож: прогон не имеет права трогать профиль рабочего места.
        // Проверка стоит последней, потому что сломать это может любая из верхних.
        check.Section("Побочные действия");
        DateTime? nowStamp = File.Exists(realProfile) ? File.GetLastWriteTimeUtc(realProfile) : null;
        check.True("профиль рабочего места не тронут", nowStamp == realStamp);

        return check.Report();
    }
}
