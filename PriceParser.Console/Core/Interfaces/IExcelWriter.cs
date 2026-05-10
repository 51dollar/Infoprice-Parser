using PriceParser.Console.Core.Models;

namespace PriceParser.Console.Core.Interfaces;

/// <summary>Построение выходного Excel-файла: создание, наполнение строками, сохранение.</summary>
public interface IExcelWriter : IAsyncDisposable
{
    /// <summary>Создаёт новый workbook c заголовками магазинов.</summary>
    void Create(string filePath, IReadOnlyList<string> storeHeaders);

    /// <summary>Добавляет строку с ценой по каждому магазину.</summary>
    void AddRow(string barcode, ParsedProductPrices prices);

    /// <summary>Сохраняет workbook на диск (один раз после всех AddRow).</summary>
    Task SaveAsync(CancellationToken cancellationToken);
}
