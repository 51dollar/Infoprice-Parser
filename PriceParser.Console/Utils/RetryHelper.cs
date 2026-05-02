using System.Net;
using System.Net.Http;

namespace PriceParser.Console.Utils;

public static class RetryHelper
{
    public static async Task<T> ExecuteAsync<T>(Func<Task<T>> action, int retryCount, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retryCount);

        Exception? lastException = null;
        for (var attempt = 0; attempt <= retryCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await action();
            }
            catch (Exception exception) when (IsTransient(exception) && attempt < retryCount)
            {
                lastException = exception;
                var delay = TimeSpan.FromMilliseconds(200 * (attempt + 1));
                await Task.Delay(delay, cancellationToken);
            }
        }

        throw lastException ?? new InvalidOperationException("Повторные попытки завершились без результата.");
    }

    private static bool IsTransient(Exception exception)
    {
        if (exception is HttpRequestException requestException)
        {
            return requestException.StatusCode is null
                or HttpStatusCode.RequestTimeout
                or HttpStatusCode.TooManyRequests
                or HttpStatusCode.InternalServerError
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout;
        }

        return exception is TaskCanceledException or TimeoutException;
    }
}
