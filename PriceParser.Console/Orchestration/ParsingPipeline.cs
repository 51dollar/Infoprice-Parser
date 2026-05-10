using PriceParser.Console.Configuration;
using PriceParser.Console.Core.Interfaces;
using PriceParser.Console.Core.Models;
using PriceParser.Console.Services.Parsing;
using PriceParser.Console.Utils;

namespace PriceParser.Console.Orchestration;

public sealed class ParsingPipeline
{
    private static readonly string[] ExcelMasks = ["*.xlsx", "*.xlsm", "*.xltx", "*.xltm"];

    private readonly AppSettings _settings;
    private readonly IExcelReader _excelReader;
    private readonly IExcelWriter _excelWriter;
    private readonly IHttpFetcher _httpFetcher;
    private readonly IPriceParser _priceParser;
    private readonly IMonitoringReportService _monitoringReportService;
    private readonly ILoggerService _logger;

    public ParsingPipeline(
        AppSettings settings,
        IExcelReader excelReader,
        IExcelWriter excelWriter,
        IHttpFetcher httpFetcher,
        IPriceParser priceParser,
        IMonitoringReportService monitoringReportService,
        ILoggerService logger)
    {
        _settings = settings;
        _excelReader = excelReader;
        _excelWriter = excelWriter;
        _httpFetcher = httpFetcher;
        _priceParser = priceParser;
        _monitoringReportService = monitoringReportService;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await _logger.LogInfoAsync("Запуск обработки прайс-листов.", cancellationToken);

        ValidationHelper.Validate(_settings);

        await _logger.LogInfoAsync(
            $"Настройки: InputFolder='{_settings.InputFolder}', OutputFolder='{_settings.OutputFolder}', BarcodeColumnNames='{string.Join(", ", _settings.BarcodeColumnNames)}', RequestDelayMs={_settings.RequestDelayMs}, TimeoutSeconds={_settings.TimeoutSeconds}, RetryCount={_settings.RetryCount}.",
            cancellationToken);

        Directory.CreateDirectory(_settings.InputFolder);
        Directory.CreateDirectory(_settings.OutputFolder);
        Directory.CreateDirectory(_settings.ProcessedFolder);

        await _logger.LogInfoAsync("Папки проверены/созданы.", cancellationToken);

        var inputFiles = GetInputFiles(_settings.InputFolder);
        await _logger.LogInfoAsync($"Найдено Excel-файлов для обработки: {inputFiles.Count}.", cancellationToken);

        if (inputFiles.Count == 0)
        {
            await _logger.LogWarningAsync("Во входной папке нет Excel-файлов. Обработка завершена.", cancellationToken);
            return;
        }

        var cache = new Dictionary<string, ParsedProductPrices>(StringComparer.OrdinalIgnoreCase);
        var processedCount = 0;

        foreach (var filePath in inputFiles)
        {
            if (await ProcessFileAsync(filePath, cache, cancellationToken))
            {
                processedCount++;
            }
        }

        await _logger.LogInfoAsync(
            $"Обработка завершена. Файлов обработано: {processedCount} из {inputFiles.Count}.", cancellationToken);
    }

    private async Task<bool> ProcessFileAsync(
        string filePath,
        Dictionary<string, ParsedProductPrices> cache,
        CancellationToken cancellationToken)
    {
        var sourceFileName = Path.GetFileName(filePath);

        try
        {
            await _logger.LogInfoAsync($"Начинаю обработку файла: {sourceFileName}", cancellationToken);

            var readResult = await _excelReader.ReadBarcodesAsync(filePath, cancellationToken);
            if (!readResult.BarcodeColumnFound)
            {
                await _logger.LogWarningAsync(
                    $"Файл {sourceFileName} пропущен: колонка со штрихкодом не найдена.", cancellationToken);
                return false;
            }

            var barcodes = readResult.Records.ToArray();
            await _logger.LogInfoAsync(
                $"Штрихкодов найдено: {barcodes.Length}.", cancellationToken);

            if (barcodes.Length == 0)
            {
                await _logger.LogWarningAsync(
                    $"В файле {sourceFileName} нет штрихкодов.", cancellationToken);
                return false;
            }

            var outputPath = BuildOutputPath(filePath);
            _excelWriter.Create(outputPath, PriceMappingService.OutputStores);
            await _logger.LogInfoAsync($"Файл результатов создан: {outputPath}", cancellationToken);

            foreach (var record in barcodes)
            {
                await _logger.LogInfoAsync(
                    $"Обработка штрихкода {record.Barcode} (строка {record.SourceRow}).", cancellationToken);

                var lookupResult = await GetPricesAsync(record.Barcode, cache, cancellationToken);

                if (!lookupResult.HasPrices)
                {
                    await _logger.LogWarningAsync(
                        $"Штрихкод {record.Barcode}: цены не найдены.", cancellationToken);
                }

                _excelWriter.AddRow(record.Barcode, lookupResult.Prices);
            }

            await _excelWriter.SaveAsync(cancellationToken);
            await _logger.LogInfoAsync($"Сохранён результат: {outputPath}", cancellationToken);

            await _monitoringReportService.ProcessFileAsync(filePath, outputPath, cancellationToken);

            return true;
        }
        catch (Exception exception)
        {
            await _logger.LogErrorAsync(sourceFileName, exception, cancellationToken);
            return false;
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
            return new PriceLookupResult(true, cachedPrices.PricesByStore.Count > 0, cachedPrices);
        }

        try
        {
            await _logger.LogInfoAsync($"Штрихкод {barcode}: запрос к infoprice.by.", cancellationToken);
            var html = await _httpFetcher.FetchAsync(barcode, cancellationToken);
            var result = _priceParser.Parse(barcode, html);
            if (result.Prices.PricesByStore.Count == 0)
            {
                await _logger.LogWarningAsync($"Штрихкод {barcode}: сервис не вернул цены.", cancellationToken);
                return new PriceLookupResult(true, false, result.Prices);
            }

            cache[barcode] = result.Prices;
            await _logger.LogInfoAsync($"Штрихкод {barcode}: цены получены.", cancellationToken);

            return new PriceLookupResult(true, true, result.Prices);
        }
        catch (Exception exception)
        {
            await _logger.LogErrorAsync(barcode, exception, cancellationToken);
            var emptyPrices = new ParsedProductPrices(new Dictionary<string, float>(0));
            return new PriceLookupResult(false, false, emptyPrices);
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

    private string BuildOutputPath(string sourceFilePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(sourceFilePath);
        return BuildUniquePath(_settings.OutputFolder, fileName, "price");
    }

    private static string BuildUniquePath(string folder, string baseName, string suffix)
    {
        var date = DateTime.Now.ToString("dd.MM.yyyy");
        var candidate = Path.Combine(folder, $"{baseName}-{suffix}-{date}.xlsx");
        if (!File.Exists(candidate))
            return candidate;

        for (var count = 1; ; count++)
        {
            candidate = Path.Combine(folder, $"{baseName}-{suffix}-{date}-{count}.xlsx");
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    private sealed record PriceLookupResult(bool ServiceSucceeded, bool HasPrices, ParsedProductPrices Prices);
}
