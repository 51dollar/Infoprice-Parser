using PriceParser.Console.Configuration;
using PriceParser.Console.Core.Interfaces;

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
        var results = await _pipeline.RunAsync(cancellationToken);

        foreach (var result in results)
        {
            if (!result.Success || result.OutputData is null || result.OutputData.Count == 0)
                continue;

            try
            {
                await _monitoringService.ProcessFileAsync(result.FilePath, result.OutputData, cancellationToken);
            }
            catch (Exception exception)
            {
                await _logger.LogErrorAsync(Path.GetFileName(result.FilePath), exception, cancellationToken);
            }
        }
    }
}
