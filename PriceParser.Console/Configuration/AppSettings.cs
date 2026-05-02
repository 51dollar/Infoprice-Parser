namespace PriceParser.Console.Configuration;

/// <summary>
/// Настройки приложения из appsettings.json.
/// </summary>
public sealed class AppSettings
{
    public string InputFolder { get; set; } = "./input";

    public string OutputFolder { get; set; } = "./output";

    public string ProcessedFolder { get; set; } = "./processed";

    public string[] BarcodeColumnNames { get; set; } = ["штрихкод"];

    public int RequestDelayMs { get; set; } = 300;

    public int TimeoutSeconds { get; set; } = 15;

    public int RetryCount { get; set; } = 3;
}
