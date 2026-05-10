namespace PriceParser.Console.Core.Interfaces;

public interface IMonitoringReportService
{
    Task ProcessFileAsync(string inputFilePath, string outputFilePath, CancellationToken cancellationToken);
}
