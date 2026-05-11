using System.Diagnostics;
using PriceParser.Console.Configuration;
using PriceParser.Console.Core.Interfaces;
using PriceParser.Console.Utils;

namespace PriceParser.Console.Application;

public sealed class Orchestrator
{
    private readonly ParsingPipeline _pipeline;
    private readonly IMonitoringReportService _monitoringService;
    private readonly AppSettings _settings;
    private readonly ILoggerService _logger;

    public Orchestrator(
        ParsingPipeline pipeline,
        IMonitoringReportService monitoringService,
        AppSettings settings,
        ILoggerService logger)
    {
        _pipeline = pipeline;
        _monitoringService = monitoringService;
        _settings = settings;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var totalStopwatch = Stopwatch.StartNew();

        // Этап 1: Парсинг
        ConsoleHelper.WriteSection("ПАРСИНГ ПРАЙС-ЛИСТОВ");
        var parseStopwatch = Stopwatch.StartNew();
        var results = await _pipeline.RunAsync(cancellationToken);
        parseStopwatch.Stop();

        var fileCount = results.Count;
        ConsoleHelper.WriteServiceResult("ParsingPipeline", fileCount, parseStopwatch.Elapsed.TotalSeconds);

        // Этап 2: Мониторинг
        var monitoringFiles = results
            .Where(r => r.Success && r.OutputData is { Count: > 0 })
            .ToList();

        if (monitoringFiles.Count > 0)
        {
            ConsoleHelper.WriteSection("МОНИТОРИНГ ЦЕН");
            var monitoringStopwatch = Stopwatch.StartNew();
            var fileIndex = 0;

            foreach (var result in monitoringFiles)
            {
                fileIndex++;
                ConsoleHelper.WriteFileHeader(fileIndex, monitoringFiles.Count, Path.GetFileName(result.FilePath));

                try
                {
                    await _monitoringService.ProcessFileAsync(result.FilePath, result.OutputData!, cancellationToken);
                }
                catch (Exception exception)
                {
                    ConsoleHelper.WriteError($"Ошибка мониторинга: {exception.Message}");
                    await _logger.LogErrorAsync(Path.GetFileName(result.FilePath), exception, cancellationToken);
                }
            }

            monitoringStopwatch.Stop();
            ConsoleHelper.WriteServiceResult("MonitoringReport", monitoringFiles.Count, monitoringStopwatch.Elapsed.TotalSeconds);
        }

        totalStopwatch.Stop();
        var processedCount = results.Count(r => r.Success);
        ConsoleHelper.WriteTotalSummary(processedCount, fileCount, totalStopwatch.Elapsed.TotalSeconds);
    }
}
