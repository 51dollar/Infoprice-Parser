using System.Text;
using PriceParser.Console.Core.Interfaces;

namespace PriceParser.Console.Infrastructure.Logging;

/// <summary>
/// Логирование ошибок: пишет в файл logs/run_yyyy-MM-dd.txt и дублирует
/// в консоль красным цветом. Только ошибки — информационные сообщения
/// идут напрямую в System.Console.
/// </summary>
public sealed class FileLoggerService : ILoggerService
{
    private readonly Lazy<string> _logsFolder;

    public FileLoggerService()
    {
        _logsFolder = new Lazy<string>(() =>
        {
            var basePath = File.Exists(Path.Combine(Environment.CurrentDirectory, "appsettings.json"))
                ? Environment.CurrentDirectory
                : AppContext.BaseDirectory;

            var path = Path.Combine(basePath, "logs");
            Directory.CreateDirectory(path);
            return path;
        });
    }

    public async Task LogErrorAsync(string context, Exception exception, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(512);
        builder.AppendLine($"Context: {context}");
        builder.AppendLine($"Message: {exception.Message}");
        builder.AppendLine("StackTrace:");
        builder.AppendLine(exception.StackTrace ?? string.Empty);

        await LogAsync("ERROR", builder.ToString().TrimEnd(), cancellationToken);
    }

    private async Task LogAsync(string level, string message, CancellationToken cancellationToken)
    {
        var timestamp = DateTime.Now;
        var header = $"{timestamp:yyyy-MM-dd HH:mm:ss.fff} [{level}]";
        var logPath = Path.Combine(_logsFolder.Value, $"run_{timestamp:yyyy-MM-dd}.txt");
        var entry = $"{header} {message}{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}";

        System.Console.ForegroundColor = ConsoleColor.Red;
        System.Console.WriteLine($"{header} {message}");
        System.Console.ResetColor();

        await File.AppendAllTextAsync(logPath, entry, cancellationToken);
    }
}
