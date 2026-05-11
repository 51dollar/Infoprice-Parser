using System.Text;
using PriceParser.Console.Configuration;
using PriceParser.Console.Core.Interfaces;
using PriceParser.Console.Utils;

namespace PriceParser.Console.Infrastructure.Http;

/// <summary>
/// HTTP-клиент для API InfoPrice. Отправляет POST с JSON-шаблоном,
/// подставляя штрихкод вместо плейсхолдера. Использует RetryHelper
/// для повторных попыток при временных ошибках.
/// </summary>
public sealed class HttpFetcher : IHttpFetcher
{
    public const string ClientName = "InfoPrice";
    private static readonly string RequestJsonTemplate =
        $$"""
        {
          "CRC": "",
          "Packet": {
            "FromId": "10003001",
            "ServerKey": "{SERVERKEY}",
            "Data": {
              "ContractorId": "",
              "GoodsGroupId": "",
              "Page": "",
              "Search": "{BARCODE}",
              "OrderBy": 0,
              "OrderByContractor": 0,
              "CatalogType": 1,
              "CompareСontractorId": 72631,
              "IsAgeLimit": 0,
              "IsPromotionalPrice": 0
            }
          }
        }
        """;

    private readonly HttpClient _httpClient;
    private readonly AppSettings _settings;

    public HttpFetcher(HttpClient httpClient, AppSettings settings)
    {
        _httpClient = httpClient;
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
        var requestJson = RequestJsonTemplate
            .Replace("{BARCODE}", barcode)
            .Replace("{SERVERKEY}", _settings.ServerKey);
        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(_settings.ApiUrl, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
