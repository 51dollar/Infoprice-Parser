namespace PriceParser.Console.Configuration;

/// <summary>
/// Настройки приложения из appsettings.json.
/// </summary>
public sealed class AppSettings
{
    public string InputFolder { get; set; } = "./input";

    public string OutputFolder { get; set; } = "./output";

    public string ProcessedFolder { get; set; } = "./processed";

    public MonitoringSettings Monitoring { get; set; } = new();

    public string[] BarcodeColumnNames { get; set; } = ["Штрихкод"];

    public int RequestDelayMs { get; set; } = 300;

    public int TimeoutSeconds { get; set; } = 15;

    public int RetryCount { get; set; } = 3;

    public int MaxParallelism { get; set; } = 4;
}

public sealed class MonitoringSettings
{
    public MonitoringStoreMapping[] StoreMappings { get; set; } = [
        new() { TargetColumnHeader = "Корона",  SourceColumnHeader = "Корона" },
        new() { TargetColumnHeader = "Гиппо",   SourceColumnHeader = "Гиппо" },
        new() { TargetColumnHeader = "Грин",    SourceColumnHeader = "Грин" },
        new() { TargetColumnHeader = "Евроопт", SourceColumnHeader = "Евроопт" },
        new() { TargetColumnHeader = "Санта",   SourceColumnHeader = "Санта" },
        new() { TargetColumnHeader = "Соседи",  SourceColumnHeader = "Соседи" }
    ];
}

public sealed class MonitoringStoreMapping
{
    public string TargetColumnHeader { get; set; } = string.Empty;

    public string SourceColumnHeader { get; set; } = string.Empty;
}
