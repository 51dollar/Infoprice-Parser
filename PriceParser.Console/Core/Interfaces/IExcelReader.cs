using PriceParser.Console.Core.Models;

namespace PriceParser.Console.Core.Interfaces;

/// <summary>Читает штрихкоды из Excel-файла, определяя колонку по заголовку.</summary>
public interface IExcelReader
{
    /// <param name="filePath">Путь к .xlsx-файлу.</param>
    /// <returns>Результат: найдена ли колонка и список записей.</returns>
    Task<ExcelBarcodeReadResult> ReadBarcodesAsync(string filePath, CancellationToken cancellationToken);
}
