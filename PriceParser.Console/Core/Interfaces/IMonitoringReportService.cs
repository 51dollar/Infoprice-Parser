using PriceParser.Console.Core.Models;

namespace PriceParser.Console.Core.Interfaces;

public interface IMonitoringReportService
{
    Task ProcessFileAsync(
        string inputFilePath,
        IReadOnlyDictionary<string, ParsedProductPrices> outputData,
        CancellationToken cancellationToken);
}
