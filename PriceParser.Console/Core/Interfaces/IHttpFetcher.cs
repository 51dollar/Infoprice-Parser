namespace PriceParser.Console.Core.Interfaces;

public interface IHttpFetcher
{
    Task<string> FetchAsync(string barcode, CancellationToken cancellationToken);
}
