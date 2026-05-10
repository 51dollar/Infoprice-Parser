namespace PriceParser.Console.Core.Interfaces;

/// <summary>Логирование ошибок: запись в файл и вывод в консоль (красным).</summary>
public interface ILoggerService
{
    /// <param name="context">Описание контекста ошибки (имя файла / штрихкод).</param>
    Task LogErrorAsync(string context, Exception exception, CancellationToken cancellationToken);
}
