using System.Collections.Concurrent;
using System.Diagnostics;
using PriceParser.Console.Configuration;
using PriceParser.Console.Core.Interfaces;
using PriceParser.Console.Core.Models;
using PriceParser.Console.Utils;

namespace PriceParser.Console.Application;

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

    public async Task<IReadOnlyList<FileProcessResult>> RunAsync(CancellationToken cancellationToken)
    {
        ValidationHelper.Validate(_settings);

        Directory.CreateDirectory(_settings.InputFolder);
        Directory.CreateDirectory(_settings.OutputFolder);
        Directory.CreateDirectory(_settings.ProcessedFolder);

        var inputFiles = GetInputFiles(_settings.InputFolder);
        ConsoleHelper.WriteInfo($"Найдено файлов: {inputFiles.Count}");

        if (inputFiles.Count == 0)
        {
            ConsoleHelper.WriteWarning("Нет Excel-файлов для обработки. Завершено.");
            return [];
        }

        var cache = new ConcurrentDictionary<string, ParsedProductPrices>(StringComparer.OrdinalIgnoreCase);
        var results = new List<FileProcessResult>(inputFiles.Count);
        var fileIndex = 0;

        foreach (var filePath in inputFiles)
        {
            fileIndex++;
            results.Add(await ProcessFileAsync(filePath, fileIndex, inputFiles.Count, cache, cancellationToken));
        }

        var processedCount = results.Count(r => r.Success);
        ConsoleHelper.WriteInfo($"Обработано файлов: {processedCount} из {inputFiles.Count}");

        return results;
    }

    private async Task<FileProcessResult> ProcessFileAsync(
        string filePath,
        int fileIndex,
        int totalFiles,
        ConcurrentDictionary<string, ParsedProductPrices> cache,
        CancellationToken cancellationToken)
    {
        var sourceFileName = Path.GetFileName(filePath);
        var fileStopwatch = Stopwatch.StartNew();

        try
        {
            ConsoleHelper.WriteFileHeader(fileIndex, totalFiles, sourceFileName);

            // Чтение штрихкодов
            var stepSw = Stopwatch.StartNew();
            var readResult = await _excelReader.ReadBarcodesAsync(filePath, cancellationToken);
            stepSw.Stop();

            if (!readResult.BarcodeColumnFound || readResult.Records.Count == 0)
            {
                ConsoleHelper.WriteStep("Чтение штрихкодов", true, stepSw.Elapsed.TotalSeconds);
                ConsoleHelper.WriteWarning("Пропущен: нет штрихкодов.");
                ConsoleHelper.WriteFileSummary(fileStopwatch.Elapsed.TotalSeconds);
                return new FileProcessResult(filePath, false, null);
            }

            ConsoleHelper.WriteStep("Чтение штрихкодов", true, stepSw.Elapsed.TotalSeconds);

            // Создание выходного файла
            stepSw.Restart();
            var outputPath = BuildOutputPath(filePath);
            _excelWriter.Create(outputPath, PriceMappingService.OutputStores);
            stepSw.Stop();
            ConsoleHelper.WriteStep("Создание выходного файла", true, stepSw.Elapsed.TotalSeconds);

            // Загрузка цен
            var barcodes = readResult.Records.ToArray();
            var loadSw = Stopwatch.StartNew();
            var results = new ConcurrentBag<(BarcodeRecord Record, ParsedProductPrices Prices)>();
            int errors = 0;
            int completed = 0;
            var total = barcodes.Length;

            ConsoleHelper.WriteProgress(0, total);

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

                    var currentCount = Interlocked.Increment(ref completed);
                    if (currentCount % 5 == 0 || currentCount == total)
                        ConsoleHelper.WriteProgress(currentCount, total);
                });

            loadSw.Stop();
            var loadDesc = $"Загрузка цен ({total} штрихкодов)";
            if (errors > 0)
                loadDesc += $" ({errors} ош.)";

            ConsoleHelper.ClearCurrentLine();
            ConsoleHelper.WriteStep(loadDesc, true, loadSw.Elapsed.TotalSeconds);

            // Сохранение результатов
            stepSw.Restart();
            foreach (var (record, prices) in results.OrderBy(r => r.Record.SourceRow))
                _excelWriter.AddRow(record.Barcode, prices);

            await _excelWriter.SaveAsync(cancellationToken);
            stepSw.Stop();
            ConsoleHelper.WriteStep("Сохранение результатов", true, stepSw.Elapsed.TotalSeconds);

            // Формирование выходных данных
            var outputData = results
                .Select(r => (r.Record.Barcode, r.Prices))
                .DistinctBy(x => x.Barcode)
                .ToDictionary(x => x.Barcode, x => x.Prices, StringComparer.OrdinalIgnoreCase);

            fileStopwatch.Stop();
            ConsoleHelper.WriteFileSummary(fileStopwatch.Elapsed.TotalSeconds);

            return new FileProcessResult(filePath, true, outputData);
        }
        catch (Exception exception)
        {
            fileStopwatch.Stop();
            ConsoleHelper.WriteError($"Ошибка: {exception.Message}");
            await _logger.LogErrorAsync(sourceFileName, exception, cancellationToken);
            ConsoleHelper.WriteFileSummary(fileStopwatch.Elapsed.TotalSeconds);
            return new FileProcessResult(filePath, false, null);
        }
    }

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

    private sealed record PriceLookupResult(bool ServiceSucceeded, bool HasPrices, ParsedProductPrices Prices);
}

public sealed record FileProcessResult(
    string FilePath,
    bool Success,
    IReadOnlyDictionary<string, ParsedProductPrices>? OutputData);
