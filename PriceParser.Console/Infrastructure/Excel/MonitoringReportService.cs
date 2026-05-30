using System.Diagnostics;
using PriceParser.Console.Configuration;
using PriceParser.Console.Core.Interfaces;
using PriceParser.Console.Core.Models;
using PriceParser.Console.Utils;

namespace PriceParser.Console.Infrastructure.Excel;

public sealed class MonitoringReportService : IMonitoringReportService
{
    private readonly AppSettings _settings;
    private readonly ILoggerService _logger;

    public MonitoringReportService(AppSettings settings, ILoggerService logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task ProcessFileAsync(
        string inputFilePath,
        IReadOnlyDictionary<string, ParsedProductPrices> outputData,
        CancellationToken cancellationToken)
    {
        var mappings = _settings.Monitoring.StoreMappings
            .Where(m => !string.IsNullOrWhiteSpace(m.TargetColumnHeader)
                     && !string.IsNullOrWhiteSpace(m.SourceColumnHeader))
            .ToArray();

        if (mappings.Length == 0)
        {
            ConsoleHelper.WriteWarning("Мониторинг пропущен: StoreMappings пустой.");
            return;
        }

        if (outputData.Count == 0)
        {
            ConsoleHelper.WriteWarning("Мониторинг пропущен: нет данных.");
            return;
        }

        var inputName = Path.GetFileName(inputFilePath);

        try
        {
            var outputFlat = FlattenOutputData(outputData, mappings);
            var baseName = Path.GetFileNameWithoutExtension(inputFilePath);
            var extension = Path.GetExtension(inputFilePath);
            var outputPath = BuildMonitoringPath(_settings.ProcessedFolder, baseName, extension);

            var stepSw = Stopwatch.StartNew();
            var filledCount = new MonitoringReportBuilder().FillPrices(
                inputFilePath, outputPath, outputFlat, mappings, _settings.BarcodeColumnNames);
            stepSw.Stop();

            ConsoleHelper.WriteStep($"Заполнение цен ({filledCount} строк)", true, stepSw.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            ConsoleHelper.WriteError($"Ошибка мониторинга: {ex.Message}");
            await _logger.LogErrorAsync(inputName, ex, cancellationToken);
        }
    }

    private static Dictionary<string, Dictionary<string, float>> FlattenOutputData(
        IReadOnlyDictionary<string, ParsedProductPrices> outputData,
        MonitoringStoreMapping[] mappings)
    {
        var result = new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (barcode, prices) in outputData)
        {
            if (!IsBarcode(barcode))
                continue;

            var storePrices = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

            foreach (var mapping in mappings)
            {
                foreach (var (store, price) in prices.PricesByStore)
                {
                    if (store.Contains(mapping.SourceColumnHeader, StringComparison.OrdinalIgnoreCase))
                    {
                        storePrices[mapping.SourceColumnHeader] = price;
                        break;
                    }
                }
            }

            if (storePrices.Count > 0)
                result[barcode] = storePrices;
        }

        return result;
    }

    private static bool IsBarcode(string value)
    {
        return value.Length >= 6 && value.All(char.IsDigit);
    }

    private static string BuildMonitoringPath(string folder, string baseName, string extension)
    {
        var candidate = Path.Combine(folder, $"{baseName}{extension}");
        if (!File.Exists(candidate))
            return candidate;

        for (var count = 1; ; count++)
        {
            candidate = Path.Combine(folder, $"{baseName}-{count}{extension}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }

}
