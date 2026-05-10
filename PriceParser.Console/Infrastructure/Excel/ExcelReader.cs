using ClosedXML.Excel;
using PriceParser.Console.Configuration;
using PriceParser.Console.Core.Interfaces;
using PriceParser.Console.Core.Models;

namespace PriceParser.Console.Infrastructure.Excel;

/// <summary>
/// Читает Excel-файл: ищет колонку с названием, содержащим фрагмент из BarcodeColumnNames,
/// собирает все штрихкоды ниже заголовка до конца used-range листа.
/// </summary>
public sealed class ExcelReader : IExcelReader
{
    private readonly string[] _barcodeColumnNames;

    public ExcelReader(AppSettings settings)
    {
        _barcodeColumnNames = settings.BarcodeColumnNames;
    }

    public async Task<ExcelBarcodeReadResult> ReadBarcodesAsync(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        System.Console.WriteLine($"Читаю файл: {filePath}");

        using var workbook = new XLWorkbook(filePath);
        if (!workbook.Worksheets.Any())
        {
            System.Console.WriteLine($"  Файл не содержит листов.");
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
                System.Console.WriteLine($"  Лист '{worksheet.Name}' — пустой, пропущен.");
                continue;
            }

            var headerCells = FindHeaderCells(usedRange, _barcodeColumnNames, cancellationToken);
            foreach (var headerCell in headerCells)
            {
                var headerText = headerCell.GetFormattedString().Trim();
                matchedColumns.Add($"лист '{worksheet.Name}', ячейка {headerCell.Address}");

                System.Console.WriteLine($"  Колонка ШК: лист '{worksheet.Name}', ячейка {headerCell.Address} ('{headerText}')");

                ReadColumnValues(worksheet, usedRange, headerCell, records, cancellationToken);
            }
        }

        if (matchedColumns.Count == 0)
        {
            System.Console.WriteLine($"  Колонка с штрихкодом не найдена.");
            return new ExcelBarcodeReadResult(false, Array.Empty<BarcodeRecord>(), Array.Empty<string>());
        }

        System.Console.WriteLine($"  Прочитано штрихкодов: {records.Count}.");
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
                continue;

            records.Add(new BarcodeRecord(barcode, worksheet.Name, rowNumber));
        }
    }

    private static bool IsBarcodeValue(string value)
    {
        return value.Length >= 6 && value.All(char.IsDigit);
    }
}
