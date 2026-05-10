using System.Text;
using PriceParser.Console.Core.Interfaces;

namespace PriceParser.Console.Services.Logging;

public sealed class FileLoggerService : ILoggerService
{
    private readonly string _logsFolder;

    public FileLoggerService()
    {
        var basePath = File.Exists(Path.Combine(Environment.CurrentDirectory, "appsettings.json"))
            ? Environment.CurrentDirectory
            : AppContext.BaseDirectory;

        _logsFolder = Path.Combine(basePath, "logs");
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
        Directory.CreateDirectory(_logsFolder);

        var timestamp = DateTime.Now;
        var header = $"{timestamp:yyyy-MM-dd HH:mm:ss.fff} [{level}]";
        var logPath = Path.Combine(_logsFolder, $"run_{timestamp:yyyy-MM-dd}.txt");
        var entry = $"{header} {message}{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}";

        System.Console.ForegroundColor = ConsoleColor.Red;
        System.Console.WriteLine($"{header} {message}");
        System.Console.ResetColor();

        await File.AppendAllTextAsync(logPath, entry, cancellationToken);
    }
}
