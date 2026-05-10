using System.Globalization;
using ClosedXML.Excel;
using PriceParser.Console.Configuration;
using PriceParser.Console.Core.Interfaces;

namespace PriceParser.Console.Services.Excel;

public sealed class MonitoringReportService : IMonitoringReportService
{
    private readonly AppSettings _settings;
    private readonly ILoggerService _logger;

    public MonitoringReportService(AppSettings settings, ILoggerService logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task ProcessFileAsync(string inputFilePath, string outputFilePath, CancellationToken cancellationToken)
    {
        var mappings = _settings.Monitoring.StoreMappings
            .Where(m => !string.IsNullOrWhiteSpace(m.TargetColumnHeader)
                     && !string.IsNullOrWhiteSpace(m.SourceColumnHeader))
            .ToArray();

        if (mappings.Length == 0)
        {
            await _logger.LogInfoAsync("Мониторинг пропущен: StoreMappings пустой.", cancellationToken);
            return;
        }

        var inputName = Path.GetFileName(inputFilePath);

        try
        {
            var outputData = ReadOutputData(outputFilePath, mappings, cancellationToken);

            if (outputData.Count == 0)
            {
                await _logger.LogWarningAsync(
                    $"Мониторинг: в '{Path.GetFileName(outputFilePath)}' нет данных.", cancellationToken);
                return;
            }

            FillInputFile(inputFilePath, outputData, mappings, cancellationToken);

            await _logger.LogInfoAsync(
                $"Мониторинг для '{inputName}' завершён.", cancellationToken);
        }
        catch (Exception ex)
        {
            await _logger.LogErrorAsync(inputName, ex, cancellationToken);
        }
    }

    private Dictionary<string, Dictionary<string, float>> ReadOutputData(
        string outputFilePath,
        MonitoringStoreMapping[] mappings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var wb = new XLWorkbook(outputFilePath);
        var ws = wb.Worksheets.First();
        var used = ws.RangeUsed();

        if (used is null)
            return [];

        var sourceHeaders = mappings.Select(m => m.SourceColumnHeader).ToArray();
        var storeColumns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var cell in used.Row(1).CellsUsed())
        {
            var header = cell.GetFormattedString().Trim();
            if (header.Length == 0)
                continue;

            foreach (var src in sourceHeaders)
            {
                if (header.Contains(src, StringComparison.OrdinalIgnoreCase))
                {
                    storeColumns[src] = cell.Address.ColumnNumber;
                    break;
                }
            }
        }

        var result = new Dictionary<string, Dictionary<string, float>>();
        var lastRow = used.LastRow().RowNumber();

        for (var r = 2; r <= lastRow; r++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var barcode = ws.Cell(r, 1).GetFormattedString().Trim();
            if (!IsBarcode(barcode))
                continue;

            var prices = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

            foreach (var (storeName, col) in storeColumns)
            {
                var raw = ws.Cell(r, col).GetFormattedString().Trim();
                if (float.TryParse(raw.Replace(',', '.'), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var val))
                {
                    prices[storeName] = val;
                }
            }

            result[barcode] = prices;
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

        using var wb = new XLWorkbook(inputFilePath);
        var sheetInfo = FindSheet(wb, mappings);

        if (sheetInfo is null)
        {
            _logger.LogWarningAsync(
                $"Мониторинг: в '{Path.GetFileName(inputFilePath)}' не найдена таблица с ШК и магазинами.",
                CancellationToken.None).Wait();
            return;
        }

        var lastRow = sheetInfo.UsedRange.LastRow().RowNumber();

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
                if (!sheetInfo.StoreColumns.TryGetValue(
                        mapping.TargetColumnHeader, out var col))
                    continue;

                if (!prices.TryGetValue(mapping.SourceColumnHeader, out var price))
                    continue;

                sheetInfo.Worksheet.Cell(r, col).SetValue(price);
            }
        }

        foreach (var ws in wb.Worksheets)
            ws.ConditionalFormats.RemoveAll();

        var baseName = Path.GetFileNameWithoutExtension(inputFilePath);
        var outputPath = BuildMonitoringPath(_settings.ProcessedFolder, baseName);

        Directory.CreateDirectory(_settings.ProcessedFolder);
        wb.SaveAs(outputPath);

        _logger.LogInfoAsync(
            $"Сохранён файл мониторинга в processed: {Path.GetFileName(outputPath)}", CancellationToken.None).Wait();
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

    private static string BuildMonitoringPath(string folder, string baseName)
    {
        var date = DateTime.Now.ToString("dd.MM.yyyy");
        var candidate = Path.Combine(folder, $"{baseName}-monitoring-{date}.xlsx");
        if (!File.Exists(candidate))
            return candidate;

        for (var count = 1; ; count++)
        {
            candidate = Path.Combine(folder, $"{baseName}-monitoring-{date}-{count}.xlsx");
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
