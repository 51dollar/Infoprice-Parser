using ClosedXML.Excel;
using PriceParser.Console.Core.Interfaces;
using PriceParser.Console.Core.Models;
using PriceParser.Console.Services.Parsing;

namespace PriceParser.Console.Services.Excel;

/// <summary>
/// Формирует итоговый Excel-файл и сохраняет его один раз после обработки.
/// </summary>
public sealed class ExcelWriter : IExcelWriter
{
    private readonly PriceMappingService _priceMappingService;
    private XLWorkbook? _workbook;
    private IXLWorksheet? _worksheet;
    private IReadOnlyList<string> _headers = Array.Empty<string>();
    private string? _filePath;
    private int _nextRow = 2;

    public ExcelWriter(PriceMappingService priceMappingService)
    {
        _priceMappingService = priceMappingService;
    }

    public void Create(string filePath, IReadOnlyList<string> storeHeaders)
    {
        _workbook?.Dispose();

        _headers = storeHeaders;
        _filePath = filePath;
        _nextRow = 2;
        _workbook = new XLWorkbook();
        _worksheet = _workbook.Worksheets.Add("Prices");

        _worksheet.Cell(1, 1).Value = "ШК";
        for (var i = 0; i < storeHeaders.Count; i++)
        {
            _worksheet.Cell(1, i + 2).Value = storeHeaders[i];
        }

        _worksheet.Row(1).Style.Font.Bold = true;
    }

    public void AddRow(string barcode, ParsedProductPrices prices)
    {
        EnsureCreated();

        _worksheet!.Cell(_nextRow, 1).Value = barcode;
        for (var i = 0; i < _headers.Count; i++)
        {
            if (_priceMappingService.TryGetPrice(prices, _headers[i], out var price))
            {
                _worksheet.Cell(_nextRow, i + 2).Value = price;
            }
        }

        _nextRow++;
    }

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCreated();

        _workbook!.SaveAs(_filePath);

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _workbook?.Dispose();
        return ValueTask.CompletedTask;
    }

    private void EnsureCreated()
    {
        if (_workbook is null || _worksheet is null || _filePath is null)
        {
            throw new InvalidOperationException("Итоговый Excel-файл не был создан.");
        }
    }
}
