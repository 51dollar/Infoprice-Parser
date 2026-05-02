using PriceParser.Console.Core.Models;

namespace PriceParser.Console.Core.Interfaces;

public interface IPriceParser
{
    ParseResult Parse(string barcode, string html);
}
