namespace PriceParser.Console.Core.Interfaces;

/// <summary>Выполняет HTTP-запрос к API InfoPrice и возвращает сырой JSON.</summary>
public interface IHttpFetcher
{
    /// <param name="barcode">Штрихкод товара.</param>
    /// <returns>JSON-строка ответа от API.</returns>
    Task<string> FetchAsync(string barcode, CancellationToken cancellationToken);
}
