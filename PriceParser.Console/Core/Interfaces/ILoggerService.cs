namespace PriceParser.Console.Core.Interfaces;

public interface ILoggerService
{
    Task LogErrorAsync(string context, Exception exception, CancellationToken cancellationToken);
}
