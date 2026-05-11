using System.Diagnostics;

namespace PriceParser.Console.Utils;

public static class ConsoleHelper
{
    private const int StepPad = 48;
    private const int BarWidth = 20;
    private static readonly object _consoleLock = new();

    public static void WriteHeader(string inputFolder, string priceFolder, string monitoringFolder, int threads)
    {
        lock (_consoleLock)
        {
            System.Console.ForegroundColor = ConsoleColor.DarkYellow;
            System.Console.WriteLine("  ════════════════════════════════════════════════════════════");
            System.Console.ResetColor();
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine("  PriceParser");
            System.Console.ResetColor();
            System.Console.ForegroundColor = ConsoleColor.DarkYellow;
            System.Console.WriteLine("  ════════════════════════════════════════════════════════════");
            System.Console.ResetColor();
            System.Console.WriteLine($"  Вход        :  {inputFolder}");
            System.Console.WriteLine($"  Цены        :  {priceFolder}");
            System.Console.WriteLine($"  Мониторинг  :  {monitoringFolder}");
            System.Console.WriteLine($"  Потоков     :  {threads}");
            System.Console.WriteLine();
        }
    }

    public static void WriteSection(string title)
    {
        lock (_consoleLock)
        {
            System.Console.ForegroundColor = ConsoleColor.DarkYellow;
            System.Console.WriteLine($"  ═══════════════ {title} ═══════════════════════════════════════");
            System.Console.ResetColor();
            System.Console.WriteLine();
        }
    }

    public static void WriteFileHeader(int current, int total, string fileName)
    {
        lock (_consoleLock)
        {
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.WriteLine($"  ── Файл {current}/{total} ─ {fileName} ──────────────────────────────────");
            System.Console.ResetColor();
        }
    }

    public static void WriteStep(string description, bool success, double? elapsedSeconds = null)
    {
        lock (_consoleLock)
        {
            var status = success ? "✓" : "✗";
            var time = elapsedSeconds.HasValue ? $"  {elapsedSeconds.Value:F2} с" : "";
            var dots = description.Length < StepPad
                ? new string('.', StepPad - description.Length)
                : "";

            System.Console.Write($"    {description}{dots} ");
            System.Console.ForegroundColor = success ? ConsoleColor.Green : ConsoleColor.Red;
            System.Console.Write(status);
            System.Console.ResetColor();
            System.Console.WriteLine(time);
        }
    }

    public static void WriteProgress(int current, int total)
    {
        lock (_consoleLock)
        {
            var pct = total > 0 ? (double)current / total : 0;
            var filled = (int)(pct * BarWidth);
            var bar = new string('█', filled) + new string('░', BarWidth - filled);
            System.Console.Write($"\r    Загрузка цен: [{bar}] {current}/{total} ({pct * 100:F0}%)");
        }
    }

    public static void ClearCurrentLine()
    {
        lock (_consoleLock)
        {
            var width = System.Console.WindowWidth > 0 ? System.Console.WindowWidth : 80;
            System.Console.Write($"\r{new string(' ', width)}\r");
        }
    }

    public static void WriteFileSummary(double elapsedSeconds)
    {
        lock (_consoleLock)
        {
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.WriteLine("  ──────────────────────────────────────────────────────────────");
            System.Console.ResetColor();
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine($"  ИТОГО ПО ФАЙЛУ ──────────────────────────  {elapsedSeconds:F2} с");
            System.Console.ResetColor();
            System.Console.WriteLine();
        }
    }

    public static void WriteServiceResult(string name, int fileCount, double elapsedSeconds)
    {
        lock (_consoleLock)
        {
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.WriteLine("  ──────────────────────────────────────────────────────────────");
            System.Console.ResetColor();
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine($"  {name} завершён за {elapsedSeconds:F2} с  ({fileCount} файлов)");
            System.Console.ResetColor();
            System.Console.WriteLine();
        }
    }

    public static void WriteTotalSummary(int processed, int total, double elapsedSeconds)
    {
        lock (_consoleLock)
        {
            System.Console.ForegroundColor = ConsoleColor.DarkYellow;
            System.Console.WriteLine("  ════════════════════════════════════════════════════════════");
            System.Console.ResetColor();
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine($"  ✔ Обработано:  {processed} / {total}  файлов");
            System.Console.WriteLine($"  ✔ Время:        {elapsedSeconds:F2} с");
            System.Console.ResetColor();
            System.Console.ForegroundColor = ConsoleColor.DarkYellow;
            System.Console.WriteLine("  ════════════════════════════════════════════════════════════");
            System.Console.ResetColor();
            System.Console.WriteLine();
        }
    }

    public static void WriteInfo(string text)
    {
        lock (_consoleLock)
        {
            System.Console.ForegroundColor = ConsoleColor.Gray;
            System.Console.WriteLine($"  {text}");
            System.Console.ResetColor();
        }
    }

    public static void WriteWarning(string text)
    {
        lock (_consoleLock)
        {
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine($"  {text}");
            System.Console.ResetColor();
        }
    }

    public static void WriteError(string text)
    {
        lock (_consoleLock)
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine($"  {text}");
            System.Console.ResetColor();
        }
    }

    public static void WaitForExit()
    {
        System.Console.ForegroundColor = ConsoleColor.Gray;
        System.Console.Write("  Нажмите Enter или Esc для выхода...");
        System.Console.ResetColor();

        while (true)
        {
            var key = System.Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter || key.Key == ConsoleKey.Escape)
                break;
        }

        System.Console.WriteLine();
    }
}
