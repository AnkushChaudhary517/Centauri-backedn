using System;
using System.Threading.Tasks;

namespace CentauriSeo.Infrastructure.Services;

/// <summary>
/// Extension methods for easier usage of the LlmCacheManager in LLM client implementations
/// </summary>
public static class LlmCacheExtensions
{
    /// <summary>
    /// Execute with automatic caching - use this wrapper pattern in LLM clients
    /// Example: return await _cacheManager.ExecuteWithCacheAsync("MyProvider", content, () => CallMyAPI(content));
    /// </summary>
    public static async Task<string> ExecuteWithCacheAsync(
        this ILlmCacheManager cacheManager,
        string provider,
        string requestContent,
        Func<Task<string>> apiCall)
    {
        if (cacheManager == null) throw new ArgumentNullException(nameof(cacheManager));
        if (provider == null) throw new ArgumentNullException(nameof(provider));
        if (apiCall == null) throw new ArgumentNullException(nameof(apiCall));

        var cacheKey = cacheManager.ComputeRequestKey(requestContent, provider);
        return await cacheManager.GetOrExecuteAsync(cacheKey, provider, apiCall);
    }

    /// <summary>
    /// Execute with automatic caching and JSON deserialization
    /// </summary>
    public static async Task<T> ExecuteWithCacheAsync<T>(
        this ILlmCacheManager cacheManager,
        string provider,
        string requestContent,
        Func<Task<T>> apiCall)
    {
        if (cacheManager == null) throw new ArgumentNullException(nameof(cacheManager));
        if (provider == null) throw new ArgumentNullException(nameof(provider));
        if (apiCall == null) throw new ArgumentNullException(nameof(apiCall));

        var cacheKey = cacheManager.ComputeRequestKey(requestContent, provider);
        return await cacheManager.GetOrExecuteAsync(cacheKey, provider, apiCall);
    }
}
