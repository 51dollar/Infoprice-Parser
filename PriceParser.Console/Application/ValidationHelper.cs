using PriceParser.Console.Configuration;

namespace PriceParser.Console.Application;

/// <summary>Валидация настроек приложения перед стартом.</summary>
public static class ValidationHelper
{
    public static void Validate(AppSettings settings)
    {
        EnsurePositive(settings.TimeoutSeconds, nameof(settings.TimeoutSeconds));
        EnsureNotNegative(settings.RequestDelayMs, nameof(settings.RequestDelayMs));
        EnsureNotNegative(settings.RetryCount, nameof(settings.RetryCount));
        EnsurePath(settings.InputFolder, nameof(settings.InputFolder));
        EnsurePath(settings.OutputFolder, nameof(settings.OutputFolder));
        EnsurePath(settings.ProcessedFolder, nameof(settings.ProcessedFolder));
        EnsureTexts(settings.BarcodeColumnNames, nameof(settings.BarcodeColumnNames));
        ValidateMonitoring(settings.Monitoring);
    }

    private static void EnsurePositive(int value, string name)
    {
        if (value <= 0)
        {
            throw new InvalidOperationException($"Настройка {name} должна быть больше нуля.");
        }
    }

    private static void EnsureNotNegative(int value, string name)
    {
        if (value < 0)
        {
            throw new InvalidOperationException($"Настройка {name} не может быть отрицательной.");
        }
    }

    private static void EnsurePath(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Настройка {name} не заполнена.");
        }
    }

    private static void EnsureTexts(IReadOnlyCollection<string>? values, string name)
    {
        if (values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException($"Настройка {name} не заполнена в appsettings.json.");
        }
    }

    private static void ValidateMonitoring(MonitoringSettings monitoring)
    {
        if (monitoring.StoreMappings is null || monitoring.StoreMappings.Length == 0)
        {
            throw new InvalidOperationException(
                "Не найдены настройки мониторинга (Monitoring.StoreMappings) в appsettings.json.\n" +
                "Проверьте секцию \"Monitoring\" в файле конфигурации.");
        }

        for (var i = 0; i < monitoring.StoreMappings.Length; i++)
        {
            var map = monitoring.StoreMappings[i];

            if (string.IsNullOrWhiteSpace(map.TargetColumnHeader))
            {
                throw new InvalidOperationException(
                    $"Monitoring.StoreMappings[{i}].TargetColumnHeader не заполнен в appsettings.json.");
            }

            if (string.IsNullOrWhiteSpace(map.SourceColumnHeader))
            {
                throw new InvalidOperationException(
                    $"Monitoring.StoreMappings[{i}].SourceColumnHeader не заполнен в appsettings.json.");
            }
        }
    }
}
