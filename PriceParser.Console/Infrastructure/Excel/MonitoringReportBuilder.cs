using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
        using PriceParser.Console.Configuration;
using PriceParser.Console.Utils;

namespace PriceParser.Console.Infrastructure.Excel;

public sealed class MonitoringReportBuilder
{
    public int FillPrices(
        string inputFilePath,
        string outputPath,
        Dictionary<string, Dictionary<string, float>> data,
        MonitoringStoreMapping[] mappings,
        string[] barcodeColumnNames)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.Copy(inputFilePath, outputPath, overwrite: true);

        using var doc = SpreadsheetDocument.Open(outputPath, isEditable: true);
        var wbPart = doc.WorkbookPart;
        if (wbPart is null)
            return 0;

        var sheetInfo = FindSheet(wbPart, mappings, barcodeColumnNames);
        if (sheetInfo is null)
        {
            var searched = string.Join(", ", barcodeColumnNames.Select(n => $"'{n}'"));
            ConsoleHelper.WriteError($"В файле '{Path.GetFileName(inputFilePath)}' не найдена таблица с штрихкодом ({searched}).");
            ConsoleHelper.WriteError("Проверьте настройки BarcodeColumnNames и Monitoring.StoreMappings в appsettings.json.");
            return 0;
        }

        var wsPart = sheetInfo.WorksheetPart;
        var ws = wsPart.Worksheet;
        if (ws is null)
            return 0;

        var rows = ws.Descendants<Row>().ToList();
        var sstPart = wbPart.SharedStringTablePart;
        var filledCount = 0;

        foreach (var row in rows)
        {
            if (row.RowIndex is not { } rowNum)
                continue;

            if (rowNum <= sheetInfo.HeaderRow)
                continue;

            var bcCell = GetCellInRow(row, sheetInfo.BarcodeColumn);
            if (bcCell is null)
                continue;

            var barcode = GetCellValue(bcCell, sstPart)?.Trim();
            if (string.IsNullOrEmpty(barcode) || !IsBarcode(barcode))
                continue;

            if (!data.TryGetValue(barcode, out var prices))
                continue;

            foreach (var mapping in mappings)
            {
                if (!sheetInfo.StoreCols.TryGetValue(mapping.TargetColumnHeader, out var col))
                    continue;
                if (!prices.TryGetValue(mapping.SourceColumnHeader, out var price))
                    continue;

                var cell = GetOrCreateCell(row, col, rowNum);
                cell.DataType = CellValues.Number;
                cell.CellValue = new CellValue(price.ToString("F2", CultureInfo.InvariantCulture));
            }

            filledCount++;
        }

        ws.Save();
        return filledCount;
    }

    private sealed record SheetInfo(
        WorksheetPart WorksheetPart,
        uint HeaderRow,
        int BarcodeColumn,
        Dictionary<string, int> StoreCols);

    private SheetInfo? FindSheet(
        WorkbookPart wbPart,
        MonitoringStoreMapping[] mappings,
        string[] barcodeColumnNames)
    {
        var targetHeaders = mappings.Select(m => m.TargetColumnHeader).ToArray();
        var sstPart = wbPart.SharedStringTablePart;
        SheetInfo? best = null;

        var workbook = wbPart.Workbook;
        if (workbook is null)
            return null;

        foreach (var sheet in workbook.Descendants<Sheet>())
        {
            if (sheet.Id?.Value is not { } sheetId)
                continue;

            var wsPart = (WorksheetPart)wbPart.GetPartById(sheetId);
            var ws = wsPart.Worksheet;
            if (ws is null)
                continue;

            var rows = ws.Descendants<Row>().ToList();

            foreach (var row in rows)
            {
                if (row.RowIndex is not { } rowNum)
                    continue;

                var cells = row.Elements<Cell>().ToList();
                var barcodeCol = FindBarcodeColumn(cells, sstPart, barcodeColumnNames);
                if (barcodeCol is null)
                    continue;

                var storeCols = FindStoreColumns(cells, sstPart, targetHeaders);
                if (storeCols.Count == 0)
                    continue;

                if (best is null || storeCols.Count > best.StoreCols.Count)
                {
                    best = new SheetInfo(wsPart, rowNum, barcodeCol.Value, storeCols);
                }
            }
        }

        return best;
    }

    private static int? FindBarcodeColumn(
        List<Cell> cells,
        SharedStringTablePart? sstPart,
        string[] barcodeColumnNames)
    {
        foreach (var cell in cells)
        {
            if (cell.CellReference?.Value is not { } refValue)
                continue;

            var text = GetCellValue(cell, sstPart)?.Trim();
            if (text is null)
                continue;

            if (barcodeColumnNames.Any(name =>
                    text.Contains(name.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return GetColumnFromReference(refValue);
            }
        }

        return null;
    }

    private static Dictionary<string, int> FindStoreColumns(
        List<Cell> cells,
        SharedStringTablePart? sstPart,
        string[] headers)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in headers)
        {
            foreach (var cell in cells)
            {
                if (cell.CellReference?.Value is not { } refValue)
                    continue;

                var text = GetCellValue(cell, sstPart)?.Trim();
                if (text is null)
                    continue;

                if (text.Contains(header.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    result[header] = GetColumnFromReference(refValue);
                    break;
                }
            }
        }

        return result;
    }

    private static string? GetCellValue(Cell cell, SharedStringTablePart? sstPart)
    {
        var value = cell.CellValue?.Text;
        if (value is null)
            return null;

        if (cell.DataType is not null && cell.DataType.Value == CellValues.SharedString && sstPart is not null)
        {
            var sst = sstPart.SharedStringTable;
            if (sst is null)
                return value;

            var items = sst.Elements<SharedStringItem>().ToList();

            if (int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var idx)
                && idx >= 0 && idx < items.Count)
            {
                return items[idx].Text?.Text;
            }
        }

        return value;
    }

    private static Cell? GetCellInRow(Row row, int colNum)
    {
        var colLetter = GetColumnLetter(colNum);
        return row.Elements<Cell>().FirstOrDefault(c =>
        {
            var reference = c.CellReference?.Value;
            return reference is not null
                && reference.StartsWith(colLetter, StringComparison.OrdinalIgnoreCase)
                && GetColumnFromReference(reference) == colNum;
        });
    }

    private static Cell GetOrCreateCell(Row row, int colNum, uint rowNum)
    {
        var colLetter = GetColumnLetter(colNum);
        var cellRef = $"{colLetter}{rowNum}";

        var cell = row.Elements<Cell>().FirstOrDefault(c =>
            string.Equals(c.CellReference?.Value, cellRef, StringComparison.OrdinalIgnoreCase));

        if (cell is not null)
            return cell;

        cell = new Cell { CellReference = cellRef };
        row.Append(cell);
        return cell;
    }

    private static string GetColumnLetter(int columnNumber)
    {
        var letter = "";
        while (columnNumber > 0)
        {
            columnNumber--;
            letter = (char)('A' + columnNumber % 26) + letter;
            columnNumber /= 26;
        }
        return letter;
    }

    private static int GetColumnFromReference(string reference)
    {
        var colPart = new string(reference.TakeWhile(char.IsLetter).ToArray());
        var result = 0;
        foreach (var c in colPart)
            result = result * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
        return result;
    }

    private static bool IsBarcode(string value)
    {
        return value.Length >= 6 && value.All(char.IsDigit);
    }
}
