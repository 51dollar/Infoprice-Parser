using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace PriceParser.Console.Services.Parsing;

/// <summary>
/// Извлекает названия магазинов и цены из HTML-разметки infoprice.by.
/// </summary>
public sealed partial class HtmlPriceExtractor
{
    public ExtractedPrices Extract(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var appNode = document.GetElementbyId("app");
        if (appNode is null)
        {
            return new ExtractedPrices(Array.Empty<string>(), Array.Empty<float>());
        }

        var storeNames = ExtractStoreNames(appNode);
        var prices = ExtractPrices(appNode);

        return new ExtractedPrices(storeNames, prices);
    }

    private static List<string> ExtractStoreNames(HtmlNode appNode)
    {
        var nodes = appNode.SelectNodes(".//header[contains(concat(' ', normalize-space(@class), ' '), ' home ')]//div[contains(concat(' ', normalize-space(@class), ' '), ' logos ') and @name='compareTable']//div[contains(concat(' ', normalize-space(@class), ' '), ' logo ')]//img[@alt]");
        if (nodes is null)
        {
            return [];
        }

        var names = new List<string>(nodes.Count);
        foreach (var node in nodes)
        {
            var name = HtmlEntity.DeEntitize(node.GetAttributeValue("alt", string.Empty)).Trim();
            if (name.Length > 0)
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static List<float> ExtractPrices(HtmlNode appNode)
    {
        var nodes = appNode.SelectNodes(".//div[contains(concat(' ', normalize-space(@class), ' '), ' content-wrapper ')]//div[contains(concat(' ', normalize-space(@class), ' '), ' body-table ') and @name='compareTable']//div[contains(concat(' ', normalize-space(@class), ' '), ' price ')]//div[contains(concat(' ', normalize-space(@class), ' '), ' price-volume ')]");
        if (nodes is null)
        {
            return [];
        }

        var prices = new List<float>(nodes.Count);
        foreach (var node in nodes)
        {
            var text = HtmlEntity.DeEntitize(node.InnerText);
            if (TryParsePrice(text, out var price))
            {
                prices.Add(price);
            }
        }

        return prices;
    }

    private static bool TryParsePrice(string text, out float price)
    {
        var match = PriceRegex().Match(text);
        if (!match.Success)
        {
            price = 0;
            return false;
        }

        var normalized = match.Value.Replace(' ', '\0').Replace("\0", string.Empty).Replace(',', '.');
        return float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out price);
    }

    [GeneratedRegex(@"\d+(?:[\s\u00A0]?\d{3})*(?:[,.]\d+)?", RegexOptions.Compiled)]
    private static partial Regex PriceRegex();
}

public sealed record ExtractedPrices(IReadOnlyList<string> StoreNames, IReadOnlyList<float> Prices);
