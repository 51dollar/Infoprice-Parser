using PriceParser.Console.Core.Models;

namespace PriceParser.Console.Core.Interfaces;

public interface IExcelWriter : IAsyncDisposable
{
    void Create(string filePath, IReadOnlyList<string> storeHeaders);

    void AddRow(string barcode, ParsedProductPrices prices);

    Task SaveAsync(CancellationToken cancellationToken);
}
