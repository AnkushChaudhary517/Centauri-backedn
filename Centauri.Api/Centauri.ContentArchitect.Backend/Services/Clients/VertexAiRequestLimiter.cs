using System.Net;

namespace Centauri.ContentArchitect.Backend.Services.Clients;

/// <summary>
/// Process-wide pacing for Vertex/Gemini requests. Both Content Architect clients
/// use this limiter, preventing concurrent analyses from bursting into a 429.
/// </summary>
internal static class VertexAiRequestLimiter
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(750);
    private const int MaximumAttempts = 4;
    private static DateTimeOffset _nextRequestAt = DateTimeOffset.MinValue;

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient httpClient,
        Func<HttpRequestMessage> requestFactory,
        CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
            {
                var delay = _nextRequestAt - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, ct);

                using var request = requestFactory();
                var response = await httpClient.SendAsync(request, ct);
                _nextRequestAt = DateTimeOffset.UtcNow.Add(MinimumInterval);

                if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt == MaximumAttempts)
                    return response;

                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
                response.Dispose();
                _nextRequestAt = DateTimeOffset.UtcNow.Add(retryAfter > MinimumInterval ? retryAfter : MinimumInterval);
            }

            throw new InvalidOperationException("Vertex retry loop completed without an HTTP response.");
        }
        finally
        {
            Gate.Release();
        }
    }
}
