using System.Text;
using System.Text.Json;
using PriceParser.Console.Configuration;
using PriceParser.Console.Core.Interfaces;
using PriceParser.Console.Utils;

namespace PriceParser.Console.Services.Network;

/// <summary>
/// Выполняет HTTP-запросы к JSON API InfoPrice через единый HttpClient.
/// </summary>
public sealed class HttpFetcher : IHttpFetcher
{
    public const string ClientName = "InfoPrice";
    private const string ApiUrl = "https://api.infoprice.by/InfoPrice.Goods?v=0";

    private readonly HttpClient _httpClient;
    private readonly AppSettings _settings;

    public HttpFetcher(IHttpClientFactory httpClientFactory, AppSettings settings)
    {
        _httpClient = httpClientFactory.CreateClient(ClientName);
        _settings = settings;
    }

    public async Task<string> FetchAsync(string barcode, CancellationToken cancellationToken)
    {
        var json = await RetryHelper.ExecuteAsync(
            () => FetchOnceAsync(barcode, cancellationToken),
            _settings.RetryCount,
            cancellationToken);

        if (_settings.RequestDelayMs > 0)
        {
            await Task.Delay(_settings.RequestDelayMs, cancellationToken);
        }

        return json;
    }

    private async Task<string> FetchOnceAsync(string barcode, CancellationToken cancellationToken)
    {
        // InfoPrice принимает поиск товара только POST-запросом с JSON-пакетом.
        var requestJson = JsonSerializer.Serialize(CreateRequestBody(barcode));
        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(ApiUrl, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static object CreateRequestBody(string barcode)
    {
        return new Dictionary<string, object?>
        {
            ["CRC"] = string.Empty,
            ["Packet"] = new Dictionary<string, object?>
            {
                ["FromId"] = "10003001",
                ["ServerKey"] = "omt5W465fjwlrtxcEco97kew2dkdrorqqq",
                ["Data"] = new Dictionary<string, object?>
                {
                    ["ContractorId"] = string.Empty,
                    ["GoodsGroupId"] = string.Empty,
                    ["Page"] = string.Empty,
                    ["Search"] = barcode,
                    ["OrderBy"] = 0,
                    ["OrderByContractor"] = 0,
                    ["CatalogType"] = 1,
                    ["CompareСontractorId"] = 72631,
                    ["IsAgeLimit"] = 0,
                    ["IsPromotionalPrice"] = 0
                }
            }
        };
    }
}
