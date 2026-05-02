namespace PriceParser.Console.Core.Models;

/// <summary>
/// Штрихкод, считанный из исходного Excel-файла.
/// </summary>
public sealed record BarcodeRecord(string Barcode, string SourceFilePath, int SourceRow);
