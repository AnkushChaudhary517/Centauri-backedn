using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using CentauriSeo.Infrastructure.Exceptions;
using CentauriSeo.Infrastructure.Logging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace CentauriSeo.Infrastructure.Services;

/// <summary>
/// Centralized cache manager for all LLM API calls (Gemini, Groq, etc.)
/// Handles cache enable/disable configuration and standardizes cache operations
/// </summary>
public interface ILlmCacheManager
{
    /// <summary>
    /// Get or execute a cached LLM call with automatic caching
    /// </summary>
    /// <typeparam name="T">The return type</typeparam>
    /// <param name="cacheKey">Unique cache key for the request</param>
    /// <param name="provider">Provider name (e.g., "Gemini", "Groq")</param>
    /// <param name="apiCall">The async function to execute if cache miss</param>
    /// <returns>Cached or fresh result</returns>
    Task<T> GetOrExecuteAsync<T>(string cacheKey, string provider, Func<Task<T>> apiCall);

    /// <summary>
    /// Get or execute a cached LLM call with automatic caching (string result)
    /// </summary>
    Task<string> GetOrExecuteAsync(string cacheKey, string provider, Func<Task<string>> apiCall);

    /// <summary>
    /// Compute a request key based on input and provider
    /// </summary>
    string ComputeRequestKey(string requestText, string provider);

    /// <summary>
    /// Check if caching is enabled
    /// </summary>
    bool IsCachingEnabled { get; }
}

public class LlmCacheManager : ILlmCacheManager
{
    private readonly ILlmCacheService _cacheService;
    private readonly IConfiguration _config;
    private readonly ILogger<LlmLogger> _logger;
    private readonly ILlmLogger _llmLogger;
    private readonly bool _cachingEnabled;

    public bool IsCachingEnabled => _cachingEnabled;

    public LlmCacheManager(ILlmCacheService cacheService, IConfiguration config, ILogger<LlmLogger> logger)
    {
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;

        // Create LlmLogger wrapper
        _llmLogger = new LlmLogger(logger);

        try
        {
            // Read caching configuration from appsettings
            _cachingEnabled = _config.GetSection("LlmCache").GetValue("Enabled", true);
            _llmLogger.LogInfo($"LlmCacheManager initialized | CachingEnabled: {_cachingEnabled}");
        }
        catch (Exception ex)
        {
            _llmLogger.LogError("Failed to read caching configuration, defaulting to enabled", ex);
            _cachingEnabled = true;
        }
    }

    public async Task<T> GetOrExecuteAsync<T>(string cacheKey, string provider, Func<Task<T>> apiCall)
    {
        if (string.IsNullOrEmpty(cacheKey))
            throw new ArgumentNullException(nameof(cacheKey));
        if (string.IsNullOrEmpty(provider))
            throw new ArgumentNullException(nameof(provider));
        if (apiCall == null)
            throw new ArgumentNullException(nameof(apiCall));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!_cachingEnabled)
            {
                _llmLogger.LogDebug("Caching is disabled. Executing API call directly.");
                return await ExecuteApiCall(apiCall, provider, stopwatch);
            }

            // Try to get from cache
            var cachedResult = await GetFromCacheAsync(cacheKey, provider);
            if (cachedResult != null)
            {
                stopwatch.Stop();
                _llmLogger.LogCacheOperation("GET", cacheKey, true, stopwatch.ElapsedMilliseconds);
                
                try
                {
                    return System.Text.Json.JsonSerializer.Deserialize<T>(cachedResult);
                }
                catch (System.Text.Json.JsonException ex)
                {
                    _llmLogger.LogWarning($"Failed to deserialize cached result for provider '{provider}'", new Dictionary<string, object> { { "CacheKey", cacheKey } });
                    // Fall through to API call
                }
            }

            _llmLogger.LogCacheOperation("MISS", cacheKey, false, stopwatch.ElapsedMilliseconds);

            // Cache miss, execute the API call
            var result = await ExecuteApiCall(apiCall, provider, stopwatch);

            // Save to cache
            await SaveToCacheAsync(cacheKey, result, provider, stopwatch);

            return result;
        }
        catch (Exception ex) when (!(ex is LlmOperationException))
        {
            stopwatch.Stop();
            var context = new Dictionary<string, object>
            {
                { "CacheKey", cacheKey },
                { "Provider", provider },
                { "DurationMs", stopwatch.ElapsedMilliseconds }
            };
            _llmLogger.LogError($"Error in GetOrExecuteAsync<T>", ex, context);
            throw new CacheOperationException("Failed to execute cached operation", cacheKey, ex, context);
        }
    }

    public async Task<string> GetOrExecuteAsync(string cacheKey, string provider, Func<Task<string>> apiCall)
    {
        if (string.IsNullOrEmpty(cacheKey))
            throw new ArgumentNullException(nameof(cacheKey));
        if (string.IsNullOrEmpty(provider))
            throw new ArgumentNullException(nameof(provider));
        if (apiCall == null)
            throw new ArgumentNullException(nameof(apiCall));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!_cachingEnabled)
            {
                _llmLogger.LogDebug("Caching is disabled. Executing API call directly.");
                return await ExecuteApiCall(apiCall, provider, stopwatch);
            }

            // Try to get from cache
            var cachedResult = await GetFromCacheAsync(cacheKey, provider);
            if (cachedResult != null)
            {
                stopwatch.Stop();
                _llmLogger.LogCacheOperation("GET", cacheKey, true, stopwatch.ElapsedMilliseconds);
                return cachedResult;
            }

            _llmLogger.LogCacheOperation("MISS", cacheKey, false, stopwatch.ElapsedMilliseconds);

            // Cache miss, execute the API call
            var result = await ExecuteApiCall(apiCall, provider, stopwatch);

            // Save to cache
            await SaveToCacheAsync(cacheKey, result, provider, stopwatch);

            return result;
        }
        catch (Exception ex) when (!(ex is LlmOperationException))
        {
            stopwatch.Stop();
            var context = new Dictionary<string, object>
            {
                { "CacheKey", cacheKey },
                { "Provider", provider },
                { "DurationMs", stopwatch.ElapsedMilliseconds }
            };
            _llmLogger.LogError($"Error in GetOrExecuteAsync", ex, context);
            throw new CacheOperationException("Failed to execute cached operation", cacheKey, ex, context);
        }
    }

    private async Task<string> GetFromCacheAsync(string cacheKey, string provider)
    {
        try
        {
            return await _cacheService.GetAsync(cacheKey);
        }
        catch (Exception ex)
        {
            _llmLogger.LogWarning($"Error retrieving from cache for provider '{provider}'", new Dictionary<string, object>
            {
                { "CacheKey", cacheKey },
                { "ExceptionType", ex.GetType().Name }
            });
            return null; // Return null to continue with API call
        }
    }

    private async Task SaveToCacheAsync<T>(string cacheKey, T result, string provider, Stopwatch stopwatch)
    {
        try
        {
            var serializedResult = System.Text.Json.JsonSerializer.Serialize(result);
            await _cacheService.SaveAsync(cacheKey, serializedResult);
            stopwatch.Stop();
            _llmLogger.LogCacheOperation("SAVE", cacheKey, true, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _llmLogger.LogWarning($"Error saving to cache for provider '{provider}'", new Dictionary<string, object>
            {
                { "CacheKey", cacheKey },
                { "ExceptionType", ex.GetType().Name }
            });
        }
    }

    private async Task SaveToCacheAsync(string cacheKey, string result, string provider, Stopwatch stopwatch)
    {
        try
        {
            await _cacheService.SaveAsync(cacheKey, result);
            stopwatch.Stop();
            _llmLogger.LogCacheOperation("SAVE", cacheKey, true, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _llmLogger.LogWarning($"Error saving to cache for provider '{provider}'", new Dictionary<string, object>
            {
                { "CacheKey", cacheKey },
                { "ExceptionType", ex.GetType().Name }
            });
        }
    }

    private async Task<T> ExecuteApiCall<T>(Func<Task<T>> apiCall, string provider, Stopwatch stopwatch)
    {
        try
        {
            return await apiCall();
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    private async Task<string> ExecuteApiCall(Func<Task<string>> apiCall, string provider, Stopwatch stopwatch)
    {
        try
        {
            return await apiCall();
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    public string ComputeRequestKey(string requestText, string provider)
    {
        return _cacheService.ComputeRequestKey(requestText, provider);
    }
}
