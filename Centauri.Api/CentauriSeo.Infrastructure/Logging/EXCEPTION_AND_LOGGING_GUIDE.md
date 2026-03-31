/**
 * EXCEPTION HANDLING & LOGGING GUIDE
 * ==================================
 * 
 * This document explains the new exception handling and logging system
 * implemented throughout the LLM clients.
 */

// ============================================================================
// ARCHITECTURE OVERVIEW
// ============================================================================

The system uses 3 layers:

1. CUSTOM EXCEPTIONS (LlmExceptions.cs)
   - LlmOperationException: Base exception for all LLM operations
   - LlmApiException: API call failures with HTTP status + response
   - CacheOperationException: Cache read/write failures
   - LlmParsingException: JSON/XML parsing errors
   - LlmValidationException: Input validation failures
   - LlmConfigurationException: Configuration errors
   - LlmTimeoutException: Operation timeouts
   - LlmRateLimitException: Rate limiting with retry info

2. ENHANCED LOGGING (ILlmLogger.cs)
   - ILlmLogger interface for structured logging
   - LlmLogger implementation using ILogger<T>
   - Automatic context enrichment
   - Specialized methods for API calls and cache operations


// ============================================================================
// EXCEPTION HIERARCHY
// ============================================================================

LlmOperationException (Base)
├─ LlmApiException
│  - Properties: HttpStatusCode, ResponseContent
│  - Use when: API call fails
│
├─ CacheOperationException
│  - Properties: CacheKey
│  - Use when: Cache read/write fails
│
├─ LlmParsingException
│  - Properties: RawContent
│  - Use when: JSON/XML parsing fails
│
├─ LlmValidationException
│  - Properties: ValidationErrors (List<string>)
│  - Use when: Input validation fails
│
├─ LlmConfigurationException
│  - Properties: ConfigurationKey
│  - Use when: Configuration is missing/invalid
│
├─ LlmTimeoutException
│  - Properties: Timeout (TimeSpan)
│  - Use when: Operation exceeds time limit
│
└─ LlmRateLimitException
   - Properties: RetryAfter (TimeSpan?)
   - Use when: API rate limit exceeded


// ============================================================================
// LOGGING LEVELS
// ============================================================================

DEBUG:
  - Cache operations (hit/miss)
  - Detailed flow information
  - Performance metrics
  Usage: Trace code execution path

INFO:
  - Successful API calls
  - Service initialization
  - Normal operations
  Usage: Track application health

WARNING:
  - Cache failures (non-blocking)
  - Retryable errors
  - API call degradation
  Usage: Monitor for issues

ERROR:
  - API call failures
  - Parsing errors
  - Validation failures
  Usage: Track errors that don't crash

CRITICAL:
  - Configuration errors
  - System-level failures
  Usage: Alert on severe issues


// ============================================================================
// USAGE PATTERNS
// ============================================================================

PATTERN 1: SIMPLE API CALL WITH VALIDATION
============================================

public async Task<string> MyMethod(string input)
{
    const string provider = "Gemini:MyOperation";
    _llmLogger.LogInfo($"MyMethod started | Input length: {input?.Length}");
    var stopwatch = Stopwatch.StartNew();

    try
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(input))
            throw new LlmValidationException(
                "Input cannot be null or empty",
                provider,
                new List<string> { "Invalid input" }
            );

        // Execute with cache
        var result = await _cacheManager.ExecuteWithCacheAsync(
            provider,
            input,
            () => CallAPI(input)
        );

        stopwatch.Stop();
        _llmLogger.LogApiCall(provider, "MyOperation", stopwatch.ElapsedMilliseconds, true);
        return result;
    }
    catch (LlmOperationException)
    {
        stopwatch.Stop();
        _llmLogger.LogApiCall(provider, "MyOperation", stopwatch.ElapsedMilliseconds, false, ex.Message);
        throw; // Re-throw LLM exceptions
    }
    catch (Exception ex)
    {
        stopwatch.Stop();
        _llmLogger.LogError("MyMethod failed", ex, new Dictionary<string, object>
        {
            { "DurationMs", stopwatch.ElapsedMilliseconds },
            { "InputLength", input?.Length }
        });
        throw new LlmApiException("Failed to execute operation", provider, null, ex.Message, ex);
    }
}


PATTERN 2: PARSING WITH ERROR DETAILS
======================================

public IReadOnlyList<MyModel> ParseResponse(string jsonResponse)
{
    const string provider = "Gemini:ParseResponse";

    try
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<List<MyModel>>(jsonResponse, options);
    }
    catch (JsonException ex)
    {
        throw new LlmParsingException(
            "Failed to parse API response",
            provider,
            jsonResponse.Substring(0, Math.Min(500, jsonResponse.Length)), // Include preview
            ex
        );
    }
    catch (Exception ex)
    {
        throw new LlmParsingException("Unexpected error during parsing", provider, jsonResponse, ex);
    }
}


PATTERN 3: VALIDATION WITH DETAILED ERRORS
============================================

private void ValidateInput(string keyword, List<string> tags)
{
    const string provider = "Gemini:ValidateInput";
    var errors = new List<string>();

    if (string.IsNullOrWhiteSpace(keyword))
        errors.Add("Keyword is required");

    if (tags == null || tags.Count == 0)
        errors.Add("At least one tag is required");

    if (tags.Any(t => t.Length > 50))
        errors.Add("Tags cannot exceed 50 characters");

    if (errors.Count > 0)
        throw new LlmValidationException("Input validation failed", provider, errors);
}


PATTERN 4: LOGGING CACHE OPERATIONS
====================================

private async Task<string> GetFromCacheAsync(string cacheKey, string provider)
{
    try
    {
        var result = await _cacheService.GetAsync(cacheKey);
        if (result != null)
        {
            _llmLogger.LogCacheOperation("GET", cacheKey, true, stopwatch.ElapsedMilliseconds);
            return result;
        }
        
        _llmLogger.LogCacheOperation("GET", cacheKey, false, stopwatch.ElapsedMilliseconds);
        return null;
    }
    catch (Exception ex)
    {
        _llmLogger.LogWarning($"Cache retrieval failed for provider '{provider}'");
        return null; // Graceful fallback
    }
}


// ============================================================================
// BEST PRACTICES
// ============================================================================

✅ DO:
  - Include provider name in all exception contexts
  - Log operation start/stop with timing
  - Validate input early and throw validation exceptions
  - Include relevant context in error logs (IDs, sizes, etc.)
  - Use specific exception types (not generic Exception)
  - Distinguish between retryable and fatal errors
  - Log successful high-value operations at INFO level
  - Use DEBUG for detailed diagnostic info

❌ DON'T:
  - Catch and swallow exceptions silently
  - Include passwords/secrets in logs or exceptions
  - Create generic "Error occurred" messages
  - Log full stack traces at WARNING level
  - Mix file logging and ILogger - use only ILogger
  - Assume Exception.Message tells the whole story
  - Ignore cache failures (they shouldn't block API calls)


// ============================================================================
// CONFIGURATION FOR LOGGING
// ============================================================================

In appsettings.json:

{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "CentauriSeo.Infrastructure": "Debug",  // LLM clients
      "CentauriSeo.Infrastructure.Services.LlmCacheManager": "Debug"
    }
  }
}

In appsettings.Development.json (override):

{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",  // More verbose in development
      "CentauriSeo.Infrastructure": "Debug"
    }
  }
}


// ============================================================================
// EXCEPTION HANDLING STRATEGIES
// ============================================================================

STRATEGY 1: Retry on Timeout
============================

catch (LlmTimeoutException ex)
{
    _llmLogger.LogWarning($"Operation timed out, retrying... | Timeout: {ex.Timeout}");
    
    // Wait before retry
    await Task.Delay(ex.Timeout ?? TimeSpan.FromSeconds(5));
    
    // Retry operation
    return await ExecuteWithRetry(operation, maxRetries: 3);
}

STRATEGY 2: Fallback on Rate Limit
==================================

catch (LlmRateLimitException ex)
{
    _llmLogger.LogWarning($"Rate limited | RetryAfter: {ex.RetryAfter}");
    
    if (ex.RetryAfter.HasValue)
        await Task.Delay(ex.RetryAfter.Value);
    
    // Use alternative provider or cached data
    return await GetAlternativeResult();
}

STRATEGY 3: Handle Parsing Errors Gracefully
==============================================

catch (LlmParsingException ex)
{
    _llmLogger.LogWarning($"Failed to parse response | Provider: {ex.Provider}");
    
    // Try alternative parsing
    try
    {
        var result = await TryAlternativeParsing(ex.RawContent);
        if (result != null) return result;
    }
    catch { /* continue */ }
    
    // Return empty/default result
    return GetDefaultResult();
}

STRATEGY 4: Log and Return Default
===================================

catch (LlmParsingException ex)
{
    _llmLogger.LogError("Parsing failed, using default", ex, 
        new Dictionary<string, object> { { "HasContent", !string.IsNullOrEmpty(ex.RawContent) } });
    
    return GetDefaultResult(); // Don't crash
}


// ============================================================================
// PERFORMANCE LOGGING
// ============================================================================

API Call Performance:
  _llmLogger.LogApiCall(provider, operation, stopwatch.ElapsedMilliseconds, success, errorMessage);

Creates log like:
  "API Call | Provider: Gemini | Operation: Get Section Score | Duration: 1234ms | Status: SUCCESS"

Cache Performance:
  _llmLogger.LogCacheOperation(operation, cacheKey, hit, stopwatch.ElapsedMilliseconds);

Creates log like:
  "Cache: GET | Key: abc123... | Hit: true | DurationMs: 1"


// ============================================================================
// CONTEXT ENRICHMENT
// ============================================================================

Context is automatically added to all logs:

_llmLogger.LogError("Operation failed", exception, new Dictionary<string, object>
{
    { "ProviderId", providerId },
    { "RequestId", requestId },
    { "DataSize", dataSize },
    { "DurationMs", durationMs }
});

Log output includes all context:
  "Operation failed [ProviderId=123 | RequestId=abc | DataSize=5000 | DurationMs=2000]"


// ============================================================================
// TROUBLESHOOTING WITH LOGS
// ============================================================================

SLOW API CALLS:
  Look for: High DurationMs in LogApiCall
  Check: Logs are at INFO level with duration
  Fix: Check network, API rate limits, payload size

CACHE NOT WORKING:
  Look for: "MISS" in cache operation logs
  Check: Are DEBUG logs enabled?
  Verify: Same provider name used for cache key

HIGH ERROR RATE:
  Look for: ERROR level logs with exception type
  Check: Exception type and message
  Analyze: Pattern - specific inputs, times, providers

MISSING RESULTS:
  Look for: Operation succeeded but returned null/empty
  Check: WARN logs might explain why
  Verify: Parsing succeeded, check parsed count


// ============================================================================
// INTEGRATION WITH EXISTING CODE
// ============================================================================

In your LLM client:

1. Inject ILogger<YourClient>:
   public MyClient(..., ILogger<MyClient> logger)
   {
       _llmLogger = new LlmLogger(logger);
   }

2. Use for all operations:
   try { ... }
   catch (LlmParsingException) { throw; }
   catch (Exception ex) { throw new LlmApiException(...); }

3. Log timing and results:
   _llmLogger.LogApiCall(provider, "Operation", stopwatch.ElapsedMilliseconds, success);


// ============================================================================
// MIGRATION GUIDE
// ============================================================================

FROM OLD CODE:
  await _logger.LogErrorAsync($"Error: {ex.Message}:{ex.StackTrace}");
  return null;

TO NEW CODE:
  throw new LlmApiException("Operation failed", provider, null, ex.Message, ex);

Benefits:
  ✓ Structured exception information
  ✓ Automatic context enrichment
  ✓ Caller decides how to handle
  ✓ No silent failures
  ✓ Better debugging capability
*/
