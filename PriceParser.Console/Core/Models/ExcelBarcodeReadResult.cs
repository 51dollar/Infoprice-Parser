namespace PriceParser.Console.Core.Models;

/// <summary>
/// Результат чтения штрихкодов из Excel-файла.
/// </summary>
public sealed record ExcelBarcodeReadResult(
    bool BarcodeColumnFound,
    IReadOnlyList<BarcodeRecord> Records,
    IReadOnlyList<string> MatchedColumns);
