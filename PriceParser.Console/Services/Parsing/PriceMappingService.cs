using PriceParser.Console.Core.Models;

namespace PriceParser.Console.Services.Parsing;

/// <summary>
/// Сопоставляет магазины и цены по индексу из HTML-таблицы.
/// </summary>
public sealed class PriceMappingService
{
    public static readonly string[] OutputStores = ["Соседи", "Корона", "Санта", "Евроопт", "Гиппо", "Грин"];

    public ParsedProductPrices Map(IReadOnlyList<string> storeNames, IReadOnlyList<float> prices)
    {
        var count = Math.Min(storeNames.Count, prices.Count);
        var result = new Dictionary<string, float>(count, StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < count; i++)
        {
            var storeName = NormalizeStoreName(storeNames[i]);
            if (storeName.Length == 0)
            {
                continue;
            }

            result[storeName] = prices[i];
        }

        return new ParsedProductPrices(result);
    }

    public bool TryGetPrice(ParsedProductPrices prices, string outputStoreName, out float price)
    {
        var normalizedHeader = NormalizeStoreName(outputStoreName);
        foreach (var item in prices.PricesByStore)
        {
            if (NormalizeStoreName(item.Key).Contains(normalizedHeader, StringComparison.OrdinalIgnoreCase))
            {
                price = item.Value;
                return true;
            }
        }

        price = 0;
        return false;
    }

    private static string NormalizeStoreName(string value)
    {
        return value.Trim();
    }
}
