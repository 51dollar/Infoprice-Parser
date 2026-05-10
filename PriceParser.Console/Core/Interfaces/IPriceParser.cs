using PriceParser.Console.Core.Models;

namespace PriceParser.Console.Core.Interfaces;

/// <summary>Разбирает ответ API InfoPrice в структурированный результат с ценами.</summary>
public interface IPriceParser
{
    /// <param name="barcode">Штрихкод (для проброса в результат).</param>
    /// <param name="html">JSON-строка ответа от API.</param>
    ParseResult Parse(string barcode, string html);
}
