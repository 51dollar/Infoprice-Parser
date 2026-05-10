using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PriceParser.Console.Configuration;
using PriceParser.Console.Core.Interfaces;
using PriceParser.Console.Application;
using PriceParser.Console.Infrastructure.Excel;
using PriceParser.Console.Infrastructure.Http;
using PriceParser.Console.Infrastructure.Logging;
using PriceParser.Console.Infrastructure.Parsing;

// Определяем базовый путь для конфигурации: сначала текущая директория, затем папка сборки.
var configurationBasePath = File.Exists(Path.Combine(Environment.CurrentDirectory, "appsettings.json"))
    ? Environment.CurrentDirectory
    : AppContext.BaseDirectory;

var configuration = new ConfigurationBuilder()
    .SetBasePath(configurationBasePath)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

var settings = configuration.Get<AppSettings>() ?? new AppSettings();
NormalizeSettingsPaths(settings, configurationBasePath);

// Настройка DI-контейнера: все зависимости регистрируются как singletons,
// чтобы не плодить HTTP-клиенты и Excel-воркбуки на каждый файл.
var services = new ServiceCollection();

services.AddSingleton(settings);
services.AddSingleton<IExcelReader, ExcelReader>();
services.AddSingleton<IExcelWriter, ExcelWriter>();
services.AddSingleton<IHttpFetcher, HttpFetcher>();
services.AddSingleton<IPriceParser, InfoPriceParser>();
services.AddSingleton<IMonitoringReportService, MonitoringReportService>();
services.AddSingleton<ILoggerService, FileLoggerService>();
services.AddSingleton<PriceMappingService>();
services.AddSingleton<ParsingPipeline>();

// Ручной HttpClient без логгирующих обработчиков (IHttpClientFactory не используется).
services.AddSingleton<HttpClient>(sp =>
{
    var s = sp.GetRequiredService<AppSettings>();
    return new HttpClient { Timeout = TimeSpan.FromSeconds(s.TimeoutSeconds) };
});

// Запуск основного цикла обработки.
await using var provider = services.BuildServiceProvider();
var pipeline = provider.GetRequiredService<ParsingPipeline>();

await pipeline.RunAsync(CancellationToken.None);

/// <summary>Преобразует относительные пути в конфиге в абсолютные относительно basePath.</summary>
static void NormalizeSettingsPaths(AppSettings settings, string basePath)
{
    settings.InputFolder = NormalizePath(settings.InputFolder, basePath);
    settings.OutputFolder = NormalizePath(settings.OutputFolder, basePath);
    settings.ProcessedFolder = NormalizePath(settings.ProcessedFolder, basePath);
}

/// <summary>Возвращает абсолютный путь, если path не является корневым — подклеивает basePath.</summary>
static string NormalizePath(string path, string basePath)
{
    return Path.IsPathRooted(path)
        ? Path.GetFullPath(path)
        : Path.GetFullPath(Path.Combine(basePath, path));
}
