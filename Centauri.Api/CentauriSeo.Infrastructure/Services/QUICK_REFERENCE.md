/**
 * QUICK REFERENCE - Centralized LLM Caching System
 * ================================================
 */

// ============================================================================
// 5-SECOND OVERVIEW
// ============================================================================

What: Centralized caching for all Gemini/Groq LLM API calls
Why: Reduce API calls, improve performance, save costs
How: Wrap API calls with _cacheManager.ExecuteWithCacheAsync()
Where: All LLM clients use it automatically
When: Enabled by default, can toggle via appsettings.json


// ============================================================================
// ENABLE/DISABLE (EASIEST THING)
// ============================================================================

ENABLE CACHING:
  In appsettings.json:
    "LlmCache": { "Enabled": true }

DISABLE CACHING:
  In appsettings.json:
    "LlmCache": { "Enabled": false }


// ============================================================================
// USE IN YOUR CODE (SIMPLEST PATTERN)
// ============================================================================

Step 1: Inject in constructor
  public MyClient(..., ILlmCacheManager cacheManager)
  {
      _cacheManager = cacheManager;
  }

Step 2: Wrap your API call
  return await _cacheManager.ExecuteWithCacheAsync(
      "MyProvider",        // Name of your provider
      input,               // Input to cache by
      () => APICall()      // Your API call function
  );

Step 3: Done! Caching works automatically


// ============================================================================
// CURRENT STATUS (AS OF NOW)
// ============================================================================

✓ IMPLEMENTED:
  - LlmCacheManager class
  - GeminiClient integration (2 methods)
  - GroqClient integration (1 method)
  - Configuration in appsettings.json
  - Dependency injection in Program.cs
  - Error handling & logging
  - Documentation & examples

✓ FILES CREATED:
  - LlmCacheManager.cs
  - LlmCacheExtensions.cs
  - CACHING_USAGE_GUIDE.md
  - REFACTORING_EXAMPLES.md
  - IMPLEMENTATION_SUMMARY.md
  - VERIFICATION_CHECKLIST.md
  - QUICK_REFERENCE.md (this file)

✓ FILES MODIFIED:
  - Program.cs (added DI registration)
  - appsettings.json (added config section)
  - GeminiClient.cs (integrated cache manager)
  - GroqClient.cs (integrated cache manager)


// ============================================================================
// 3 CODE PATTERNS (COPY-PASTE)
// ============================================================================

PATTERN 1: String Result (Most Common)
  public async Task<string> MyMethod(string input)
  {
      return await _cacheManager.ExecuteWithCacheAsync(
          "MyProvider",
          input,
          () => CallAPI(input)
      );
  }

PATTERN 2: Typed Result (JSON)
  public async Task<MyDTO> MyMethod(string input)
  {
      return await _cacheManager.ExecuteWithCacheAsync<MyDTO>(
          "MyProvider",
          input,
          () => CallAPITyped(input)
      );
  }

PATTERN 3: With Error Handling
  public async Task<string> MyMethod(string input)
  {
      try
      {
          return await _cacheManager.ExecuteWithCacheAsync(
              "MyProvider",
              input,
              () => CallAPI(input)
          );
      }
      catch (Exception ex)
      {
          _logger.LogError($"Error: {ex.Message}");
          return string.Empty;
      }
  }


// ============================================================================
// CONFIGURATION REFERENCE
// ============================================================================

DEFAULT CONFIGURATION:
{
  "LlmCache": {
    "Enabled": true,                        // On/off switch
    "CacheType": "Memory",                  // Memory or Database
    "DurationHours": 24,                    // How long to cache
    "IncludedProviders": ["Gemini", "Groq"] // Which providers
  }
}

FOR DEVELOPMENT (appsettings.Development.json):
{
  "LlmCache": {
    "Enabled": false  // Disable during development
  }
}

FOR TESTING:
{
  "LlmCache": {
    "Enabled": true,
    "DurationHours": 1  // Short cache for testing
  }
}


// ============================================================================
// MAGIC NUMBERS (DON'T CHANGE THESE)
// ============================================================================

Cache Key Algorithm: SHA256
Cache Key Format: SHA256(provider + ":" + input)
Default Cache Duration: 24 hours
Default Cache Type: In-Memory
Error Handling: Graceful fallback (always completes)


// ============================================================================
// HOW TO ADD TO YOUR METHOD
// ============================================================================

ORIGINAL METHOD:
  public async Task<string> GetAnalysis(string content)
  {
      var result = await MyAPI(content);
      return result;
  }

ADD CACHING:
  public async Task<string> GetAnalysis(string content)
  {
      return await _cacheManager.ExecuteWithCacheAsync(
          "Gemini:Analysis",
          content,
          () => MyAPI(content)
      );
  }

THAT'S IT! Caching is now active.


// ============================================================================
// WHAT HAPPENS UNDER THE HOOD
// ============================================================================

1. Request comes in
   ↓
2. Is caching enabled? (check appsettings.json)
   ├─ No → Skip to step 6
   └─ Yes → Continue
   ↓
3. Compute cache key = SHA256(provider + ":" + input)
   ↓
4. Is value in cache?
   ├─ Yes → Return cached value
   └─ No → Continue
   ↓
5. Call your API function
   ↓
6. Save result to cache
   ↓
7. Return result to caller


// ============================================================================
// LOGS YOU'LL SEE
// ============================================================================

When cache is HIT:
  [DEBUG] Cache hit for provider 'Gemini' with key 'abc123...'

When cache is MISS:
  [DEBUG] Cache miss for provider 'Gemini' with key 'abc123...'

When result is SAVED:
  [DEBUG] Cached result for provider 'Gemini' with key 'abc123...'

When caching is DISABLED:
  [DEBUG] Caching is disabled. Executing API call directly.

When cache has ERROR:
  [WARNING] Error retrieving from cache: ...


// ============================================================================
// THINGS TO CHECK IF NOT WORKING
// ============================================================================

Caching not working?
  ✓ Check: "LlmCache.Enabled": true in appsettings.json
  ✓ Check: Application restarted after config change
  ✓ Check: Method is using _cacheManager.ExecuteWithCacheAsync()

No cache hit?
  ✓ Check: Making exact same request twice
  ✓ Check: Request content must be identical
  ✓ Check: Wait between requests (shouldn't matter)
  ✓ Check: Check logs at DEBUG level

Cache disabled in production?
  ✓ Check: appsettings.json not appsettings.Development.json
  ✓ Check: "LlmCache.Enabled": true not false
  ✓ Check: Environment is "Production" not "Development"


// ============================================================================
// PERFORMANCE EXPECTATIONS
// ============================================================================

Cache Hit Response Time: ~1-5ms (from memory)
API Call Response Time: ~500ms-5s (depends on API)
Improvement Factor: 100x-1000x faster for cache hits

If you make 100 identical requests:
  Without Cache: 100 API calls = ~50 seconds
  With Cache: 1 API call + 99 cache hits = ~0.5 seconds
  Savings: ~99x faster


// ============================================================================
// COMMON QUESTIONS
// ============================================================================

Q: Do I need to clear cache manually?
A: No, it expires automatically after DurationHours (default: 24)

Q: Does caching work for errors?
A: No, only successful results are cached

Q: Can I cache different providers separately?
A: Yes, each provider name gets separate cache entries

Q: What if input is very large?
A: Works fine, SHA256 handles any size input

Q: Can I use different cache for different providers?
A: Not yet, but possible in future (Phase 2)

Q: Is this thread-safe?
A: Yes, InMemoryCacheService is thread-safe

Q: Does it work with database cache?
A: Yes, can switch to LlmCacheService (database-backed)

Q: How much memory does it use?
A: Depends on results size and hit rate (~1MB per 100 hits typically)


// ============================================================================
// NEXT STEPS FOR YOU
// ============================================================================

IMMEDIATELY:
  1. Read CACHING_USAGE_GUIDE.md
  2. Test by making 2 identical API calls
  3. Watch for "Cache hit" in logs

THIS WEEK:
  1. Update remaining high-volume methods
  2. Monitor cache performance
  3. Adjust duration if needed

THIS MONTH:
  1. Complete refactoring Phase 2
  2. Review performance gains
  3. Share with team

THIS QUARTER:
  1. Consider database cache backend
  2. Add cache metrics
  3. Optimize based on usage patterns


// ============================================================================
// FILES TO READ (IN ORDER)
// ============================================================================

1. This file (QUICK_REFERENCE.md) - Overview
2. CACHING_USAGE_GUIDE.md - Detailed patterns
3. REFACTORING_EXAMPLES.md - Code examples
4. IMPLEMENTATION_SUMMARY.md - Technical details
5. VERIFICATION_CHECKLIST.md - Testing guide

Source Files:
  - LlmCacheManager.cs - Core implementation
  - LlmCacheExtensions.cs - Helper methods
  - GeminiClient.cs - Example 1
  - GroqClient.cs - Example 2


// ============================================================================
// SUPPORT
// ============================================================================

If something doesn't work:
  1. Check VERIFICATION_CHECKLIST.md
  2. Enable DEBUG logging
  3. Review logs for error messages
  4. Check appsettings.json syntax
  5. Restart application
  6. Review REFACTORING_EXAMPLES.md for pattern

Everything working?
  1. Monitor cache hit rates
  2. Watch for any warnings
  3. Measure performance improvement
  4. Share results with team!
