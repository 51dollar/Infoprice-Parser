using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using PriceParser.Console.Core.Interfaces;
using PriceParser.Console.Core.Models;

namespace PriceParser.Console.Infrastructure.Parsing;

/// <summary>
/// Преобразует JSON-ответ InfoPrice API в доменный результат с ценами.
/// </summary>
public sealed class InfoPriceParser : IPriceParser
{
    public ParseResult Parse(string barcode, string html)
    {
        var response = JsonSerializer.Deserialize<InfoPriceResponse>(html);
        var nullablePrices = ExtractPrices(response);
        var prices = nullablePrices
            .Where(item => item.Value.HasValue)
            .ToDictionary(item => item.Key, item => item.Value!.Value, StringComparer.OrdinalIgnoreCase);

        return new ParseResult(barcode, new ParsedProductPrices(prices));
    }

    private static Dictionary<string, float?> ExtractPrices(InfoPriceResponse? response)
    {
        var result = new Dictionary<string, float?>(StringComparer.OrdinalIgnoreCase);
        if (response?.Table is null)
        {
            return result;
        }

        foreach (var tableItem in response.Table)
        {
            // ContractorId из Offers связывается с названием магазина из TradingCompany.
            var contractors = new Dictionary<int, string>();
            foreach (var company in tableItem.TradingCompany ?? [])
            {
                if (company.ContractorId.HasValue && !string.IsNullOrWhiteSpace(company.ContractorName))
                {
                    contractors[company.ContractorId.Value] = company.ContractorName;
                }
            }

            if (tableItem.GoodsOffer is null)
            {
                continue;
            }

            foreach (var goodsOffer in tableItem.GoodsOffer)
            {
                if (goodsOffer.Offers is null)
                {
                    continue;
                }

                foreach (var offer in goodsOffer.Offers)
                {
                    if (!offer.ContractorId.HasValue || !contractors.TryGetValue(offer.ContractorId.Value, out var contractorName))
                    {
                        continue;
                    }

                    result[contractorName] = TryParsePrice(offer.Price, out var price) ? price : null;
                }
            }
        }

        return result;
    }

    private static bool TryParsePrice(string? value, out float price)
    {
        return float.TryParse(
            value?.Replace(',', '.'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out price);
    }

    private sealed class InfoPriceResponse
    {
        [JsonPropertyName("Table")]
        public List<TableItem>? Table { get; init; }
    }

    private sealed class TableItem
    {
        [JsonPropertyName("GoodsOffer")]
        public List<GoodsOfferItem>? GoodsOffer { get; init; }

        [JsonPropertyName("TradingCompany")]
        public List<TradingCompanyItem>? TradingCompany { get; init; }
    }

    private sealed class GoodsOfferItem
    {
        [JsonPropertyName("Offers")]
        public List<OfferItem>? Offers { get; init; }
    }

    private sealed class OfferItem
    {
        [JsonPropertyName("Price")]
        public string? Price { get; init; }

        [JsonPropertyName("ContractorId")]
        public int? ContractorId { get; init; }
    }

    private sealed class TradingCompanyItem
    {
        [JsonPropertyName("ContractorId")]
        public int? ContractorId { get; init; }

        [JsonPropertyName("ContractorName")]
        public string? ContractorName { get; init; }
    }
}
