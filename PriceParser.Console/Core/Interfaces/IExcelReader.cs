using PriceParser.Console.Core.Models;

namespace PriceParser.Console.Core.Interfaces;

public interface IExcelReader
{
    Task<ExcelBarcodeReadResult> ReadBarcodesAsync(string filePath, CancellationToken cancellationToken);
}
