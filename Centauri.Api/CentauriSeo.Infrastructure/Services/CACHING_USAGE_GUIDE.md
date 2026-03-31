/**
 * LLM CACHING SYSTEM - USAGE GUIDE
 * ================================
 * 
 * This centralized caching system simplifies and standardizes LLM API caching
 * across all providers (Gemini, Groq, etc.)
 * 
 * CONFIGURATION (appsettings.json):
 * ----------------------------------
 * {
 *   "LlmCache": {
 *     "Enabled": true,              // Toggle all caching on/off globally
 *     "CacheType": "Memory",        // Or "Database" (future support)
 *     "DurationHours": 24,          // Cache duration
 *     "IncludedProviders": ["Gemini", "Groq"]  // Whitelist providers
 *   }
 * }
 * 
 * DEVELOPMENT (appsettings.Development.json) - optional override:
 * {
 *   "LlmCache": {
 *     "Enabled": false  // Disable caching during development
 *   }
 * }
 * 
 * 
 * USAGE PATTERN 1 - Simple String Result:
 * ========================================
 * 
 *   private readonly ILlmCacheManager _cacheManager;
 *   
 *   public async Task<string> CallGeminiAPI(string prompt)
 *   {
 *       return await _cacheManager.ExecuteWithCacheAsync(
 *           "Gemini",
 *           prompt,
 *           () => DoActualAPICall(prompt)
 *       );
 *   }
 *   
 *   private async Task<string> DoActualAPICall(string prompt)
 *   {
 *       // Actual API call logic
 *       var response = await _httpClient.PostAsync(...);
 *       return await response.Content.ReadAsStringAsync();
 *   }
 * 
 * 
 * USAGE PATTERN 2 - Typed/JSON Result:
 * ======================================
 * 
 *   public async Task<SomeResponse> CallGroqAPI(string request)
 *   {
 *       return await _cacheManager.ExecuteWithCacheAsync<SomeResponse>(
 *           "Groq",
 *           request,
 *           () => DoActualAPICall(request)
 *       );
 *   }
 *   
 *   private async Task<SomeResponse> DoActualAPICall(string request)
 *   {
 *       var response = await _httpClient.PostAsync(...);
 *       var json = await response.Content.ReadAsStringAsync();
 *       return JsonSerializer.Deserialize<SomeResponse>(json);
 *   }
 * 
 * 
 * USAGE PATTERN 3 - Manual Cache Key Management:
 * ===============================================
 * 
 *   public async Task<string> ComplexCacheLogic(string input)
 *   {
 *       var cacheKey = _cacheManager.ComputeRequestKey(input, "Gemini");
 *       
 *       return await _cacheManager.GetOrExecuteAsync(
 *           cacheKey,
 *           "Gemini",
 *           () => DoActualAPICall(input)
 *       );
 *   }
 *
 *
 * REFACTORING EXISTING CODE:
 * ==========================
 * 
 * OLD CODE (inconsistent):
 * ----
 *   var cacheKey = _cache.ComputeRequestKey(userContent, "Gemini:SectionScore");
 *   var cachedResponse = await _cache.GetAsync(cacheKey);
 *   if (cachedResponse != null)
 *       return cachedResponse;
 *   
 *   var response = await ProcessContent(...);
 *   await _cache.SaveAsync(cacheKey, response);
 *   return response;
 * 
 * NEW CODE (clean & centralized):
 * ----
 *   return await _cacheManager.ExecuteWithCacheAsync(
 *       "Gemini:SectionScore",
 *       userContent,
 *       () => ProcessContent(...)
 *   );
 * 
 * 
 * DISABLING CACHING:
 * ==================
 * 
 * Option 1: Global toggle in appsettings.json
 *   "LlmCache": { "Enabled": false }
 * 
 * Option 2: Development environment in appsettings.Development.json
 *   "LlmCache": { "Enabled": false }
 * 
 * Option 3: Environment variable override (future enhancement)
 * 
 * 
 * BENEFITS:
 * =========
 * - Single source of truth for caching logic
 * - Reduced code duplication across LLM clients
 * - Easier to add new caching strategies (DB, Redis, etc.)
 * - Configuration-driven enable/disable
 * - Built-in logging for cache hits/misses
 * - Handles errors gracefully (falls back to API call if cache fails)
 * - Automatic key generation based on content + provider
 * 
 * 
 * LOGGING:
 * ========
 * 
 * The cache manager logs:
 * - Cache hits/misses at DEBUG level
 * - Errors at WARNING level
 * 
 * Enable debug logging in appsettings.json to see cache activity:
 * "Logging": {
 *   "LogLevel": {
 *     "CentauriSeo.Infrastructure.Services.LlmCacheManager": "Debug"
 *   }
 * }
 * 
 * 
 * IMPLEMENTATION NOTES:
 * ====================
 * 
 * 1. Register in Program.cs:
 *    builder.Services.AddSingleton<ILlmCacheManager, LlmCacheManager>();
 * 
 * 2. Inject in LLM Client constructors:
 *    public GeminiClient(..., ILlmCacheManager cacheManager)
 *    {
 *        _cacheManager = cacheManager;
 *    }
 * 
 * 3. Use in API call methods:
 *    return await _cacheManager.ExecuteWithCacheAsync("Gemini", input, () => APICall());
 * 
 * 4. Configuration is read from IConfiguration during construction
 * 
 * 5. Cache service selection (Memory vs Database) can be changed in Program.cs
 * 
 */
