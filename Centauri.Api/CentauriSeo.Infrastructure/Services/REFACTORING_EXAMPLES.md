/**
 * REFACTORING EXAMPLES - Before & After
 * ====================================
 * 
 * This file shows concrete examples of how to refactor existing LLM client code
 * to use the new centralized LlmCacheManager.
 */

// ============================================================================
// EXAMPLE 1: Simple String Result Caching
// ============================================================================

// BEFORE (Inconsistent, duplicated code):
/*
public async Task<string> GetAnalysis(string content)
{
    var cacheKey = _cache.ComputeRequestKey(content, "MyProvider");
    var cachedResponse = await _cache.GetAsync(cacheKey);
    
    if (cachedResponse != null)
    {
        return cachedResponse;
    }
    
    try
    {
        var response = await CallAPI(content);
        if (!string.IsNullOrEmpty(response))
        {
            await _cache.SaveAsync(cacheKey, response);
            return response;
        }
    }
    catch (Exception ex)
    {
        _logger.LogError($"Error: {ex.Message}");
    }
    
    return string.Empty;
}
*/

// AFTER (Clean, centralized):
/*
public async Task<string> GetAnalysis(string content)
{
    try
    {
        return await _cacheManager.ExecuteWithCacheAsync(
            "MyProvider",
            content,
            () => CallAPI(content)
        );
    }
    catch (Exception ex)
    {
        _logger.LogError($"Error: {ex.Message}");
        return string.Empty;
    }
}
*/


// ============================================================================
// EXAMPLE 2: JSON/Typed Result Caching  
// ============================================================================

// BEFORE (Manual serialization, parsing):
/*
public async Task<MyResponse> AnalyzeJson(string input)
{
    var cacheKey = _cache.ComputeRequestKey(input, "JsonProvider");
    var cached = await _cache.GetAsync(cacheKey);
    
    if (cached != null)
    {
        try
        {
            return JsonSerializer.Deserialize<MyResponse>(cached, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { /* continue */ }
    }
    
    var result = await CallAPI(input);
    if (result != null)
    {
        await _cache.SaveAsync(cacheKey, JsonSerializer.Serialize(result));
    }
    
    return result;
}
*/

// AFTER (Automatic serialization):
/*
public async Task<MyResponse> AnalyzeJson(string input)
{
    return await _cacheManager.ExecuteWithCacheAsync<MyResponse>(
        "JsonProvider",
        input,
        () => CallAPI(input)
    );
}
*/


// ============================================================================
// EXAMPLE 3: Multiple Providers (Gemini + Groq)
// ============================================================================

// CLIENT 1: GeminiClient
/*
public async Task<string> ProcessWithGemini(string content)
{
    return await _cacheManager.ExecuteWithCacheAsync(
        "Gemini:Advanced",  // Provider name
        content,
        () => GeminiAPI(content)
    );
}
*/

// CLIENT 2: GroqClient
/*
public async Task<string> ProcessWithGroq(string content)
{
    return await _cacheManager.ExecuteWithCacheAsync(
        "Groq:FastAnalysis",  // Different provider
        content,
        () => GroqAPI(content)
    );
}
*/

// BENEFITS:
// - Each provider has separate cache entries (different keys)
// - Global configuration controls all caching
// - Easy to disable for testing: set LlmCache.Enabled = false


// ============================================================================
// EXAMPLE 4: Manual Cache Key Management (Advanced)
// ============================================================================

// Use this pattern when you need custom cache key logic:
/*
public async Task<string> ProcessWithCustomKey(string id, string content, string version)
{
    // Create custom cache key based on multiple inputs
    var customKey = $"ProcessV{version}:ID{id}";
    var cacheKey = _cacheManager.ComputeRequestKey(customKey + content, "CustomProvider");
    
    return await _cacheManager.GetOrExecuteAsync(
        cacheKey,
        "CustomProvider",
        () => MyComplexAPI(id, content, version)
    );
}
*/


// ============================================================================
// EXAMPLE 5: Configuration Control
// ============================================================================

// IN APPSETTINGS.JSON:
/*
{
  "LlmCache": {
    "Enabled": true,                    // Global on/off switch
    "CacheType": "Memory",              // Memory or Database (future)
    "DurationHours": 24,                // Cache duration
    "IncludedProviders": ["Gemini", "Groq"]  // Whitelist
  }
}
*/

// IN APPSETTINGS.DEVELOPMENT.JSON (override for dev):
/*
{
  "LlmCache": {
    "Enabled": false  // Disable caching during development
  }
}
*/

// IN CODE - Check if caching is enabled:
/*
if (_cacheManager.IsCachingEnabled)
{
    // Caching is active
}
else
{
    // Caching disabled - direct API calls
}
*/


// ============================================================================
// EXAMPLE 6: Error Handling
// ============================================================================

// The cache manager handles errors gracefully:
/*
public async Task<string> FaultTolerant(string input)
{
    return await _cacheManager.ExecuteWithCacheAsync(
        "MyProvider",
        input,
        () => CallAPI(input)
    );
    
    // What happens:
    // 1. Try to get from cache
    // 2. If cache fails (exception), LOG WARNING and continue
    // 3. Execute API call
    // 4. Try to save to cache
    // 5. If cache save fails (exception), LOG WARNING and continue
    // 6. Return result regardless of cache state
}

// Result: Your code is resilient to cache failures!
*/


// ============================================================================
// STEP-BY-STEP REFACTORING CHECKLIST
// ============================================================================

/*
BEFORE REFACTORING:
[ ] Identify methods that have manual caching logic
[ ] List all cache key patterns used
[ ] Note any custom serialization/deserialization

DURING REFACTORING:
[ ] Add ILlmCacheManager to constructor (field + dependency injection)
[ ] Replace manual cache logic with ExecuteWithCacheAsync
[ ] Remove manual cache key computation (manager does it)
[ ] Remove manual serialization (manager handles JSON)
[ ] Simplify error handling

AFTER REFACTORING:
[ ] Test that cache still works
[ ] Verify in appsettings: "LlmCache.Enabled": true
[ ] Disable caching and test: "LlmCache.Enabled": false
[ ] Monitor logs for cache warnings (if any)
[ ] Count lines of code removed (should be significant!)

TESTING:
[ ] Call the method twice with same input -> should hit cache on 2nd call
[ ] Disable caching -> should always call API
[ ] Log level=Debug -> should see cache hit/miss messages
[ ] Verify serialization/deserialization works for typed results
*/


// ============================================================================
// MIGRATION TIMELINE
// ============================================================================

/*
PHASE 1 (DONE):
✓ Created ILlmCacheManager interface & implementation
✓ Created LlmCacheExtensions for easy usage
✓ Added configuration section to appsettings.json
✓ Registered in Program.cs
✓ Example: Updated GeminiClient.GetSectionScore()
✓ Example: Updated GeminiClient.TagArticleAsync()
✓ Example: Updated GroqClient.TagArticleAsync()

PHASE 2 (RECOMMENDED):
[ ] Update remaining Gemini methods
[ ] Update remaining Groq methods
[ ] Update PerplexityClient
[ ] Update OpenAiClient
[ ] Add integration tests
[ ] Monitor cache hit rates

PHASE 3 (OPTIONAL):
[ ] Add database cache backend (Redis, SQL)
[ ] Add cache expiration policies per provider
[ ] Add cache statistics/metrics
[ ] Add cache invalidation endpoints
[ ] Add distributed caching for multi-server setup
*/
