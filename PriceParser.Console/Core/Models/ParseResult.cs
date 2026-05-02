namespace PriceParser.Console.Core.Models;

/// <summary>
/// Результат разбора страницы infoprice.by для одного штрихкода.
/// </summary>
public sealed class ParseResult
{
    public ParseResult(string barcode, ParsedProductPrices prices)
    {
        Barcode = barcode;
        Prices = prices;
    }

    public string Barcode { get; }

    public ParsedProductPrices Prices { get; }
}
