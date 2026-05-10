using PriceParser.Console.Core.Models;

namespace PriceParser.Console.Core.Interfaces;

/// <summary>Строит мониторинговый отчёт — заполняет цены в копии исходного файла.</summary>
public interface IMonitoringReportService
{
    /// <param name="inputFilePath">Исходный файл (его копия станет отчётом).</param>
    /// <param name="outputData">Цены по штрихкодам, полученные на этапе парсинга.</param>
    Task ProcessFileAsync(
        string inputFilePath,
        IReadOnlyDictionary<string, ParsedProductPrices> outputData,
        CancellationToken cancellationToken);
}
