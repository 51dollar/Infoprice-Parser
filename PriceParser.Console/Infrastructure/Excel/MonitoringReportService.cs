using System.Diagnostics;
using System.Globalization;
using ClosedXML.Excel;
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
            FillInputFile(inputFilePath, outputFlat, mappings, cancellationToken);
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

    private void FillInputFile(
        string inputFilePath,
        Dictionary<string, Dictionary<string, float>> outputData,
        MonitoringStoreMapping[] mappings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stepSw = Stopwatch.StartNew();

        using var wb = new XLWorkbook(inputFilePath);
        var sheetInfo = FindSheet(wb, mappings);

        if (sheetInfo is null)
        {
            ConsoleHelper.WriteWarning("Не найдена таблица с ШК и магазинами.");
            return;
        }

        var lastRow = sheetInfo.UsedRange.LastRow().RowNumber();
        var filledCount = 0;

        for (var r = sheetInfo.HeaderRow + 1; r <= lastRow; r++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bcCell = sheetInfo.Worksheet.Cell(r, sheetInfo.BarcodeColumn);
            var barcode = bcCell.GetFormattedString().Trim();

            if (!IsBarcode(barcode))
                continue;

            if (!outputData.TryGetValue(barcode, out var prices))
                continue;

            foreach (var mapping in mappings)
            {
                if (!sheetInfo.StoreColumns.TryGetValue(mapping.TargetColumnHeader, out var col))
                    continue;

                if (!prices.TryGetValue(mapping.SourceColumnHeader, out var price))
                    continue;

                sheetInfo.Worksheet.Cell(r, col).SetValue(price);
            }

            filledCount++;
        }

        foreach (var ws in wb.Worksheets)
            ws.ConditionalFormats.RemoveAll();

        var baseName = Path.GetFileNameWithoutExtension(inputFilePath);
        var extension = Path.GetExtension(inputFilePath);
        var outputPath = BuildMonitoringPath(_settings.ProcessedFolder, baseName, extension);

        Directory.CreateDirectory(_settings.ProcessedFolder);
        wb.SaveAs(outputPath);

        stepSw.Stop();
        ConsoleHelper.WriteStep($"Заполнение цен ({filledCount} строк)", true, stepSw.Elapsed.TotalSeconds);
    }

    private SheetInfo? FindSheet(XLWorkbook workbook, MonitoringStoreMapping[] mappings)
    {
        var targetHeaders = mappings.Select(m => m.TargetColumnHeader).ToArray();

        SheetInfo? best = null;
        foreach (var ws in workbook.Worksheets)
        {
            var used = ws.RangeUsed();
            if (used is null)
                continue;

            foreach (var row in used.RowsUsed())
            {
                var barcodeCol = FindBarcodeColumn(row);
                if (barcodeCol is null)
                    continue;

                var storeCols = FindStoreColumns(row, targetHeaders);
                if (storeCols.Count == 0)
                    continue;

                if (best is null || storeCols.Count > best.StoreColumns.Count)
                {
                    best = new SheetInfo(ws, used, row.RowNumber(), barcodeCol.Value, storeCols);
                }
            }
        }

        return best;
    }

    private int? FindBarcodeColumn(IXLRangeRow headerRow)
    {
        foreach (var cell in headerRow.CellsUsed())
        {
            var text = cell.GetFormattedString().Trim();
            if (_settings.BarcodeColumnNames.Any(name =>
                    text.Contains(name.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return cell.Address.ColumnNumber;
            }
        }

        return null;
    }

    private static Dictionary<string, int> FindStoreColumns(
        IXLRangeRow headerRow,
        string[] headers)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in headers)
        {
            foreach (var cell in headerRow.CellsUsed())
            {
                var text = cell.GetFormattedString().Trim();
                if (text.Contains(header.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    result[header] = cell.Address.ColumnNumber;
                    break;
                }
            }
        }

        return result;
    }

    private static bool IsBarcode(string value)
    {
        return value.Length >= 6 && value.All(char.IsDigit);
    }

    private static string BuildMonitoringPath(string folder, string baseName, string extension)
    {
        var date = DateTime.Now.ToString("dd.MM.yyyy");
        var candidate = Path.Combine(folder, $"{baseName}-monitoring-{date}{extension}");
        if (!File.Exists(candidate))
            return candidate;

        for (var count = 1; ; count++)
        {
            candidate = Path.Combine(folder, $"{baseName}-monitoring-{date}-{count}{extension}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    private sealed record SheetInfo(
        IXLWorksheet Worksheet,
        IXLRange UsedRange,
        int HeaderRow,
        int BarcodeColumn,
        Dictionary<string, int> StoreColumns);
}
