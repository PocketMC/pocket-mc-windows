using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PocketMC.Desktop.Features.CloudBackups;

public static class ResilientUploadPolicy
{
    public static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        ILogger logger,
        CancellationToken ct,
        int maxRetries = 3)
    {
        int attempt = 0;
        while (true)
        {
            try
            {
                return await action(ct);
            }
            catch (Exception ex) when (IsRecoverable(ex) && attempt < maxRetries)
            {
                attempt++;
                TimeSpan delay = CalculateDelay(attempt, ex);
                logger.LogWarning(ex, "Transient error during upload (attempt {Attempt}/{MaxRetries}). Retrying in {Delay}s...", attempt, maxRetries, delay.TotalSeconds);
                await Task.Delay(delay, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unrecoverable error or max retries exceeded during upload.");
                throw;
            }
        }
    }

    private static bool IsRecoverable(Exception ex)
    {
        if (ex is OperationCanceledException) return false;

        if (ex is HttpRequestException httpEx)
        {
            if (!httpEx.StatusCode.HasValue) return true; // DNS/Connection failures

            var code = (int)httpEx.StatusCode.Value;
            // 408 Request Timeout, 429 Too Many Requests, 5xx Server Errors
            return code == 408 || code == 429 || (code >= 500 && code <= 599);
        }

        var typeName = ex.GetType().Name;
        if (typeName.Contains("GoogleApiException"))
        {
            if (ex.Message.Contains("403") || ex.Message.Contains("quota")) return false; // usually unrecoverable quota
            return ex.Message.Contains("429") || ex.Message.Contains("500") || ex.Message.Contains("502") || ex.Message.Contains("503");
        }

        return false;
    }

    private static TimeSpan CalculateDelay(int attempt, Exception ex)
    {
        var random = new Random();
        int jitterMs = random.Next(100, 1000);
        int baseSeconds = (int)Math.Pow(2, attempt);
        return TimeSpan.FromSeconds(baseSeconds) + TimeSpan.FromMilliseconds(jitterMs);
    }
}
