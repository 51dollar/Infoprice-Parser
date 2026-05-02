namespace PriceParser.Console.Core.Interfaces;

public interface ILoggerService
{
    Task LogInfoAsync(string message, CancellationToken cancellationToken);

    Task LogWarningAsync(string message, CancellationToken cancellationToken);

    Task LogErrorAsync(string barcode, Exception exception, CancellationToken cancellationToken);
}
