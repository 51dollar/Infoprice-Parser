using ClosedXML.Excel;
using PriceParser.Console.Configuration;
using PriceParser.Console.Core.Interfaces;
using PriceParser.Console.Core.Models;

namespace PriceParser.Console.Services.Excel;

/// <summary>
/// Читает штрихкоды из найденных колонок на всех листах Excel-файла.
/// </summary>
public sealed class ExcelReader : IExcelReader
{
    private readonly AppSettings _settings;
    private readonly ILoggerService _logger;

    public ExcelReader(AppSettings settings, ILoggerService logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<ExcelBarcodeReadResult> ReadBarcodesAsync(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _logger.LogInfoAsync($"Читаю Excel-файл: {filePath}", cancellationToken);

        using var workbook = new XLWorkbook(filePath);
        if (!workbook.Worksheets.Any())
        {
            await _logger.LogWarningAsync($"В файле {Path.GetFileName(filePath)} нет листов.", cancellationToken);
            return new ExcelBarcodeReadResult(false, Array.Empty<BarcodeRecord>(), Array.Empty<string>());
        }

        var records = new List<BarcodeRecord>();
        var matchedColumns = new List<string>();

        foreach (var worksheet in workbook.Worksheets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var usedRange = worksheet.RangeUsed();
            if (usedRange is null)
            {
                await _logger.LogWarningAsync(
                    $"В файле {Path.GetFileName(filePath)} лист '{worksheet.Name}' пустой.",
                    cancellationToken);

                continue;
            }

            var headerCells = FindHeaderCells(usedRange, _settings.BarcodeColumnNames, cancellationToken);
            foreach (var headerCell in headerCells)
            {
                var headerText = headerCell.GetFormattedString().Trim();
                matchedColumns.Add($"лист '{worksheet.Name}', ячейка {headerCell.Address}, заголовок '{headerText}'");

                await _logger.LogInfoAsync(
                    $"Колонка штрихкодов найдена: лист '{worksheet.Name}', ячейка {headerCell.Address}, заголовок '{headerText}'.",
                    cancellationToken);

                ReadColumnValues(filePath, worksheet, usedRange, headerCell, records, cancellationToken);
            }
        }

        if (matchedColumns.Count == 0)
        {
            await _logger.LogWarningAsync(
                $"В файле {Path.GetFileName(filePath)} не найдены колонки, содержащие одно из значений: {string.Join(", ", _settings.BarcodeColumnNames)}.",
                cancellationToken);

            return new ExcelBarcodeReadResult(false, Array.Empty<BarcodeRecord>(), Array.Empty<string>());
        }

        await _logger.LogInfoAsync(
            $"Из файла {Path.GetFileName(filePath)} прочитано штрихкодов: {records.Count}.",
            cancellationToken);

        return new ExcelBarcodeReadResult(true, records, matchedColumns);
    }

    private static List<IXLCell> FindHeaderCells(
        IXLRange usedRange,
        IReadOnlyCollection<string> expectedHeaderParts,
        CancellationToken cancellationToken)
    {
        var headerCells = new List<IXLCell>();

        foreach (var row in usedRange.RowsUsed())
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var cell in row.CellsUsed())
            {
                var value = cell.GetFormattedString().Trim();
                if (expectedHeaderParts.Any(expectedHeaderPart =>
                        value.Contains(expectedHeaderPart.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    headerCells.Add(cell);
                }
            }
        }

        return headerCells;
    }

    private static void ReadColumnValues(
        string filePath,
        IXLWorksheet worksheet,
        IXLRange usedRange,
        IXLCell headerCell,
        ICollection<BarcodeRecord> records,
        CancellationToken cancellationToken)
    {
        var lastRowNumber = usedRange.LastRow().RowNumber();
        var barcodeColumnNumber = headerCell.Address.ColumnNumber;

        for (var rowNumber = headerCell.Address.RowNumber + 1; rowNumber <= lastRowNumber; rowNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var barcode = worksheet.Cell(rowNumber, barcodeColumnNumber).GetFormattedString().Trim();
            if (!IsBarcodeValue(barcode))
            {
                continue;
            }

            records.Add(new BarcodeRecord(barcode, filePath, rowNumber));
        }
    }

    private static bool IsBarcodeValue(string value)
    {
        return value.Length >= 6 && value.All(char.IsDigit);
    }
}
