using PriceParser.Console.Configuration;

namespace PriceParser.Console.Utils;

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

    private static void EnsureTexts(IReadOnlyCollection<string> values, string name)
    {
        if (values.Count == 0 || values.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException($"Настройка {name} не заполнена.");
        }
    }
}
