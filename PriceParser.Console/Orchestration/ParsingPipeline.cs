using PriceParser.Console.Configuration;
using PriceParser.Console.Core.Interfaces;
using PriceParser.Console.Core.Models;
using PriceParser.Console.Services.Parsing;
using PriceParser.Console.Utils;

namespace PriceParser.Console.Orchestration;

/// <summary>
/// Координирует чтение файлов, загрузку HTML, парсинг, запись результата и перенос исходников.
/// </summary>
public sealed class ParsingPipeline
{
    private static readonly string[] ExcelMasks = ["*.xlsx", "*.xlsm", "*.xltx", "*.xltm"];

    private readonly AppSettings _settings;
    private readonly IExcelReader _excelReader;
    private readonly IExcelWriter _excelWriter;
    private readonly IHttpFetcher _httpFetcher;
    private readonly IPriceParser _priceParser;
    private readonly ILoggerService _logger;

    public ParsingPipeline(
        AppSettings settings,
        IExcelReader excelReader,
        IExcelWriter excelWriter,
        IHttpFetcher httpFetcher,
        IPriceParser priceParser,
        ILoggerService logger)
    {
        _settings = settings;
        _excelReader = excelReader;
        _excelWriter = excelWriter;
        _httpFetcher = httpFetcher;
        _priceParser = priceParser;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await _logger.LogInfoAsync("Запуск обработки прайс-листов.", cancellationToken);

        ValidationHelper.Validate(_settings);

        await _logger.LogInfoAsync(
            $"Настройки: InputFolder='{_settings.InputFolder}', OutputFolder='{_settings.OutputFolder}', ProcessedFolder='{_settings.ProcessedFolder}', BarcodeColumnNames='{string.Join(", ", _settings.BarcodeColumnNames)}', RequestDelayMs={_settings.RequestDelayMs}, TimeoutSeconds={_settings.TimeoutSeconds}, RetryCount={_settings.RetryCount}.",
            cancellationToken);

        Directory.CreateDirectory(_settings.InputFolder);
        Directory.CreateDirectory(_settings.OutputFolder);
        Directory.CreateDirectory(_settings.ProcessedFolder);

        await _logger.LogInfoAsync("Папки проверены/созданы.", cancellationToken);

        var inputFiles = GetInputFiles(_settings.InputFolder);
        await _logger.LogInfoAsync($"Найдено Excel-файлов для обработки: {inputFiles.Count}.", cancellationToken);

        if (inputFiles.Count == 0)
        {
            await _logger.LogWarningAsync("Во входной папке нет Excel-файлов. Обработка завершена без создания результата.", cancellationToken);
            return;
        }

        var outputPath = Path.Combine(_settings.OutputFolder, $"prices_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        _excelWriter.Create(outputPath, PriceMappingService.OutputStores);
        await _logger.LogInfoAsync($"Итоговый Excel-файл создан в памяти: {outputPath}", cancellationToken);

        var cache = new Dictionary<string, ParsedProductPrices>(StringComparer.OrdinalIgnoreCase);
        var processedFiles = 0;

        foreach (var filePath in inputFiles)
        {
            await ProcessFileAsync(filePath, cache, cancellationToken);
            processedFiles++;
        }

        await _excelWriter.SaveAsync(cancellationToken);
        await _logger.LogInfoAsync($"Обработка завершена. Файлов обработано: {processedFiles}. Результат сохранён: {outputPath}", cancellationToken);
    }

    private async Task ProcessFileAsync(
        string filePath,
        Dictionary<string, ParsedProductPrices> cache,
        CancellationToken cancellationToken)
    {
        try
        {
            await _logger.LogInfoAsync($"Начинаю обработку файла: {Path.GetFileName(filePath)}", cancellationToken);

            var readResult = await _excelReader.ReadBarcodesAsync(filePath, cancellationToken);
            if (!readResult.BarcodeColumnFound)
            {
                await _logger.LogWarningAsync(
                    $"Файл {Path.GetFileName(filePath)} пропущен: колонка со штрихкодом не найдена. Файл не переносится и дальше не обрабатывается.",
                    cancellationToken);

                return;
            }

            var barcodes = readResult.Records.ToArray();
            await _logger.LogInfoAsync(
                $"Штрихкоды перенесены в массив для дальнейшей обработки. Размер массива: {barcodes.Length}.",
                cancellationToken);

            if (barcodes.Length == 0)
            {
                await _logger.LogWarningAsync($"В файле {Path.GetFileName(filePath)} нет штрихкодов для обработки.", cancellationToken);
            }

            var parsedRows = new List<ParsedBarcodeRow>(barcodes.Length);
            foreach (var record in barcodes)
            {
                await _logger.LogInfoAsync(
                    $"Обработка штрихкода {record.Barcode} из строки {record.SourceRow} файла {Path.GetFileName(filePath)}.",
                    cancellationToken);

                var lookupResult = await GetPricesAsync(record.Barcode, cache, cancellationToken);
                if (!lookupResult.Found)
                {
                    await _logger.LogWarningAsync(
                        $"Штрихкод {record.Barcode} не найден сервисом InfoPrice. Пропуск.",
                        cancellationToken);

                    parsedRows.Add(new ParsedBarcodeRow(record.Barcode, lookupResult.Prices));
                    continue;
                }

                parsedRows.Add(new ParsedBarcodeRow(record.Barcode, lookupResult.Prices));
            }

            foreach (var row in parsedRows)
            {
                _excelWriter.AddRow(row.Barcode, row.Prices);
            }

            MoveToProcessed(filePath);
            await _logger.LogInfoAsync(
                $"Файл {Path.GetFileName(filePath)} обработан. Штрихкодов: {parsedRows.Count}. Файл перенесён в processed.",
                cancellationToken);
        }
        catch (Exception exception)
        {
            await _logger.LogErrorAsync(Path.GetFileName(filePath), exception, cancellationToken);
        }
    }

    private async Task<PriceLookupResult> GetPricesAsync(
        string barcode,
        Dictionary<string, ParsedProductPrices> cache,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(barcode, out var cachedPrices))
        {
            await _logger.LogInfoAsync($"Штрихкод {barcode}: взято из кэша.", cancellationToken);
            return new PriceLookupResult(cachedPrices.PricesByStore.Count > 0, cachedPrices);
        }

        try
        {
            await _logger.LogInfoAsync($"Штрихкод {barcode}: запрос к infoprice.by.", cancellationToken);
            var html = await _httpFetcher.FetchAsync(barcode, cancellationToken);
            var result = _priceParser.Parse(barcode, html);
            cache[barcode] = result.Prices;
            if (result.Prices.PricesByStore.Count == 0)
            {
                await _logger.LogWarningAsync($"Штрихкод {barcode}: сервис не вернул цены.", cancellationToken);
                return new PriceLookupResult(false, result.Prices);
            }

            await _logger.LogInfoAsync($"Штрихкод {barcode}: цены успешно разобраны.", cancellationToken);

            return new PriceLookupResult(true, result.Prices);
        }
        catch (Exception exception)
        {
            await _logger.LogErrorAsync(barcode, exception, cancellationToken);
            var emptyPrices = new ParsedProductPrices(new Dictionary<string, float>(0));
            cache[barcode] = emptyPrices;

            return new PriceLookupResult(false, emptyPrices);
        }
    }

    private static List<string> GetInputFiles(string inputFolder)
    {
        var files = new List<string>();
        foreach (var mask in ExcelMasks)
        {
            files.AddRange(Directory.EnumerateFiles(inputFolder, mask, SearchOption.TopDirectoryOnly)
                .Where(file => !Path.GetFileName(file).StartsWith("~$", StringComparison.Ordinal)));
        }

        return files;
    }

    private void MoveToProcessed(string filePath)
    {
        var destinationPath = Path.Combine(_settings.ProcessedFolder, Path.GetFileName(filePath));
        if (File.Exists(destinationPath))
        {
            var extension = Path.GetExtension(filePath);
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            destinationPath = Path.Combine(_settings.ProcessedFolder, $"{fileName}_{DateTime.Now:yyyyMMdd_HHmmss}{extension}");
        }

        File.Move(filePath, destinationPath);
    }

    private sealed record PriceLookupResult(bool Found, ParsedProductPrices Prices);

    private sealed record ParsedBarcodeRow(string Barcode, ParsedProductPrices Prices);
}
