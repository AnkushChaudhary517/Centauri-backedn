using System;
using System.Collections.Generic;

namespace CentauriSeo.Infrastructure.Exceptions;

/// <summary>
/// Base exception for all LLM-related operations
/// </summary>
public class LlmOperationException : Exception
{
    public string Provider { get; set; }
    public string OperationName { get; set; }
    public Dictionary<string, object> Context { get; set; }

    public LlmOperationException(string message, string provider = null, string operationName = null, 
        Exception innerException = null, Dictionary<string, object> context = null)
        : base(message, innerException)
    {
        Provider = provider;
        OperationName = operationName;
        Context = context ?? new Dictionary<string, object>();
    }
}

/// <summary>
/// Exception thrown when API call fails
/// </summary>
public class LlmApiException : LlmOperationException
{
    public int? HttpStatusCode { get; set; }
    public string ResponseContent { get; set; }

    public LlmApiException(string message, string provider = null, int? statusCode = null, 
        string responseContent = null, Exception innerException = null, Dictionary<string, object> context = null)
        : base(message, provider, "API Call", innerException, context)
    {
        HttpStatusCode = statusCode;
        ResponseContent = responseContent;
    }
}

/// <summary>
/// Exception thrown when caching operation fails
/// </summary>
public class CacheOperationException : LlmOperationException
{
    public string CacheKey { get; set; }

    public CacheOperationException(string message, string cacheKey = null, Exception innerException = null, 
        Dictionary<string, object> context = null)
        : base(message, null, "Cache Operation", innerException, context)
    {
        CacheKey = cacheKey;
    }
}

/// <summary>
/// Exception thrown when parsing/deserialization fails
/// </summary>
public class LlmParsingException : LlmOperationException
{
    public string RawContent { get; set; }

    public LlmParsingException(string message, string provider = null, string rawContent = null, 
        Exception innerException = null, Dictionary<string, object> context = null)
        : base(message, provider, "Parsing", innerException, context)
    {
        RawContent = rawContent;
    }
}

/// <summary>
/// Exception thrown when validation fails
/// </summary>
public class LlmValidationException : LlmOperationException
{
    public List<string> ValidationErrors { get; set; }

    public LlmValidationException(string message, string provider = null, List<string> errors = null, 
        Exception innerException = null, Dictionary<string, object> context = null)
        : base(message, provider, "Validation", innerException, context)
    {
        ValidationErrors = errors ?? new List<string>();
    }
}

/// <summary>
/// Exception thrown when configuration is invalid
/// </summary>
public class LlmConfigurationException : LlmOperationException
{
    public string ConfigurationKey { get; set; }

    public LlmConfigurationException(string message, string configKey = null, Exception innerException = null, 
        Dictionary<string, object> context = null)
        : base(message, null, "Configuration", innerException, context)
    {
        ConfigurationKey = configKey;
    }
}

/// <summary>
/// Exception thrown when operation times out
/// </summary>
public class LlmTimeoutException : LlmOperationException
{
    public TimeSpan Timeout { get; set; }

    public LlmTimeoutException(string message, string provider = null, TimeSpan? timeout = null, 
        Exception innerException = null, Dictionary<string, object> context = null)
        : base(message, provider, "Timeout", innerException, context)
    {
        Timeout = timeout ?? TimeSpan.Zero;
    }
}

/// <summary>
/// Exception thrown when rate limit is exceeded
/// </summary>
public class LlmRateLimitException : LlmOperationException
{
    public TimeSpan? RetryAfter { get; set; }

    public LlmRateLimitException(string message, string provider = null, TimeSpan? retryAfter = null, 
        Exception innerException = null, Dictionary<string, object> context = null)
        : base(message, provider, "Rate Limit", innerException, context)
    {
        RetryAfter = retryAfter;
    }
}
