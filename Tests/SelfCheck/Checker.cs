namespace SelfCheck;

/// <summary>
/// Копилка результатов. Не тестовый фреймворк — просто прогон, который можно
/// запустить одной командой и увидеть, не разъехалось ли поведение.
/// </summary>
public sealed class Checker
{
    private int _passed;
    private readonly List<string> _failures = new();

    public void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine("--- " + title + " ---");
    }

    public void Info(string line) => Console.WriteLine("  " + line);

    public void Equal(string what, string? actual, string expected)
    {
        bool ok = string.Equals(actual ?? "(null)", expected, StringComparison.OrdinalIgnoreCase);
        Console.WriteLine($"{(ok ? "  ok" : "FAIL")}  {what}");
        if (ok)
        {
            _passed++;
            return;
        }

        Console.WriteLine($"        ожидалось: {expected}");
        Console.WriteLine($"        получено : {actual ?? "(null)"}");
        _failures.Add(what);
    }

    public void True(string what, bool value)
    {
        Console.WriteLine($"{(value ? "  ok" : "FAIL")}  {what}");
        if (value) _passed++; else _failures.Add(what);
    }

    public void Fail(string what)
    {
        Console.WriteLine("FAIL  " + what);
        _failures.Add(what);
    }

    public int Report()
    {
        Console.WriteLine();
        Console.WriteLine($"Проверок пройдено: {_passed}, провалено: {_failures.Count}");
        foreach (string f in _failures)
            Console.WriteLine("  - " + f);
        return _failures.Count == 0 ? 0 : 1;
    }
}
