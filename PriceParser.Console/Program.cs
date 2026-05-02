using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PriceParser.Console.Configuration;
using PriceParser.Console.Core.Interfaces;
using PriceParser.Console.Orchestration;
using PriceParser.Console.Services.Excel;
using PriceParser.Console.Services.Logging;
using PriceParser.Console.Services.Network;
using PriceParser.Console.Services.Parsing;

var configurationBasePath = File.Exists(Path.Combine(Environment.CurrentDirectory, "appsettings.json"))
    ? Environment.CurrentDirectory
    : AppContext.BaseDirectory;

var configuration = new ConfigurationBuilder()
    .SetBasePath(configurationBasePath)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

var settings = configuration.Get<AppSettings>() ?? new AppSettings();
NormalizeSettingsPaths(settings, configurationBasePath);

var services = new ServiceCollection();

services.AddSingleton(settings);
services.AddSingleton<IExcelReader, ExcelReader>();
services.AddSingleton<IExcelWriter, ExcelWriter>();
services.AddSingleton<IHttpFetcher, HttpFetcher>();
services.AddSingleton<IPriceParser, InfoPriceParser>();
services.AddSingleton<ILoggerService, FileLoggerService>();
services.AddSingleton<HtmlPriceExtractor>();
services.AddSingleton<PriceMappingService>();
services.AddSingleton<ParsingPipeline>();

services.AddHttpClient(HttpFetcher.ClientName, (sp, client) =>
{
    var settings = sp.GetRequiredService<AppSettings>();
    client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
    client.BaseAddress = new Uri("https://infoprice.by/");
});

await using var provider = services.BuildServiceProvider();
var pipeline = provider.GetRequiredService<ParsingPipeline>();

await pipeline.RunAsync(CancellationToken.None);

static void NormalizeSettingsPaths(AppSettings settings, string basePath)
{
    settings.InputFolder = NormalizePath(settings.InputFolder, basePath);
    settings.OutputFolder = NormalizePath(settings.OutputFolder, basePath);
    settings.ProcessedFolder = NormalizePath(settings.ProcessedFolder, basePath);
}

static string NormalizePath(string path, string basePath)
{
    return Path.IsPathRooted(path)
        ? Path.GetFullPath(path)
        : Path.GetFullPath(Path.Combine(basePath, path));
}
