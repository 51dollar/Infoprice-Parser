using System.Collections.Concurrent;
using PriceParser.Console.Configuration;
using PriceParser.Console.Core.Interfaces;
using PriceParser.Console.Core.Models;

namespace PriceParser.Console.Application;

/// <summary>
/// Главный оркестратор: обходит файлы во входной папке, для каждого запускает
/// параллельный опрос API InfoPrice, сохраняет результат и строит мониторинг.
/// </summary>
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

    /// <summary>Точка входа: поиск файлов, итерация по ним, запуск обработки.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        System.Console.WriteLine("Запуск обработки прайс-листов.");
        System.Console.WriteLine($"  Папка ввода:    {_settings.InputFolder}");
        System.Console.WriteLine($"  Папка вывода:   {_settings.OutputFolder}");
        System.Console.WriteLine($"  Потоков:        {_settings.MaxParallelism}");

        ValidationHelper.Validate(_settings);

        Directory.CreateDirectory(_settings.InputFolder);
        Directory.CreateDirectory(_settings.OutputFolder);
        Directory.CreateDirectory(_settings.ProcessedFolder);
        System.Console.WriteLine("Папки проверены.");

        var inputFiles = GetInputFiles(_settings.InputFolder);
        System.Console.WriteLine($"Найдено файлов: {inputFiles.Count}");

        if (inputFiles.Count == 0)
        {
            System.Console.WriteLine("Нет Excel-файлов для обработки. Завершено.");
            return;
        }

        var cache = new ConcurrentDictionary<string, ParsedProductPrices>(StringComparer.OrdinalIgnoreCase);
        var processedCount = 0;

        foreach (var filePath in inputFiles)
        {
            if (await ProcessFileAsync(filePath, cache, cancellationToken))
                processedCount++;
        }

        System.Console.WriteLine($"Обработка завершена. Обработано файлов: {processedCount} из {inputFiles.Count}.");
    }

    private async Task<bool> ProcessFileAsync(
        string filePath,
        ConcurrentDictionary<string, ParsedProductPrices> cache,
        CancellationToken cancellationToken)
    {
        var sourceFileName = Path.GetFileName(filePath);

        try
        {
            System.Console.WriteLine($"--- {sourceFileName} ---");

            var readResult = await _excelReader.ReadBarcodesAsync(filePath, cancellationToken);
            if (!readResult.BarcodeColumnFound || readResult.Records.Count == 0)
            {
                System.Console.WriteLine($"  Пропущен: нет штрихкодов.");
                return false;
            }

            var barcodes = readResult.Records.ToArray();
            var outputPath = BuildOutputPath(filePath);

            _excelWriter.Create(outputPath, PriceMappingService.OutputStores);
            System.Console.WriteLine($"Создан файл результатов: {Path.GetFileName(outputPath)}");
            System.Console.WriteLine($"Обработка {barcodes.Length} штрихкодов в {_settings.MaxParallelism} потоков...");

            var results = new ConcurrentBag<(BarcodeRecord Record, ParsedProductPrices Prices)>();
            int errors = 0;

            await Parallel.ForEachAsync(
                barcodes,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = _settings.MaxParallelism,
                    CancellationToken = cancellationToken
                },
                async (record, ct) =>
                {
                    var lookupResult = await GetPricesAsync(record.Barcode, cache, ct);

                    if (!lookupResult.ServiceSucceeded)
                        Interlocked.Increment(ref errors);

                    results.Add((record, lookupResult.Prices));
                });

            foreach (var (record, prices) in results.OrderBy(r => r.Record.SourceRow))
                _excelWriter.AddRow(record.Barcode, prices);

            await _excelWriter.SaveAsync(cancellationToken);
            System.Console.WriteLine($"Сохранено: {outputPath}");

            if (errors > 0)
                System.Console.WriteLine($"  Ошибок по штрихкодам: {errors}");

            var outputData = results
                .Select(r => (r.Record.Barcode, r.Prices))
                .DistinctBy(x => x.Barcode)
                .ToDictionary(x => x.Barcode, x => x.Prices, StringComparer.OrdinalIgnoreCase);

            await _monitoringReportService.ProcessFileAsync(filePath, outputData, cancellationToken);

            outputData.Clear();

            return true;
        }
        catch (Exception exception)
        {
            await _logger.LogErrorAsync(sourceFileName, exception, cancellationToken);
            return false;
        }
    }

    /// <summary>Получает цены для штрихкода: проверяет кэш, при промахе — HTTP + парсинг.</summary>
    private async Task<PriceLookupResult> GetPricesAsync(
        string barcode,
        ConcurrentDictionary<string, ParsedProductPrices> cache,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(barcode, out var cachedPrices))
            return new PriceLookupResult(true, cachedPrices.PricesByStore.Count > 0, cachedPrices);

        try
        {
            var html = await _httpFetcher.FetchAsync(barcode, cancellationToken);
            var result = _priceParser.Parse(barcode, html);

            if (result.Prices.PricesByStore.Count == 0)
                return new PriceLookupResult(true, false, result.Prices);

            cache[barcode] = result.Prices;
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
        var extension = Path.GetExtension(sourceFilePath);
        return BuildUniquePath(_settings.OutputFolder, fileName, "price", extension);
    }

    private static string BuildUniquePath(string folder, string baseName, string suffix, string extension)
    {
        var date = DateTime.Now.ToString("dd.MM.yyyy");
        var candidate = Path.Combine(folder, $"{baseName}-{suffix}-{date}{extension}");
        if (!File.Exists(candidate))
            return candidate;

        for (var count = 1; ; count++)
        {
            candidate = Path.Combine(folder, $"{baseName}-{suffix}-{date}-{count}{extension}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    /// <summary>Результат поиска цен: успешен ли запрос, есть ли цены, сами цены.</summary>
    private sealed record PriceLookupResult(bool ServiceSucceeded, bool HasPrices, ParsedProductPrices Prices);
}
