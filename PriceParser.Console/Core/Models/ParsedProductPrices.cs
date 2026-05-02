namespace PriceParser.Console.Core.Models;

/// <summary>
/// Цены товара, сгруппированные по названию магазина.
/// </summary>
public sealed class ParsedProductPrices
{
    public ParsedProductPrices(IReadOnlyDictionary<string, float> pricesByStore)
    {
        PricesByStore = pricesByStore;
    }

    public IReadOnlyDictionary<string, float> PricesByStore { get; }
}
