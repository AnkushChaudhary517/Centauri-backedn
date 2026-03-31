using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CentauriSeo.Infrastructure.Logging;

/// <summary>
/// Enhanced logging interface for structured logging with context
/// </summary>
public interface ILlmLogger
{
    /// <summary>
    /// Log a debug message with optional context
    /// </summary>
    void LogDebug(string message, Dictionary<string, object> context = null);

    /// <summary>
    /// Log an information message with optional context
    /// </summary>
    void LogInfo(string message, Dictionary<string, object> context = null);

    /// <summary>
    /// Log a warning message with optional context
    /// </summary>
    void LogWarning(string message, Dictionary<string, object> context = null);

    /// <summary>
    /// Log an error message with exception and optional context
    /// </summary>
    void LogError(string message, Exception exception = null, Dictionary<string, object> context = null);

    /// <summary>
    /// Log a critical error with exception and optional context
    /// </summary>
    void LogCritical(string message, Exception exception = null, Dictionary<string, object> context = null);

    /// <summary>
    /// Log an API call with timing information
    /// </summary>
    void LogApiCall(string provider, string operation, long durationMs, bool success, string errorMessage = null);

    /// <summary>
    /// Log a cache operation with result
    /// </summary>
    void LogCacheOperation(string operation, string cacheKey, bool hit, long durationMs = 0);

    /// <summary>
    /// Create a scope for grouping related logs
    /// </summary>
    IDisposable BeginScope(string scopeName);
}

/// <summary>
/// Implementation of ILlmLogger using ASP.NET Core ILogger
/// </summary>
public class LlmLogger : ILlmLogger
{
    private readonly ILogger<LlmLogger> _logger;

    public LlmLogger(ILogger<LlmLogger> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void LogDebug(string message, Dictionary<string, object> context = null)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var enrichedMessage = EnrichMessage(message, context);
            _logger.LogDebug(enrichedMessage);
        }
    }

    public void LogInfo(string message, Dictionary<string, object> context = null)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            var enrichedMessage = EnrichMessage(message, context);
            _logger.LogInformation(enrichedMessage);
        }
    }

    public void LogWarning(string message, Dictionary<string, object> context = null)
    {
        if (_logger.IsEnabled(LogLevel.Warning))
        {
            var enrichedMessage = EnrichMessage(message, context);
            _logger.LogWarning(enrichedMessage);
        }
    }

    public void LogError(string message, Exception exception = null, Dictionary<string, object> context = null)
    {
        if (_logger.IsEnabled(LogLevel.Error))
        {
            var enrichedMessage = EnrichMessage(message, context);
            if (exception != null)
                _logger.LogError(exception, enrichedMessage);
            else
                _logger.LogError(enrichedMessage);
        }
    }

    public void LogCritical(string message, Exception exception = null, Dictionary<string, object> context = null)
    {
        var enrichedMessage = EnrichMessage(message, context);
        if (exception != null)
            _logger.LogCritical(exception, enrichedMessage);
        else
            _logger.LogCritical(enrichedMessage);
    }

    public void LogApiCall(string provider, string operation, long durationMs, bool success, string errorMessage = null)
    {
        var logLevel = success ? LogLevel.Information : LogLevel.Warning;
        if (!_logger.IsEnabled(logLevel)) return;

        var context = new Dictionary<string, object>
        {
            { "Provider", provider },
            { "Operation", operation },
            { "DurationMs", durationMs },
            { "Success", success },
            { "Timestamp", DateTime.UtcNow }
        };

        var message = $"API Call | Provider: {provider} | Operation: {operation} | Duration: {durationMs}ms | Status: {(success ? "SUCCESS" : "FAILED")}";
        
        if (!string.IsNullOrEmpty(errorMessage))
        {
            message += $" | Error: {errorMessage}";
            context.Add("ErrorMessage", errorMessage);
        }

        var enrichedMessage = EnrichMessage(message, context);
        
        if (success)
            _logger.LogInformation(enrichedMessage);
        else
            _logger.LogWarning(enrichedMessage);
    }

    public void LogCacheOperation(string operation, string cacheKey, bool hit, long durationMs = 0)
    {
        if (!_logger.IsEnabled(LogLevel.Debug)) return;

        var context = new Dictionary<string, object>
        {
            { "CacheOp", operation },
            { "Hit", hit },
            { "DurationMs", durationMs },
            { "Timestamp", DateTime.UtcNow }
        };

        var keyPreview = cacheKey?.Substring(0, Math.Min(16, cacheKey.Length)) + "...";
        var message = $"Cache: {operation} | Key: {keyPreview} | Hit: {hit} | DurationMs: {durationMs}";
        var enrichedMessage = EnrichMessage(message, context);
        
        _logger.LogDebug(enrichedMessage);
    }

    public IDisposable BeginScope(string scopeName)
    {
        return _logger.BeginScope(new Dictionary<string, object> { { "Scope", scopeName } });
    }

    private static string EnrichMessage(string message, Dictionary<string, object> context)
    {
        if (context == null || context.Count == 0)
            return message;

        var contextStr = string.Join(" | ", context.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        return $"{message} [{contextStr}]";
    }
}
